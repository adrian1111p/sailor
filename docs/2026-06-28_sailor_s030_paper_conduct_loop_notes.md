# SAILOR-030 — Paper conduct loop implementation notes

Date: 2026-06-28

## Scope implemented

SAILOR-030 adds the first real `paper run` conduct loop on top of the SAILOR-028 order router and SAILOR-029 reconciliation state.

Implemented files:

```text
src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs
src/Sailor.App/Runtime/Paper/PaperRuntimeHostRequest.cs
src/Sailor.App/Runtime/Paper/PaperSymbolSession.cs
src/Sailor.App/Runtime/Paper/PaperConductLoop.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyAdapter.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyPositionContext.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
```

## Runtime behavior

`paper run` now:

1. Builds normal Sailor runtime options from the command line.
2. Runs pre-run reconciliation.
   - Dry-run uses local-only reconciliation and assumes dry-run fills locally.
   - Send-orders requests broker reconciliation and blocks the runtime when broker state is not matched.
3. Runs the paper scanner using the existing SAILOR-027 scanner adapter.
4. Activates scanner top-N symbols.
5. Falls back to prepared symbols if scanner ranking returns no candidates, so smoke tests can still exercise the loop.
6. Builds `SailorStrategyFrame` objects on the configured cadence.
7. Evaluates the selected strategy profile using `SailorStrategyAdapter`.
8. Converts strategy decisions to normalized `SailorOrderIntent` objects.
9. Routes intents through the dry-run router or IBKR paper router.
10. Appends all generated intents/receipts to the Sailor order ledger.
11. Applies dry-run fills in-memory so entry and exit conduct paths can be tested without broker orders.
12. Force-flats open runtime positions at or after the configured force-flat minute.

## Commands

Dry-run smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1
```

IBKR paper send-orders test:

```powershell
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --send-orders --account DUN559573 --wait-seconds 15 --iterations 10
```

Useful options:

```text
--dry-run              do not send broker orders; assume local fills for conduct-loop testing
--send-orders          send paper orders only after broker reconciliation matches
--quantity N           order quantity per new entry; default 1
--iterations N         number of conduct-loop iterations; default derived from --seconds
--seconds N            run duration used to derive iterations when --iterations is not provided
--cadence-seconds N    conduct-loop cadence; default 1
--force-flat-now       force exit for any seeded/open runtime position immediately
--local-cache          use local cached/backtest data instead of IBKR history/market data
--no-quotes            do not request L1/L2 snapshots during scanner activation
--no-depth             disable L2 depth during scanner activation
```

## Safety behavior

- Live mode remains blocked.
- Send-orders mode is paper-only.
- Send-orders mode does not start unless SAILOR-029 broker reconciliation returns `Matched`.
- External/manual broker positions or unmapped open orders block entries.
- Dry-run mode never sends orders to IBKR.
- Entry orders are blocked after the configured last-entry minute.
- Force-flat creates exit intents for open runtime positions.

## Deferred to later milestones

- Persistent all-day paper daemon with reconnect/recovery.
- Continuous live bar aggregation from tick data.
- Full broker fill reconciliation after every order.
- Close-only degraded state and kill-switch handling.
- Certification report.
