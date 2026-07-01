# SAILOR-062 — Manual TWS strategy-managed workflow

## Goal

Manual TWS orders and positions should not freeze the scanner/order workflow. If a manual broker position exists before Sailor starts, or if one appears while Sailor is running, Sailor must synchronize it into the runtime and evaluate it through the selected strategy.

## Implemented behavior

- Pre-run broker reconciliation may start the paper conduct loop even when reconciliation is `CriticalMismatch`, if the mismatch is explained by available manual/external broker state and broker truth is otherwise available.
- Manual/pre-start, manual/intraday, unknown broker, and Sailor manual-command origins now resolve to the configured strategy lifecycle policy instead of `manual-managed-exit-only`.
- The conduct loop runs a manual broker-position monitor with a separate read-only client id: `orderRouterClientId + ManualBrokerPositionMonitorClientIdOffset`.
- New broker positions detected during the loop are promoted to active `PaperSymbolSession` instances and registered in the trade lifecycle registry.
- Existing manual strategy sessions are synchronized to the latest broker quantity and average price.
- Scanner replenishment remains independent: manual broker sessions do not count toward `TargetScannerTrades`.

## Safety retained

SAILOR-062 does not bypass stale-candle gates, force-flat, order-router failures, disconnect handling, market-data degradation handling, max-notional limits, or LastEntryMinute.

## Validation command

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

Expected self-test addition:

```text
manual-broker-strategy-managed: PASS
```
