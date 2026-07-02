# SAILOR-069 — Live UI hardening

## Purpose

SAILOR-069 hardens SailorUI for live mode after the paper-only interactive controls introduced by SAILOR-067 and the paper multi-strategy routing introduced by SAILOR-068.

The goal is simple and conservative:

- paper SailorUI may persist desired-state controls when started with `--ui-controls`;
- live SailorUI is always read-only;
- live SailorUI never persists browser desired-state actions;
- live SailorUI never activates UI-driven conduct routing;
- live SailorUI is loopback-only.

## Commands

### Paper controls remain available

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --port 5101 --ui-controls --account DUN559573
```

### Live UI is read-only

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live sailor-ui --port 5101 --read-only
```

If `--ui-controls` is accidentally supplied in live mode, SAILOR-069 ignores it and logs a warning. The UI still starts in `live-read-only-locked` mode.

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live sailor-ui --port 5101 --ui-controls --account DU123456
```

Expected behavior:

```text
SAILOR-069 live UI hardening.
SAILOR-069 live hardening is active. --ui-controls was requested but ignored.
```

## Live hardening rules

| Rule | Behavior |
|---|---|
| Controls in live | Always disabled |
| `POST /api/desired-state` in live | `403 Forbidden` |
| Live desired-state files | Not written by browser actions |
| Live desired-state action CSV | Not written by browser actions |
| Live conduct routing from UI | Disabled |
| Live host binding | Forced to loopback if a non-loopback host is requested |
| UI badge | `LIVE read-only lock` |
| Snapshot control mode | `live-read-only-locked` |

## Endpoints

### `GET /api/snapshot`

Live snapshots remain available for monitoring, but expose:

```json
{
  "controlsEnabled": false,
  "controlMode": "live-read-only-locked",
  "activeDesiredStrategies": []
}
```

### `POST /api/desired-state`

In live mode the endpoint rejects all write attempts:

```json
{
  "accepted": false,
  "rejectedReason": "SAILOR-069 live SailorUI desired-state controls are locked read-only. Live UI browser actions cannot create, modify, or route desired-state entries."
}
```

## Safety contract

SAILOR-069 does not add live trading control from the browser. It only protects the existing monitoring surface.

Live trading remains controlled by existing live command-line gates such as:

- `--confirm-live`;
- `--operator-watching-tws`;
- account and max-notional gates;
- broker reconciliation gates;
- stale-candle and force-flat gates.

## Self-test

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario sailor-ui-live-hardening
```

Expected:

```text
sailor-ui-live-hardening: PASS
```

Full suite after SAILOR-069:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

Expected:

```text
PASS passed=19/19
```
