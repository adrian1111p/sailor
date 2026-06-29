# SAILOR-047 — Points Scanner Steps 11–14 Implementation Notes

Date: 2026-06-29

## Scope

This milestone continues the points-based scanner migration described in `docs/2026-06-29_sailor_scanner_points_based_audit.md` and implements Steps 11 through 14.

The scanner can now rank symbols with points, preserve legacy block reasons as evidence, and expose the points evidence into scan-list runtime evidence and the paper certification report. The conduct strategy still keeps its own V18-Silver entry checks; removing or replacing conduct-layer legacy blocks is a later milestone.

## Step 11 — Wait-for-scan-entry diagnostics

`--wait-for-scan-entry` now explains why Sailor is still waiting in points scanner modes.

When no trade-eligible symbol is retained, the runtime prints:

- scanner mode,
- trade-eligible count,
- Ready count,
- WeakReady count,
- WatchOnly count,
- NotReady count,
- minimum trade score,
- whether weak entries are enabled,
- top points candidates with their factor evidence.

Behavior remains conservative:

| Points state | Default behavior |
|---|---|
| Ready and score >= minimum | Eligible for conduct selection |
| WeakReady | Retained, but not trade eligible unless `--points-allow-weak-entry true` |
| WatchOnly | Visible and retained as watch evidence only |
| NotReady | Not trade eligible |

This means the operator can see the best 10 symbols and why Sailor is still waiting, instead of seeing only `tradeEligible=0`.

## Step 12 — Scanner-only diagnostics command

Added a dedicated command:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

The command is scanner-only:

- it does not start the paper conduct loop,
- it does not create order intents,
- it does not route orders,
- it defaults to `--scanner-mode points-only` when no scanner mode is supplied,
- it defaults to `--no-depth` unless depth is explicitly requested.

Output includes the same detailed points factors as the scan-list observation path.

## Step 13 — Hybrid comparison audit report paths

`--scanner-mode hybrid-compare` now exposes both generated audit artifacts:

```text
logs/Paper/Scanner/points_vs_legacy_<profile>_<timeframe>_<timestamp>.csv
logs/Paper/Scanner/points_vs_legacy_<profile>_<timeframe>_<timestamp>.md
```

The scan-list runtime and evidence JSON now preserve both paths:

- `LegacyComparisonReportPath`
- `LegacyComparisonMarkdownReportPath`

This lets us measure how many opportunities the legacy block scanner removes and open the Markdown report directly after a diagnostic run.

## Step 14 — Paper certification report scanner fields

The latest scan-list evidence now stores and the paper report now displays:

```text
scannerMode
pointsCandidates
readyCandidates
weakReadyCandidates
watchOnlyCandidates
notReadyCandidates
minimumTradeScore
pointsReportPath
legacyComparisonReportPath
legacyComparisonMarkdownReportPath
watchCandidateSymbols
watchCandidatePreview
```

The paper certification Markdown and daily CSV now include the scanner-mode and points fields. The console output of `paper report latest` also prints them.

## Test commands

Build:

```powershell
dotnet clean
dotnet build
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

Scanner-only points diagnostics:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

Wait-for-entry diagnostics:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --dry-run --iterations 300 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --scanner-mode points-only --points-min-trade-score 45 --wait-for-scan-entry --scan-entry-target 10 --scan-entry-wait-seconds 600 --scan-refresh-seconds 300
```

Hybrid comparison diagnostics:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode hybrid-compare --no-depth --wait-seconds 15
```

Paper certification report:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

## Remaining limitation

The scanner and scan-list evidence are now points-aware. The next implementation should decide how the conduct layer consumes points approval so V18-Silver does not re-apply legacy hard entry blocks after the scanner has already selected a points-approved symbol.
