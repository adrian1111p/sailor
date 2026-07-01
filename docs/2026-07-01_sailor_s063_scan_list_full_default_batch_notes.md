# SAILOR-063 — Scan-list full default history batch

## Purpose

The previous scan-list default history batch size was 45 symbols. With a 145-symbol workbook this meant the first scan cycle ranked only the first alphabetic batch, then later batches became due at 10-minute intervals.

SAILOR-063 changes the default behavior so the first scan cycle ranks the full scan-list size unless the user explicitly overrides the batch size.

## New default behavior

When `--history-batch-size` is not supplied, Sailor resolves the batch size as follows:

1. use `--max-symbols` when it is supplied;
2. otherwise read the scan-list workbook and use the loaded symbol count;
3. otherwise fall back to `145`.

For the current `scan_default.xlsx` list with 145 symbols, this makes the effective default:

```text
historyBatchSize=145
```

Therefore the first scan cycle can score and rank the best 10 from all 145 symbols instead of only the first 45 alphabetically.

## Override remains available

IBKR pacing-safe batching is still available explicitly:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --account DUN559573 --quantity 10 --iterations 60 --cadence-seconds 60 --wait-seconds 15 --max-symbols 145 --history-batch-size 45 --history-batch-interval-minutes 10 --no-depth --market-data-type 1
```

## Recommended command after this change

The `--history-batch-size 145` argument is no longer necessary for the standard 145-symbol list:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --send-orders --account DUN559573 --quantity 10 --iterations 60 --cadence-seconds 60 --wait-seconds 15 --max-symbols 145 --no-depth --market-data-type 1
```

Expected scan-list evidence:

```text
workbookSymbols=145
prepared=145
candidates=10
```

This confirms the top 10 are ranked from the complete scan-list universe.
