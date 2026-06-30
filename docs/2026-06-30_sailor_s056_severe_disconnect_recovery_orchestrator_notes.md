# SAILOR-056 — Severe disconnect recovery orchestrator

Date: 2026-06-30  
Status: implemented as a safe runtime recovery scaffold.

## Goal

SAILOR-056 extends the previous SAILOR-031 reconnect-only behavior. A severe broker disconnect must not only attempt reconnect/reconcile; it must also rebuild the runtime from broker truth before normal trading can continue.

The desired behavior is:

1. Detect severe disconnect/degraded socket signal.
2. Move runtime to close-only/reconnecting.
3. Reconnect and run broker reconciliation.
4. Use broker truth as the recovery source of truth.
5. Mirror broker state into the SAILOR-052 detector and SAILOR-051 registry.
6. Rebuild sessions for every broker position and every broker open-order symbol.
7. Refresh history/candles for all recovered symbols before resuming strategy evaluation.
8. Prioritize exits and flatten/reconcile safety.
9. Resume new entries only if reconciliation is clean and current ET time is before `LastEntryMinute`.
10. Resume scanner slot replenishment only under the same clean/pre-last-entry gate.
11. Write a recovery report.

## Implemented files

- `src/Sailor.App/Runtime/Paper/SevereDisconnectRecoveryOrchestrator.cs`
- `src/Sailor.App/Runtime/Paper/SevereDisconnectRecoveryReport.cs`
- `src/Sailor.App/Runtime/Paper/SevereDisconnectRecoveryReportWriter.cs`
- `src/Sailor.App/Runtime/Paper/PaperConductLoop.cs`
- `src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs`
- `src/Sailor.App/Runtime/Paper/ScannerSlotManager.cs`
- `src/Sailor.App/Configuration/SailorAppSettings.cs`
- `src/Sailor.App/appsettings.json`
- `docs/2026-06-30_sailor_order_trade_management_audit.md`

## Runtime behavior

When the conduct loop observes a degraded safety state and send-orders mode is active, it now routes recovery through `SevereDisconnectRecoveryOrchestrator` instead of only using the old reconnect service.

The orchestrator:

- calls the existing `ConnectionRecoveryService` for reconnect/reconcile attempts;
- mirrors the recovered reconciliation through `BrokerStateManualTradeDetector`;
- rebuilds active sessions from broker non-flat positions, broker open-order symbols, active scanner slots, and non-flat local positions;
- preserves known scanner-owned origins and scanner slot IDs where the registry contains them;
- classifies unknown broker/open-order symbols as `UnknownBroker`, which is exit-only under SAILOR-054;
- refreshes history for every recovered symbol using the paper scanner history path;
- blocks entries after `LastEntryMinute=945` even if reconnect/reconcile succeeded;
- requests immediate scanner replenishment only when clean reconciliation recovered before `LastEntryMinute`.

## New settings

Added under `Sailor:Runtime:Safety`:

```json
"SevereDisconnectRecoveryEnabled": true,
"SevereDisconnectRefreshHistoryBeforeResume": true,
"SevereDisconnectResumeEntriesOnlyAfterCleanReconciliation": true,
"SevereDisconnectResumeScannerBeforeLastEntry": true
```

## Report output

Recovery reports are written to:

```text
logs/<mode>/Recovery/severe_recovery_latest.json
logs/<mode>/Recovery/severe_recovery_yyyyMMdd.csv
```

Main report fields:

```text
reconnectRecovered
reconciliationStatus
brokerTruthAvailable
sessionsRebuilt
historyRefreshOk/historyRefreshTotal
easternMinuteOfDay
lastEntryMinute
canResumeEntries
scannerReplenishmentAllowed
sessionsBefore/sessionsAfter
activeSymbolsBefore
brokerPositionSymbols
brokerOpenOrderSymbols
rebuiltSymbols
warnings
```

## Safety notes

- If recovery is clean but occurs at/after `LastEntryMinute`, runtime is intentionally moved back to close-only. Existing positions can still be exited/flattened, but new entries and scanner replenishment remain blocked.
- If broker positions/open orders are not known in the registry, they are rebuilt as unknown broker sessions and therefore treated as exit-only.
- The previous SAILOR-031 reconnect path remains as fallback when `SevereDisconnectRecoveryEnabled=false`.

## Recommended validation

Build:

```powershell
dotnet clean
dotnet build
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

Dry-run smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 3
```

Send-orders recovery simulation, only in paper and only when ready:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --send-orders --iterations 5 --simulate-disconnect-at 2 --quantity 1
```

Expected recovery log lines:

```text
SAILOR-056 severe disconnect recovery orchestrator.
SAILOR-056 severe disconnect recovery report
severeRecovery reconnectRecovered=...
Severe recovery latest JSON: ...\severe_recovery_latest.json
```
