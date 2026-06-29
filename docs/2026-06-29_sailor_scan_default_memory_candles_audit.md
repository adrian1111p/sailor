# 2026-06-29 Sailor Scan-Default Memory Candles Audit and Implementation Plan

## 1. Purpose

This audit defines a new Sailor module for trading from an operator-maintained scan list, such as:

```text
scan/data/scan_default.xlsx
```

The module should support a workflow where Sailor starts with a larger Excel symbol list, builds real-time in-memory 1-minute candles immediately for all configured symbols, requests broker historical bars in controlled batches, merges broker history with the live in-memory candles, ranks the best symbols with the existing Sailor scanner, and then trades the selected top symbols in backtest, paper, and eventually live mode.

The requested behavior is:

1. Use the implemented Sailor scanner to choose the best `5` or `10` symbols from the Excel list and trade those selected symbols.
2. If the Excel list contains more than `45` symbols, request historical data in batches of `45` symbols every `10` minutes and keep the state in memory.
3. From startup, build real-time in-memory `1m` candles for the Excel symbols, or the same timeframe candles requested by history.
4. When history arrives for a symbol, merge the broker historical candles with the real-time candles already built in memory.
5. Add commands for backtest, paper, and live that use this option.
6. Provide a detailed implementation plan.

This is an audit and implementation plan only. It does not yet implement the module.

---

## 2. Uploaded Excel analysis

The uploaded `scan_default.xlsx` has two sheets:

| Sheet | Observed shape | Meaning for Sailor |
|---|---:|---|
| `Candidates` | 131 rows, one symbol column | Recommended default input sheet. |
| `Sheet1` | 232 rows, one symbol column | Raw/fallback symbol sheet. |

The first rows of `Candidates` are examples such as:

```text
AMSS, SNGX, LGHL, FGL, UZX, IPWR, NCPL, NNVC, DY, APPS
```

The first rows of `Sheet1` are examples such as:

```text
AUUD, PAPL, QS, AKAN, TRT, YCBD, TZOO, MWYN, HELE, BURU
```

Recommended first implementation decision:

```text
Use sheet `Candidates` by default.
Allow override with --scan-sheet Sheet1.
Read the first non-empty column unless --symbol-column is supplied.
```

Recommended repository location:

```text
scan/data/scan_default.xlsx
```

The file should be treated as operator input. It can be committed only if it is a generic/default list. Daily/private scan exports should normally be ignored or placed under a generated/local folder.

---

## 3. Current Sailor baseline relevant to this module

Current Sailor already has the important pieces needed for the new module:

| Existing Sailor area | Current capability | How the new module should use it |
|---|---|---|
| `Scanner/Universe` | Built-in and file-based symbol universe providers. | Add an Excel workbook universe provider. |
| `Scanner/Runtime/PaperScannerRunner` | Resolves universe, prepares history, ranks symbols, optionally captures snapshots. | Extend or wrap it with dynamic scan-list memory behavior. |
| `MarketData/History` | IBKR and local CSV historical bar providers. | Use for batched historical `1m` requests. |
| `MarketData/Live` | L1/L2 snapshot provider and snapshot store. | Extend to streaming/incremental events for candle building. |
| `Runtime/Paper/PaperConductLoop` | Runs active symbol sessions, creates strategy frames, routes intents. | Feed selected top symbols from dynamic scan memory. |
| `Runtime/Live/LiveReadinessGate` | Blocks unsafe live trading. | Keep live dynamic scan read-only until paper certification passes. |
| `Reporting` | Paper certification, live readiness, live pilot reports. | Add scan-list data quality evidence. |

Important current limitation:

```text
The existing scanner prepares a fixed list at startup. It does not yet rotate large scan lists in history batches and it does not yet build continuous in-memory candles for all Excel symbols.
```

---

## 4. Proposed new module name and folder layout

Recommended module name:

```text
ScanListRuntime
```

Recommended folders:

```text
scan/
└── data/
    └── scan_default.xlsx

src/Sailor.App/
├── Scanner/
│   ├── ScanList/
│   │   ├── ScanListWorkbookReader.cs
│   │   ├── ScanListWorkbookOptions.cs
│   │   ├── ScanListSymbolSource.cs
│   │   ├── ScanListSymbolState.cs
│   │   ├── ScanListMemoryStore.cs
│   │   ├── ScanListSelection.cs
│   │   ├── ScanListSelectionStore.cs
│   │   ├── ScanListHistoryBatchScheduler.cs
│   │   ├── ScanListRuntime.cs
│   │   ├── ScanListRunRequest.cs
│   │   ├── ScanListRunResult.cs
│   │   └── ScanListReportWriter.cs
│   └── Universe/
│       └── XlsxUniverseProvider.cs
├── MarketData/
│   ├── Aggregation/
│   │   ├── RealtimeCandleAccumulator.cs
│   │   ├── InMemoryCandleSeries.cs
│   │   ├── InMemoryCandleStore.cs
│   │   ├── HistoricalRealtimeBarMerger.cs
│   │   └── CandleQuality.cs
│   └── Live/
│       ├── IRealtimeMarketEventSource.cs
│       ├── RealtimeMarketEvent.cs
│       └── IbkrRealtimeMarketEventSource.cs
└── Runtime/
    ├── Common/
    │   └── ScanListRuntimeOptions.cs
    ├── Paper/
    │   └── PaperDynamicScanConductHost.cs
    └── Live/
        └── LiveDynamicScanPilotHost.cs
```

The first implementation can be smaller than the final folder list. The important architectural boundary is:

```text
Excel symbol source -> in-memory candle store -> historical batch merger -> scanner ranker -> selected active symbols -> conduct loop
```

---

## 5. Target runtime flow

### 5.1 Startup flow

```text
1. Load scan/data/scan_default.xlsx.
2. Read symbols from configured sheet/column.
3. Normalize symbols: trim, uppercase, remove duplicates, validate ticker characters.
4. Create ScanListMemoryStore with one ScanListSymbolState per symbol.
5. Start real-time market event collection for all scan-list symbols where allowed.
6. Start 1-minute candle aggregation immediately for all symbols.
7. Start historical request scheduler.
8. Request historical bars for first batch of up to 45 symbols.
9. Continue requesting the next batch every 10 minutes until all symbols have history.
10. Every scanner interval, merge available historical + in-memory bars.
11. Rank ready symbols with the existing Sailor scanner/profile.
12. Select top N, for example top 5 or top 10.
13. Feed selected symbols into the conduct loop.
14. Continue updating candles and selection memory.
```

### 5.2 Large list batching rule

For a list with 131 symbols from `Candidates`:

```text
T+00 min: request history for symbols 001-045
T+10 min: request history for symbols 046-090
T+20 min: request history for symbols 091-131
T+30 min: next refresh cycle can start, depending on --history-refresh-mode
```

For a list with 232 symbols from `Sheet1`:

```text
T+00 min: request history for symbols 001-045
T+10 min: request history for symbols 046-090
T+20 min: request history for symbols 091-135
T+30 min: request history for symbols 136-180
T+40 min: request history for symbols 181-225
T+50 min: request history for symbols 226-232
```

Default options:

```text
--history-batch-size 45
--history-batch-interval-minutes 10
```

Why this matters:

```text
The app must avoid requesting historical bars for 131-232 symbols at once. It should stay broker-friendly and deterministic.
```

---

## 6. In-memory candle model

### 6.1 Candle timeframe

Default:

```text
1m
```

But the module should be generic enough to support the runtime timeframe:

```text
--timeframe 1m
--timeframe 5m later
--timeframe 15m later
```

First milestone recommendation:

```text
Implement only 1m aggregation.
Allow higher timeframe candles later by rolling up from 1m.
```

### 6.2 Candle source types

Each candle should carry a quality/source flag:

| Source | Meaning | Priority in merge |
|---|---|---:|
| `HistoricalBroker` | Complete broker historical bar. | Highest for closed minutes. |
| `RealtimeTrade` | Built from real trade ticks. | High for current/new minutes. |
| `RealtimeQuoteMid` | Built from bid/ask midpoint when trade price is unavailable. | Medium; mark synthetic. |
| `RealtimeLastPrice` | Built from last price updates. | Medium/high depending on IBKR field quality. |
| `SyntheticSnapshot` | Built from periodic snapshot only. | Lowest; usable for smoke tests, not certification. |

### 6.3 In-memory candle structure

Recommended model:

```csharp
public sealed record InMemoryCandle(
    string Symbol,
    string Timeframe,
    DateTimeOffset StartUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    CandleQuality Quality,
    bool IsClosed,
    DateTimeOffset UpdatedUtc);
```

Recommended per-symbol state:

```csharp
public sealed class ScanListSymbolState
{
    public string Symbol { get; init; }
    public bool HistoryRequested { get; set; }
    public bool HistoryLoaded { get; set; }
    public DateTimeOffset? LastHistoryRequestUtc { get; set; }
    public DateTimeOffset? LastHistoryBarUtc { get; set; }
    public DateTimeOffset? LastRealtimeEventUtc { get; set; }
    public InMemoryCandleSeries RealtimeCandles { get; }
    public IReadOnlyList<MarketBar> HistoricalBars { get; set; }
    public IReadOnlyList<MarketBar> MergedBars { get; set; }
    public ScannerCandidate? LastCandidate { get; set; }
    public string DataStatus { get; set; }
}
```

### 6.4 Memory retention

Recommended default retention:

```text
Keep current trading day 1m candles in memory.
Optionally keep last 390-800 1m bars per symbol.
Do not persist the complete in-memory candle stream by default.
```

Optional debug snapshot:

```text
state/{mode}/scan-memory-latest.json
logs/{Mode}/ScanList/scan_memory_YYYYMMDD_HHMMSS.json
```

These files must be ignored by Git.

---

## 7. Historical + real-time merge rules

The merge must be deterministic and safe because strategy decisions depend on the final bar sequence.

### 7.1 Core merge algorithm

For each symbol:

```text
1. Normalize all bars to the same timeframe and UTC minute boundary.
2. Add broker historical bars first.
3. Add closed real-time candles whose start time is after the last historical bar.
4. For overlapping closed minutes:
   - prefer HistoricalBroker if it exists,
   - keep the real-time candle only as debug/quality evidence.
5. Keep the current open real-time minute outside the strategy input unless the strategy explicitly allows forming candles.
6. Sort ascending by time.
7. Remove duplicates by Symbol + Timeframe + StartUtc.
8. Validate no negative OHLC, no invalid high/low relation, and no backwards timestamps.
```

### 7.2 Why broker history should win on overlap

Broker historical bars are normally complete bars. Real-time in-memory candles can be partial or synthetic, especially if they are built from snapshots instead of trade ticks. Therefore:

```text
HistoricalBroker closed bar > Realtime closed bar > Realtime open bar.
```

### 7.3 Gap handling

If history arrives late and there is a gap between broker history and real-time candles:

```text
- keep the gap visible in DataStatus,
- allow scanner ranking only if minimum bar coverage is met,
- block trading on symbols with critical data gaps,
- write gap evidence to ScanList report.
```

Recommended minimum data requirement before trading:

```text
At least profile warmup bars + current session bars are available.
No critical gap in the last 30 minutes.
Latest merged closed candle is not older than 2 minutes during market hours.
```

---

## 8. Scanner selection memory

The module should keep selected candidates in memory so the conduct loop is not constantly changing active symbols every second.

Recommended behavior:

```text
- scan/rank every 60 seconds by default,
- keep selected top N in ScanListSelectionStore,
- only rotate active traded symbols every 5 or 10 minutes,
- do not drop a symbol that currently has an open position,
- allow exits/flatten for symbols removed from top N,
- block new entries for symbols no longer selected.
```

Recommended selection model:

```csharp
public sealed record ScanListSelection(
    string Symbol,
    int Rank,
    decimal Score,
    string ProfileName,
    DateTimeOffset SelectedUtc,
    string DataStatus,
    bool HasOpenPosition,
    bool CanOpenNewEntry,
    string Reason);
```

Important rule:

```text
Scanner selection controls entry eligibility only. It must never prevent exit or flatten of an existing position.
```

---

## 9. Backtest command design

Backtest mode should support the Excel file without broker calls. It should read symbols from the workbook and run the existing CSV/history backtest/ranking path.

### 9.1 Inspect Excel list

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Candidates
```

Expected output:

```text
scan-list file=scan/data/scan_default.xlsx sheet=Candidates symbols=131 unique=131
```

### 9.2 Backtest ranking from Excel list

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache
```

Expected behavior:

```text
- read symbols from Excel,
- use local CSV/backtest data,
- rank the best 10 symbols,
- write report under logs/Backtest/ScanList.
```

### 9.3 Backtest dynamic scan simulation

This command simulates the paper/live dynamic behavior using local data:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest scan-list-run 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --history-batch-size 45 --history-batch-interval-minutes 10 --selection-refresh-seconds 60
```

Expected behavior:

```text
- simulate history batch availability,
- simulate in-memory candle build from local historical bars,
- merge bars,
- rank top 10,
- run conduct/backtest on selected symbols,
- write a report showing when each symbol became data-ready.
```

---

## 10. Paper command design

Paper mode is the first real target for this module.

### 10.1 Paper read-only scan-list smoke test

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-quotes --iterations 3
```

Expected behavior:

```text
No broker calls.
Read Excel symbols.
Build local/simulated memory.
Rank top 10.
No orders sent.
```

### 10.2 Paper scan-list with broker history batches

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DU123456 --history-batch-size 45 --history-batch-interval-minutes 10 --wait-seconds 15 --market-data-type 2
```

Expected behavior:

```text
- request broker history for up to 45 symbols immediately,
- build real-time in-memory candles for all symbols where market data is enabled,
- after 10 minutes request next 45 symbols,
- merge history as it arrives,
- rank top 10 from ready symbols,
- write scan-list report,
- send no orders.
```

### 10.3 Paper dynamic conduct loop dry-run

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1
```

Expected behavior:

```text
- select top 10 from the scan list,
- create symbol sessions for selected symbols,
- run conduct loop,
- create dry-run order intents if strategy triggers,
- assume fills locally only in dry-run mode,
- write paper runtime evidence.
```

### 10.4 Paper dynamic conduct loop with broker orders

Before running:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DU123456 --wait-seconds 15
```

Then:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 5 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --send-orders --account DU123456 --history-batch-size 45 --history-batch-interval-minutes 10 --selection-refresh-seconds 60 --wait-seconds 15
```

Safety expectations:

```text
- entries are blocked if paper reconciliation is not Matched,
- entries are blocked for symbols not selected in current top N,
- exits remain allowed for positions that fall out of top N,
- history/market data failures move affected symbols to data-degraded,
- runtime degraded state moves global runtime to CloseOnly.
```

### 10.5 Paper certification report extension

The existing command should include scan-list evidence after implementation:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

Add evidence fields:

```text
scanListFile
scanListSheet
scanListSymbols
historyBatchSize
historyBatchIntervalMinutes
symbolsHistoryRequested
symbolsHistoryMerged
symbolsRealtimeCandlesBuilt
selectedSymbols
selectionRefreshCount
dataQualityWarnings
```

---

## 11. Live command design

Live must remain conservative.

### 11.1 Live read-only scan-list

This should be the first live feature for the module:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth
```

Broker-enabled read-only live scan:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DU123456 --history-batch-size 45 --history-batch-interval-minutes 10 --market-data-type 2 --wait-seconds 15
```

Expected behavior:

```text
Read-only only.
No order router is created.
No live order is sent.
Rank best 10 for observation.
Write logs/Live/ScanList evidence.
```

### 11.2 Live dynamic pilot with scan-list selection

Current SAILOR-034 live pilot is one explicit symbol only. Therefore, the first live implementation should keep trading limited to one selected symbol:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run 1m v21-15minutes 1 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DU123456 --max-notional 100 --confirm-live --operator-watching-tws --wait-seconds 15 --iterations 5
```

Expected behavior:

```text
- rank the scan list,
- select best 1 symbol,
- run SAILOR-034 live pilot restrictions,
- require live-readiness gate to pass,
- require pre-run live reconciliation to pass,
- route no order unless all gates pass.
```

### 11.3 Future multi-symbol live pilot

Only after paper certification proves the top 5/top 10 flow:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run 1m v21-15minutes 5 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DU123456 --max-notional 100 --confirm-live --operator-watching-tws --allow-multi-symbol-live-pilot --wait-seconds 15
```

This should remain blocked until a later milestone adds:

```text
Runtime.Live.AllowMultiSymbolPilot=true
per-symbol max notional
total pilot max notional
max concurrent open positions
operator multi-symbol confirmation
```

Recommended first live rule:

```text
Live can scan top 5/top 10, but live trading starts with best 1 only.
```

---

## 12. New command option reference

Recommended new options:

| Option | Meaning | Default |
|---|---|---:|
| `--scan-file PATH` / `--file PATH` | Excel scan-list file. | `scan/data/scan_default.xlsx` |
| `--scan-sheet NAME` / `--sheet NAME` | Excel sheet to read. | `Candidates` |
| `--symbol-column NAME_OR_INDEX` | Symbol column. First implementation can support `A` or `1`. | `A` |
| `--history-batch-size N` | Number of symbols per historical request batch. | `45` |
| `--history-batch-interval-minutes N` | Delay between history batches. | `10` |
| `--selection-refresh-seconds N` | How often to rerank and update selected symbols. | `60` |
| `--trade-top N` | Number of selected symbols eligible for entries. Same as positional `top` if omitted. | positional `top` |
| `--memory-candles` | Enable in-memory candle aggregation. | enabled for scan-list runtime |
| `--no-memory-candles` | Disable real-time aggregation; history only. | false |
| `--merge-history-realtime` | Merge broker history with memory candles. | true |
| `--no-merge-history-realtime` | Debug option to disable merge. | false |
| `--min-ready-bars N` | Minimum merged bars required before scanner ranks a symbol. | profile warmup |
| `--max-data-age-seconds N` | Maximum age of latest merged candle during market hours. | `120` |
| `--l2-selected-only` | Request L2 only for selected top N symbols. | true |
| `--quotes-all-symbols` | Request L1 quotes for all Excel symbols. Use carefully. | false in live |
| `--persist-scan-memory` | Write debug memory snapshot to state/logs. | false |

---

## 13. Detailed step-by-step implementation plan

### Step 1 — Add scan-list input files and Git rules

Files:

```text
scan/data/.gitkeep
scan/data/scan_default.xlsx optional
```

Rules:

```text
- Commit scan_default.xlsx only if it is a generic default list.
- Ignore daily/private scan exports if needed, for example scan/data/local/*.xlsx.
```

Add to `.gitignore` if daily files are used:

```gitignore
scan/data/local/
```

Acceptance:

```powershell
git status
```

No generated state/log files should be staged.

### Step 2 — Implement workbook reader without Excel Interop

Create:

```text
src/Sailor.App/Scanner/ScanList/ScanListWorkbookReader.cs
src/Sailor.App/Scanner/ScanList/ScanListWorkbookOptions.cs
```

Implementation notes:

```text
- Do not use Microsoft Excel Interop.
- Either add a small OpenXML reader or a minimal .xlsx ZIP/XML parser.
- For the first milestone, support one column of symbols.
- Read shared strings from xl/sharedStrings.xml.
- Resolve sheet relationship from xl/workbook.xml and xl/_rels/workbook.xml.rels.
- Read xl/worksheets/sheetN.xml.
- Extract configured column A cells.
- Ignore blank rows and headers named SYMBOL/TICKER.
- Normalize, distinct, uppercase.
```

Acceptance command:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Candidates
```

Expected:

```text
symbols=131 unique=131
```

### Step 3 — Add Xlsx universe provider

Create:

```text
src/Sailor.App/Scanner/Universe/XlsxUniverseProvider.cs
```

Update:

```text
src/Sailor.App/Scanner/Universe/SymbolUniverseProviderFactory.cs
```

Behavior:

```text
- if path ends with .xlsx, use XlsxUniverseProvider,
- support --scan-sheet or default Candidates,
- keep existing CSV/TXT behavior unchanged.
```

Acceptance:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- rank 1m v21-15minutes 10 scan\data\scan_default.xlsx
```

If the old `rank` command cannot pass sheet options yet, use the new `scan-list` command first.

### Step 4 — Add in-memory candle models

Create:

```text
src/Sailor.App/MarketData/Aggregation/CandleQuality.cs
src/Sailor.App/MarketData/Aggregation/InMemoryCandleSeries.cs
src/Sailor.App/MarketData/Aggregation/InMemoryCandleStore.cs
src/Sailor.App/MarketData/Aggregation/RealtimeCandleAccumulator.cs
```

Rules:

```text
- store per symbol + timeframe,
- bucket events by minute,
- update OHLC from trade/last/mid price,
- mark current candle IsClosed=false,
- close candle when next minute starts,
- retain bounded candles per symbol.
```

Acceptance:

```text
Unit/self-test with synthetic events:
09:30:01 price 10.00 -> O/H/L/C 10.00
09:30:20 price 10.20 -> H/C 10.20
09:30:45 price 9.90  -> L/C 9.90
09:31:01 price 10.10 -> close 09:30 candle, open 09:31 candle
```

### Step 5 — Add real-time market event source abstraction

Create:

```text
src/Sailor.App/MarketData/Live/IRealtimeMarketEventSource.cs
src/Sailor.App/MarketData/Live/RealtimeMarketEvent.cs
src/Sailor.App/MarketData/Live/DisabledRealtimeMarketEventSource.cs
src/Sailor.App/MarketData/Live/LocalCsvRealtimeMarketEventSource.cs
src/Sailor.App/MarketData/Live/IbkrRealtimeMarketEventSource.cs
```

First implementation can use local CSV/snapshots for dry-run. IBKR streaming can be added after the dry-run memory path is verified.

Important:

```text
Do not request L2 for all 131/232 symbols by default.
Request L1 for all only when explicitly allowed.
Request L2 only for selected top N symbols.
```

### Step 6 — Add historical/realtime merger

Create:

```text
src/Sailor.App/MarketData/Aggregation/HistoricalRealtimeBarMerger.cs
```

Merge behavior:

```text
- broker history wins on completed overlapping bars,
- realtime closed candles extend history after last historical timestamp,
- current open candle excluded from strategy unless explicitly allowed,
- output is IReadOnlyList<MarketBar> sorted ascending.
```

Acceptance:

```text
History: 09:30, 09:31, 09:32
Realtime: 09:32, 09:33, 09:34 open
Merged for strategy: 09:30, 09:31, 09:32(history), 09:33(realtime closed)
Current 09:34 open not used by default.
```

### Step 7 — Add history batch scheduler

Create:

```text
src/Sailor.App/Scanner/ScanList/ScanListHistoryBatchScheduler.cs
```

Behavior:

```text
- accept normalized symbol list,
- create batches of --history-batch-size,
- schedule next batch every --history-batch-interval-minutes,
- track per-symbol status in ScanListSymbolState,
- avoid duplicate outstanding requests,
- retry failed symbols with backoff.
```

Acceptance with `Candidates`:

```text
131 symbols -> 3 batches: 45, 45, 41
```

Acceptance with `Sheet1`:

```text
232 symbols -> 6 batches: 45, 45, 45, 45, 45, 7
```

### Step 8 — Add ScanListMemoryStore

Create:

```text
src/Sailor.App/Scanner/ScanList/ScanListMemoryStore.cs
src/Sailor.App/Scanner/ScanList/ScanListSymbolState.cs
```

Responsibilities:

```text
- keep raw symbol list,
- keep history status,
- keep real-time candle series,
- keep merged bars,
- keep latest scanner score/candidate,
- expose ready symbols,
- expose data quality warnings.
```

Important:

```text
Memory store is runtime state. It must not become the source of truth for broker positions. Broker positions remain managed by reconciliation/state modules.
```

### Step 9 — Add ScanListRuntime

Create:

```text
src/Sailor.App/Scanner/ScanList/ScanListRuntime.cs
src/Sailor.App/Scanner/ScanList/ScanListRunRequest.cs
src/Sailor.App/Scanner/ScanList/ScanListRunResult.cs
```

Responsibilities:

```text
- load workbook,
- start candle builders,
- run history scheduler,
- merge bars per symbol,
- call existing SailorScanner/ranking logic,
- maintain selected top N,
- produce data quality report.
```

### Step 10 — Integrate with PaperRuntimeHost

Update:

```text
src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs
src/Sailor.App/Runtime/Paper/PaperConductLoop.cs
src/Sailor.App/Runtime/Paper/PaperRuntimeHostRequest.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
```

Behavior:

```text
- when --scan-file is supplied, use ScanListRuntime selection instead of explicit universe selection,
- create active sessions only for selected top N,
- keep exits allowed for any symbol with an open position,
- block entries for symbols not currently selected,
- refresh selection on --selection-refresh-seconds.
```

Acceptance dry-run:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1
```

### Step 11 — Integrate broker history batch mode

Update:

```text
src/Sailor.App/MarketData/History/IbkrApiHistoricalBarProvider.cs
src/Sailor.App/Scanner/ScanList/ScanListRuntime.cs
```

Behavior:

```text
- run at most one batch at a time,
- use request ids deterministically,
- write per-symbol history result,
- merge immediately when a symbol history response succeeds,
- continue real-time candle building while waiting.
```

Acceptance broker paper:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DU123456 --history-batch-size 45 --history-batch-interval-minutes 10 --wait-seconds 15
```

### Step 12 — Add reports

Create:

```text
src/Sailor.App/Scanner/ScanList/ScanListReportWriter.cs
```

Output:

```text
logs/{Mode}/ScanList/scanlist_latest.json
logs/{Mode}/ScanList/scanlist_YYYYMMDD.csv
logs/{Mode}/ScanList/scanlist_YYYYMMDD.md optional
```

Report fields:

```text
file, sheet, symbol count, unique count,
history batches planned/completed,
history success/fail per symbol,
realtime candle count per symbol,
merged candle count per symbol,
ready/not-ready status,
scanner rank/score,
selected top N,
trade eligibility,
warnings.
```

### Step 13 — Extend paper certification report

Update:

```text
src/Sailor.App/Reporting/PaperSessionReportWriter.cs
src/Sailor.App/Reporting/PaperCertificationReport.cs
```

Add:

```text
scanListEvidencePath
scanListSymbolCount
scanListSelectedSymbols
scanListDataQualityStatus
scanListHistoryBatchStatus
scanListRealtimeCandleStatus
```

Certification should block if:

```text
- selected traded symbol had critical data gaps,
- historical/realtime merge produced duplicate/conflicting bars,
- latest selected symbol candle was stale,
- scanner selected a symbol that was not data-ready.
```

### Step 14 — Add live read-only scan-list

Update command runner:

```text
live scan-list
```

Safety:

```text
- read-only gate only,
- no order router,
- no live trading gate needed,
- no live orders sent,
- report under logs/Live/ScanList.
```

Acceptance:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth
```

### Step 15 — Add live one-symbol dynamic pilot

Update:

```text
src/Sailor.App/Runtime/Live/LiveDynamicScanPilotHost.cs
src/Sailor.App/Runtime/Live/LivePilotReport.cs
```

Behavior:

```text
- scan top N for observation,
- pick best 1 for SAILOR-034 live pilot,
- enforce all SAILOR-034 restrictions,
- require operator watching TWS,
- require clean pre-run live reconciliation,
- require clean final live reconciliation,
- write pilot evidence.
```

### Step 16 — Add future multi-symbol live pilot only after certification

Do not enable multi-symbol live trading in the first implementation.

When enabled later, add:

```text
Runtime.Live.AllowMultiSymbolPilot=false default
Runtime.Live.MaxConcurrentPositions=1 default
Runtime.Live.MaxTotalPilotNotional=100 default
Runtime.Live.MaxPerSymbolNotional=100 default
```

Only then allow:

```powershell
--allow-multi-symbol-live-pilot
```

---

## 14. Acceptance test matrix

### Build

```powershell
dotnet clean
dotnet build
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

### Excel reader

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Candidates
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Sheet1
```

Expected:

```text
Candidates symbols around 131
Sheet1 symbols around 232
```

### Backtest

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache
```

### Paper dry-run

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-quotes --iterations 3
```

### Paper broker scan-list

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DU123456 --history-batch-size 45 --history-batch-interval-minutes 10 --wait-seconds 15
```

### Paper dynamic trading dry-run

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1
```

### Paper dynamic trading with send-orders

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DU123456 --wait-seconds 15
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 5 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --send-orders --account DU123456 --wait-seconds 15
```

### Paper report

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

### Live read-only scan-list

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth
```

### Live dynamic one-symbol pilot

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run 1m v21-15minutes 1 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DU123456 --max-notional 100 --confirm-live --operator-watching-tws --wait-seconds 15 --iterations 5
```

Expected until all gates pass:

```text
Blocked before any broker order route could be created.
No live order was sent.
```

---

## 15. Risks and safeguards

| Risk | Safeguard |
|---|---|
| Too many historical requests at once | Batch size 45 and 10-minute interval. |
| Too many real-time subscriptions | L1 all-symbols must be explicit; L2 selected-only by default. |
| In-memory candles are partial or synthetic | Quality flags; block certification on weak data. |
| Scanner rotates symbols while position is open | Selection controls entries only; exits always allowed. |
| Live mode trades too many symbols | Keep live pilot best 1 only until separate multi-symbol gate exists. |
| Merge duplicates or stale bars | Deterministic merge and data-quality report. |
| Excel contains duplicates/invalid tickers | Normalize, de-duplicate, report rejected rows. |
| Broker state drift | Existing reconciliation gate remains mandatory. |

---

## 16. Recommended milestone split

```text
SAILOR-035 — Scan-default Excel universe provider and inspect command
SAILOR-036 — In-memory 1m candle accumulator and merge engine
SAILOR-037 — Historical batch scheduler for scan-list symbols
SAILOR-038 — Backtest/paper scan-list ranking reports
SAILOR-039 — Paper dynamic scan-list conduct loop top 5/top 10
SAILOR-040 — Scan-list evidence in paper certification report
SAILOR-041 — Live read-only scan-list observation
SAILOR-042 — Live scan-list best-1 pilot gate
SAILOR-043 — Optional multi-symbol live pilot after paper certification
```

Recommended next milestone:

```text
SAILOR-035 — Scan-default Excel universe provider and inspect command
```

Reason:

```text
The Excel reader and inspect command are low-risk, easy to test, and create the foundation for every later scan-list feature.
```

---

## 17. Harvester code dependency

The Harvester code is not required to start SAILOR-035. The current Sailor code already has enough scanner, history, runtime, and reporting structure to design the module.

Harvester code may still be useful later for comparison when implementing:

```text
- high-volume symbol batching,
- in-memory candle aggregation details,
- scanner admission/rotation policies,
- conduct-loop symbol activation/deactivation rules.
```

Upload Harvester later only if the Sailor implementation reaches one of those detailed behavior decisions and we want to compare against the existing Harvester pattern.

---

## 18. SAILOR-037 implementation update — Steps 1, 2, and 3

Commit reference used by the operator before this milestone:

```text
SAILOR-036 Add scan-default memory candles audit
```

SAILOR-037 implements the first low-risk foundation layer from this audit.

### 18.1 Implemented scope

Implemented now:

```text
Step 1 — scan-list input files and Git rules
Step 2 — workbook reader without Excel Interop
Step 3 — Xlsx universe provider
```

New/updated files:

```text
scan/data/.gitkeep
scan/data/scan_default.xlsx
.gitignore
src/Sailor.App/Scanner/ScanList/ScanListWorkbookOptions.cs
src/Sailor.App/Scanner/ScanList/ScanListWorkbookResult.cs
src/Sailor.App/Scanner/ScanList/ScanListWorkbookReader.cs
src/Sailor.App/Scanner/Universe/XlsxUniverseProvider.cs
src/Sailor.App/Scanner/Universe/SymbolUniverseProviderFactory.cs
src/Sailor.App/Backtest/Profiles/SailorSymbolUniverses.cs
src/Sailor.App/Program.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
```

### 18.2 Workbook input rules

The default workbook location is now:

```text
scan/data/scan_default.xlsx
```

Default sheet and column:

```text
sheet=Candidates
symbolColumn=A
```

The reader:

```text
- reads .xlsx directly through ZIP/XML OpenXML parts,
- does not use Microsoft Excel Interop,
- supports shared strings and inline strings,
- reads one symbol column,
- ignores blank rows,
- ignores header values such as SYMBOL/TICKER/CANDIDATES,
- normalizes symbols to uppercase,
- removes duplicates,
- rejects invalid/empty tokens.
```

The file `scan/data/local/` is ignored so daily/private exports can be placed there without accidental Git commits.

### 18.3 Daily list changes and intraday symbol additions

SAILOR-037 introduces the command/options contract for daily-changing lists and intraday additions:

```text
--scan-refresh-seconds 300
--trade-top 10
```

Runtime meaning for the next dynamic milestone:

```text
- at startup, load the current trading-day workbook,
- every 5 minutes, reload the workbook,
- detect added symbols and create new symbol state for them,
- detect removed symbols but do not remove exit/flatten management for symbols with open positions,
- keep the previous clean scan-list snapshot in memory if the file is temporarily missing or partially written,
- write warnings if a reload fails.
```

SAILOR-037 does not yet keep a long-running in-memory reload loop. It makes the input reader/provider and command contract available so the dynamic runtime can be built safely in the next step.

### 18.4 Connection interruption handling contract

SAILOR-037 scan-list commands are read-only and do not route orders.

For the later dynamic backtest/paper/live runtime, the implemented command output now documents this policy:

```text
- if TWS/server/history connection is interrupted, keep the last clean scan-list snapshot in memory,
- continue managing exits/flatten for any already-active positions,
- block new entries by moving the runtime to CloseOnly when broker/server state is degraded,
- resume entry eligibility only after reconnect + reconciliation is clean.
```

This must be connected to the already implemented SAILOR-031 degraded-state handling in the next dynamic runtime milestone.

### 18.5 Top-10 trade eligibility contract

The default trade retention contract is now:

```text
--trade-top 10
--scan-refresh-seconds 300
```

Meaning:

```text
- every 5 minutes, rerank the workbook symbols,
- keep at least the best 10 scan-rated symbols in memory for paper/live entry eligibility,
- entries are allowed only for symbols in the retained top list,
- exits and flatten remain allowed even if a symbol falls out of the top list,
- live trading remains subject to SAILOR-033/034 live gates.
```

SAILOR-037 applies this contract to scan-list reporting and command output. Actual dynamic entry gating by the retained top list is planned for the next paper dynamic scan-list conduct milestone.

### 18.6 New commands implemented now

Inspect the default workbook:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Candidates
```

Inspect the fallback/raw sheet:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Sheet1
```

Backtest ranking from the workbook:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates
```

Equivalent rank command:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- rank 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates
```

Paper read-only scan-list ranking:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-quotes --scan-refresh-seconds 300 --trade-top 10
```

Live read-only scan-list ranking:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth --scan-refresh-seconds 300 --trade-top 10
```

Direct universe argument form, useful for advanced backtest/rank commands:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- rank 1m v21-15minutes 10 "scan\data\scan_default.xlsx#Candidates#A"
```

### 18.7 Acceptance tests for SAILOR-037

Build:

```powershell
dotnet clean
dotnet build
```

Workbook reader:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Candidates
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan-list inspect --file scan\data\scan_default.xlsx --sheet Sheet1
```

Expected:

```text
Candidates: around 131 unique symbols
Sheet1: around 232 unique symbols
```

Paper read-only smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-quotes --max-symbols 45
```

Live read-only smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth --max-symbols 45
```

IBApi build:

```powershell
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

### 18.8 Remaining work after SAILOR-037

Not implemented yet:

```text
- long-running 5-minute workbook reload loop,
- in-memory 1m candle accumulator,
- historical + real-time candle merger,
- 45-symbol / 10-minute history batch scheduler,
- dynamic top-10 entry gating inside paper conduct,
- paper certification scan-list evidence,
- live read-only streaming candle observation,
- live best-1/top-N dynamic pilot.
```

Recommended next milestone:

```text
SAILOR-038 — Scan-list memory store, refresh loop, and history batch scheduler contract
```

Recommended implementation sequence:

```text
1. Create ScanListMemoryStore and ScanListSymbolState.
2. Add workbook reload every --scan-refresh-seconds.
3. Detect added/removed symbols.
4. Keep removed symbols if there is an open position or recent selection.
5. Add ScanListHistoryBatchScheduler for 45 symbols every 10 minutes.
6. Connect scheduler status to SAILOR-031 degraded-state handling.
7. Write logs/{Mode}/ScanList/scanlist_latest.json evidence.
```
