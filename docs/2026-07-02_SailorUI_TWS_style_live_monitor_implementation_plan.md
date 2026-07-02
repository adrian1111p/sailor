# SAILOR-065 — SailorUI TWS-style live monitor implementation plan

Date: 2026-07-02  
Scope: Sailor paper/live monitor UI, focused on a compact TWS-style line table for fast conduct-strategy control and observation.  
Status: Implementation plan only. No runtime behavior is changed by this document.

---

## 1. Goal

Create a new **SailorUI** web monitor that runs next to the existing Harvester MonitorUI without taking over its port.

Default endpoint:

```text
http://localhost:5101/
```

The UI must be intentionally simple, dense, and line-based, similar to the TWS portfolio/monitor style:

- live refresh every 1 second;
- no card layout;
- compact rows with horizontal scrolling only if the browser is too narrow;
- vertical scrolling only when the symbol list is longer than the screen;
- dark/TWS-like color style;
- green/red numeric coloring for P&L and exposure;
- sticky header rows;
- paper and live modes supported;
- live mode must start read-only unless explicitly armed in a later milestone.

The first deliverable after this plan should be a read-only UI. Interactive order controls must be added only after the state contract is stable.

---

## 2. User-visible sections

### Section 1 — Daily account P&L header

Single compact header line:

```text
P&L DAILY <DailyPnL> | Unrealized <UnrealizedPnL> | Realized <RealizedPnL> | NetLiq <NetLiquidity> | ExcessLiq <ExcessLiquidity>
```

Minimum required fields:

| Field | Meaning | Preferred source | Fallback source |
|---|---|---|---|
| `P&L DAILY` | Current account daily P&L | IBKR account summary / PnL feed | Sum of closed + open Sailor rows for today |
| `Unrealized` | Unrealized P&L for open broker positions | IBKR positions + last price | Sailor session position + latest bar close |
| `Realized` | Realized P&L today | IBKR executions / account summary | `HarshConduct` or trade registry realized P&L |
| `NetLiq` | Net liquidation | IBKR account summary | empty / `n/a` |
| `ExcessLiq` | Excess liquidity | IBKR account summary | empty / `n/a` |

Display rules:

- positive values green;
- negative values red;
- zero/unknown values neutral;
- refresh every second with timestamp and connection status.

---

### Section 2 — Active/today trade rows

Rows included here:

1. symbols currently in a broker/Sailor position;
2. symbols that had a trade today even if currently flat;
3. manual TWS symbols accepted by SAILOR-062;
4. harsh-test symbols from SAILOR-064;
5. scanner-owned symbols that are active conduct sessions.

Column order:

| Column | Label | Description |
|---:|---|---|
| 0 | `DLY P&L` | Per-symbol daily realized + unrealized P&L. Show only if the symbol is or was in trade today. Sort descending by this field by default. |
| 1 | `Scan Ranking` | Latest scanner rank. Descending/ascending option must be clear in the header; default should show best rank first. |
| 2 | `Symbol` | Symbol. Include a small status marker: `P` paper, `L` live, `M` manual, `S` scanner, `H` harsh. |
| 3 | `Position` | Signed position. Long positive, short negative. |
| 4 | `MKT VAL` | Position market value = `Position × Price`. Signed, TWS-like. |
| 5 | `Buy value` | Entry/buy value = absolute quantity × open/entry price. For shorts this is still displayed as absolute entry exposure. |
| 6 | `Open` | Buying price / entry price. For Sailor-managed positions this is the trade open price; for synchronized manual broker positions this is the broker average/open price. |
| 7 | `Price` | Last price / current 1-minute decision price. Mark stale data clearly. |
| 8 | `Trade` | Checkbox. Checked = symbol is allowed/desired for active trade management. Unchecked = close/exclude workflow, depending on mode. |
| 9 | `Strategy` | Dropdown with strategy choices sorted by historical `TotalPnL$` descending. Default is the best `TotalPnL$` strategy. |
| 10 | `Volume` | Actual latest volume, preferably current live 1-minute bar volume. |

Compact row example:

```text
+82.20 | 001 | KUST | -750 | -915 | 1008.16 | 1.344 | 1.22 | [x] | V18-Silver | 206583
```

In the example above, `Open=1.344` and `Price=1.22`. The UI must not show the old `AVG Preis` / `Last` labels in Section 2.

Important visual behavior:

- one row per symbol;
- no expanded cards;
- row height target: 22–28 px;
- highlight stale rows with muted amber/gray background;
- highlight active broker mismatch rows with red edge marker;
- highlight SAILOR-062 manual rows with `M` marker but do not block scanner rows.

---

### Section 3 — Rest scanner symbols

Rows included here:

1. latest scanner candidates not in Section 2;
2. ranked symbols retained by the scan-list memory;
3. symbols available for manual selection into the harsh/conduct workflow.

Column order:

| Column | Label | Description |
|---:|---|---|
| 1 | `Scan Ranking` | Latest scanner rank from points scanner. |
| 2 | `Symbol` | Symbol. |
| 3 | `Trade` | Checkbox. Checked = request/allow entry for this symbol. Unchecked = inactive/excluded. |
| 4 | `Strategy` | Strategy dropdown sorted by `TotalPnL$` descending. Default is best strategy. |
| 5 | `Volume` | Latest actual/scanner volume. |

Compact row example:

```text
011 | BCRX | [ ] | V21-15Minutes | 985000
```

Section 3 should be scrollable independently when many symbols exist.

---

## 3. Strategy dropdown behavior

### 3.1 Source of strategy order

Primary ranking source:

```text
D:\Site\sailor\logs\Backtest\Html\strategy_trades_report_smallcaps_1m_20260628_154010.html
```

The parser must read the strategy summary table and order strategies by:

```text
TotalPnL$ descending
```

Expected strategy examples from the current report/list:

```text
V18-Silver
V20-GEN001-ChoppyShield
Harvester-ConductV9
V15-ShortCap
V19-PurpleCloud
V21-15Minutes
V16-SqzBreakout
V22-15Minutes
V23-5Minutes
V24-5Minutes
V13
Sailor-ConductV3
Conduct-V3
Harvester-ConductV3
V2-Conduct
Sailor-TrendVolume
V12
V10-Hybrid
V17-HybridFlow
V1-First
V14-SmallCap
SimpleMomentum
```

Fallback strategy source:

1. `SailorConductStrategyRegistry.SupportedProfileNames`;
2. `SailorStrategyProfile` aliases;
3. hard-coded safe fallback list only if both sources fail.

### 3.2 Default strategy per row

Default rule:

1. choose the highest `TotalPnL$` strategy that is compatible with the symbol and command mode;
2. if no backtest report exists, use current command profile, for example `v21-15minutes`;
3. if no current command profile exists, use `v18-silver` or configured application default.

### 3.3 Maximum two active strategies

Initial UI must support strategy diversity, but only up to **two active strategy profiles at the same time**.

Rule:

- Each selected/checked row has exactly one active strategy.
- Across all checked rows, maximum two different strategy names may be active.
- Once two strategies are active, all other strategy choices become disabled in dropdowns until one active strategy is removed.
- Unchecked rows may preview any strategy, but activating them must respect the two-strategy limit.

Example:

```text
Checked rows use V18-Silver and V21-15Minutes.
All other checked rows must use V18-Silver or V21-15Minutes.
V20, V19, etc. remain visible but disabled until one of the two active strategies is removed.
```

This rule avoids mixing too many conduct policies during live testing.

---

## 4. Checkbox semantics

The checkbox must not be a visual-only control. It is a desired-state control.

### Section 2 checkbox

| State | Paper behavior | Live behavior |
|---|---|---|
| Checked | Keep symbol allowed for strategy management. If flat and selected by scanner/harsh test, entry may be requested. | Read-only in SAILOR-065/066. Later requires explicit live arm/confirm. |
| Unchecked | Request close/exclude. If position exists, strategy/controller prepares an exit workflow; if flat, remove from active selection. | Read-only first. Later requires explicit live arm/confirm. |

### Section 3 checkbox

| State | Paper behavior | Live behavior |
|---|---|---|
| Checked | Request entry/activation for this scanner symbol using selected strategy, subject to max two strategies. | Read-only first. Later requires explicit live arm/confirm. |
| Unchecked | Keep inactive/excluded. | Read-only first. |

Safety rule:

- The UI may write desired state.
- The runtime must decide whether that desired state is executable.
- Router, broker, account, force-flat, disconnect, stale-data, and live-confirm gates remain server-side.

---

## 5. Architecture overview

### 5.1 New module namespace

Proposed folder:

```text
src/Sailor.App/Runtime/Ui
```

Proposed files:

```text
SailorUiServer.cs
SailorUiSnapshotService.cs
SailorUiStateStore.cs
SailorUiStrategyRankingService.cs
SailorUiHtmlRenderer.cs
SailorUiDto.cs
SailorUiCommandHandler.cs
SailorUiOptions.cs
SailorUiLogWriter.cs
```

### 5.2 No heavy web framework in first version

The current project is a .NET console app using `Microsoft.NET.Sdk`, not `Microsoft.NET.Sdk.Web`. To minimize risk:

- use a small in-process HTTP server;
- prefer `HttpListener` or a minimal `TcpListener` HTTP implementation;
- no ASP.NET/Kestrel dependency in SAILOR-065/066 unless needed later;
- serve one static HTML page and a JSON endpoint.

Proposed endpoints:

| Endpoint | Method | Purpose |
|---|---|---|
| `/` | GET | Serve the compact UI HTML. |
| `/api/state` | GET | Return full UI snapshot JSON. Browser polls every second. |
| `/api/desired-state` | POST | Update checkbox/strategy desired state. Paper only in the first interactive milestone. |
| `/api/health` | GET | Return server/version/status. |
| `/api/strategies` | GET | Return strategy ranking and active two-strategy constraint. |

### 5.3 Refresh model

Initial implementation:

```text
Browser fetch('/api/state') every 1000 ms
```

Later optimization:

```text
Server-Sent Events /api/stream
```

Polling is acceptable because the UI is local-only and the JSON is small.

---

## 6. Runtime commands

### 6.1 Read-only monitor command

New command:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --account DUN559573 --port 5101 --max-symbols 145
```

Aliases:

```text
paper ui
paper monitor-ui
paper sailor-ui
```

Live read-only:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live sailor-ui --account <LIVE_ACCOUNT> --port 5101 --read-only
```

### 6.2 Integrated run with UI

Add optional flags to conduct/harsh commands:

```powershell
--ui
--ui-port 5101
--ui-refresh-ms 1000
--ui-read-only true
```

Example:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper harsh-test 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --account DUN559573 --quantity 10 --iterations 60 --cadence-seconds 60 --max-symbols 145 --no-depth --market-data-type 1 --ui --ui-port 5101
```

### 6.3 Interactive controls command gate

Interactive paper controls should require:

```text
--ui-controls true
```

Interactive live controls must require a stronger gate in a later milestone:

```text
--ui-controls true --confirm-live-ui-orders
```

SAILOR-065/066 should be read-only first.

---

## 7. Data model

### 7.1 Snapshot DTO

```csharp
public sealed record SailorUiSnapshot(
    DateTimeOffset ObservedUtc,
    string Mode,
    string Account,
    string RuntimeStatus,
    SailorUiPnlHeader Pnl,
    IReadOnlyList<SailorUiTradeRow> ActiveRows,
    IReadOnlyList<SailorUiScannerRow> ScannerRows,
    IReadOnlyList<SailorUiStrategyOption> StrategyOptions,
    SailorUiControlState ControlState,
    IReadOnlyList<string> Warnings);
```

### 7.2 Section 1 DTO

```csharp
public sealed record SailorUiPnlHeader(
    decimal DailyPnl,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal NetLiquidation,
    decimal ExcessLiquidity,
    bool BrokerVerified,
    bool IsFallback);
```

### 7.3 Section 2 row DTO

```csharp
public sealed record SailorUiTradeRow(
    int? ScanRank,
    string Symbol,
    string Source,
    int Position,
    decimal MarketValue,
    decimal BuyValue,
    decimal OpenPrice,
    decimal Price,
    decimal DailyPnl,
    bool DesiredTradeEnabled,
    string SelectedStrategy,
    long Volume,
    bool Stale,
    string Status,
    string Reason);
```

Field naming rules:

- `OpenPrice` is the entry/open price shown in the UI as `Open`.
- `Price` is the current last/decision price shown in the UI as `Price`.
- stale `Price` values must be flagged with `Stale=true` and a clear `Reason`.

### 7.4 Section 3 row DTO

```csharp
public sealed record SailorUiScannerRow(
    int ScanRank,
    string Symbol,
    bool DesiredTradeEnabled,
    string SelectedStrategy,
    long Volume,
    decimal FinalScore,
    string SelectedSide,
    string Status,
    bool Stale,
    string Reason);
```

### 7.5 Strategy option DTO

```csharp
public sealed record SailorUiStrategyOption(
    string DisplayName,
    string CommandProfile,
    decimal TotalPnl,
    decimal WinRate,
    decimal ProfitFactor,
    decimal Sharpe,
    bool IsDefault,
    bool IsSelectable,
    string DisabledReason);
```

---

## 8. Data sources and merge order

The UI snapshot service must merge state from these sources in deterministic order.

### 8.1 Broker/account state

Sources:

```text
state\paper\broker-mirror\broker_state_mirror_latest.json
state\live\broker-mirror\broker_state_mirror_latest.json
```

and, when connected:

```text
IBKR position provider / account summary provider
```

Fields used:

- account;
- broker verified;
- positions;
- open orders;
- executions;
- manual/external detections;
- reconciliation status.

### 8.2 Trade lifecycle registry

Sources:

```text
state\paper\trades\trade_registry_latest.json
state\live\trades\trade_registry_latest.json
```

Fields used:

- lifecycle id;
- origin;
- symbol;
- status;
- scanner slot;
- stopped-for-day flag;
- profile/lifecycle mode.

### 8.3 Scan-list and scanner reports

Sources:

```text
logs\Paper\ScanList\scanlist_latest.json
logs\Paper\Scanner\scanner_<profile>_<timeframe>_<timestamp>.csv
logs\Live\ScanList\scanlist_latest.json
logs\Live\Scanner\scanner_<profile>_<timeframe>_<timestamp>.csv
```

Fields used:

- rank;
- symbol;
- side;
- final score;
- status;
- volume;
- volume ratio;
- L1/L2 flags;
- stale/quality blockers.

SAILOR-063 rule applies:

```text
The UI must show/rank symbols from the full max-symbol list, e.g. 145, not only the first 45 symbols.
```

### 8.4 Harsh conduct logs

Sources:

```text
logs\Paper\HarshConduct\harsh_conduct_trades_latest.csv
logs\Paper\HarshConduct\harsh_conduct_summary_latest.csv
logs\Live\HarshConduct\harsh_conduct_trades_latest.csv
logs\Live\HarshConduct\harsh_conduct_summary_latest.csv
```

Fields used:

- entry/exit decisions;
- filled quantity;
- realized P&L;
- strategy;
- variant;
- governance stops;
- summary metrics.

### 8.5 Backtest strategy ranking HTML

Source configured by default:

```text
logs\Backtest\Html\strategy_trades_report_smallcaps_1m_20260628_154010.html
```

Parser responsibilities:

1. locate the strategy summary table;
2. extract at least `Strategy`, `TotalPnL$`, `WinRate`, `PF`, `Sharpe`;
3. normalize strategy display names to command profiles;
4. sort by `TotalPnL$` descending;
5. expose result to dropdowns.

---

## 9. Desired-state persistence

The UI must persist operator decisions so a refresh does not lose selected strategies or checkboxes.

Proposed files:

```text
state\paper\ui\desired_state_latest.json
state\paper\ui\desired_state_yyyyMMdd.jsonl
state\live\ui\desired_state_latest.json
state\live\ui\desired_state_yyyyMMdd.jsonl
```

Desired state shape:

```json
{
  "mode": "paper",
  "account": "DUN559573",
  "updatedUtc": "2026-07-02T10:00:00Z",
  "maxActiveStrategies": 2,
  "rows": [
    {
      "symbol": "TSLA",
      "desiredTradeEnabled": true,
      "selectedStrategy": "v21-15minutes",
      "updatedBy": "SailorUI"
    }
  ]
}
```

The runtime must treat this file as desired state only. It must not bypass broker safety.

---

## 10. UI layout specification

### 10.1 Page structure

```html
<body>
  <header id="pnl-header"></header>
  <section id="active-section">
    <table id="active-table"></table>
  </section>
  <section id="scanner-section">
    <table id="scanner-table"></table>
  </section>
</body>
```

### 10.2 CSS rules

- body background: near-black;
- font: compact system monospace or TWS-like sans serif;
- headers sticky;
- rows single-line with `white-space: nowrap`;
- numeric columns right-aligned;
- P&L positive green, negative red;
- stale row amber/gray;
- manual rows with a small left marker;
- selected checkbox rows slightly brighter;
- no large margins;
- no Bootstrap dependency.

### 10.3 Update behavior

Every second:

1. fetch `/api/state`;
2. update Section 1 text;
3. patch table rows by symbol key;
4. preserve dropdown focus if the user is editing;
5. preserve scroll position;
6. show `STALE` if snapshot age exceeds 3 seconds;
7. show `DISCONNECTED` if HTTP request fails.

---

## 11. SAILOR-065 implementation plan

SAILOR-065 should be the foundation milestone. It should **not** route orders from the browser.

### Step 1 — Add documentation and command contract

Files:

```text
docs\2026-07-02_SailorUI_TWS_style_live_monitor_implementation_plan.md
```

Acceptance:

- plan committed;
- command names agreed;
- data sources agreed;
- live controls remain read-only in the first UI implementation.

### Step 2 — Add UI options to configuration

Files:

```text
src\Sailor.App\Configuration\SailorAppSettings.cs
src\Sailor.App\appsettings.json
```

Settings:

```json
"Ui": {
  "Enabled": false,
  "DefaultPort": 5101,
  "RefreshMilliseconds": 1000,
  "MaxActiveStrategies": 2,
  "DefaultReadOnly": true,
  "StrategyReportPath": "logs/Backtest/Html/strategy_trades_report_smallcaps_1m_20260628_154010.html"
}
```

Acceptance:

- defaults load without requiring command-line flags;
- port 5101 does not conflict with existing Harvester MonitorUI on 5100.

### Step 3 — Add DTOs and snapshot service

Files:

```text
src\Sailor.App\Runtime\Ui\SailorUiDto.cs
src\Sailor.App\Runtime\Ui\SailorUiSnapshotService.cs
```

Acceptance:

- snapshot can be built without IBKR connected, using latest JSON/CSV logs;
- snapshot can be built while a paper run is active;
- missing files produce warnings, not crashes.

### Step 4 — Add strategy ranking parser

Files:

```text
src\Sailor.App\Runtime\Ui\SailorUiStrategyRankingService.cs
```

Acceptance:

- parses the backtest HTML strategy report;
- sorts by `TotalPnL$` descending;
- maps display strategy names to command profiles;
- falls back to registry profiles if HTML file is missing;
- dropdown default is the best strategy by `TotalPnL$`.

### Step 5 — Add local HTTP server

Files:

```text
src\Sailor.App\Runtime\Ui\SailorUiServer.cs
src\Sailor.App\Runtime\Ui\SailorUiHtmlRenderer.cs
```

Acceptance:

- starts on `http://localhost:5101/`;
- serves `/` and `/api/state`;
- browser updates every second;
- no external JS/CSS dependencies;
- line-based layout only.

### Step 6 — Add command routing

Files:

```text
src\Sailor.App\Runtime\Commands\SailorRuntimeCommandRunner.cs
```

Acceptance:

Commands work:

```powershell
paper sailor-ui --account DUN559573 --port 5101
paper ui --account DUN559573
live sailor-ui --account <account> --port 5101 --read-only
```

### Step 7 — Add UI smoke self-test

Files:

```text
src\Sailor.App\Runtime\TradeManagement\SelfTests\TradeManagementSelfTestRunner.cs
```

Suggested scenario:

```text
sailor-ui-snapshot
```

Acceptance:

```text
PASS passed=16/16
```

Self-test checks:

1. snapshot builds with missing files;
2. strategy list is sorted by `TotalPnL$`;
3. max two active strategies is enforced in desired-state validation;
4. active rows and scanner rows are separated correctly;
5. no order intent is created by read-only UI.

---

## 12. Follow-up milestones after SAILOR-065

### SAILOR-066 — Read-only SailorUI implementation

Deliver:

- local UI server;
- `/api/state` endpoint;
- Section 1, Section 2, Section 3 rendering;
- strategy dropdown visible but disabled/read-only;
- no browser order actions.

### SAILOR-067 — Paper interactive desired-state controls

Deliver:

- checkbox changes persist to desired-state JSON;
- strategy dropdown changes persist to desired-state JSON;
- paper runtime consumes desired state;
- unchecked active paper row requests close/exclude;
- checked scanner row requests entry/activation;
- two active strategy limit enforced server-side.

### SAILOR-068 — Multi-strategy conduct routing, max two strategies

Deliver:

- one symbol/session can run with selected strategy;
- at most two strategy engines active in one paper run;
- scanner replenishment respects selected strategy availability;
- metrics grouped by strategy.

### SAILOR-069 — Live UI hardening

Deliver:

- live mode still read-only by default;
- explicit live UI order confirmation gate;
- emergency disable button;
- audit trail for every UI action;
- live account mismatch protection;
- browser warning banner for live mode.

### SAILOR-070 — UI report export

Deliver:

- CSV snapshot export;
- HTML daily summary;
- active rows + scanner rows + strategy selection audit.

---

## 13. Logging plan

New folder:

```text
logs\Paper\SailorUI
logs\Live\SailorUI
```

Files:

```text
sailor_ui_snapshot_latest.json
sailor_ui_snapshot_yyyyMMdd.jsonl
sailor_ui_actions_yyyyMMdd.csv
sailor_ui_server_yyyyMMdd.log
```

Action CSV header:

```text
TimeUtc,Mode,Account,Symbol,OldEnabled,NewEnabled,OldStrategy,NewStrategy,Accepted,RejectedReason,UserAgent,Source
```

Snapshot JSONL is useful for reconstructing what the UI showed at any second.

---

## 14. Safety rules

The UI must never become a direct uncontrolled order sender.

Mandatory rules:

1. UI writes desired state, not direct broker commands.
2. Runtime validates desired state against broker/account safety.
3. Live actions are read-only until a dedicated live confirmation milestone.
4. Unchecked active row should close/exclude only through existing router/strategy lifecycle.
5. Manual TWS positions remain accepted under SAILOR-062, but must be clearly marked.
6. Stale data must be visible in the row and must not silently create entries.
7. SAILOR-058 stale-candle protection remains active unless a dedicated harsh-test command explicitly bypasses it.
8. SAILOR-064 harsh mode must be visibly marked as `HARSH` in the UI.
9. All UI actions must be logged.
10. If UI server crashes, conduct runtime must continue safely.

---

## 15. Acceptance criteria for SAILOR-065/066 read-only UI

The milestone is accepted when:

1. `paper sailor-ui --account DUN559573 --port 5101` starts successfully.
2. Browser opens at `http://localhost:5101/`.
3. Section 1 shows daily P&L, unrealized, realized, and broker status.
4. Section 2 shows active/today trade rows with DLY P&L, scan rank, symbol, position, MKT VAL, buy value, Open, Price, checkbox, strategy dropdown, and volume.
5. Section 3 shows remaining scanner symbols with rank, symbol, checkbox, strategy dropdown, and volume.
6. Rows update every second without page reload.
7. Strategy dropdown is ordered by `TotalPnL$` descending when the HTML report exists.
8. At most two active strategies are selectable in validation logic.
9. UI is compact and line-based; no large card layout.
10. Existing paper/live/harsh commands continue to build and run.
11. Self-test includes `sailor-ui-snapshot` and passes.
12. No browser action sends a broker order in read-only mode.

---

## 16. Test plan

### 16.1 Build tests

```powershell
dotnet clean
dotnet build
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

### 16.2 Self-tests

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

Expected after SAILOR-065/066:

```text
PASS passed=16/16
```

### 16.3 Read-only UI smoke test

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --account DUN559573 --port 5101 --max-symbols 145
```

Open:

```text
http://localhost:5101/
```

Expected:

- page loads;
- refresh counter changes every second;
- no order actions are possible;
- warning displayed if no active paper run is producing fresh state.

### 16.4 Integrated harsh test with UI

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper harsh-test 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --account DUN559573 --quantity 10 --iterations 60 --cadence-seconds 60 --max-symbols 145 --no-depth --market-data-type 1 --ui --ui-port 5101
```

Expected:

- Section 2 shows up to 10 harsh/scanner rows;
- exited rows move to today-trade/flat status;
- replenished rows appear in Section 2;
- Section 3 shows remaining scanner symbols;
- P&L updates every second when broker/account data changes.

---

## 17. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| UI causes accidental live order | Critical | Read-only live by default; live actions require later explicit confirmation gate. |
| HTML report parser fails | Dropdown order wrong | Fallback to strategy registry and log warning. |
| Browser refresh interrupts dropdown edit | Operator frustration | Patch rows by key and preserve focused control. |
| Local server port conflict | UI not available | Default to 5101; allow `--port`; detect conflict and print clear error. |
| Data source files missing | Blank UI | Show warning row; keep server alive. |
| Too much vertical scrolling | Hard to monitor | Compact line rows, sticky headers, separate Section 2/3 scroll panes. |
| Two-strategy rule bypassed client-side | Wrong strategy mix | Enforce on server in `SailorUiCommandHandler`. |
| Stale market data looks tradable | Bad entries | Mark stale rows and keep server-side stale gate active. |

---

## 18. Proposed implementation order

1. Commit this SAILOR-065 plan.
2. Implement DTOs and snapshot service.
3. Implement strategy ranking parser.
4. Implement read-only local HTTP server.
5. Add `paper sailor-ui` command.
6. Add compact HTML/CSS/JS renderer.
7. Add snapshot/log files.
8. Add self-test `sailor-ui-snapshot`.
9. Validate with market closed using existing logs.
10. Validate during paper harsh-test after market open.
11. Only after read-only UI is stable, implement paper interactive controls.
12. Only after paper controls are stable, implement live control gates.

---

## 19. Definition of done for this document

This plan is complete when it is available at:

```text
docs\2026-07-02_SailorUI_TWS_style_live_monitor_implementation_plan.md
```

and can be used as the checklist for SAILOR-066 read-only UI implementation.
