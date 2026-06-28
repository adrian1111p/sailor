# 2026-06-28 Sailor Paper/Live Architecture Audit and Implementation Plan

## 1. Purpose

This audit defines how Sailor should evolve from the current backtest-first application into a simple but production-oriented paper/live trading application.

The goal is **not** to import the Harvester live runtime as-is. Harvester is powerful, but it is too large and has too many coupled modules for the current Sailor goal. The goal is to reuse the **good architecture ideas** from Harvester and implement them in a cleaner Sailor-native style.

Scope of this audit:

- Adapt the current Sailor scanner to backtest, paper, and live using one shared scanner concept where possible.
- Adapt the current Sailor conduct strategies to run in backtest, paper, and live using one shared strategy/conduct layer where possible.
- Define connection, historical data, L1/L2 snapshot, order sending, order closing, reconciliation, and disconnection handling.
- Define which functions should be shared across backtest/paper/live and which functions must stay mode-specific.
- Create a detailed step-by-step implementation plan for the next Sailor milestones.

Current Sailor baseline assumed by this audit:

- CSV backtest module exists.
- Strategy profile registry exists.
- Scanner and ranking reports exist.
- HTML strategy/trade report exists.
- Logs are under root `logs`.
- Harvester-inspired conduct strategies exist in Sailor-native form.
- Long/short mirrored strategy behavior exists.
- V21 minimal completed-15m angle entry behavior exists.
- L1/L2 snapshot layer has started in Sailor-native form.
- Harvester legacy code remains excluded and should stay excluded.

---

## 2. Executive conclusion

Sailor should move to paper/live in **two layers**:

1. **Shared strategy engine layer**
   - scanner
   - indicators
   - L1/L2 snapshot model
   - strategy profile selection
   - conduct state
   - entry/exit decision generation
   - order intent creation
   - reporting

2. **Mode adapter layer**
   - backtest adapter reads CSV and simulates fills
   - paper adapter connects to IBKR paper and sends real paper orders
   - live adapter connects to IBKR live and sends real live orders only after explicit operator confirmation

The clean Sailor architecture should be:

```text
Scanner + data snapshot
    -> strategy conduct profile
    -> entry/exit/flatten decision
    -> normalized order intent
    -> runtime-specific order router
    -> position/order lifecycle store
    -> conduct continues until flat or force-flat
```

This mirrors the strongest Harvester idea: **paper and live use the same code path; only the IBKR session/account changes**. However, Sailor should keep the implementation smaller, clearer, and less coupled.

Recommended next sequence:

```text
SAILOR-022 Runtime contracts and command model
SAILOR-023 IBKR connection and account/session handshake
SAILOR-024 Historical 1m data loader from IBKR paper
SAILOR-025 Live L1/L2 snapshot stream in paper mode
SAILOR-026 Paper scanner using live/history snapshots
SAILOR-027 Paper order intent -> IBKR paper order submission
SAILOR-028 Order lifecycle, fills, positions, and reconciliation
SAILOR-029 Paper conduct loop until flat / force-flat
SAILOR-030 Disconnection, reconnect, close-only, and kill-switch handling
SAILOR-031 Paper certification report
SAILOR-032 Live-readiness gate, no live order by default
SAILOR-033 Live small-size pilot after paper certification
```

---

## 3. Important Harvester lessons to keep

The following Harvester design ideas are useful and should be preserved in Sailor, but in simplified form.

| Harvester concept | Why it is good | Sailor implementation style |
|---|---|---|
| Same paper/live order path | Paper and live behavior are consistent. | One `IOrderRouter`; paper/live only differ by connection/account and safety flags. |
| Strategy creates an order intent, not an IBKR order directly | Keeps strategy independent from broker API. | `SailorOrderIntent` created by strategy/conduct; broker adapter translates it. |
| Connection waits for `nextValidId` and accounts | Prevents order submission before IBKR session is ready. | `IbkrConnectionSession.ConnectAsync()` must wait for order id and account list. |
| Order id allocator seeded by IBKR `nextValidId` | IBKR requires valid unique order ids. | `IbkrOrderIdAllocator` with persisted last-observed id. |
| Order lifecycle ledger | Required to map internal intent to broker order/fill/cancel. | Simple JSON/CSV `OrderLedgerStore`. |
| Reconciliation after reconnect | Prevents app state from drifting from broker state. | On reconnect request open orders, positions, executions, then reconcile. |
| Scanner input validation at startup | Prevents silent no-trade sessions. | Fail fast if universe file is missing or empty. |
| Cancel working entry orders before flatten/exit | Avoids accidental re-entry while exiting. | `FlattenAsync()` first cancels working entries for the symbol. |
| Close-only/halt modes | Allows exits but blocks new entries during problems. | `RuntimeSafetyState`: Normal, CloseOnly, Halted. |
| Disconnection runbook | Operator behavior must be clear. | Simple terminal status plus log files; no web monitor in first phase. |

---

## 4. Harvester concepts not to import directly

Sailor should explicitly avoid importing these Harvester complexities in the first paper/live implementation.

| Harvester feature | Decision for Sailor | Reason |
|---|---|---|
| Full `SnapshotRuntime` monolith | Do not import | Too large and hard to debug. |
| Runtime cache/reducer/event pipeline | Defer | Useful later, but not needed for first paper/live. |
| Self-learning runtime gates | Do not import now | The current priority is deterministic, auditable behavior. |
| Full governor DSL | Do not import now | Replace with simple hard-coded risk gates first. |
| Monitor web server | Defer | Start with console commands and logs. |
| Paired account / FA routing | Defer | Not needed for first small paper validation. |
| Broker protective stops | Defer until order lifecycle stable | First implement plain entries/exits/flatten correctly. |
| Multiple scanner lanes with complex admission policies | Defer | Start with one selected universe and one active profile. |

---

## 5. Proposed Sailor architecture

### 5.1 Project folders

Recommended new folders:

```text
src/Sailor.App/
├── Backtest/                 existing
├── Broker/
│   ├── Contracts/
│   ├── Orders/
│   ├── State/
│   └── Ibkr/
├── MarketData/
│   ├── History/
│   ├── Live/
│   ├── Snapshots/
│   └── Aggregation/
├── Runtime/
│   ├── Common/
│   ├── Backtest/
│   ├── Paper/
│   └── Live/
├── Strategy/
│   ├── Runtime/
│   ├── Profiles/
│   └── Conduct/
├── Scanner/
│   ├── Universe/
│   └── Runtime/
└── Reporting/
```

Logs:

```text
logs/
├── Backtest/
├── Backtest/Html/
├── Paper/
├── Paper/Orders/
├── Paper/Snapshots/
├── Live/
├── Live/Orders/
└── Live/Snapshots/
```

State:

```text
state/
├── paper/
│   ├── order-ledger.json
│   ├── positions.json
│   └── runtime-checkpoint.json
└── live/
    ├── order-ledger.json
    ├── positions.json
    └── runtime-checkpoint.json
```

---

## 6. One scanner for backtest, paper, and live

### 6.1 Current Sailor scanner concept

Current Sailor scanner ranks symbols using historical data and profile filters. This should stay the canonical scanner logic.

The scanner should be split into two parts:

```text
symbol universe provider
    -> market snapshot provider
    -> scanner scoring engine
    -> selected candidates
```

### 6.2 Shared scanner contracts

Proposed contracts:

```csharp
public interface ISymbolUniverseProvider
{
    Task<IReadOnlyList<string>> LoadSymbolsAsync(CancellationToken token);
}

public interface IScannerSnapshotProvider
{
    Task<SailorScannerSnapshot?> GetSnapshotAsync(string symbol, string timeframe, CancellationToken token);
}

public interface ISailorScannerEngine
{
    IReadOnlyList<SailorScannerCandidate> Rank(
        IReadOnlyList<SailorScannerSnapshot> snapshots,
        SailorStrategyProfile profile,
        int topN);
}
```

### 6.3 Backtest scanner

Backtest scanner uses:

```text
BacktestCsvUniverseProvider
CsvScannerSnapshotProvider
SailorScannerEngine
```

Data source:

```text
backtest/data/{SYMBOL}/{timeframe}.csv
```

### 6.4 Paper/live scanner

Paper/live scanner should use the same scanner engine, but with a live snapshot provider:

```text
ConfiguredUniverseProvider or ScannerFileUniverseProvider
IbkrHistorySnapshotProvider
IbkrLiveSnapshotProvider
SailorScannerEngine
```

Paper/live scanner should support these universe sources:

| Source | Use case |
|---|---|
| `smallcaps` built-in list | Fast paper testing. |
| explicit symbol list | Manual testing: `TSLA,NVDA,SOFI`. |
| CSV file | Export from external scanner. |
| appsettings profile | Daily configured scanner. |
| IBKR scanner later | Optional later. |

### 6.5 Recommended scanner commands

Backtest:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan 1m sailor-trend-volume 20 smallcaps
```

Paper dry scan without orders:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan 1m sailor-trend-volume 20 smallcaps
```

Paper scan and conduct top N:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 3 smallcaps
```

Live scan should exist later, but must require explicit confirmation:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan 1m v21-15minutes 3 smallcaps
```

Live run should require explicit flag:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live run 1m v21-15minutes 1 TSLA --confirm-live
```

---

## 7. Strategy/conduct architecture

### 7.1 Current strategy set

Sailor currently has Sailor-style and Harvester-inspired strategies. V11 is intentionally excluded.

| Profile id | Style | Paper/live suitability | Notes |
|---|---|---|---|
| `sailor-trend-volume` | Sailor trend/volume | Good for paper first | Uses simple indicators and scanner filters. |
| `sailor-conduct-v3` | Sailor conduct | Good for paper first | Good base for order/exit pipeline testing. |
| `simple-momentum` | Simple baseline | Backtest/test only | Useful for smoke tests, not priority for live. |
| `harvester-conduct-v3` | Harvester-inspired conduct | Good after paper smoke test | Uses conduct exits and L1/L2 advisory layer. |
| `harvester-conduct-v9` | Harvester-inspired conduct | Good after paper smoke test | Slightly more aggressive/tighter conduct. |
| `v21-15minutes` | 15m EMA9 angle conductor | Good for paper after history/aggregation works | Must use completed 15m candles. |
| `v23-5minutes` | 5m EMA9 angle conductor | Good for paper after history/aggregation works | Faster version of V21. |
| `v24-5minutes` | 5m enhanced angle conductor | Good for paper after V23 | Uses different threshold/min bars. |
| `v22-15minutes` | 15m enhanced angle conductor | Good for paper after V21 | Uses different threshold/min bars. |
| `v16-sqzbreakout` | Harvester-inspired | Later paper | More scanner/condition dependent. |
| `v13` | Harvester-inspired | Later paper | Needs conduct audit in paper. |
| `v10-hybrid` | Harvester-inspired | Later paper | Needs conduct audit in paper. |
| `v17-hybridflow` | Harvester-inspired | Later paper | Needs conduct audit in paper. |
| `v2-conduct` | Harvester-inspired | Later paper | Good once exits stable. |
| `v18-silver` | Harvester-inspired | Later paper | Needs paper validation. |
| `v1-first` | Harvester-inspired | Low priority | Older baseline. |
| `conduct-v3` | Harvester table profile | Good after paper smoke test | Similar family to V3. |
| `v19-purplecloud` | Harvester-inspired | Later paper | Needs validation. |
| `v15-shortcap` | Harvester-inspired | Later paper | Must validate shortability before live. |
| `v14-smallcap` | Harvester-inspired | Later paper | Small-cap risk controls needed. |
| `v20-gen001-choppyshield` | Harvester-inspired | Later paper | Needs choppy environment paper validation. |
| `v12` | Harvester-inspired | Low/medium priority | Needs paper validation. |

### 7.2 Unified strategy runtime contract

The key design is that a strategy should not know whether it is running in backtest, paper, or live. It should receive a normalized market frame and return normalized actions.

```csharp
public interface ISailorRuntimeStrategy
{
    string ProfileId { get; }

    SailorStrategyDecision Evaluate(
        SailorStrategyFrame frame,
        SailorStrategyState state,
        SailorRuntimeMode mode);
}
```

`SailorStrategyFrame` should contain:

```text
symbol
current 1m bar
recent 1m bars
higher timeframe candles
technical indicators
L1 snapshot
L2 snapshot
current position state
open order state
market clock state
profile settings
```

`SailorStrategyDecision` should contain:

```text
Hold
EnterLong
EnterShort
ExitLong
ExitShort
Flatten
CancelWorkingEntries
Reason
SuggestedLimitPrice
SuggestedQuantity
Stop/metadata if needed later
```

### 7.3 Strategy switching

Strategies should be switchable by profile id.

Examples:

```powershell
-- backtest TSLA 1m v21-15minutes
-- paper run 1m v21-15minutes 1 TSLA
-- paper run 1m harvester-conduct-v3 3 smallcaps
```

Dynamic switching while a position is open must be controlled:

| Situation | Allowed? | Rule |
|---|---:|---|
| Switch profile while flat | Yes | State reset allowed. |
| Switch profile with open position | Not by default | Require `--switch-after-flat` or explicit flatten. |
| Switch from V21 to V23 while open | No default | Different timeframe conductor can conflict. |
| Live strategy switch | Strongly restricted | Paper-only first. |

Recommended command later:

```powershell
paper switch-profile TSLA v23-5minutes --after-flat
```

---

## 8. Market data architecture

### 8.1 Shared market data model

Sailor should use one model for backtest, paper, and live:

```csharp
public sealed record SailorMarketFrame(
    string Symbol,
    DateTimeOffset Time,
    BacktestBar CurrentBar,
    IReadOnlyList<BacktestBar> RecentOneMinuteBars,
    IReadOnlyList<BacktestBar> FiveMinuteBars,
    IReadOnlyList<BacktestBar> FifteenMinuteBars,
    BacktestIndicatorSnapshot Indicators,
    SailorL1Snapshot? L1,
    SailorL2Snapshot? L2,
    SailorMarketClock Clock);
```

### 8.2 Backtest market data

Backtest market data:

```text
CSV 1m bars
-> technical indicators
-> synthetic L1/L2 snapshots where needed
-> strategy frame
```

Synthetic L1/L2 snapshots should be advisory only. They can test code paths, but they should not strongly change strategy behavior.

### 8.3 Paper/live market data

Paper/live market data:

```text
IBKR historical 1m bars at startup
IBKR live quote / top-of-book updates
IBKR market depth updates if subscription available
local candle aggregator
technical indicators refreshed from rolling bars
strategy frame every second or configured cadence
```

### 8.4 History loader

First paper/live history implementation should request only 1m bars.

Required behavior:

- Request history for all selected symbols before strategy starts.
- Store 1m bars locally for debug/replay.
- Build 5m/15m candles locally from 1m bars.
- Do not request separate 5m/15m history in first version unless needed.
- Throttle requests to avoid IBKR pacing violations.
- If a symbol has no history, mark it inactive and continue.

Proposed folder:

```text
cache/history/{yyyy-MM-dd}/{symbol}/1m.csv
```

Proposed service:

```csharp
public interface IHistoricalBarProvider
{
    Task<IReadOnlyList<BacktestBar>> GetOneMinuteHistoryAsync(
        string symbol,
        DateTimeOffset endTime,
        TimeSpan lookback,
        CancellationToken token);
}
```

### 8.5 L1/L2 snapshots

Sailor already started a Sailor-native L1/L2 layer. Paper/live should fill it from IBKR callbacks.

Recommended L1 model:

```text
symbol
timestampUtc
bid
ask
last
bidSize
askSize
lastSize
spread
mid
marketDataType
isStale
```

Recommended L2 model:

```text
symbol
timestampUtc
levels
best bid/ask depth
total bid depth
total ask depth
imbalance
weighted mid / microprice
isStale
isHealthy
source venue / smart depth
```

L1/L2 should be used in paper/live like this:

| Strategy family | L1/L2 usage |
|---|---|
| `sailor-trend-volume` | Entry quality guard: spread, staleness, last vs mid. |
| `sailor-conduct-v3` | Entry guard and exit warning. |
| `harvester-conduct-v3/v9` | Entry guard, exit warning, micro trail hints. |
| V16/V10/V13/V17/V18/V19/V20 families | Suitable for L1/L2 quality and imbalance filters. |
| V21/V22/V23/V24 angle strategies | L1/L2 should not replace angle rules; only block bad spread/stale data and improve fill price. |
| `simple-momentum` | Optional only; mainly smoke test. |

---

## 9. Broker and order architecture

### 9.1 Do not let strategies call IBKR directly

Strategies should emit normalized intents:

```csharp
public sealed record SailorOrderIntent(
    string IntentId,
    string Symbol,
    SailorOrderPurpose Purpose,
    SailorSide Side,
    SailorOrderType OrderType,
    int Quantity,
    decimal? LimitPrice,
    decimal? StopPrice,
    string TimeInForce,
    string StrategyProfileId,
    string Reason,
    DateTimeOffset CreatedAtUtc);
```

Purpose values:

```text
Entry
Exit
Flatten
Cancel
ProtectiveStop later
```

Side values:

```text
Buy
Sell
SellShort
BuyToCover
```

For IBKR, `SellShort` and `Sell` both become IBKR `SELL`; Sailor position state decides whether it is reducing long or opening short.

### 9.2 Order router abstraction

```csharp
public interface IOrderRouter
{
    Task<SailorOrderReceipt> SubmitAsync(SailorOrderIntent intent, CancellationToken token);
    Task<SailorCancelResult> CancelAsync(string brokerOrderId, string reason, CancellationToken token);
    Task<SailorFlattenResult> FlattenAsync(string symbol, string reason, CancellationToken token);
}
```

Mode-specific implementations:

| Mode | Router |
|---|---|
| Backtest | `BacktestOrderRouter` simulates fills. |
| Paper sim | `PaperSimOrderRouter` optional, local only. |
| IBKR paper | `IbkrOrderRouter` connected to paper TWS/Gateway. |
| IBKR live | Same `IbkrOrderRouter`, but live safety enabled and different account/session. |

### 9.3 Broker adapter responsibilities

A Sailor IBKR adapter should be small:

```text
BuildContract(symbol)
BuildIbOrder(intent)
PlaceOrder(orderId, contract, order)
CancelOrder(orderId)
RequestOpenOrders()
RequestPositions()
RequestExecutions()
RequestHistoricalData()
RequestMarketData()
RequestMarketDepth()
Disconnect()
```

This follows Harvester's good separation between order intent and IBKR order translation, but with fewer features.

### 9.4 Order lifecycle

Paper/live must track:

```text
IntentCreated
Submitted
OpenOrderAck
PartiallyFilled
Filled
Cancelled
Rejected
Expired
Replaced
FlattenRequested
FlattenFilled
```

A simple ledger row:

```text
intentId
brokerOrderId
permId
symbol
side
quantity
filled
remaining
averageFillPrice
lastFillPrice
status
strategyProfileId
purpose
createdUtc
lastUpdateUtc
reason
```

### 9.5 Entry order behavior

First paper version should use one of these two modes:

| Mode | Description |
|---|---|
| Limit-at-current-derived-price | Uses L1 midpoint/ask/bid plus small offset. |
| Market order for very small paper validation only | Good for proving pipeline, not for live. |

Recommended default for paper:

```text
Long entry: limit near ask / mid depending on spread
Short entry: limit near bid / mid depending on spread
Exit/flatten: market or aggressive limit depending on config
```

### 9.6 Close and flatten behavior

Flatten must be first-class and simple.

For a long position:

```text
Cancel working entries for symbol
Submit SELL flatten order for current long quantity
Track until filled
Retry if stale
Escalate if not filled
```

For a short position:

```text
Cancel working entries for symbol
Submit BUY flatten order for current short quantity
Track until filled
Retry if stale
Escalate if not filled
```

Important rule:

```text
Exits and flatten orders are allowed in CloseOnly mode.
Entries are blocked in CloseOnly mode.
```

---

## 10. Paper/live runtime architecture

### 10.1 Runtime modes

Sailor should define explicit modes:

```csharp
public enum SailorRuntimeMode
{
    Backtest,
    PaperSim,
    PaperIbkr,
    LiveIbkr
}
```

Recommended commands:

```powershell
backtest SYMBOL 1m PROFILE
scan 1m PROFILE 20 smallcaps
rank 1m PROFILE 20 smallcaps
html-report 1m smallcaps
paper connect
paper history 1m smallcaps
paper scan 1m PROFILE 20 smallcaps
paper run 1m PROFILE 3 smallcaps
paper flatten SYMBOL
paper status
live connect --read-only
live scan 1m PROFILE 3 smallcaps
live run 1m PROFILE 1 TSLA --confirm-live
live flatten SYMBOL --confirm-live
```

### 10.2 Paper runtime loop

Recommended paper loop:

```text
1. Load appsettings.
2. Resolve runtime mode = PaperIbkr.
3. Connect to TWS/Gateway paper port 7497.
4. Wait for nextValidId and accounts.
5. Validate account is paper account.
6. Load universe.
7. Request historical 1m bars.
8. Subscribe to L1 and optionally L2.
9. Build initial scanner candidates.
10. Activate top N symbols.
11. For each active symbol:
    - build market frame
    - evaluate current strategy profile
    - create order intents if needed
    - send through paper order router
    - update position/order state from broker callbacks
    - conduct open trade until exit/flatten
12. At 15:45 ET block new entries.
13. At 15:55 ET force flatten all Sailor-managed positions.
14. Persist final report.
```

### 10.3 Live runtime loop

Live runtime should be identical in structure but guarded:

```text
Live disabled by default.
Requires appsettings AllowLiveTrading=true.
Requires command line --confirm-live.
Requires max position size very small initially.
Requires paper certification artifact from recent session.
Requires all preflight checks green.
```

---

## 11. Connection and disconnection handling

### 11.1 Connection states

Proposed state machine:

```text
Disconnected
Connecting
Connected
Degraded
Reconnecting
CloseOnly
Halting
Halted
```

### 11.2 Connect function

`ConnectAsync()` must:

```text
open IBKR socket
start EReader thread
start API
wait for nextValidId
wait for managed accounts
set connection state connected
seed order id allocator
log account/session details
```

### 11.3 Heartbeat

Paper/live should monitor:

```text
last callback time
last quote update time
last historical request response
connection state
current time request response
order status callback freshness
```

If stale:

```text
mark Degraded
block new entries
keep existing conduct if data is fresh enough
otherwise enter CloseOnly
```

### 11.4 Reconnect

Reconnect strategy:

```text
1. Mark CloseOnly.
2. Stop new entries.
3. Cancel stale market data subscriptions locally.
4. Disconnect socket.
5. Wait with exponential backoff.
6. Reconnect.
7. Wait for nextValidId/accounts.
8. Request open orders.
9. Request positions.
10. Request executions since last known timestamp.
11. Reconcile ledger and positions.
12. Replay market data subscriptions.
13. If state matches broker, return to Normal.
14. If mismatch remains, stay CloseOnly or Halted.
```

### 11.5 Disconnect while holding a position

If disconnected and holding a position:

```text
Do not create new entries.
Do not assume the position is flat.
Try reconnect.
After reconnect, request positions/open orders/executions.
If still exposed and after force-flat time, submit flatten.
If unable to reconnect, emit alert and write emergency report.
```

### 11.6 Error codes

Sailor should implement a simple error policy table later. First useful categories:

| Category | Behavior |
|---|---|
| Market data farm disconnected | Degraded, block new entries if data stale. |
| Market data farm connected | Resume if snapshots fresh. |
| Historical data pacing | Backoff and retry. |
| No market data permissions | Disable symbol or L2 guard, do not crash session. |
| Order rejected | Mark order rejected, block symbol until reviewed. |
| Socket disconnected | CloseOnly + reconnect. |
| Duplicate order id | Halt order sending and re-seed id after reconnect. |

---

## 12. Shared vs mode-specific functions

### 12.1 Shared across backtest/paper/live

| Function/module | Shared? | Notes |
|---|---:|---|
| Technical indicators | Yes | EMA, SMA, VWAP, volume average, angle, ATR. |
| 1m to 5m/15m aggregation | Yes | Critical for V21/V22/V23/V24 parity. |
| Strategy profile registry | Yes | Same profile id in every mode. |
| Strategy conduct logic | Yes | Same decision logic, different execution adapter. |
| Scanner scoring | Yes | Same engine, different snapshot provider. |
| Market clock rules | Yes | 15:45 ET last entry, 15:55 ET force-flat. |
| SideMode long/short mirror | Yes | Must be identical. |
| Trade reporting | Yes | Backtest and paper/live should use same report model. |
| L1/L2 snapshot model | Yes | Backtest synthetic, paper/live real. |
| Risk settings | Mostly yes | Live has stricter overrides. |
| Order intent model | Yes | Broker-independent. |

### 12.2 Mode-specific

| Function/module | Backtest | Paper/live |
|---|---|---|
| Data source | CSV | IBKR history/live callbacks. |
| Fill model | Simulated | Broker fill callbacks. |
| Order submission | Local simulation | IBKR `placeOrder`. |
| Order status | Simulated | IBKR `orderStatus/openOrder/execDetails`. |
| Position state | Derived from fills | Broker reconciliation + local ledger. |
| Disconnection | Not applicable | Required. |
| Reconnect | Not applicable | Required. |
| Real-time pacing | Not applicable | Required. |

---

## 13. Safety controls

### 13.1 Always-on controls

These should be active in both paper and live:

```text
last entry 15:45 ET
force flat 15:55 ET
max active symbols
max active positions
max shares
max position notional
max daily orders
max open orders
cancel stale entry orders
block entries during stale data
block entries during reconnect
block entries when account/position reconciliation fails
```

### 13.2 Live-only controls

```text
AllowLiveTrading=false by default
--confirm-live required
max live position notional much smaller than paper
live profile allow-list
live account validation
live kill-switch command
live no-start if paper certification artifact missing
```

### 13.3 Short-side controls

Because Sailor now supports mirrored long/short behavior:

```text
Backtest can allow shorts freely.
Paper can test shorts only if IBKR paper accepts them.
Live must validate short availability/locate constraints before sending short entries.
If short entry rejected, block symbol short side for session.
```

---

## 14. Strategy-specific paper/live notes

### 14.1 V21/V22/V23/V24 angle strategies

These strategies are special. They must not become generic 1m scalpers in paper/live.

Required behavior:

```text
1m live bars/history
-> aggregate to completed 5m or 15m candles
-> calculate EMA9 on completed higher-timeframe candles
-> calculate EMA9 angle
-> conduct based on completed candle signal
```

Rules:

```text
angle >= positive threshold -> long bias
neutral angle -> flatten/no position
angle <= negative threshold -> short bias
red/green crossing candle rules -> flatten/re-entry lockout
15:45 ET last entry
15:55 ET force-flat
```

L1/L2 for V21/V22/V23/V24:

```text
Use L1/L2 only to avoid bad fills/stale data.
Do not let L1/L2 override the 5m/15m angle decision.
```

### 14.2 V16 and other Harvester-inspired strategies

These are more suitable for L1/L2 microstructure.

Paper/live improvements should include:

```text
spread quality check
staleness check
bid/ask size ratio check
L2 imbalance advisory
order book depth floor
avoid entries if L2 missing and profile requires it
```

### 14.3 SimpleMomentum

Use only for smoke testing:

```text
connectivity
history
scanner
order intent
paper order submission
flatten
reporting
```

Do not promote it as a serious paper/live strategy.

---

## 15. Proposed appsettings structure

```json
{
  "Runtime": {
    "DefaultMode": "Backtest",
    "DefaultTimeframe": "1m",
    "DefaultProfile": "sailor-trend-volume"
  },
  "Ibkr": {
    "Host": "127.0.0.1",
    "PaperPort": 7497,
    "LivePort": 7496,
    "ClientId": 21,
    "ConnectTimeoutSeconds": 15,
    "ReconnectMaxAttempts": 5,
    "ReconnectBaseBackoffSeconds": 2
  },
  "Paper": {
    "Account": "",
    "MaxActiveSymbols": 3,
    "MaxPositionNotional": 1000,
    "AllowShort": true,
    "UseL2": true,
    "SendOrders": false
  },
  "Live": {
    "AllowLiveTrading": false,
    "RequireConfirmLiveFlag": true,
    "Account": "",
    "MaxActiveSymbols": 1,
    "MaxPositionNotional": 250,
    "AllowShort": false,
    "UseL2": true
  },
  "MarketHours": {
    "ExchangeTimeZone": "America/New_York",
    "LastEntryEt": "15:45",
    "ForceFlatEt": "15:55"
  },
  "Scanner": {
    "DefaultUniverse": "smallcaps",
    "TopN": 3,
    "RefreshSeconds": 60
  },
  "Orders": {
    "DefaultEntryOrderType": "Limit",
    "DefaultExitOrderType": "Market",
    "EntryLimitOffsetCents": 1,
    "StaleOrderSeconds": 20,
    "FlattenRetrySeconds": 5,
    "MaxFlattenRetries": 3
  }
}
```

Important: `Paper.SendOrders` should default to `false` for first testing. The first paper implementation should support:

```text
paper run --dry-run
paper run --send-orders
```

---

## 16. Detailed step-by-step implementation plan

## SAILOR-022 — Runtime contracts and command skeleton

Goal: add structure only, no IBKR dependency yet.

Files:

```text
src/Sailor.App/Runtime/Common/SailorRuntimeMode.cs
src/Sailor.App/Runtime/Common/SailorRuntimeOptions.cs
src/Sailor.App/Runtime/Common/SailorRuntimeState.cs
src/Sailor.App/Strategy/Runtime/ISailorRuntimeStrategy.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyFrame.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyDecision.cs
src/Sailor.App/Broker/Orders/SailorOrderIntent.cs
src/Sailor.App/Broker/Orders/SailorOrderStatus.cs
```

Tasks:

1. Create runtime mode enum.
2. Create strategy frame model.
3. Create strategy decision model.
4. Create normalized order intent model.
5. Create command skeleton:

```text
paper connect
paper scan
paper run --dry-run
paper status
paper flatten SYMBOL
```

Acceptance:

```text
dotnet build
paper commands show help
no IBKR dependency yet
backtest behavior unchanged
```

---

## SAILOR-023 — IBKR connection session

Goal: connect to TWS/Gateway but do not request data or send orders yet.

Files:

```text
src/Sailor.App/Broker/Ibkr/IbkrConnectionSession.cs
src/Sailor.App/Broker/Ibkr/IbkrWrapper.cs
src/Sailor.App/Broker/Ibkr/IbkrConnectionState.cs
src/Sailor.App/Broker/Ibkr/IbkrAccountSnapshot.cs
```

Tasks:

1. Add IBKR API reference only if needed.
2. Implement `ConnectAsync(host, port, clientId)`.
3. Start EReader thread.
4. Wait for `nextValidId`.
5. Wait for managed accounts.
6. Print connection summary.
7. Implement disconnect.

Commands:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper connect
```

Acceptance:

```text
paper TWS port 7497 connects
nextValidId received
managed accounts listed
no orders sent
```

---

## SAILOR-024 — IBKR historical 1m data loader

Goal: download history for symbols and cache it.

Files:

```text
src/Sailor.App/MarketData/History/IHistoricalBarProvider.cs
src/Sailor.App/MarketData/History/IbkrHistoricalBarProvider.cs
src/Sailor.App/MarketData/History/HistoricalRequestRegistry.cs
src/Sailor.App/MarketData/History/HistoricalCacheWriter.cs
```

Tasks:

1. Request 1m historical bars for one symbol.
2. Map IBKR bars to Sailor `BacktestBar` model.
3. Save to `cache/history`.
4. Add pacing queue.
5. Add timeout and retry.
6. Add command:

```powershell
paper history 1m TSLA
paper history 1m smallcaps --top 10
```

Acceptance:

```text
history file written
backtest can run from cached history
missing permissions produce clear message
```

---

## SAILOR-025 — Live L1/L2 snapshot stream

Goal: fill Sailor-native L1/L2 snapshots from IBKR.

Files:

```text
src/Sailor.App/MarketData/Snapshots/SailorL1Snapshot.cs
src/Sailor.App/MarketData/Snapshots/SailorL2Snapshot.cs
src/Sailor.App/MarketData/Live/IbkrMarketDataSubscriptionService.cs
src/Sailor.App/MarketData/Live/LiveMarketSnapshotStore.cs
```

Tasks:

1. Subscribe to L1 market data.
2. Subscribe to L2 market depth if enabled.
3. Store latest per-symbol snapshot.
4. Mark stale snapshots.
5. Add logging under `logs/Paper/Snapshots`.
6. Do not trade yet.

Commands:

```powershell
paper quotes TSLA
paper depth TSLA
```

Acceptance:

```text
bid/ask/last visible
spread visible
L2 visible if subscription available
missing L2 does not crash session
```

---

## SAILOR-026 — Paper scanner from live/history snapshots

Goal: run current Sailor scanner in paper mode.

Files:

```text
src/Sailor.App/Scanner/Universe/ISymbolUniverseProvider.cs
src/Sailor.App/Scanner/Universe/BuiltInUniverseProvider.cs
src/Sailor.App/Scanner/Universe/FileUniverseProvider.cs
src/Sailor.App/Scanner/Runtime/PaperScannerSnapshotProvider.cs
src/Sailor.App/Scanner/Runtime/PaperScannerRunner.cs
```

Tasks:

1. Load smallcaps universe.
2. Ensure history exists or request it.
3. Build scanner snapshots.
4. Rank with existing Sailor scanner engine.
5. Output top N.
6. Do not trade yet.

Command:

```powershell
paper scan 1m sailor-trend-volume 20 smallcaps
```

Acceptance:

```text
paper scan produces same style candidates as backtest scan
symbols with missing data are listed
no orders sent
```

---

## SAILOR-027 — Paper order intent and IBKR paper order router

Goal: send first controlled paper order, but not full strategy loop yet.

Files:

```text
src/Sailor.App/Broker/Orders/IOrderRouter.cs
src/Sailor.App/Broker/Ibkr/IbkrOrderRouter.cs
src/Sailor.App/Broker/Ibkr/IbkrOrderTranslator.cs
src/Sailor.App/Broker/Ibkr/IbkrContractFactory.cs
src/Sailor.App/Broker/State/OrderLedgerStore.cs
```

Tasks:

1. Convert `SailorOrderIntent` to IBKR `Contract` and `Order`.
2. Allocate broker order id from `nextValidId`.
3. Submit paper limit order.
4. Track submitted/open/filled/rejected events.
5. Persist order ledger.
6. Add manual test command:

```powershell
paper order TSLA BUY 1 LMT 400.00 --send-orders
```

Acceptance:

```text
paper order appears in TWS paper
ledger stores intent id and broker order id
order status callbacks update ledger
```

---

## SAILOR-028 — Positions and reconciliation

Goal: know what the broker actually holds.

Files:

```text
src/Sailor.App/Broker/State/PositionStore.cs
src/Sailor.App/Broker/State/IPositionProvider.cs
src/Sailor.App/Broker/Ibkr/IbkrPositionProvider.cs
src/Sailor.App/Broker/State/ReconciliationService.cs
```

Tasks:

1. Request IBKR positions.
2. Request open orders.
3. Request recent executions.
4. Compare broker state to Sailor ledger.
5. Mark external/manual positions separately.
6. Block entries if reconciliation fails.

Command:

```powershell
paper status
paper reconcile
```

Acceptance:

```text
TWS position matches Sailor state
external positions are detected
no strategy entry if state mismatch is critical
```

---

## SAILOR-029 — Paper conduct loop

Goal: strategy runs in paper and conducts open positions until exit/flat.

Files:

```text
src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs
src/Sailor.App/Runtime/Paper/PaperSymbolSession.cs
src/Sailor.App/Runtime/Paper/PaperConductLoop.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyAdapter.cs
```

Tasks:

1. Run scanner top N.
2. Activate symbols.
3. Build strategy frames every second or configured cadence.
4. Evaluate selected strategy profile.
5. Convert decisions to order intents.
6. Send entry/exit/flatten intents if `--send-orders`.
7. If dry-run, log intents without sending.
8. Force-flat at 15:55 ET.

Commands:

```powershell
paper run 1m v21-15minutes 1 TSLA --dry-run
paper run 1m v21-15minutes 1 TSLA --send-orders
```

Acceptance:

```text
multiple intraday entries/exits are possible
open position is conducted after fill
force-flat works
logs under logs/Paper
```

---

## SAILOR-030 — Disconnection and degraded-state handling

Goal: robust paper runtime under disconnect/reconnect.

Files:

```text
src/Sailor.App/Runtime/Common/RuntimeHealthMonitor.cs
src/Sailor.App/Runtime/Common/ConnectionRecoveryService.cs
src/Sailor.App/Runtime/Common/RuntimeSafetyState.cs
src/Sailor.App/Runtime/Common/RuntimeIncidentReporter.cs
```

Tasks:

1. Detect socket disconnect.
2. Mark CloseOnly.
3. Stop new entries.
4. Reconnect with backoff.
5. Reconcile open orders/positions/executions after reconnect.
6. Replay market data subscriptions.
7. Resume normal only if state is clean.
8. Persist incident report.

Acceptance:

```text
manual disconnect does not crash runtime
runtime blocks entries while disconnected
runtime can recover and reconcile
exits remain allowed after reconnect
```

---

## SAILOR-031 — Paper certification report

Goal: one report to prove paper readiness.

Files:

```text
src/Sailor.App/Reporting/PaperSessionReportWriter.cs
src/Sailor.App/Reporting/PaperCertificationReport.cs
```

Report contents:

```text
session mode
account
profile
symbols
orders submitted
orders filled
orders rejected
positions opened/closed
force-flat result
disconnect incidents
reconciliation status
L1/L2 health
P&L
strategy decisions
all open exposure at end = zero
```

Command:

```powershell
paper report latest
```

Acceptance:

```text
paper certification report is generated
session cannot be promoted if end exposure is non-zero
```

---

## SAILOR-032 — Live-readiness gate

Goal: live mode exists but cannot accidentally trade.

Tasks:

1. Add `Live.AllowLiveTrading=false` default.
2. Require `--confirm-live`.
3. Require recent paper certification report.
4. Require account match.
5. Require max notional small.
6. Require manual confirmation printed in console.
7. Start live read-only first.

Commands:

```powershell
live connect --read-only
live scan 1m v21-15minutes 3 smallcaps
```

Acceptance:

```text
live connect works
no live order can be sent without both config and command confirmation
```

---

## SAILOR-033 — Live pilot

Goal: one-symbol live pilot with very small size.

Restrictions:

```text
one symbol
one profile
long only first unless short explicitly enabled
max notional very small
close-only command available
force-flat required
operator watches TWS
```

Command:

```powershell
live run 1m v21-15minutes 1 TSLA --confirm-live
```

Acceptance:

```text
entry/exit lifecycle works
flatten works
end exposure zero
all artifacts produced
```

---

## 17. Implementation priority recommendation

Do not start with full live trading.

Recommended immediate next implementation:

```text
SAILOR-022 Runtime contracts and command skeleton
```

Then:

```text
SAILOR-023 IBKR connection session
SAILOR-024 historical data loader
SAILOR-025 live L1/L2 snapshots
```

Only after those are stable:

```text
SAILOR-027 paper order router
SAILOR-029 paper conduct loop
```

This keeps Sailor simple and avoids the main Harvester complexity problem: too many live responsibilities coupled inside one large runtime.

---

## 18. Acceptance checklist before any live trading

Before `LiveIbkr` can send orders, require all of these:

```text
[ ] dotnet build clean
[ ] paper connect passes
[ ] paper history passes for selected symbols
[ ] paper L1 snapshots fresh
[ ] paper L2 either fresh or explicitly optional
[ ] paper scanner returns candidates
[ ] paper dry-run produces strategy decisions
[ ] paper order test submits/fills/cancels one order
[ ] paper flatten works for long
[ ] paper flatten works for short or short is disabled
[ ] paper conduct session ends flat
[ ] reconnect test passes
[ ] reconciliation test passes
[ ] close-only mode tested
[ ] halt mode tested
[ ] paper certification report generated
[ ] live config still disabled by default
[ ] live requires --confirm-live
```

---

## 19. Final recommendation

Sailor should now move toward paper/live, but in a controlled way.

The best next coding step is not order submission yet. It is:

```text
SAILOR-022: runtime contracts + paper/live command skeleton
```

Then add IBKR connection and historical data. Only after live market data and reconciliation are working should Sailor send paper orders.

The architecture must stay simple:

```text
one scanner engine
one strategy/conduct engine
one order intent model
mode-specific data/broker adapters
paper/live same order path
live disabled by default
```

This gives Sailor the best parts of Harvester without inheriting Harvester's complexity.
