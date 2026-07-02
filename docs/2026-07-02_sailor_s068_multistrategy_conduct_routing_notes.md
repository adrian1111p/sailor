# SAILOR-068 — Multi-strategy conduct routing, max two strategies

Date: 2026-07-02

## Purpose

SAILOR-068 connects the SAILOR-067 paper SailorUI desired-state controls to the paper conduct runtime. The goal is to allow the operator to choose which symbols should be allowed to trade and which strategy profile should manage each selected symbol, while keeping the strategy universe limited to a maximum of two active strategies for the first implementation.

This milestone is intentionally paper-first. Live SailorUI remains read-only.

## Operator workflow

1. Start SailorUI with controls:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --port 5101 --ui-controls --account DUN559573
```

2. In the browser, check the symbols that are allowed to trade.
3. Select the desired strategy per checked symbol.
4. Keep the total number of active strategy profiles to a maximum of two.
5. Start a normal paper conduct run or SAILOR-064 harsh test.

The paper conduct runtime reads:

```text
state\paper\ui\desired_state_latest.json
```

Unless disabled by:

```powershell
--no-ui-desired-state
```

## Runtime rules

### Checked symbol

A checked symbol is allowed to enter/continue under the selected strategy profile. The selected profile is normalized from the SailorUI dropdown label into the runtime profile name, for example:

```text
V18-Silver      -> v18-silver
V21-15Minutes  -> v21-15minutes
```

### Unchecked symbol with no open position

The flat scanner session is held inactive. It must not create a new entry. The scanner slot is closed for entry so that replenishment can replace it when appropriate.

### Unchecked symbol with open position

The paper conduct runtime creates an exit decision for the current position. This implements the SailorUI meaning:

```text
unchecked = go out of the trade
```

### Max two active strategies

SailorUI already rejects a third active strategy in SAILOR-067. SAILOR-068 adds runtime-side routing validation and summary logging, so the conduct runtime also understands the same max-two strategy rule.

### No desired-state rows

If no SailorUI desired-state rows are present, the runtime preserves the previous behavior and uses the command profile for all scanner-selected sessions.

### Active desired-state rows exist

When at least one active/checked desired-state row exists, scanner symbols not checked in the UI remain inactive. This implements the requested behavior that only the selected strategy groups/symbols are active and the rest are inactive.

## Commands

Normal paper conduct with SailorUI desired state enabled by default:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --account DUN559573 --quantity 10 --iterations 60 --cadence-seconds 60 --wait-seconds 15 --max-symbols 145 --no-depth --market-data-type 1
```

SAILOR-064 harsh test with SailorUI desired-state routing:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper harsh-test 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --account DUN559573 --quantity 10 --iterations 60 --cadence-seconds 60 --wait-seconds 15 --max-symbols 145 --no-depth --market-data-type 1
```

Disable SailorUI desired-state routing and return to pure command-profile behavior:

```powershell
--no-ui-desired-state
```

## Expected log evidence

```text
SAILOR-068 multi-strategy conduct routing.
SAILOR-068 multi-strategy conduct routing is active: maxStrategies=2
SAILOR-068 desired-state routing iteration=1 enabled=True activeStrategies=2/2 active=v18-silver,v21-15minutes
SAILOR-068 switched SYMBOL strategy v21-15minutes -> v18-silver
SAILOR-068 SailorUI desired state unchecked this symbol; route existing paper position out/flat.
```

## Self-test

Run all trade-management tests:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

Expected after SAILOR-068:

```text
PASS passed=18/18
```

Run only SAILOR-068:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario sailor-ui-multistrategy-routing
```

## Safety notes

- SAILOR-068 does not bypass broker reconciliation.
- SAILOR-068 does not bypass order-router receipt handling.
- SAILOR-068 does not enable live UI controls.
- SAILOR-058 stale candle protection still applies unless the SAILOR-064 harsh-test command intentionally bypasses entry filters for short harsh-condition testing.
- The max-two active strategy limit remains enforced by SailorUI desired-state validation.
