# SAILOR-049 — Common Points Scoring for All Strategies

Date: 2026-06-30

## Scope

This milestone removes the scanner-side assumption that V18-Silver/selective-short is the only strategy profile with dedicated points factors.

Before this change, the points scanner already had common base factors for all profiles, but the extra factors that made V18-Silver strong were implemented in `PointsScannerV18SilverScoring` and were only applied when the active profile name was `v18-silver`.

After this change, the same strategy-sensitive points add-on factors are applied to every `SailorStrategyProfile`:

- `sailor-trend-volume`
- `simple-momentum`
- `conduct-v3`
- `harvester-conduct-v3`
- `harvester-conduct-v9`
- V-series conduct profiles from `SailorConductStrategyRegistry`
- configured profiles from `appsettings.json`
- future profiles, as long as they resolve to `SailorStrategyProfile`

The scanner remains profile-aware because the common scorer uses each active profile's own values for:

- side mode,
- entry momentum threshold,
- minimum price and maximum price,
- minimum volume,
- minimum volume ratio,
- VWAP preference,
- EMA9/SMA20 preference,
- SMA200 preference,
- scanner lookback bars,
- minimum bars,
- top count.

## Implementation summary

### New shared scoring module

Added:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerCommonStrategyScoring.cs
```

This module is used by the main `PointsScanner` for every profile. It adds profile-neutral versions of the former V18-specific factors:

| Factor family | New common factor codes | Meaning |
|---|---|---|
| Candle direction | `PROFILE_CANDLE_COLOR`, `PROFILE_CANDLE_COLOR_ADVERSE` | Add/penalize candle color against the selected LONG/SHORT side. |
| Bar momentum | `PROFILE_BAR_MOMENTUM`, `PROFILE_BAR_MOMENTUM_MISSING` | Score latest bar directional momentum versus the active profile's `EntryMomentumPercent`. |
| VWAP reversion/extension | `PROFILE_VWAP_REVERSION`, `PROFILE_VWAP_EXTENSION_OK`, `PROFILE_VWAP_EXTENSION_HIGH`, `PROFILE_VWAP_MISSING` | Reward near-VWAP/reasonable extension and penalize over-extension. |
| Body control | `PROFILE_BODY_CONTROLLED`, `PROFILE_BODY_EXTENDED` | Reward controlled candle body and penalize extended/choppy body. |
| Profile volume ratio | `PROFILE_VOL_RATIO_OK`, `PROFILE_VOL_RATIO_LOW`, `PROFILE_VOL_RATIO_NOT_REQUIRED` | Score whether the current volume ratio fits the active profile. |

### Updated main scanner

Updated:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScanner.cs
```

The scanner now calls:

```csharp
PointsScannerCommonStrategyScoring.Score(...)
```

instead of a V18-only scorer.

### Backward-compatible V18 shim

Updated:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerV18SilverScoring.cs
```

The old V18 class name is kept as a compatibility shim, but it delegates to `PointsScannerCommonStrategyScoring`. This avoids breaking older references while making the actual scoring common for all strategies.

### Settings

Updated:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerSettings.cs
```

Added:

```csharp
ProfileVolumeRatioPoints = 8m
```

`V18VolumeRatioProfilePoints` remains for backward compatibility, but the common scanner uses the new profile-neutral setting.

## What changes in behavior

### Before

With `--scanner-mode points-only`, every strategy received the base points factors:

- data availability,
- price,
- volume,
- volume ratio,
- lookback momentum,
- EMA trend,
- VWAP side,
- SMA200 side.

Only `v18-silver` received the additional factors:

- candle color,
- bar-to-bar momentum,
- VWAP reversion/extension,
- body/choppy-shield control,
- profile volume-ratio points.

### After

Every profile receives both the base factors and the strategy-sensitive add-on factors.

Therefore, the following examples all use the same common points model, adjusted by their own profile values:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m harvester-conduct-v3 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m harvester-conduct-v9 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

## What does not change

This milestone changes the scanner-ranking model only.

It does not yet remove hard entry checks inside each conduct strategy. The scanner can select `Ready` points candidates for any strategy, but the conduct strategy may still return `Hold` if its own internal entry timing/pattern/volume checks are not satisfied.

That means:

```text
points scanner = common for all strategies
conduct entry  = still strategy-specific
```

The next conduct milestone should decide how points approval is passed into conduct entry logic, so strategies can choose to trust scanner-level points approval instead of re-applying the same hard scanner-style filters.

## Recommended validation commands

Build:

```powershell
dotnet clean
dotnet build
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

Common points model smoke tests:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points-test 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --no-depth --wait-seconds 15
```

Expected evidence:

- scanner output includes `scannerMode=points-only`,
- scanner output includes `pointsCandidates`, `ready`, `weakReady`, `watchOnly`, and `notReady`,
- top candidate factor strings contain `PROFILE_*` factor codes,
- legacy block reasons remain evidence only in points mode,
- no orders are sent by `paper scan-points` or `paper scan-points-test`.
