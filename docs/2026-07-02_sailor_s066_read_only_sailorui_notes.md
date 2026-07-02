# SAILOR-066 — Read-only SailorUI implementation

Date: 2026-07-02

## Purpose

SAILOR-066 introduces a compact, TWS-style SailorUI monitor for paper and live runtime observation. The first implementation is intentionally read-only: it does not send orders, does not modify strategy selections, and does not open a broker API connection. It reads existing Sailor state and log files and refreshes the browser every second.

## Commands

Paper monitor:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --port 5101
```

Live monitor:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live sailor-ui --port 5101
```

Aliases:

```text
paper ui
paper monitor-ui
live ui
live monitor-ui
```

Open the browser at:

```text
http://localhost:5101/
```

## Read-only safety contract

The SAILOR-066 server:

- is loopback-only by default (`127.0.0.1` / `localhost`);
- refreshes data through browser polling every 1 second;
- reads only files under `state`, `logs/Paper`, `logs/Live`, and `logs/Backtest/Html`;
- sends no broker orders;
- performs no broker API requests;
- has disabled checkboxes and disabled strategy dropdowns.

Write controls are reserved for later SailorUI milestones.

## UI sections

### Section 1 — P&L

Compact TWS-style header:

```text
P&L DAILY | Unrealized | Realized
```

`P&L DAILY` is derived as:

```text
Realized + Unrealized
```

In SAILOR-066, realized P&L is loaded from harsh-conduct trade CSV files when available. Unrealized P&L is estimated from broker mirror positions and the latest scanner decision price. Stale prices are marked clearly.

### Section 2 — Active / today trade rows

Columns:

| # | Column | Meaning |
|---|---|---|
| 0 | `DLY P&L` | Row-level daily P&L. Rows are sorted descending by this value. |
| 1 | `Scan Ranking` | Last scanner rank if the symbol exists in the current scanner report. |
| 2 | `Symbol` | Ticker. |
| 3 | `Position` | Broker/lifecycle quantity. |
| 4 | `MKT VAL` | Position × Price. |
| 5 | `Buy value` | Absolute quantity × Open. |
| 6 | `Open` | Buying price / entry price. |
| 7 | `Price` | Last price / current 1-minute decision price. Stale data is marked with `*`. |
| 8 | `Trade` | Read-only checkbox. Future checked = go in trade / unchecked = go out of trade. |
| 9 | `Strategy` | Read-only dropdown ordered by TotalPnL$ descending from latest strategy report. |
| 10 | `Volume` | Actual/latest scanner volume. |

### Section 3 — Rest scanner symbols

Columns:

| # | Column | Meaning |
|---|---|---|
| 1 | `Scan Ranking` | Scanner rank. |
| 2 | `Symbol` | Ticker. |
| 3 | `Trade` | Read-only checkbox. Future checked = go in trade / unchecked = go out of trade. |
| 4 | `Strategy` | Read-only dropdown ordered by TotalPnL$ descending. |
| 5 | `Volume` | Actual/latest scanner volume. |

## Strategy dropdown ordering

SailorUI loads strategy options in this order:

1. Latest HTML strategy report under `logs/Backtest/Html/strategy_trades_report_*.html`.
2. Latest harsh-conduct summary CSV under `logs/{Paper|Live}/HarshConduct/harsh_conduct_summary_latest.csv`.
3. Built-in fallback order matching the current Sailor/Harvester strategy list.

When a report is available, strategies are ordered by `TotalPnL$` descending, then by number of trades descending.

The SAILOR-066 contract keeps the future active-strategy limit at 2. The current implementation displays the dropdowns but disables them.

## Data sources

| Source | File pattern |
|---|---|
| Broker positions | `state/{paper|live}/broker-mirror/broker_state_mirror_latest.json` |
| Trade lifecycle | `state/{paper|live}/trades/trade_registry_latest.json` |
| Scanner ranking | `logs/{Paper|Live}/Scanner/scanner_*.csv` |
| Scan-list freshness | `logs/{Paper|Live}/ScanList/scanlist_latest.json` |
| Harsh realized P&L | `logs/{Paper|Live}/HarshConduct/harsh_conduct_trades_yyyyMMdd.csv` |
| Strategy ranking | `logs/Backtest/Html/strategy_trades_report_*.html` |

## API endpoints

| Endpoint | Purpose |
|---|---|
| `/` | Compact HTML monitor. |
| `/api/snapshot` | JSON snapshot used by the browser once per second. |
| `/api/health` | Read-only health probe. |

## Tests

Run:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

Expected after SAILOR-066:

```text
PASS passed=16/16
```

Specific SAILOR-066 test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario sailor-ui-readonly
```

Expected checks:

- default port is 5101;
- refresh interval is 1 second;
- future active strategy cap is 2;
- Section 2 uses `Open` and `Price` labels;
- Section 3 includes read-only trade checkbox and strategy dropdown columns.

## Acceptance criteria

SAILOR-066 is accepted when:

1. `dotnet build` passes.
2. IBKR-enabled build passes.
3. `trade-management-test --scenario all` passes with 16/16 tests.
4. `paper sailor-ui --port 5101` starts and prints the browser URL.
5. Browser opens `http://localhost:5101/` and refreshes every second.
6. Section 1 shows daily P&L, unrealized, and realized values.
7. Section 2 shows active/today trade rows with `Open` and `Price` columns.
8. Section 3 shows scanner rows in compact line format.
9. Stale prices are visibly marked with `*` and yellow text.
10. Checkboxes and strategy dropdowns are present but disabled/read-only.
