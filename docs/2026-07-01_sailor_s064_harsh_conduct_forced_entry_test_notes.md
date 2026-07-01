# SAILOR-064 — Harsh conduct forced-entry test module

## Purpose

SAILOR-064 adds a short-test command for paper/live conduct strategy evaluation under harsh market/data conditions.

The module is intentionally different from normal scanner/conduct trading:

- the scanner still chooses the ranked symbols;
- the runtime directly creates one market entry per selected scanner symbol;
- strategy entry filters and stale-bar entry blocks are bypassed for these forced test entries;
- normal broker routing, receipts, position updates, exits, force-flat handling, lifecycle registry updates, and scanner replenishment continue to run.

This is for controlled short testing only. It is not the normal production trading workflow.

## Commands

Paper send-orders example:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper harsh-test 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --account DUN559573 --quantity 10 --iterations 10 --cadence-seconds 60 --wait-seconds 15 --max-symbols 145 --no-depth --market-data-type 1
```

Dry-run example:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper harsh-test 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --dry-run --quantity 10 --iterations 10 --cadence-seconds 60 --max-symbols 145 --no-depth
```

Live send-orders requires an additional explicit operator confirmation flag:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live harsh-test 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --confirm-live-force --account DU123456 --quantity 10 --iterations 10 --cadence-seconds 60 --max-symbols 145 --no-depth --market-data-type 1
```

Without `--confirm-live-force`, live harsh-test remains dry-run even if `--send-orders` is supplied.

## S063 integration

S064 uses the S063 scan-list default behavior. With `--max-symbols 145`, the scan-list ranking uses the full 145-symbol universe in the first batch instead of the old first alphabetical 45-symbol batch.

## Direct entry behavior

For each selected scanner-owned symbol:

- LONG scanner side creates a BUY market order;
- SHORT scanner side creates a SELL SHORT market order;
- quantity comes from `--quantity`; if not supplied, S064 defaults to 10 shares because no scanner sizing field exists yet.

## Replenishment behavior

Scanner-slot replenishment remains active. For S064, the target is the command top count, for example 10 in `v21-15minutes 10`.

After a scanner-owned test trade exits, S064 closes that scanner slot for entry. On the next five-minute replenishment cycle, the scanner can add a different ranked symbol until the target is restored.

## Logs

S064 writes trade and summary logs under:

- `logs\Paper\HarshConduct`
- `logs\Live\HarshConduct`

Files:

- `harsh_conduct_trades_yyyyMMdd.csv`
- `harsh_conduct_trades_latest.csv`
- `harsh_conduct_summary_yyyyMMdd.csv`
- `harsh_conduct_summary_latest.csv`

The summary CSV includes the requested columns:

```text
Strategy,Variant,Style,Symbols,Trades,>=50,WinRate,PF,Sharpe,EqSharpe,EqSortino,EqDownDev,TotalPnL$,MaxDD$,AvgWin$,AvgLoss$,Expectancy,GovStops,GovReason
```

Metrics are populated from routed/filled or dry-run-assumed fills. PnL metrics become meaningful after exits/flatten receipts occur.

## Self-test

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario harsh-conduct-forced-entries
```

`--scenario all` now includes this S064 case.
