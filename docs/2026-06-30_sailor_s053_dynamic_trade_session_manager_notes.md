# SAILOR-053 — Dynamic trade session manager

Date: 2026-06-30

## Purpose

SAILOR-053 adds the first dynamic trade-session planning layer between scanner/broker evidence and the conduct loop.

Before this milestone, the conduct loop mostly created sessions from the scanner output or from a prepared-symbol fallback. That was not enough for the target operating model where SAILOR must conduct:

- scanner-owned trades;
- Sailor pre-existing trades;
- manual/pre-start TWS positions;
- manual intraday/external TWS positions detected by the broker mirror;
- recovered active lifecycle rows after restart/disconnect.

SAILOR-053 keeps order routing behavior unchanged. It changes the session planning evidence and the ownership passed into the lifecycle registry.

## Files added

- `src/Sailor.App/Runtime/Paper/DynamicTradeSessionSeed.cs`
- `src/Sailor.App/Runtime/Paper/DynamicTradeSessionPlan.cs`
- `src/Sailor.App/Runtime/Paper/DynamicTradeSessionManager.cs`

## Files updated

- `src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs`
- `src/Sailor.App/Runtime/Paper/PaperSymbolSession.cs`
- `src/Sailor.App/Runtime/Paper/PaperConductLoop.cs`
- `src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs`
- `docs/2026-06-30_sailor_order_trade_management_audit.md`

## Implemented behavior

### 1. Dynamic session plan

`PaperRuntimeHost` now creates a `DynamicTradeSessionPlan` before constructing `PaperSymbolSession` objects.

The plan merges these sources:

1. scanner-selected candidates, limited by the configured scanner/active-symbol target;
2. verified non-flat broker positions from reconciliation;
3. local non-flat Sailor positions;
4. active registry lifecycle rows with non-zero broker quantity;
5. smoke-test fallback symbols only when no scanner/broker/local/registry session exists.

The runtime prints:

```text
SAILOR-053 dynamic trade session manager.
dynamicSessions=... scannerOwned=... manualOrExternal=... brokerPositions=... localPositions=... registryRecovered=... fallback=...
dynamic-session: SYMBOL origin=... slot=... scannerTarget=... reason=...
```

### 2. Ownership is preserved into the conduct loop

`PaperSymbolSession` now stores:

- `TradeOrigin`
- `ScannerSlotId`

`PaperConductLoop` now writes order receipts to the registry using the session origin and scanner slot instead of always forcing `ScannerOwned`.

This is required so manual/external trades are conducted by the strategy but do not count toward the scanner target.

### 3. Manual/external trades are separate from scanner target

Manual/pre-start, manual intraday, unknown broker, explicit runtime, and Sailor manual-command sessions are included in the conduct plan when evidence exists, but they are not counted as scanner-owned slots.

This implements the foundation for the user rule:

> Manual orders are separate, not included in the scanner minimum-order target, but are still conducted by the strategy.

### 4. External open orders are detected but not converted to sessions yet

If the broker mirror sees external open orders without an active position, SAILOR-053 logs a warning but does not create a conduct session yet. The conversion of external open orders into fill-aware sessions belongs to a later order-management milestone.

### 5. Stop-for-day evidence is respected as evidence

The manager counts same-day stopped-for-day lifecycle rows and avoids recovering them as registry sessions. If the scanner later reselects the symbol, the session plan records that as an explicit scanner reselection event for the later replenishment/reentry policy.

## What SAILOR-053 does not change

SAILOR-053 does not yet:

- replenish scanner slots every 5 minutes;
- dynamically add sessions in the middle of a running conduct loop;
- poll broker mirror on every cadence;
- convert external open orders into managed sessions before they become broker positions;
- implement V21/V22/V23/V24 multiple-entry policy;
- change last-entry / force-flat timing;
- change order routing gates.

Those items remain planned for later milestones.

## Safety notes

Universal timing remains unchanged for every strategy:

```text
LastEntryMinute = 945  -> 15:45 ET
ForceFlatMinute = 955  -> 15:55 ET
```

SAILOR-053 cannot override these timing gates.

## Recommended tests

Build:

```powershell
dotnet clean
dotnet build
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

Smoke-test explicit runtime session:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 3
```

Scanner-backed dynamic session plan:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --dry-run --max-symbols 45 --no-depth --iterations 3 --quantity 1
```

Broker mirror before send-orders:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper broker mirror --account DUN559573 --wait-seconds 15
```

Registry status:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trades status --all
```
