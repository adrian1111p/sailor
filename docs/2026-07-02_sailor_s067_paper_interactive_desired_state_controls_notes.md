# SAILOR-067 — Paper interactive desired-state controls

Date: 2026-07-02

## Purpose

SAILOR-067 extends the SAILOR-066 read-only SailorUI with paper-only interactive desired-state controls. The goal is to let the operator prepare and adjust the desired trading state from the compact TWS-style UI without giving the browser direct broker-order authority.

The controls are intentionally paper-only in this milestone. Live SailorUI remains read-only.

## Commands

Read-only, unchanged from SAILOR-066:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --port 5101
```

Paper controls enabled:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --port 5101 --ui-controls --account DUN559573
```

Aliases:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper ui --port 5101 --ui-controls --account DUN559573

dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper monitor-ui --port 5101 --ui-controls --account DUN559573
```

Live mode remains read-only even if a control flag is passed.

## UI behavior

When `--ui-controls` is not supplied, checkboxes and strategy dropdowns remain disabled.

When `--ui-controls` is supplied in paper mode:

- the `Trade` checkbox becomes enabled;
- the `Strategy` dropdown becomes enabled;
- browser updates are posted to `/api/desired-state`;
- accepted changes are persisted to state and audit logs;
- rejected changes are shown in the browser status line and the UI reloads the last accepted state.

## Desired-state meaning

The persisted desired state records operator intent:

| UI action | Stored meaning |
|---|---|
| Checkbox checked | The symbol is desired for paper trade participation. |
| Checkbox unchecked | The symbol is desired out of trade / not selected for new paper entry. |
| Strategy dropdown changed | The operator-selected strategy profile for this symbol. |

This milestone persists and displays desired state. It does not bypass paper runtime safety gates. Browser actions do not directly submit orders.

## Strategy limit

The desired-state validator enforces the SAILOR-065 / SAILOR-066 rule:

```text
maxActiveStrategies = 2
```

Only two distinct active strategies may be selected across checked symbols. If the operator checks a third strategy, the update is rejected and the previous state is preserved.

This keeps the first interactive implementation safe while preparing for later mixed-strategy paper execution.

## New HTTP endpoints

### `GET /api/snapshot`

Returns the same SAILOR-066 snapshot plus desired-state fields:

- `controlsEnabled`
- `controlMode`
- `activeDesiredStrategies`
- `desiredStateUpdatedUtc`

### `POST /api/desired-state`

Paper-only desired-state update.

Example request:

```json
{
  "symbol": "TSLA",
  "desiredTradeEnabled": true,
  "selectedStrategy": "v21-15minutes",
  "source": "SailorUI"
}
```

Example success response:

```json
{
  "accepted": true,
  "rejectedReason": "",
  "row": {
    "symbol": "TSLA",
    "desiredTradeEnabled": true,
    "selectedStrategy": "v21-15minutes"
  }
}
```

Example rejection:

```json
{
  "accepted": false,
  "rejectedReason": "Rejected because 3 active strategies would be selected; maxActiveStrategies=2: v21-15minutes,v18-silver,v19-purplecloud."
}
```

## Files written by SAILOR-067

Latest desired state:

```text
state\paper\ui\desired_state_latest.json
```

Daily desired-state event stream:

```text
state\paper\ui\desired_state_YYYYMMDD.jsonl
```

Daily operator action audit log:

```text
logs\Paper\SailorUI\sailor_ui_actions_YYYYMMDD.csv
```

CSV columns:

```text
TimeUtc,Mode,Account,Symbol,OldEnabled,NewEnabled,OldStrategy,NewStrategy,Accepted,RejectedReason,UserAgent,Source
```

## Safety rules

- Controls are available only for paper mode.
- Live SailorUI remains read-only.
- Browser controls do not submit broker orders.
- Browser controls do not override force-flat, stale-data, severe-disconnect, broker reconciliation, manual-position, or router gates.
- The UI endpoint persists operator desired state; paper conduct consumption can be hardened in the next milestone if required.

## Tests

Run all self-tests:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

Expected after SAILOR-067:

```text
PASS passed=17/17
```

Run only the SAILOR-067 test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario sailor-ui-paper-controls
```

Expected:

```text
sailor-ui-paper-controls: PASS
```

The test verifies:

- `/api/desired-state` contract exists;
- two active strategies are accepted;
- the third active strategy is rejected;
- unchecked/go-out state is persisted;
- latest desired-state JSON is written;
- daily action CSV is written;
- active desired strategies remain within the configured maximum;
- controls are paper-only.

## Acceptance criteria

SAILOR-067 is accepted when:

1. `dotnet build` passes.
2. `paper trade-management-test --scenario all` shows `PASS passed=17/17`.
3. `paper sailor-ui --port 5101` still opens read-only UI.
4. `paper sailor-ui --port 5101 --ui-controls --account DUN559573` enables checkboxes/dropdowns.
5. Checking/unchecking a row updates `state\paper\ui\desired_state_latest.json`.
6. The action is written to `logs\Paper\SailorUI\sailor_ui_actions_YYYYMMDD.csv`.
7. A third active strategy selection is rejected.
8. Live UI remains read-only.
