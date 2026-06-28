# 2026-06-28 Sailor / Harvester Conduct Strategy Audit

## Scope

This audit compares the current Sailor backtest strategy layer with the Harvester backtest strategy code, with focus on the folder:

```text
src/Sailor.App/Backtest/Strategies/HarvesterConduct
```

The audit intentionally keeps the Sailor architecture decision already agreed in the project:

```text
Sailor scanners select symbols.
Harvester-inspired strategies provide only the conduct / entry / exit behavior for an already selected symbol.
No Harvester scanner, live runtime, IBKR runtime, risk governor, or self-learning code is imported.
```

The current Sailor implementation remains a Sailor-native backtest implementation. It does not try to compile or directly reuse the original Harvester classes.

## Harvester files inspected

The following Harvester backtest sources were used as reference:

```text
src/Harvester.App/Backtest/Strategies/ConductStrategyV3.cs
src/Harvester.App/Backtest/Strategies/StrategyV1.cs
src/Harvester.App/Backtest/Strategies/StrategyV2.cs
src/Harvester.App/Backtest/Strategies/StrategyV10.cs
src/Harvester.App/Backtest/Strategies/StrategyV12.cs
src/Harvester.App/Backtest/Strategies/StrategyV13.cs
src/Harvester.App/Backtest/Strategies/StrategyV14.cs
src/Harvester.App/Backtest/Strategies/StrategyV15.cs
src/Harvester.App/Backtest/Strategies/StrategyV16.cs
src/Harvester.App/Backtest/Strategies/StrategyV17.cs
src/Harvester.App/Backtest/Strategies/StrategyV18.cs
src/Harvester.App/Backtest/Strategies/StrategyV19.cs
src/Harvester.App/Backtest/Strategies/StrategyV20.cs
src/Harvester.App/Backtest/Strategies/StrategyV21.cs
src/Harvester.App/Backtest/Strategies/StrategyV22.cs
src/Harvester.App/Backtest/Strategies/StrategyV23.cs
src/Harvester.App/Backtest/Strategies/StrategyV24.cs
```

`StrategyV11` remains intentionally excluded because it did not perform well and was explicitly removed from the Sailor strategy set.

## Current Sailor strategy architecture

Sailor currently has two strategy families.

### 1. Sailor-native profiles

These are not Harvester ports. They are Sailor baseline profiles used for comparison and development:

| Profile | Purpose | Core behavior |
|---|---|---|
| `simple-momentum` | Simple baseline | Momentum entry with indicators; useful for smoke tests. |
| `sailor-trend-volume` | First Sailor scanner/strategy profile | EMA9/SMA20/VWAP/volume confirmation. |
| `sailor-conduct-v3` | Sailor conduct baseline | Sailor scanner entry plus conduct-style exits. |

### 2. Harvester-inspired conduct profiles

These profiles keep the Sailor scanner infrastructure but use Harvester-inspired conduct logic:

| Profile | Sailor class | Source inspiration | Status |
|---|---|---|---|
| `conduct-v3` / `harvester-conduct-v3` | `ConductV3CatamaranStrategy` | Harvester `ConductStrategyV3` | Sailor-native approximation. |
| `v1-first` | `V1FirstConductStrategy` | Harvester V1 | Sailor-native momentum baseline. |
| `v2-conduct` | `V2ConductFlowStrategy` | Harvester V2 | Momentum + pullback conduct approximation. |
| `v10-hybrid` | `V10HybridConductStrategy` | Harvester V10 | Hybrid momentum/pullback/VWAP reversion. |
| `v12` | `V12ConductStrategy` | Harvester V12 | Hybrid momentum/pullback/breakout/VWAP reversion. |
| `v13` | `V13ConductStrategy` | Harvester V13 | Momentum/pullback/breakout trend profile. |
| `v14-smallcap` | `V14SmallCapConductStrategy` | Harvester V14 | Small-cap tolerant conduct profile. |
| `v15-shortcap` | `V15ShortCapConductStrategy` | Harvester V15 | Selective short-cap / retained breakdown profile. |
| `v16-sqzbreakout` | `V16SqzBreakoutConductStrategy` | Harvester V16 | Breakout / squeeze-style approximation. |
| `v17-hybridflow` | `V17HybridFlowConductStrategy` | Harvester V17 | Hybrid flow / choppy-shield approximation. |
| `v18-silver` | `V18SilverConductStrategy` | Harvester V18 | Silver / selective short approximation. |
| `v19-purplecloud` | `V19PurpleCloudConductStrategy` | Harvester V19 | PurpleCloud / retained breakout approximation. |
| `v20-gen001-choppyshield` | `V20Gen001ChoppyShieldConductStrategy` | Harvester V20 | ChoppyShield / conservative profile. |
| `v21-15minutes` | `V21_15MinutesConductStrategy` | Harvester V21 | 15m EMA9 angle conductor. |
| `v22-15minutes` | `V22_15MinutesConductStrategy` | Harvester V22 | 15m EMA9 angle enhanced variant, simplified. |
| `v23-5minutes` | `V23_5MinutesConductStrategy` | Harvester V23 | 5m EMA9 angle conductor. |
| `v24-5minutes` | `V24_5MinutesConductStrategy` | Harvester V24 | 5m EMA9 angle enhanced variant, simplified. |

## Common Sailor conduct behavior

The non-angle Harvester-inspired profiles inherit from:

```text
SailorConductProfileStrategyBase
```

They share these concepts:

| Component | Behavior |
|---|---|
| Side mode | `LongOnly`, `ShortOnly`, or `LongAndShort`. Current default supports mirror long/short behavior. |
| Common filters | Price range, minimum volume, volume average ratio. |
| Trend filters | EMA9 vs SMA20, VWAP, optional SMA200. Mirrored for short side. |
| Entry patterns | Momentum, breakout/breakdown, pullback, VWAP reversion, choppy-shield. |
| Exit layer | `SailorConductExitEngine` for hard stop, breakeven, trailing/giveback, indicator exits, max hold, force-flat. |
| Market timing | Last entry 15:45 ET, force-flat 15:55 ET. |

The conduct profiles are intentionally simplified compared with Harvester. They do not include Harvester self-learning, risk governor snapshots, replay metadata, live IBKR snapshots, or L1/L2 modules.

## Strategy details

### `conduct-v3`, `sailor-conduct-v3`, `harvester-conduct-v3`

| Setting | Current value |
|---|---|
| Base class | `SailorConductProfileStrategyBase` |
| Strategy class | `ConductV3CatamaranStrategy` |
| Variant | `catamaran` |
| Entry patterns | Momentum + pullback |
| Entry momentum | 0.12% |
| Minimum volume | 50,000 |
| Minimum volume ratio | 0.75 |
| Trend filters | EMA9>SMA20, Close>VWAP |
| SMA200 filter | Disabled |
| Green/red candle confirmation | Enabled and mirrored |
| Exit style | Conduct exit engine |

### `v16-sqzbreakout`

| Setting | Current value |
|---|---|
| Strategy class | `V16SqzBreakoutConductStrategy` |
| Entry patterns | Momentum + breakout/breakdown |
| Entry momentum | 0.18% |
| Minimum volume | 75,000 |
| Minimum volume ratio | 1.00 |
| Trend filters | EMA9>SMA20, Close>VWAP |
| Minimum EMA spread | 0.04% |
| Breakout lookback | 12 bars |
| Choppy maximum momentum | 0.70% |
| Exit style | Conduct exit engine |

### `v13`

| Setting | Current value |
|---|---|
| Strategy class | `V13ConductStrategy` |
| Entry patterns | Momentum + pullback + breakout/breakdown |
| Entry momentum | 0.15% |
| Minimum volume | 50,000 |
| Minimum volume ratio | 0.80 |
| Trend filters | EMA9>SMA20, Close>VWAP |
| Minimum EMA spread | 0.02% |
| Exit style | Conduct exit engine |

### `v10-hybrid`

| Setting | Current value |
|---|---|
| Strategy class | `V10HybridConductStrategy` |
| Entry patterns | Momentum + pullback + VWAP reversion |
| Entry momentum | 0.12% |
| Minimum volume | 40,000 |
| Minimum volume ratio | 0.70 |
| Trend filters | Loose; EMA/VWAP requirements disabled |
| Green/red candle confirmation | Enabled and mirrored |
| Exit style | Conduct exit engine |

### `v17-hybridflow`

| Setting | Current value |
|---|---|
| Strategy class | `V17HybridFlowConductStrategy` |
| Entry patterns | Momentum + pullback + choppy-shield |
| Entry momentum | 0.12% |
| Minimum volume | 40,000 |
| Minimum volume ratio | 0.65 |
| Trend filter | EMA9>SMA20 required, VWAP not required |
| Exit style | Conduct exit engine |

### `v2-conduct`

| Setting | Current value |
|---|---|
| Strategy class | `V2ConductFlowStrategy` |
| Entry patterns | Momentum + pullback |
| Entry momentum | 0.12% |
| Minimum volume | 50,000 |
| Minimum volume ratio | 0.75 |
| Trend filters | EMA9>SMA20, Close>VWAP |
| Exit style | Conduct exit engine |

### `v18-silver`

| Setting | Current value |
|---|---|
| Strategy class | `V18SilverConductStrategy` |
| Entry patterns | Momentum + VWAP reversion + choppy-shield |
| Entry momentum | 0.10% |
| Minimum volume | 35,000 |
| Minimum volume ratio | 0.70 |
| Trend filters | Loose; EMA/VWAP requirements disabled |
| Choppy maximum momentum | 0.65% |
| Exit style | Conduct exit engine |

### `v1-first`

| Setting | Current value |
|---|---|
| Strategy class | `V1FirstConductStrategy` |
| Entry patterns | Momentum only |
| Entry momentum | 0.20% |
| Minimum volume | 30,000 |
| Minimum volume ratio | 0.60 |
| Trend filters | Loose; EMA/VWAP requirements disabled |
| Green/red candle confirmation | Disabled |
| Exit style | Conduct exit engine |

### `v19-purplecloud`

| Setting | Current value |
|---|---|
| Strategy class | `V19PurpleCloudConductStrategy` |
| Entry patterns | Momentum + breakout/breakdown |
| Entry momentum | 0.18% |
| Minimum volume | 75,000 |
| Minimum volume ratio | 1.00 |
| Trend filters | EMA9>SMA20, Close>VWAP |
| Minimum EMA spread | 0.04% |
| Exit style | Conduct exit engine |

### `v15-shortcap`

| Setting | Current value |
|---|---|
| Strategy class | `V15ShortCapConductStrategy` |
| Entry patterns | Retained breakdown / short-cap tuned approximation |
| Entry momentum | 0.10% |
| Minimum volume | 25,000 |
| Minimum volume ratio | 0.80 |
| Trend filters | Loose; EMA/VWAP requirements disabled |
| Choppy maximum momentum | 0.55% |
| Exit style | Conduct exit engine |

### `v14-smallcap`

| Setting | Current value |
|---|---|
| Strategy class | `V14SmallCapConductStrategy` |
| Entry patterns | Momentum + pullback + VWAP reversion |
| Entry momentum | 0.10% |
| Minimum volume | 20,000 |
| Minimum volume ratio | 0.65 |
| Trend filters | Loose; EMA/VWAP requirements disabled |
| Choppy maximum momentum | 0.75% |
| Exit style | Conduct exit engine |

### `v20-gen001-choppyshield`

| Setting | Current value |
|---|---|
| Strategy class | `V20Gen001ChoppyShieldConductStrategy` |
| Entry patterns | Momentum + choppy-shield |
| Entry momentum | 0.08% |
| Minimum volume | 35,000 |
| Minimum volume ratio | 0.70 |
| Trend filter | EMA9>SMA20 required, VWAP not required |
| Choppy maximum momentum | 0.45% |
| Exit style | Conduct exit engine |

### `v12`

| Setting | Current value |
|---|---|
| Strategy class | `V12ConductStrategy` |
| Entry patterns | Momentum + pullback + breakout/breakdown + VWAP reversion |
| Entry momentum | 0.14% |
| Minimum volume | 45,000 |
| Minimum volume ratio | 0.75 |
| Trend filter | EMA9>SMA20 required, VWAP not required |
| Minimum EMA spread | 0.01% |
| Exit style | Conduct exit engine |

## V21 / V22 / V23 / V24 angle strategy audit

The angle strategies inherit from:

```text
AngleEmaConductStrategyBase
```

They use this higher-timeframe pipeline:

```text
1m bars
→ market-open anchored 15m or 5m candles
→ completed candle only
→ EMA9 on higher-timeframe closes
→ ATR14 on higher-timeframe candles
→ angle = atan((EMA9_now - EMA9_previous) / ATR14) in degrees
```

This matches the most important Harvester design point: the angle is normalized by ATR, not by simple percent slope.

### V21 — `v21-15minutes`

| Setting | Current value |
|---|---|
| Strategy class | `V21_15MinutesConductStrategy` |
| Higher timeframe | 15m |
| EMA | EMA9 on completed 15m candles |
| Angle threshold | ±12° |
| Minimum completed candles | 9 |
| Initial entry | Directly from angle, per user request |
| Re-entry | Confirmation candle after flatten |
| Short side | Enabled |
| Last entry | 15:45 ET |
| Force flat | 15:55 ET |

V21 is the closest Sailor implementation to the written user definition. It still uses Sailor scanner selection and Sailor position sizing rather than Harvester's full risk-based sizing.

### V23 — `v23-5minutes`

| Setting | Current value |
|---|---|
| Strategy class | `V23_5MinutesConductStrategy` |
| Higher timeframe | 5m |
| EMA | EMA9 on completed 5m candles |
| Angle threshold | ±12° |
| Minimum completed candles | 9 |
| Entry | Angle + re-entry confirmation |
| Short side | Enabled |
| Last entry | 15:45 ET |
| Force flat | 15:55 ET |

V23 mirrors the Harvester V23 structure more closely after the shared angle-base fixes below.

### V22 — `v22-15minutes`

| Setting | Current value |
|---|---|
| Strategy class | `V22_15MinutesConductStrategy` |
| Higher timeframe | 15m |
| EMA | EMA9 on completed 15m candles |
| Angle threshold | ±8.5° |
| Minimum completed candles | 12 |
| Short side | Enabled |
| Last entry | 15:45 ET |
| Force flat | 15:55 ET |

Harvester V22 is more complex than Sailor V22. It includes additional enhanced candle/bucket behavior. Sailor V22 currently keeps the simple shared angle-conduct engine but now matches the Harvester V22 default threshold and readiness count.

### V24 — `v24-5minutes`

| Setting | Current value |
|---|---|
| Strategy class | `V24_5MinutesConductStrategy` |
| Higher timeframe | 5m |
| EMA | EMA9 on completed 5m candles |
| Angle threshold | ±8.5° |
| Minimum completed candles | 12 |
| Short side | Enabled |
| Last entry | 15:45 ET |
| Force flat | 15:55 ET |

Harvester V24 is the enhanced 5m counterpart of V22. Sailor V24 now matches the Harvester V24 default threshold and readiness count, but does not yet implement the full Harvester V24 enhanced diagnostics/recovery behavior.

## Updates applied in this audit

### 1. Same-day higher-timeframe reference candles

Harvester V21/V23 search for the last green/red reference candle only inside the same ET trading day. Sailor previously used the last green/red candle from the available lookback window, which could leak a reference from a previous trading day.

Updated:

```text
AngleEmaConductStrategyBase
MarketTime.GetEasternDate(...)
```

Now flatten and re-entry reference candles are resolved inside the same Eastern trading date as the signal candle.

### 2. Harvester-equivalent flatten reference price checks

Harvester V21/V23 flatten logic uses these comparisons:

```text
Long flatten:
red candle low <= last green low
OR red candle close <= last green open

Short flatten:
green candle high >= last red high
OR green candle close >= last red open
```

Sailor previously collapsed this to a single support/resistance value. That could miss the `close <= last green open` and `close >= last red open` cases.

Updated:

```text
ShouldFlattenLong(...)
ShouldFlattenShort(...)
```

### 3. Harvester-equivalent re-entry price checks

Harvester V21/V23 re-entry uses the opposite reference candle high/open or low/open.

Updated Sailor checks:

```text
Long re-entry:
green candle crosses or recovers EMA9
AND high > previous red high OR close > previous red open

Short re-entry:
red candle crosses or recovers EMA9
AND low < previous green low OR close < previous green open
```

### 4. V22/V24 default angle threshold and readiness count

Harvester V22/V24 default `AngleThresholdDegrees` is `8.5`, not `12.0`.

Updated:

```text
V22_15MinutesConductStrategy: 8.5° and 12 completed candles
V24_5MinutesConductStrategy: 8.5° and 12 completed candles
```

V21 and V23 remain at 12° and 9 completed candles.

## Remaining intentional differences from Harvester

These differences are intentional for now because the Sailor project is still keeping the code simple and independent.

| Area | Harvester | Sailor current decision |
|---|---|---|
| Scanner | Full Harvester scanners and strategy-specific scanner logic | Not imported; Sailor scanners select symbols. |
| Position sizing | Risk-based sizing using risk dollars, account size, ATR stop distance, max shares | Sailor currently uses its own simpler sizing/risk settings. |
| Self-learning | Harvester has self-learning hooks and post-trade recovery | Not imported. |
| Risk governor | Harvester has risk governor and snapshots | Not imported. |
| L1/L2/live snapshots | Harvester uses live/paper runtime data structures | Not imported. |
| Exact V22/V24 enhanced logic | Harvester V22/V24 include significantly more enhanced behavior | Sailor uses simplified shared angle-conduct engine. |
| Retry lockout reference | Harvester tracks an explicit retry lockout after reversal flatten | Sailor approximates with same-day opposite reference candle logic. |

## Assessment

The current Sailor implementation is appropriate for the current project stage:

```text
Backtest-first
No live broker risk yet
Simple Sailor scanners
Harvester-inspired conduct behavior
Long/short mirror behavior
Clear logs and HTML reports
```

The biggest confirmed gaps were in the V21/V23/V22/V24 angle base. The current update fixes the safe and important mismatches without reintroducing the strict V21 changes that previously hurt the strategy behavior.

## Recommended next steps

1. Run the HTML report after this update:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps
```

2. Compare especially:

```text
v21-15minutes
v22-15minutes
v23-5minutes
v24-5minutes
```

3. If V22/V24 still underperform but V21/V23 remain stable, create a separate task for full V22/V24 enhanced behavior instead of changing the common angle base again.

4. Keep `V11` excluded.

5. Only after backtest strategy behavior is stable, continue with paper/live skeleton.
