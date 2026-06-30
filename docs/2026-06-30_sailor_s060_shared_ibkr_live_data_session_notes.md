# SAILOR-060 — Shared IBKR live market-data/history session

## Goal

Prevent paper send-orders runtime from opening competing IBKR sockets for history, L1/L2 snapshots, scanner replenishment, and live candle refresh.

The issue seen during paper testing was:

- the order-router/reconciliation path used the order client id, for example `22`;
- scanner history and market-data snapshot providers also tried to use the same client id;
- live candle refresh used a second client id, but other scanner/replenishment requests could still collide;
- IBKR returned `code=326 client id is already in use`, `code=501 Already Connected`, socket aborts, and then candle refresh returned no bars;
- SAILOR-058 correctly blocked stale candles, but the live refresh path could not recover.

## Implementation

SAILOR-060 adds a shared IBKR data-session provider:

- `Broker/Ibkr/Shared/IbkrSharedMarketDataHistoryProvider.cs`
- implements both `IHistoricalBarProvider` and `ILiveMarketDataSnapshotProvider`
- shares one IBKR `EClientSocket` per `mode/host/port/clientId`
- serializes historical and market-data snapshot requests with one request lock
- uses one EReader thread per shared data client
- keeps order routing on the original order-router client id
- routes scanner/history/snapshot/replenishment/live candle refresh through the data client id offset configured by `Runtime.Safety.LiveCandleRefreshClientIdOffset`

Default effective paper IDs:

- order router / reconciliation: `22`
- shared data client: `22 + 200 = 222`

## Runtime safety

If live candle refresh fails for all active symbols during a paper send-orders run, the runtime now moves to `CloseOnly` and blocks new entries:

```text
SAILOR-060 live data refresh failed for all active symbol(s). Runtime moved to CloseOnly; entries remain blocked until the shared IBKR data session is healthy.
```

Exits and force-flat remain controlled by existing runtime safety gates.

## Evidence

Expected runtime lines:

```text
History provider: ibkr-shared-data-session clientId=222
Market data provider: ibkr-shared-data-session clientId=222
SAILOR-060 shared IBKR data connection: mode=paper host=127.0.0.1 port=7497 clientId=222 ... sendOrders=False
SAILOR-060 shared IBKR live market-data/history session is active...
```

Self-test scenario added:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario shared-ibkr-data-session
```

