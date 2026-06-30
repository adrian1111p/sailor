# SAILOR Order and Trade Management Audit

Date: 2026-06-30  
Scope: paper/live order management, scanner-selected trades, TWS/manual trades, strategy ownership, minimum active order/trade target, and severe-disconnection recovery.  
Input reviewed: uploaded `sailor-main.zip` source tree after the scanner/points milestones.  
Status: audit only; no source implementation is changed in this document.

---

## 1. Executive summary

The current SAILOR implementation is safe and useful for scanner diagnostics, dry-run conduct testing, limited paper execution, and controlled live-pilot experiments. It is **not yet complete** for the desired full trading-management model where every TWS position/order is dynamically discovered and conducted in parallel, including scanner orders, pre-existing TWS orders, and manual TWS orders created after SAILOR starts.

The current runtime has this shape:

```text
scan list / scanner
  -> select fixed symbols before conduct starts
  -> create PaperSymbolSession objects for selected symbols
  -> run one sequential conduct loop over those sessions
  -> submit order intents through dry-run or IBKR router
  -> update only the local session position from the order receipt
  -> optional reconciliation before/after send-orders and once during recovery
```

The desired target model is stronger:

```text
TWS broker-state mirror + scanner manager + trade registry
  -> continuously discover all broker positions, open orders, fills, and manual changes
  -> classify every trade by origin: scanner, pre-existing TWS, manual before start, manual after start
  -> create/resume one managed trade session per active symbol/trade
  -> run strategy-specific conduct for every managed trade in parallel
  -> replenish scanner-owned trade slots every 5 minutes when below target
  -> after severe disconnect, rebuild all sessions from broker truth and continue exits/replenishment
```

### Market-open readiness conclusion

For today, before market open, the existing code can be used for:

- scanner validation;
- `paper scan-points` diagnostics;
- `paper run --dry-run` conduct observation;
- controlled paper tests with very small quantities, only if reconciliation is clean.

It should **not** yet be considered complete for unattended or semi-unattended management of all TWS trades, manual trades, pre-existing orders, and minimum-10 replenishment. Those features need the improvement milestones proposed in this audit.

Important market-close correction: the existing `LastEntryMinute=945` and `ForceFlatMinute=955` rule is intentional and must remain common to all strategies. V21-V24 may be multi-entry/multi-exit only before 15:45 ET; from 15:45 to 15:55 ET they must continue conduct/exits only.

---

## 2. User requirements and implementation status

| # | Requirement | Current status | Risk if used as-is | Required improvement |
|---|---|---|---|---|
| 1 | All trades in TWS must be conducted in parallel/asynchronously by the running SAILOR strategy, including SAILOR scanner orders, pre-existing TWS orders, and manual TWS orders created after strategy start. | **Not implemented completely.** Current sessions are created once from scanner-selected symbols, then run sequentially in `PaperConductLoop`. Broker/local positions are only used as seeds for selected symbols. Manual orders after start are not continuously discovered and attached. | SAILOR may ignore manual or pre-existing positions that are not in the selected runtime symbols. It may not exit or manage them. | Add broker-state mirror, trade registry, ownership classification, dynamic session manager, and per-symbol async conduct workers. |
| 2a | `v21-15minutes`, `v22-15minutes`, `v23-5minutes`, `v24-5minutes`: multiple entries and exits while entries are allowed; all active trades remain conducted until strategy exit or force-flat. Manual close in TWS stops that symbol for the day, except scanner re-entry rule in requirement 3. | **Partially implemented.** V21-V24 use `AngleEmaConductStrategyBase`, which can re-enter after exits on later completed candles. The correct universal market-close rule is already `LastEntryMinute=945` (15:45 ET) and `ForceFlatMinute=955` (15:55 ET). This must remain for **all strategies**, including V21-V24. Manual close detection and per-day stop state are not implemented. | Without explicit lifecycle state, manual closes may be interpreted only as flat state if reconciled, and the bot may not know to stop the symbol for the day. | Keep the universal 15:45/15:55 timing rule. Add profile lifecycle policy: V21-V24 = multi-cycle mode before 15:45; after 15:45 manage/exit only; add manual close detector and manual stop-for-day registry. |
| 2b | Rest of strategies: every entry is conducted until the strategy decides to exit. | **Partially implemented.** Strategy adapter conducts open positions via strategy decisions and conduct exits. But dynamic external positions/manual trades are not attached unless they become a selected session or are seeded into that session at startup. | A strategy-created session may be managed correctly, but external/manual positions may not. | Add dynamic session attachment for all broker positions and open orders, then apply the selected strategy to each. |
| 3 | If minimum order target is enabled, e.g. 10, scanner-created orders must be preserved and every 5 minutes SAILOR must emit new orders to restore the target number. Manual trades are separate and not counted in the scanner 10, but must still be conducted by strategy. | **Not implemented.** The scanner selects symbols before the conduct loop. The conduct loop does not replenish symbols every 5 minutes. Runtime safety has `MaxActiveSymbols=3` by default, which prevents 10 active scanner sessions unless changed. Manual trades are not separated from scanner target accounting. | The bot may not maintain 10 scanner trades. Manual trades may be counted incorrectly or ignored. | Add scanner slot manager, target-size setting, 5-minute replenishment loop, manual-vs-scanner ownership separation, and active slot state. |
| 4 | After severe disconnect, all processes must resume for all trades: history request, candle creation, strategy conduct, exits, and new orders when below minimum order target. | **Partially implemented for safety only.** Runtime can move to CloseOnly and attempt recovery/reconciliation. It does not rebuild dynamic sessions for all broker positions and open orders after reconnect. | After reconnect, some trades may remain unmanaged or only exits may be allowed; scanner replenishment is not resumed because it does not exist yet. | Add recovery orchestrator: broker truth snapshot -> rebuild registry -> refresh history for every active symbol -> recreate sessions -> resume exits first -> resume scanner replenishment only after clean reconciliation. |

---

## 3. Existing implementation map

### 3.1 Runtime entry points

The relevant command flow is in:

```text
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
```

Important runtime commands and flows:

| Command family | Current role |
|---|---|
| `paper scan-list` / `paper scan-points` | Scanner-only observation/diagnostics; no conduct loop, no orders. |
| `paper run` | Scanner-backed conduct loop. Can be dry-run or paper send-orders. |
| `live run` | Restricted live pilot. Current design is one-symbol pilot unless future multi-symbol gate is explicitly enabled and upgraded. |
| `paper reconcile` / `live reconcile` | Broker/local state check before allowing safer send-orders. |
| `paper report latest` | Certification evidence built from latest logs, ledger, local state, reconciliation, incidents. |
| `live flatten` | Close-only live flatten command path. |

### 3.2 Scanner selection before conduct

For `paper run` with scan-list input, the code runs scan-list selection first and then rewrites the runtime universe to the selected trade-eligible symbols.

Current behavior:

```text
Run scan-list selection
  -> selectedScanListSymbols = SelectScanListTradeSymbols(...)
  -> if zero and --wait-for-scan-entry, wait/rescan
  -> if still zero, block conduct and send no orders
  -> else universe = selected symbols
  -> start PaperRuntimeHost
```

This is good for safety and deterministic testing. It is not enough for live intraday slot replenishment because the selected symbols are fixed before the conduct loop begins.

### 3.3 Active session creation

The conduct runtime creates sessions in:

```text
src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs
```

Current key behavior:

```text
selected = scannerResult.Candidates.Take(maxActive)
maxActive = min(runtime TopCount, Runtime.Safety.MaxActiveSymbols)
```

Default configuration currently includes:

```text
Runtime.Safety.MaxActiveSymbols = 3
Runtime.Live.MaxConcurrentPositions = 1
```

Therefore, even if the scanner finds 10 candidates, the conduct runtime can still activate fewer sessions unless safety settings and live gates are explicitly changed.

### 3.4 Position seeding

`PaperRuntimeHost.CreateSessions` builds dictionaries from reconciliation:

```text
localBySymbol = request.Reconciliation.LocalPositions where not flat
brokerBySymbol = request.Reconciliation.BrokerPositions where not flat
```

Then it creates `PaperSymbolSession` only for selected symbols.

This means:

- if a broker/manual position exists in a selected symbol, it can seed that session;
- if a broker/manual position exists in a symbol not selected by scanner/runtime universe, it is not automatically managed by the conduct loop;
- positions or manual orders created after start are not dynamically discovered and attached.

### 3.5 Conduct loop model

The main conduct loop is:

```text
src/Sailor.App/Runtime/Paper/PaperConductLoop.cs
```

Current model:

```text
for each iteration:
    for each PaperSymbolSession session:
        frame = session.NextFrame(runtimeState)
        decision = strategy.EvaluateAsync(...)
        if decision creates order:
            run safety checks
            create SailorOrderIntent
            submit through router
            append ledger
            apply receipt to local in-memory session
```

This is **sequential**, not per-symbol asynchronous. It is safe and simple, but it is not yet the target parallel/asynchronous design.

### 3.6 Strategy adapter and position management

The strategy adapter is:

```text
src/Sailor.App/Strategy/Runtime/SailorStrategyAdapter.cs
```

Current behavior:

- If a position is open and conduct exits are configured, `SailorConductExitEngine` evaluates exit rules.
- If no position is open, the strategy entry logic evaluates entry signals.
- `Buy` while short becomes `ExitShort`.
- `Sell` while long becomes `ExitLong`.
- `Buy` while flat becomes `EnterLong`.
- `Sell` while flat becomes `EnterShort` only if the strategy allows short entries.

This is a good foundation for strategy-specific conduct, but it depends on the runtime having a correct `SailorStrategyPositionContext`. That context is currently owned by the in-memory `PaperSymbolSession`, not by a continuously updated TWS truth model.

### 3.7 Order routing

Order routing is abstracted behind:

```text
src/Sailor.App/Broker/Orders/IOrderRouter.cs
```

Current implementations include:

```text
DryRunOrderRouter
DisabledBrokerOrderRouter
IbkrPaperOrderRouter, compiled with SAILOR_IBAPI
```

`IbkrPaperOrderRouter` submits a single order, waits for acknowledgement/fill status up to the configured timeout, and returns a `SailorOrderReceipt`. If no final acknowledgement arrives, it returns `Submitted` with a warning telling the operator to check TWS.

This is a usable order-submission primitive. It is not yet a full order-state manager because it does not continuously track every TWS open order/manual order as an event stream attached to trade lifecycles.

### 3.8 Disconnection and recovery

Current recovery components:

```text
src/Sailor.App/Runtime/Common/RuntimeHealthMonitor.cs
src/Sailor.App/Runtime/Common/ConnectionRecoveryService.cs
src/Sailor.App/Runtime/Common/RuntimeIncidentReporter.cs
```

Current behavior:

- Runtime starts Normal only when pre-run gates allow entries.
- Routing failures/degraded signals move runtime to CloseOnly.
- In send-orders mode, recovery can call reconciliation.
- If recovery succeeds, the runtime status can return to running.

Limitations:

- recovery does not rebuild sessions for all broker positions/open orders;
- recovery does not dynamically add manual trades created while disconnected;
- recovery does not resume scanner replenishment because replenishment does not exist yet;
- recovery currently protects safety more than it restores the full desired trading model.

---

## 4. Requirement 1 audit: all TWS trades conducted in parallel/asynchronously

### 4.1 Desired behavior

SAILOR must manage all of these trade sources at the same time:

1. scanner-created SAILOR orders;
2. SAILOR orders already open in TWS before the current process started;
3. pre-existing manual TWS orders/positions before SAILOR started;
4. manual TWS orders/positions created after SAILOR started.

Each active trade must have:

- history request;
- candle building/merge;
- selected strategy profile;
- position-state tracking;
- exit decision loop;
- order/fill reconciliation;
- manual intervention detection;
- final close/stop state.

### 4.2 Current implementation status

Current implementation: **partial foundation only**.

Already present:

- strategy sessions per selected symbol;
- broker/local reconciliation before send-orders;
- order intent/receipt abstraction;
- ledger writing;
- CloseOnly safety mode;
- recovery attempt after degraded state.

Missing:

- no always-on TWS state mirror;
- no dynamic discovery of manual orders after start;
- no dynamic discovery of broker positions not in selected symbols;
- no per-symbol async conduct worker;
- no trade origin classification;
- no manual intervention policy;
- no order-state machine that owns open/submitted/partial-filled/cancelled/manual-filled states for every TWS order.

### 4.3 Required design

Add a new trade-management layer:

```text
BrokerStateMirror
  -> polls/subscribes TWS positions, open orders, order status, executions
  -> produces BrokerStateSnapshot every N seconds and event deltas

TradeRegistry
  -> persistent file-backed registry of all known trades
  -> assigns TradeLifecycleId
  -> classifies Origin
  -> tracks ScannerSlotId when scanner-owned

TradeSessionManager
  -> creates/resumes/stops one SymbolTradeSession per symbol/trade
  -> attaches strategy profile
  -> owns history refresh and candle merge for each active symbol

OrderSupervisor
  -> serializes broker order sends/cancels
  -> tracks open orders and fills
  -> reconciles broker truth against local state

StrategyConductWorkers
  -> one async worker per active symbol/trade
  -> evaluates exits/entries according to lifecycle policy
```

### 4.4 Proposed origin model

```csharp
public enum SailorTradeOrigin
{
    ScannerOwned,
    SailorPreExisting,
    ManualPreStart,
    ManualIntraday,
    UnknownBroker
}
```

Required rule:

```text
Manual trades are managed by strategy for exits, but do not count toward scanner minimum target.
```

### 4.5 Acceptance tests

| Test | Expected result |
|---|---|
| Start SAILOR with existing TWS manual long position in ABCD. | SAILOR detects ABCD, creates a managed manual trade session, builds candles, and conducts exits. |
| Start SAILOR, then manually buy XYZ in TWS. | SAILOR discovers XYZ within one broker-state refresh interval and starts strategy conduct. |
| Scanner opens 10 symbols; manual user opens 2 more. | Scanner target remains 10; manual 2 are managed separately and not counted. |
| TWS open order is partially filled. | Registry updates partial fill quantity and conducts only actual position size. |
| TWS order is cancelled manually. | Registry marks manual cancel and stops or reissues according to policy. |

---

## 5. Requirement 2a audit: V21/V22/V23/V24 multi-cycle entries before 15:45 and conduct/exit until 15:55

### 5.1 Current implementation foundation

The profiles are implemented as angle/EMA conduct strategies:

```text
src/Sailor.App/Backtest/Strategies/HarvesterConduct/V21_15Minutes/V21_15MinutesConductStrategy.cs
src/Sailor.App/Backtest/Strategies/HarvesterConduct/V22_15Minutes/V22_15MinutesConductStrategy.cs
src/Sailor.App/Backtest/Strategies/HarvesterConduct/V23_5Minutes/V23_5MinutesConductStrategy.cs
src/Sailor.App/Backtest/Strategies/HarvesterConduct/V24_5Minutes/V24_5MinutesConductStrategy.cs
```

They inherit:

```text
src/Sailor.App/Backtest/Strategies/HarvesterConduct/AngleEma/AngleEmaConductStrategyBase.cs
```

Current useful behavior:

- entry when flat;
- exit when opposite/neutral completed candle logic is triggered;
- re-entry possible after exit on a later signal candle;
- duplicate entry/exit signal on the same completed candle is blocked;
- force-flat uses runtime safety minute.

### 5.2 Correct universal market-close timing rule

Important correction: the current timing values are **not a defect** and must remain the baseline for every strategy, including V21/V22/V23/V24.

Required universal rule:

```text
LastEntryMinute = 945  -> 15:45 ET
ForceFlatMinute = 955  -> 15:55 ET
```

Meaning:

- **15:45 ET is the last allowed new-entry time** for all strategies.
- **15:45-15:55 ET is manage-only / exit-only time**.
- **15:55 ET is the force-flat protection time** for all strategies.
- No strategy, including V21-V24, should open new scanner entries after `LastEntryMinute=945`.
- The 5-minute-before-close rule is the force-flat / final cleanup protection at 15:55 ET, not permission to keep opening new positions until 15:55 ET.

Therefore the audit recommendation is corrected as follows:

```text
Do not change LastEntryMinute to 950 or 955.
Keep LastEntryMinute = 945 for all strategies.
Keep ForceFlatMinute = 955 for all strategies.
```

### 5.2.1 How this applies to V21/V22/V23/V24

V21/V22/V23/V24 should still support multiple entries and exits, but only inside the permitted entry window:

```text
09:30-15:45 ET:
  - scanner may create new strategy-owned entries;
  - V21/V22/V23/V24 may re-enter after a strategy exit;
  - scanner replenishment may restore the configured scanner target.

15:45-15:55 ET:
  - no new scanner entries;
  - no re-entry after an exit;
  - existing trades are still conducted by the strategy;
  - strategy exits, manual exits, protective exits, and force-flat preparation remain active.

15:55 ET and later:
  - force-flat rule has priority;
  - no new entries;
  - only close/flatten/reconcile behavior is allowed.
```

This gives V21-V24 their intended multi-cycle behavior without weakening the universal market-close safety window.

### 5.2.2 Priority rule when scanner target conflicts with market-close timing

If scanner minimum target is enabled, for example 10 scanner-owned trades, the target must be enforced only before `LastEntryMinute=945`.

```text
If activeScannerTrades < targetScannerTrades before 15:45 ET:
  run scanner replenishment every 5 minutes and open new scanner-owned slots if all safety gates pass.

If activeScannerTrades < targetScannerTrades at or after 15:45 ET:
  do not open replacement entries.
  continue conducting existing positions and exits only.
```

The market-close timing rule has higher priority than the minimum-order target.

### 5.2.3 Required documentation / implementation guard

Every future order-management implementation should treat these values as common runtime safety constants, not per-strategy tuning values:

```text
UniversalLastEntryMinute = 945
UniversalForceFlatMinute = 955
AppliesTo = all strategies, all scanner modes, all manual/pre-existing managed trades
```

Profile-specific lifecycle logic may decide **how often** a strategy can re-enter before 15:45, but it must not extend the final new-entry time beyond 15:45.

### 5.3 Gap: manual close stops trade for the rest of the day

Current code does not have a persisted daily manual-stop state.

Desired behavior:

```text
If SAILOR has an active managed trade in symbol ABCD and TWS/manual action closes it,
then ABCD becomes ManualStoppedForDay for that strategy/origin.
SAILOR must not re-open ABCD automatically for the rest of the day.
Exception: scanner can re-enter ABCD only through the explicit scanner replenishment/re-entry path.
```

Required persistent state:

```json
{
  "symbol": "ABCD",
  "tradeDate": "2026-06-30",
  "profile": "v21-15minutes",
  "state": "ManualStoppedForDay",
  "reason": "Broker position became flat without matching SAILOR exit order",
  "detectedUtc": "...",
  "canReenterOnlyIfScannerReSelects": true
}
```

### 5.4 Proposed lifecycle policy for V21-V24

```csharp
public enum StrategyLifecycleMode
{
    SingleLifecycleUntilStrategyExit,
    MultiCycleUntilLastEntryMinute,
    ManualManagedExitOnly
}
```

Policy table:

| Profile | Lifecycle policy | Entry rule | Exit rule | Manual close rule |
|---|---|---|---|---|
| `v21-15minutes` | `MultiCycleUntilLastEntryMinute` | Re-enter after strategy exit while scanner slot remains active and before 15:45 ET. | Strategy exit or force-flat at 15:55 ET. | Manual close -> stop for day unless scanner re-selects as new slot before 15:45 ET. |
| `v22-15minutes` | `MultiCycleUntilLastEntryMinute` | Same as V21. | Same as V21. | Same as V21. |
| `v23-5minutes` | `MultiCycleUntilLastEntryMinute` | Same as V21, but 5-minute signal cadence. | Same as V21. | Same as V21. |
| `v24-5minutes` | `MultiCycleUntilLastEntryMinute` | Same as V23. | Same as V23. | Same as V23. |

Lifecycle timing note: `MultiCycleUntilLastEntryMinute` means **multi-entry/multi-exit only until 15:45 ET**. From 15:45 to 15:55 ET, the session remains alive for strategy conduct and exits, but new entries and replenishment are blocked.

---

## 6. Requirement 2b audit: all other strategies conduct until strategy exit

### 6.1 Current behavior

For strategies outside V21/V22/V23/V24, SAILOR currently evaluates entry/exit through `SailorStrategyAdapter` and the conduct strategy/exit engine. This is enough for a selected runtime session.

### 6.2 Gap

The strategy cannot conduct a trade if no session was created for that symbol.

Therefore this requirement is only true for:

- scanner-selected active sessions;
- explicit runtime universe symbols;
- selected symbols with seeded broker/local position.

It is not true yet for:

- manual TWS trades created after start;
- pre-existing broker positions that were not selected by scanner;
- TWS open orders outside selected symbols.

### 6.3 Proposed lifecycle policy for all other strategies

| Strategy group | Proposed lifecycle |
|---|---|
| `v18-silver`, `v16-sqzbreakout`, `v13`, `v12`, `v10-hybrid`, `v17-hybridflow`, `v2-conduct`, `v1-first`, `v19-purplecloud`, `v15-shortcap`, `v14-smallcap`, `v20-gen001-choppyshield`, conduct-v3 aliases | `SingleLifecycleUntilStrategyExit` by default. |
| Manual trades under any strategy | `ManualManagedExitOnly` unless operator allows SAILOR to add/re-enter. |
| Scanner-owned trades under non-V21/V22/V23/V24 | Enter once, conduct until exit, then release slot for scanner replenishment. |

Recommended rule:

```text
For non-V21/V22/V23/V24 scanner-owned trades:
  after strategy exit, the specific trade lifecycle is complete;
  the scanner slot can be refilled by a new symbol on the next 5-minute replenishment cycle;
  the same symbol may be re-entered only if the scanner selects it again as a new lifecycle.
```

---

## 7. Requirement 3 audit: minimum scanner orders/trades and 5-minute replenishment

### 7.1 Desired behavior

Example:

```text
minimum scanner orders/trades target = 10
```

If scanner-owned active trades drop from 10 to 7, SAILOR must:

```text
on next 5-minute replenishment cycle:
  find 3 new scanner-qualified symbols
  create 3 new scanner-owned trade lifecycles
  emit new orders if all gates pass
```

Manual trades are separate:

```text
scannerOwnedActive = 7
manualManagedActive = 2
requiredScannerNew = 10 - 7 = 3
manualManagedActive does not reduce requiredScannerNew
```

### 7.2 Current implementation status

Current implementation: **not implemented**.

Current behavior:

- scanner selection is performed before conduct;
- selected symbols are fixed for the conduct loop;
- no 5-minute intraday scanner cycle runs inside conduct;
- no scanner-owned slot registry exists;
- no minimum scanner target is enforced;
- default `Runtime.Safety.MaxActiveSymbols=3` conflicts with a target of 10;
- manual trades are not classified separately from scanner trades.

### 7.3 Proposed scanner slot manager

Add:

```text
src/Sailor.App/Runtime/TradeManagement/ScannerSlotManager.cs
```

Responsibilities:

```text
- maintain scanner target count, e.g. 10;
- count only active ScannerOwned trade lifecycles;
- ignore ManualPreStart and ManualIntraday for the scanner target;
- every 5 minutes, run points scanner and choose new symbols;
- skip symbols already active, manually stopped for day, blocked, or not data-clean;
- create new trade lifecycles for missing slots;
- hand them to TradeSessionManager for history/candles/conduct;
- write scanner slot evidence.
```

Suggested settings:

```json
"TradeManagement": {
  "EnableScannerReplenishment": false,
  "ScannerTargetActiveTrades": 10,
  "ScannerReplenishmentSeconds": 300,
  "ManualTradesCountTowardScannerTarget": false,
  "AllowSameSymbolReentryAfterScannerReselect": true,
  "ManualCloseBlocksSymbolForDay": true
}
```

### 7.4 Required conflict checks

Before enabling minimum target 10:

| Setting | Current/default | Required for target 10 |
|---|---:|---:|
| `Runtime.Safety.MaxActiveSymbols` | 3 | >= 10 for paper; carefully gated for live |
| `Runtime.Live.MaxConcurrentPositions` | 1 | live should remain 1 until multi-symbol live engine is implemented |
| `Runtime.Live.MaxTotalPilotNotional` | 100 | must match risk design before live multi-symbol |
| `Scanner.TargetActiveTrades` | not implemented | 10 |
| `ScannerReplenishmentSeconds` | not implemented | 300 |

### 7.5 Acceptance tests

| Test | Expected result |
|---|---|
| Scanner target 10, currently 0. | First cycle opens/activates up to 10 scanner-owned lifecycles, subject to safety gates. |
| Three scanner trades exit by strategy. | Next 5-minute cycle selects 3 new symbols. |
| User manually opens 2 TWS trades. | They are managed but scanner still targets 10 scanner-owned trades. |
| User manually closes one scanner trade. | Symbol marked manual-stopped for day; scanner target is now short by 1; next cycle selects a replacement unless scanner explicitly reselects same symbol as allowed exception. |
| Scanner list has only 7 valid candidates. | SAILOR opens 7 and records `targetShortfall=3` without forcing bad trades. |

---

## 8. Requirement 4 audit: severe disconnection recovery

### 8.1 Current implementation status

Current implementation: **partial safety foundation**.

Already present:

- incident reporting;
- health monitor;
- CloseOnly degraded state;
- reconciliation delegate;
- reconnect attempt in send-orders mode;
- exits remain allowed when safety permits.

Missing for the desired model:

- no full broker-state reconstruction after reconnect;
- no dynamic session creation for all broker positions/open orders discovered after reconnect;
- no history refresh for every discovered active symbol;
- no candle rebuild for every discovered active symbol;
- no scanner target recalculation after reconnect;
- no manual trade reconciliation after disconnected period.

### 8.2 Required recovery algorithm

After severe disconnection:

```text
1. Move runtime to CloseOnly immediately.
2. Stop new scanner entries while disconnected.
3. Keep existing local sessions alive but do not trust their broker state.
4. Reconnect to TWS.
5. Request complete broker truth:
   - positions;
   - open orders;
   - order statuses;
   - executions/fills since last known execution time;
   - account status.
6. Merge broker truth into TradeRegistry.
7. Detect manual changes during disconnect.
8. For every active broker position/open order:
   - create/resume SymbolTradeSession;
   - request/refresh history;
   - rebuild merged candles;
   - rebuild indicators;
   - set position context from broker truth.
9. Prioritize exits/flatten decisions before new entries.
10. If reconciliation is clean and all active sessions have candles, leave CloseOnly.
11. Resume scanner replenishment only after clean state.
12. Write recovery evidence report.
```

### 8.3 Acceptance tests

| Test | Expected result |
|---|---|
| Disconnect while 10 scanner trades are active. | Runtime goes CloseOnly; after reconnect all 10 broker positions are rebuilt as sessions. |
| Manual close happens during disconnect. | On reconnect, registry detects manual close and applies stop-for-day policy. |
| Manual open happens during disconnect. | On reconnect, registry creates manual-managed session and conducts exits. |
| Scanner target 10 but only 7 scanner-owned positions remain after reconnect. | After clean recovery, scanner replenishment creates 3 replacement slots. |
| Open order exists with no local matching intent. | Registry creates `UnknownBroker` or `ManualIntraday` lifecycle and blocks unsafe duplicate entry. |

---

## 9. Detailed proposed implementation roadmap

### SAILOR-050 — Order/trade management audit

Deliverable: this document.

No source code change.

### SAILOR-051 — Trade lifecycle registry and ownership model

Add persistent registry under file storage, not a database:

```text
state/{paper|live}/trades/trade_registry_latest.json
state/{paper|live}/trades/trade_registry_yyyyMMdd.jsonl
```

Core models:

```csharp
public sealed record TradeLifecycle(
    string TradeId,
    string Symbol,
    string ProfileName,
    SailorTradeOrigin Origin,
    string? ScannerSlotId,
    TradeLifecycleStatus Status,
    int BrokerQuantity,
    decimal BrokerAveragePrice,
    bool ManualStoppedForDay,
    DateOnly TradeDate,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
```

Statuses:

```text
PendingEntry
EntrySubmitted
Open
ExitSubmitted
ClosedByStrategy
ClosedManually
StoppedForDay
Recovered
UnknownBroker
Error
```

Implementation steps:

1. Create models.
2. Create registry store.
3. Write every strategy entry/exit receipt into registry.
4. Add origin classification for scanner-created and explicit runtime-created orders.
5. Add report command: `paper trades status`.

### SAILOR-052 — Broker state mirror and manual trade detector

Add broker mirror:

```text
src/Sailor.App/Runtime/TradeManagement/BrokerStateMirror.cs
```

Implementation steps:

1. Poll/reconcile broker positions and open orders every configurable interval.
2. Create a normalized `BrokerStateSnapshot`.
3. Compare snapshot against `TradeRegistry`.
4. Detect:
   - manual open;
   - manual close;
   - manual quantity increase/decrease;
   - unknown open order;
   - cancelled order;
   - partial fill.
5. Emit events to `TradeSessionManager`.

Suggested settings:

```json
"BrokerStateMirror": {
  "Enabled": true,
  "PollSeconds": 5,
  "ExecutionLookbackMinutes": 120,
  "AttachManualTrades": true,
  "ManualCloseBlocksForDay": true
}
```

### SAILOR-053 — Dynamic trade session manager

Add:

```text
TradeSessionManager
SymbolTradeSession
StrategyWorker
OrderSupervisor
```

Implementation steps:

1. Replace fixed `IReadOnlyList<PaperSymbolSession>` with dynamic session collection.
2. Allow sessions to be added/removed during runtime.
3. Preserve one strategy instance per active lifecycle/symbol.
4. Add history/candle refresh per session.
5. Run conduct workers asynchronously per symbol.
6. Use one serialized order-submission queue to avoid IBKR socket/order-id race conditions.

Suggested design:

```text
SymbolTradeSession worker:
  - owns symbol/profile/lifecycle
  - consumes broker-state events and candle events
  - evaluates strategy
  - emits order intents to OrderSupervisor

OrderSupervisor:
  - single queue to IBKR router
  - writes ledger and registry
  - handles submit/cancel/flatten result
```

### SAILOR-054 — Strategy lifecycle policies

Add policy map:

```json
"StrategyLifecyclePolicies": {
  "v21-15minutes": "MultiCycleUntilLastEntryMinute",
  "v22-15minutes": "MultiCycleUntilLastEntryMinute",
  "v23-5minutes": "MultiCycleUntilLastEntryMinute",
  "v24-5minutes": "MultiCycleUntilLastEntryMinute",
  "default": "SingleLifecycleUntilStrategyExit"
}
```

Implementation steps:

1. Add lifecycle enum.
2. Add policy resolver by profile name.
3. For V21-V24, allow re-entry after strategy exit only while scanner slot remains active and only before the universal `LastEntryMinute=945`.
4. For all others, close lifecycle after strategy exit, also respecting `LastEntryMinute=945` for any new entry.
5. Add manual-close stop-for-day policy.
6. Add scanner-reselection exception.

### SAILOR-055 — Scanner slot target and 5-minute replenishment

Implementation steps:

1. Add `ScannerSlotManager`.
2. Add settings for target, replenish interval, allow weak entry, avoid same-day stopped symbols.
3. Run scanner every 300 seconds while runtime is clean and current ET minute is before `LastEntryMinute=945`.
4. Count only `Origin=ScannerOwned` active lifecycles.
5. Create new scanner slots when below target, but never at or after 15:45 ET.
6. Do not count manual trades.
7. Do not open entries when broker state is degraded, reconciliation is stale, or the market-close last-entry window has passed.
8. From 15:45 to 15:55 ET, keep all active sessions managed for exits only.
9. Write slot report.

Slot report fields:

```text
targetScannerTrades
activeScannerTrades
manualManagedTrades
shortfall
newSlotsRequested
newSlotsCreated
blockedSymbols
reason
```

### SAILOR-056 — Severe disconnect recovery orchestrator

Implementation steps:

1. Detect severe disconnect.
2. Move to CloseOnly.
3. Reconnect.
4. Build broker truth snapshot.
5. Merge with registry.
6. Create/resume sessions for every broker position and open order.
7. Refresh history and candles for every session.
8. Prioritize exits.
9. Re-enable entries only after clean reconciliation and only before `LastEntryMinute=945`.
10. Resume scanner replenishment only before 15:45 ET; after 15:45 ET resume exits/flatten/reconcile only.
11. Write recovery report.

### SAILOR-057 — Order/trade management self-tests

Add commands:

```powershell
paper trade-management-test --scenario preexisting-position
paper trade-management-test --scenario manual-open-after-start
paper trade-management-test --scenario manual-close-stop-day
paper trade-management-test --scenario scanner-target-10-replenish
paper trade-management-test --scenario severe-disconnect-recovery
paper trade-management-test --scenario v21-multi-entry-until-close
paper trade-management-test --scenario non-v21-single-lifecycle
paper trade-management-test --scenario last-entry-945-blocks-replenishment
paper trade-management-test --scenario force-flat-955-all-strategies
```

Tests should not send broker orders unless `--send-orders` is explicitly supplied. Default should be simulation/dry-run.

Additional market-close acceptance criteria:

| Test | Expected result |
|---|---|
| V21/V22/V23/V24 has an open scanner slot and strategy exits at 15:44 ET. | Re-entry may be allowed if scanner slot remains active and all gates pass. |
| V21/V22/V23/V24 has an open scanner slot and strategy exits at 15:45 ET or later. | No re-entry. Session remains managed for exits/reconcile only. |
| Scanner target is 10 but active scanner trades drop to 7 at 15:46 ET. | No 3 replacement entries are opened. Market-close last-entry rule wins. |
| Any strategy has an open position at 15:55 ET. | Force-flat/close-only behavior starts according to existing force-flat rule. |
| Severe disconnect recovers at 15:47 ET. | Rebuild sessions and manage/exit positions, but do not create new scanner entries. |

---

## 10. Proposed target architecture

```text
+-------------------+       +---------------------+
| Points Scanner    |       | TWS Broker Mirror   |
| every 5 minutes   |       | positions/orders    |
+---------+---------+       +----------+----------+
          |                            |
          v                            v
+-------------------------------------------------+
| Trade Registry                                  |
| - scanner slots                                 |
| - manual trades                                 |
| - trade lifecycle state                         |
| - manual stop-for-day state                     |
+-----------------------+-------------------------+
                        |
                        v
+-------------------------------------------------+
| Trade Session Manager                           |
| - create/resume/stop sessions dynamically       |
| - history request per active symbol             |
| - candle merge per active symbol                |
| - strategy policy per profile                   |
+-----------------------+-------------------------+
                        |
                        v
+-------------------+       +---------------------+
| Strategy Workers  | ----> | Order Supervisor    |
| async per symbol  |       | serialized IBKR I/O |
+-------------------+       +----------+----------+
                                      |
                                      v
                              +---------------+
                              | TWS / IBKR    |
                              +---------------+
```

---

## 11. Today-before-market-open checklist

This checklist is for safe observation only.

### 11.1 Safe scanner check

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

### 11.2 Reconciliation check

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DUN559573 --wait-seconds 15
```

Expected before any paper send-orders test:

```text
status=Matched
canOpenEntries=True
brokerPositions=0 or expected known positions
openOrders=0 or expected known orders
```

### 11.3 Dry-run conduct check

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --dry-run --iterations 300 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --scanner-mode points-only --points-min-trade-score 45
```

### 11.4 Disconnect simulation check

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 3 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --dry-run --iterations 60 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --scanner-mode points-only --simulate-disconnect-at 10
```

Expected today:

```text
runtime moves CloseOnly or logs degraded state
no broker orders are sent in dry-run
```

Not expected today:

```text
full rebuild of all TWS manual/pre-existing trades after disconnect
5-minute scanner replenishment
manual close stop-for-day
```

---

## 12. Go / no-go guidance for current code

### OK today

```text
paper scan-list / paper scan-points
paper run --dry-run
paper reconcile
paper report latest
small controlled paper send-orders only after clean reconciliation and while operator watches TWS
```

### Not OK yet for the desired target model

```text
unattended paper/live trading
assuming manual TWS trades are automatically managed
assuming pre-existing TWS positions outside selected symbols are automatically managed
assuming 10 scanner trades are preserved/replenished every 5 minutes
assuming recovery rebuilds all trade sessions after severe disconnect
multi-symbol live send-orders beyond the existing live pilot gate
```

---

## 13. Final recommendation

The next implementation should not be another scanner change. The scanner is now strong enough for selection/testing. The next work should be the **trade-management foundation**:

```text
SAILOR-051 — Trade lifecycle registry and ownership model
SAILOR-052 — Broker state mirror and manual trade detector
SAILOR-053 — Dynamic async trade session manager
SAILOR-054 — Strategy lifecycle policies
SAILOR-055 — Scanner minimum-target replenishment
SAILOR-056 — Severe disconnect recovery orchestrator
SAILOR-057 — Trade-management self-tests
```

The highest priority is SAILOR-051 + SAILOR-052. Without trade ownership and broker-state truth, SAILOR cannot safely decide whether a trade is scanner-owned, manual, pre-existing, stopped for day, or eligible for scanner re-entry.

---

## 12. SAILOR-051 implementation update — trade lifecycle registry and ownership model

Date: 2026-06-30  
Status: implemented as the first source milestone after this audit.

SAILOR-051 implements the persistent registry foundation proposed in section 9.

### 12.1 Implemented now

New persistent files:

```text
state/{paper|live}/trades/trade_registry_latest.json
state/{paper|live}/trades/trade_registry_yyyyMMdd.jsonl
```

New source namespace:

```text
Sailor.App.Runtime.TradeManagement
```

New model:

```text
TradeLifecycle
TradeLifecycleStatus
SailorTradeOrigin
TradeLifecycleRegistrySnapshot
TradeLifecycleRegistryStore
TradeLifecycleEvent
```

New command:

```powershell
sailor paper trades status
sailor paper trades status --all --symbol TSLA
```

### 12.2 Ownership model now available

SAILOR can now persist the ownership type of a lifecycle:

```text
ScannerOwned        -> selected by scanner; future scanner target accounting uses only this origin
SailorPreExisting   -> selected session had an existing broker/local position seed
ExplicitRuntime     -> explicit/fallback runtime symbol
SailorManualCommand -> created by sailor order command
UnknownBroker       -> broker-sourced lifecycle not yet classified by the broker mirror
ManualPreStart      -> reserved for SAILOR-052
ManualIntraday      -> reserved for SAILOR-052
```

### 12.3 Runtime integration now available

During `paper run` / shared host activation, active sessions are registered before the conduct loop starts.

During the conduct loop, every routed intent/receipt updates the registry after the local session position update.

The manual order command path also writes registry evidence.

### 12.4 Still not implemented in SAILOR-051

SAILOR-051 does **not** yet implement:

```text
- continuous TWS broker-state mirror;
- manual order/position detection after runtime start;
- asynchronous per-symbol trade workers;
- manual close = stop-for-day policy;
- scanner target 10 replenishment every 5 minutes;
- severe-disconnect full trade rebuild.
```

Those remain correctly assigned to SAILOR-052 through SAILOR-057.
