# SAILOR-046 — Points Scanner Steps 6–10 Implementation Notes

Date: 2026-06-29

## Scope

This milestone continues the scanner migration described in `docs/2026-06-29_sailor_scanner_points_based_audit.md` and implements Steps 6 through 10.

The scope is still scanner and scan-list selection only. It does not yet remove the legacy entry blocks inside the V18-Silver conduct strategy. The previous paper dry-run showed this clearly: `scanner-mode=points-only` can rank symbols, but the conduct layer can still hold because of legacy V18 volume checks.

## Implemented steps

### Step 6 — Preserve legacy block reasons as evidence

The points scanner keeps legacy block reasons as evidence instead of discarding the symbol. Examples include:

- insufficient bars,
- price outside preferred range,
- latest volume below profile minimum,
- volume ratio below profile minimum,
- required EMA/VWAP/SMA200 setup not aligned,
- side disabled by profile.

These reasons remain visible in the candidate reason string and scanner CSV report.

### Step 7 — V18-Silver scoring module

The V18-specific points factors were moved into a dedicated module:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerV18SilverScoring.cs
```

The module scores:

- candle color direction,
- bar-to-bar momentum,
- VWAP reversion distance,
- VWAP extension penalty,
- choppy-shield body size,
- V18 profile volume-ratio score.

### Step 8 — PaperScannerRunner integration

`PaperScannerRunner` now supports the three scanner modes with real behavior:

| Mode | Behavior |
|---|---|
| `legacy-blocks` | Uses the original `SailorScanner` and keeps old selection behavior. |
| `points-only` | Uses `PointsScanner` for ranking and selection. |
| `hybrid-compare` | Runs both scanners, writes a points-vs-legacy report, but routes selected symbols from legacy mode for safety. |

Hybrid mode is intentionally conservative. It creates comparison evidence but does not silently switch trading selection to points-only.

### Step 9 — Candidate models and reports

Scanner candidate reporting now includes points evidence columns:

```text
ScannerMode, Status, SelectedSide, FinalScore, LegacyCandidateScore,
LongScore, ShortScore, PositivePoints, NegativePoints,
LegacyBlockReasons, TopPositiveFactors, TopNegativeFactors
```

Hybrid comparison reports are written to:

```text
logs/Paper/Scanner/points_vs_legacy_<profile>_<timeframe>_<timestamp>.csv
logs/Paper/Scanner/points_vs_legacy_<profile>_<timeframe>_<timestamp>.md
```

The CSV/MD comparison explains whether each points-ranked symbol would have been selected by the legacy scanner and which legacy block reasons were attached.

### Step 10 — Scan-list retention logic

Scan-list retention now understands points status separately from trade eligibility.

| Points status | Retained in memory | Trade eligible by default |
|---|---:|---:|
| `Ready` | yes | yes, if score >= minimum trade score |
| `WeakReady` | yes | no, unless weak entries are enabled |
| `WatchOnly` | yes, configurable | no |
| `NotReady` | no | no |

New command options:

```text
--points-min-trade-score 45
--points-allow-weak-entry false
--points-retain-watch-only true
```

Defaults are also available in `appsettings.json` under `Scanner`:

```json
"PointsMinimumTradeScore": 45.0,
"PointsAllowWeakEntry": false,
"PointsRetainWatchOnly": true
```

Important safety change: for points scanner modes, paper conduct selection no longer falls back to all scanner candidates when `tradeEligible=0`. This prevents `WatchOnly` or non-tradeable weak candidates from being routed as order candidates.

## Test commands

Build:

```powershell
dotnet clean
dotnet build
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

Points-only scan-list:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --no-depth --max-symbols 45 --scanner-mode points-only --points-min-trade-score 45 --wait-seconds 15
```

Allow weak candidates for paper observation:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --no-depth --max-symbols 45 --scanner-mode points-only --points-min-trade-score 45 --points-allow-weak-entry true --wait-seconds 15
```

Hybrid comparison:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --no-depth --max-symbols 45 --scanner-mode hybrid-compare --wait-seconds 15
```

Dry-run conduct with points selection:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --dry-run --iterations 60 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --scanner-mode points-only --points-min-trade-score 45
```

## Remaining limitation

The scanner is now points-capable, but V18-Silver conduct still has legacy hard entry filters. The next milestone should decide whether to introduce a `points-approved` conduct entry context or a profile option that lets conduct trust the points scanner instead of re-applying the same hard blocks.

## SAILOR-049 update — V18-only scorer replaced by common profile scorer

SAILOR-046 Step 7 originally introduced `PointsScannerV18SilverScoring` as the first dedicated points add-on module. SAILOR-049 supersedes that runtime behavior.

The real scorer used by `PointsScanner` is now:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerCommonStrategyScoring.cs
```

It applies the former V18-style add-on factors to every active strategy profile as profile-aware `PROFILE_*` factor codes. `PointsScannerV18SilverScoring` remains as a compatibility shim only.
