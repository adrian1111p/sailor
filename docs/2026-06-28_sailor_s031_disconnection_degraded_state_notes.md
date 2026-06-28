# SAILOR-031 — Disconnection and degraded-state handling

Date: 2026-06-28

## Goal

Make the paper conduct runtime safe when IBKR/TWS, broker state, or market-data connectivity becomes degraded during a run.

The SAILOR-031 rule is conservative:

```text
Any broker disconnect, degraded broker/data signal, failed broker-state request, or order-routing failure moves the runtime to CloseOnly.
CloseOnly blocks new entries.
Exits/flatten remain allowed only when routing is available again.
Normal entries resume only after reconnect + broker reconciliation is clean.
```

## Implemented files

```text
src/Sailor.App/Runtime/Common/RuntimeSafetyState.cs
src/Sailor.App/Runtime/Common/RuntimeHealthMonitor.cs
src/Sailor.App/Runtime/Common/ConnectionRecoveryService.cs
src/Sailor.App/Runtime/Common/RuntimeIncidentReporter.cs
src/Sailor.App/Runtime/Paper/PaperRuntimeHostRequest.cs
src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs
src/Sailor.App/Runtime/Paper/PaperConductLoop.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
```

## Runtime safety states

```text
Normal       broker/reconciliation gate is clean; entries and exits may proceed
CloseOnly    new entries blocked; existing positions may still be conducted/closed when routing is available
Reconnecting reconnect + reconciliation is active; entries and broker-routed exits are paused
KillSwitch   all broker order routing is blocked
```

## Detection

`RuntimeHealthMonitor` watches broker/order/scanner messages for disconnect and degraded-state signals, including:

```text
IBKR 1100 / 1300 style connection loss
socket disconnected / not connected / connection reset / timeout
IBKR 2103 / 2105 / 2157 market-data or HMDS degradation
order-router failure receipts
broker-state request failure
scanner/activation exceptions
```

IBKR farm messages such as `connection is OK`, 1101, or 1102 are not treated as failures.

## Recovery behavior

In paper `--send-orders` mode, the conduct loop can attempt recovery when a degraded runtime state is detected:

1. Switch runtime safety to `Reconnecting`.
2. Back off between attempts.
3. Request broker positions, open orders, and executions again.
4. Run the SAILOR-029 reconciliation service.
5. Queue market-data replay for active symbols in the incident/recovery log.
6. Resume `Normal` only if reconciliation status is `Matched` and `CanOpenNewEntries=true`.
7. Otherwise remain `CloseOnly`.

The recovery logic is intentionally not used in dry-run mode because no broker session exists.

## Incident persistence

Runtime incidents are written under:

```text
logs/Paper/Incidents/incidents_YYYYMMDD.jsonl
logs/Paper/Incidents/incidents_YYYYMMDD.csv
logs/Paper/Incidents/latest_incident.json
```

The `paper status` command now also displays the latest runtime incident if one exists.

## New / updated command options

```powershell
--reconnect-attempts 3
--reconnect-backoff-seconds 2
--simulate-disconnect-at 2
```

`--simulate-disconnect-at` is for smoke testing the SAILOR-031 close-only path without manually disconnecting TWS.

## Suggested local smoke tests

Build:

```powershell
dotnet clean
dotnet build
```

Dry-run degraded-state simulation:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 5 --cadence-seconds 1 --simulate-disconnect-at 2
```

Expected result:

```text
runtime incident is created
safety moves to CloseOnly
new entries are blocked
runtime does not crash
latest incident appears in paper status
```

Status check:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper status
```

IBKR build:

```powershell
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

IBKR paper runtime after reconciliation is clean:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --send-orders --account DUN559573 --wait-seconds 15 --iterations 10 --reconnect-attempts 3 --reconnect-backoff-seconds 2
```

## Notes

- SAILOR-031 does not add live trading. Live order sending remains blocked.
- Market-data replay is logged/queued at the runtime level. The current paper runtime creates fresh scanner/snapshot providers per run, so there is no persistent subscription registry to replay yet.
- The next certification report milestone can consume the incident JSONL and latest reconciliation JSON.
