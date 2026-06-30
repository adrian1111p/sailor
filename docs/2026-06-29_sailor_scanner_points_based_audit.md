# SAILOR Scanner Audit — Current Block-Based Scanner and Target Points-Based Scanner

Date: 2026-06-29  
Scope: `Sailor.App` scanner, scan-list runtime, paper/live scanner selection, and conduct-entry interaction.  
Target milestone proposal: **SAILOR-044 — Points-Based Scanner V1**.

---

## 1. Executive summary

The current Sailor scanner is a hybrid of:

1. **Hard data gates** before a symbol can be scanned.
2. **Hard scanner filters** inside `SailorScanner.TryCreateCandidate`.
3. **Directional hard filters** for LONG/SHORT scanner candidates.
4. **Optional L1/L2 snapshot guards** after scanner ranking.
5. **Conduct strategy entry blockers** after a scanner symbol is selected.
6. **Runtime safety blockers** for broker/reconciliation/order routing.

This design is safe, but it creates the exact behavior observed in the live paper tests: IBKR history can be available for all 45 prepared symbols, but the scanner may still return `candidates=0`, `tradeEligible=0`, and `NoSelection`. In that case, the conduct runtime correctly blocks orders.

The requested new direction is a scanner based **100% on points gained** and **0% scanner blocks**. That means the scanner should not discard a symbol merely because one condition fails. Instead, every symbol with usable market data should produce a ranked candidate row with:

- LONG points,
- SHORT points,
- selected side,
- positive factors,
- negative penalties,
- final score,
- readiness/advisory status,
- evidence text.

The scanner should still respect **external non-scanner safety gates**. For example, if there is no usable candle data, no broker connection, no clean reconciliation, no account match, or max notional is exceeded, the runtime must stay safe. The 0%-blocks principle applies to scanner ranking, not to broker safety.

---

## 2. Current scanner architecture

### 2.1 Current high-level flow

```text
Excel / built-in / CSV universe
        |
        v
SymbolUniverseProviderFactory
        |
        v
PaperScannerRunner
        |
        +--> prepare history per symbol
        |       - IBKR historical bars when enabled
        |       - local CSV mirror/cache when enabled
        |       - only HistorySuccess symbols continue
        |
        v
SailorScanner.Scan
        |
        +--> TryCreateCandidate(symbol)
        |       - hard price filter
        |       - hard volume filter
        |       - hard volume-ratio filter
        |       - hard minimum-bars filter
        |       - hard directional trend/VWAP/SMA filters depending on profile
        |       - score only if candidate survives all hard filters
        |
        v
Top N scanner candidates
        |
        v
Optional L1/L2 snapshot capture
        |
        v
ScanListMemoryStore.RetainTradeCandidates
        |
        v
paper run / live run conduct loop
        |
        v
Conduct strategy entry decision
        |
        +--> additional hard entry filters
        |
        v
Order intent -> paper/live safety gate -> router
```

### 2.2 Current source files involved

| Area | Main files | Current role |
|---|---|---|
| Backtest scanner | `src/Sailor.App/Backtest/Scanner/SailorScanner.cs` | Creates one best LONG/SHORT candidate per symbol, but only after hard filters pass. |
| Scanner candidate model | `src/Sailor.App/Backtest/Scanner/ScannerCandidate.cs` | Holds the current final candidate values and a single numeric `Score`. |
| Runtime scanner | `src/Sailor.App/Scanner/Runtime/PaperScannerRunner.cs` | Loads universe, prepares history, runs `SailorScanner`, captures optional snapshots, writes reports. |
| Runtime candidate wrapper | `src/Sailor.App/Scanner/Runtime/PaperScannerCandidate.cs` | Adds rank and L1/L2 snapshot fields to `ScannerCandidate`. |
| Scan-list runtime | `src/Sailor.App/Scanner/ScanList/ScanListRuntime.cs` | Reads Excel, batches history, retains trade-eligible top symbols, writes scan-list evidence. |
| Strategy profile | `src/Sailor.App/Backtest/Profiles/SailorStrategyProfile.cs` | Provides scanner thresholds and profile flags. |
| Conduct registry | `src/Sailor.App/Backtest/Strategies/HarvesterConduct/SailorConductStrategyRegistry.cs` | Creates built-in profile values for `v18-silver`, `v21-15minutes`, and others. |
| Conduct entry rules | `src/Sailor.App/Backtest/Strategies/HarvesterConduct/SailorConductEntryRules.cs` | Defines conduct entry thresholds and pattern flags. |
| V18 conduct strategy | `src/Sailor.App/Backtest/Strategies/HarvesterConduct/V18_Silver/V18SilverConductStrategy.cs` | Defines V18-Silver / selective-short conduct settings. |
| Conduct base | `src/Sailor.App/Backtest/Strategies/HarvesterConduct/SailorConductProfileStrategyBase.cs` | Applies hard entry filters after scanner selection. |
| Snapshot guard | `src/Sailor.App/Backtest/Strategies/HarvesterConduct/SailorSnapshotEntryGuard.cs` | Can accept or reject entries based on spread, liquidity, and book imbalance. |

---

## 3. Current scanner input parameters and calculations

### 3.1 Command/runtime scanner parameters

Current paper/live scan-list and paper run commands feed these scanner inputs through `PaperScannerOptions`:

| Parameter | Example | Meaning |
|---|---:|---|
| `Mode` | `paper`, `live` | Runtime mode. |
| `Timeframe` | `1m` | Candle timeframe used by scanner. |
| `ProfileName` | `v18-silver` | Strategy/scanner profile. |
| `Universe` | `scan\data\scan_default.xlsx#Candidates#A` | Symbol source. |
| `TopCount` | `10` | Maximum ranked candidates returned. |
| `MaxSymbolsToPrepare` | `45` | First N resolved symbols prepared for history in one scanner call. |
| `HistoryDays` | `5` | Historical lookback requested from provider. |
| `RequestIbkrHistory` | `true` in IBApi paper mode | Whether to request IBKR historical data. |
| `MirrorHistoryToBacktest` | `true` | Whether to mirror IBKR history to local backtest CSV. |
| `UseRegularTradingHours` | `true` | Historical bars use RTH when configured. |
| `CaptureSnapshots` | `true` unless disabled | Whether to request L1/L2 snapshots after ranking. |
| `RequestIbkrMarketData` | `true` unless local mode/no-quotes | Whether to use IBKR market data snapshots. |
| `UseL1` | `true` | Whether L1 is requested/used. |
| `UseL2` | `true` unless `--no-depth` | Whether L2 depth is requested/used. |
| `SnapshotSeconds` | `3` or `2` | Snapshot capture duration. |
| `DepthLevels` | `5` | L2 depth rows requested. |
| `MarketDataType` | `1` real-time, `2` frozen/delayed depending command | IBKR market data type. |
| `PrimaryExchange` | `NASDAQ` | Contract/exchange hint. |
| `UseSmartDepth` | `false` | IBKR smart-depth flag. |

### 3.2 Profile parameters used by the scanner

The scanner reads these fields from `SailorStrategyProfile`:

| Profile field | Used by scanner? | Current effect |
|---|---:|---|
| `MinimumPrice` | yes | Hard block if latest close is below minimum. |
| `MaximumPrice` | yes | Hard block if latest close is above maximum. |
| `MinimumVolume` | yes | Hard block if latest bar volume is below minimum. |
| `MinimumVolumeRatio` | yes | Hard block if latest volume divided by 20-bar average is below minimum. |
| `RequirePriceAboveVwap` | yes | Hard directional block: LONG close must be above VWAP; SHORT close must be below VWAP. |
| `RequireEma9AboveSma20` | yes | Hard directional block: LONG EMA9 > SMA20; SHORT EMA9 < SMA20. |
| `RequirePriceAboveSma200WhenAvailable` | yes | Hard directional block when SMA200 exists. |
| `ScannerLookbackBars` | yes | Momentum lookback window. |
| `ScannerMinimumBars` | yes | Hard block if insufficient bars. |
| `ScannerTopCount` | yes | Default top N when command does not supply top count. |
| `SideMode` | yes | Enables LONG, SHORT, or both sides. |
| `EntryMomentumPercent` | indirectly in conduct, not current scanner score threshold | Conduct uses it; scanner uses raw momentum percent in score. |
| `ExitMomentumPercent` | no scanner effect | Exit/conduct context. |
| `UseConductExits` | no scanner effect | Runtime/conduct context. |
| `ConductProfileName` | no scanner effect | Runtime/conduct context. |
| `UseMarketHours` / `LastEntryMinute` / `ForceFlatMinute` | no scanner ranking effect | Runtime/conduct safety context. |

### 3.3 Current built-in profile values relevant to V18-Silver

For `v18-silver`, `SailorConductStrategyRegistry.CreateBuiltInProfile` creates a scanner profile approximately as follows:

| Field | V18-Silver value | Current scanner impact |
|---|---:|---|
| `EntryMomentumPercent` | `0.10` | Used later by conduct momentum rules. |
| `ExitMomentumPercent` | `0.15` | Exit/conduct context. |
| `MinimumPrice` | `0.50` | Hard scanner block below $0.50. |
| `MaximumPrice` | `1000.00` | Hard scanner block above $1000. |
| `MinimumVolume` | `35000` | Hard scanner block below 35k latest-bar volume. |
| `MinimumVolumeRatio` | `0.70` | Hard scanner block below 0.70x average volume. |
| `RequireEma9AboveSma20` | `false` | No scanner hard trend block. |
| `RequirePriceAboveVwap` | `false` | No scanner hard VWAP block. |
| `RequirePriceAboveSma200WhenAvailable` | `false` | No scanner hard SMA200 block. |
| `MinimumBarsBetweenEntries` | `3` | Conduct/runtime entry spacing. |
| `LastEntryMinute` | `945` | No entries after 15:45 ET. |
| `ForceFlatMinute` | `955` | Force flat near 15:55 ET. |
| `ScannerLookbackBars` | `20` | 20-bar momentum lookback. |
| `ScannerMinimumBars` | `35` | Hard scanner block if fewer than 35 bars. |
| `ScannerTopCount` | `20` | Default scanner top count; command can override to 10. |
| `SideMode` | inherited default `LongAndShort` | Scanner can evaluate both sides. |

### 3.4 Current V18-Silver conduct-entry parameters

`V18SilverConductStrategy` is not the scanner, but it can still block entry after the scanner has selected a symbol. Current V18-Silver conduct settings:

| Conduct field | Value | Current conduct effect |
|---|---:|---|
| `profileName` | `v18-silver` | Profile key. |
| `strategyName` | `V18-Silver` | Display/report name. |
| `variantName` | `selective-short` | Variant label. |
| `Patterns` | `Momentum | VwapReversion | ChoppyShield` | Entry requires at least one pattern to pass. |
| `EntryMomentumPercent` | `0.10` | Required LONG/SHORT bar-to-bar move for momentum setup. |
| `MinimumVolume` | `35000` | Hard conduct block below 35k current volume. |
| `MinimumVolumeRatio` | `0.70` | Hard conduct block below 0.70x 20-bar avg volume. |
| `RequireEma9AboveSma20` | `false` | No trend hard block. |
| `RequireCloseAboveVwap` | `false` | No VWAP hard block. |
| `RequireCloseAboveSma200WhenAvailable` | `false` | No SMA200 hard block. |
| `RequireGreenBar` | `true` | LONG requires green bar; SHORT requires red bar. |
| `MinimumEmaSpreadPercent` | `0.0` | Disabled. |
| `MaximumVwapExtensionPercent` | `2.0` | Hard conduct block if too far from VWAP. |
| `PullbackMaximumDistanceFromEmaPercent` | `0.75` | Pullback setup threshold, not active in V18 pattern set. |
| `BreakoutLookbackBars` | `10` | Breakout threshold, not active in V18 pattern set. |
| `BreakoutBufferPercent` | `0.04` | Breakout threshold, not active in V18 pattern set. |
| `VwapReversionMaximumDistancePercent` | `1.0` | VWAP reversion setup threshold. |
| `ChoppyMaximumMomentumPercent` | `0.65` | Choppy-shield setup threshold. |

### 3.5 Current scanner score formula

For each symbol, the scanner can produce at most one selected side: the best of LONG and SHORT.

The current base score is:

```text
score =
  directionalMomentumPercent * 2.0
+ directionalEmaSpreadPercent * 2.0
+ directionalVwapSpreadPercent * 1.5
+ min(volumeRatio, 5.0) * 10.0
+ max(0, directionalSma200SpreadPercent) * 0.5
```

Where:

| Term | LONG meaning | SHORT meaning |
|---|---|---|
| `directionalMomentumPercent` | positive when price rose from lookback close | positive when price fell from lookback close |
| `directionalEmaSpreadPercent` | positive when EMA9 > SMA20 | positive when EMA9 < SMA20 |
| `directionalVwapSpreadPercent` | positive when close > VWAP | positive when close < VWAP |
| `volumeRatio` | latest volume / VolumeAverage20, capped at 5 | same |
| `directionalSma200SpreadPercent` | positive when close > SMA200 | positive when close < SMA200 |

Current hard block after scoring:

```text
if score <= 0 => candidate is discarded
```

---

## 4. Current blockers and where they happen

### 4.1 Data/universe blockers before scanner scoring

| Block | Location | Current result |
|---|---|---|
| Empty universe | `PaperScannerRunner.RunAsync` | No scanner run. |
| Only first `--max-symbols` prepared | `PaperScannerRunner.RunAsync` | Later symbols are not scanned until another batch/cycle. |
| History request fails | `PaperScannerSnapshotProvider.PrepareHistoryAsync` and `PaperScannerRunner` | Symbol excluded from `preparedSymbols`. |
| No prepared symbols | `PaperScannerRunner.RunAsync` | `candidates=0`; no report. |
| Excel cannot be read | `ScanListWorkbookReader` | Fixed by SAILOR-042 v2 to tolerate open workbook via shared read/memory copy. |
| Insufficient local mirrored CSV after history | `ScanListRuntime.MergePreparedSymbols` | Data quality may become Blocked / NoSelection. |

These are not scanner-preference blocks; they are data availability gates. They should remain as safety/data readiness gates, but the new scanner should record them as evidence rows where possible.

### 4.2 Hard scanner filters inside `SailorScanner`

| Current hard filter | Effect |
|---|---|
| `bars.Count < ScannerMinimumBars` | Symbol discarded. |
| `latestBar.Close < MinimumPrice` | Symbol discarded. |
| `latestBar.Close > MaximumPrice` | Symbol discarded. |
| `latestBar.Volume < MinimumVolume` | Symbol discarded. |
| `VolumeAverage20 missing or volumeRatio < MinimumVolumeRatio` | Symbol discarded. |
| Profile requires EMA trend and EMA9/SMA20 missing | Direction discarded. |
| Profile requires EMA trend and trend wrong for side | Direction discarded. |
| Profile requires VWAP and VWAP missing | Direction discarded. |
| Profile requires VWAP and side is wrong relative to VWAP | Direction discarded. |
| Profile requires SMA200 and side is wrong relative to SMA200 | Direction discarded. |
| Final score <= 0 | Direction discarded. |
| Both directions discarded | Symbol produces no candidate. |

These are the main blockers to replace with points/penalties.

### 4.3 Snapshot blockers after scanner ranking

`SailorSnapshotEntryGuard` can reject if:

| Guard | Current effect |
|---|---|
| Snapshot required and missing | Entry rejected. |
| Spread bps > `MaxSpreadBps` | Entry rejected. |
| Liquidity score < `MinimumLiquidityScore` | Entry rejected. |
| LONG with adverse negative book imbalance | Entry rejected. |
| SHORT with adverse positive book imbalance | Entry rejected. |

For a 0%-block scanner, these should become scanner points/penalties when the scanner ranks candidates. Runtime may still keep a final separate order-safety kill switch if data is too dangerous.

### 4.4 Conduct-entry blockers after scanner selection

The scanner can choose a symbol but the conduct strategy can still return Hold. Common conduct blocks include:

| Conduct block | Current example effect |
|---|---|
| Price outside min/max | Hold. |
| Volume below `MinimumVolume` | Hold. |
| VolumeAverage20 missing | Hold. |
| Volume ratio below minimum | Hold. |
| LONG green-bar requirement failed | Hold. |
| SHORT red-bar requirement failed | Hold. |
| Previous high/low requirement failed | Hold. |
| Trend/VWAP/SMA filters failed | Hold. |
| VWAP extension too high | Hold. |
| No enabled setup pattern passed | Hold. |
| L1/L2 entry guard rejected | Hold. |
| Last-entry minute passed | Runtime blocks entry. |
| Minimum bars between entries | Runtime/conduct spacing blocks entry. |

The target implementation should separate **scanner ranking** from **conduct timing**. The scanner should say “this symbol is the best candidate now by points,” while conduct may still wait for the exact bar trigger. If the business goal is “find orders 1 by 1 until up to 10,” the scanner should not block all symbols just because the final trigger is not present yet; it should rank near-entry symbols and allow the runtime to keep watching them.

### 4.5 Runtime safety blockers that must remain

The following are not scanner blocks and should never be converted into scoring-only logic:

| Safety gate | Reason it must remain hard |
|---|---|
| Paper broker reconciliation mismatch | Prevents sending orders when local/broker state differs. |
| Open external orders not controlled by Sailor | Prevents duplicated/unknown risk. |
| TWS/API disconnected or degraded | Prevents orders without state awareness. |
| Account mismatch | Prevents sending to wrong account. |
| Live trading disabled | Prevents accidental live trades. |
| Max notional exceeded | Risk cap. |
| Emergency flatten / close-only state | Protects existing positions. |
| End-of-day force-flat | Prevents overnight exposure. |

The “0% blocks” scanner must not remove these runtime safety controls.

---

## 5. Why the current scanner produced no orders during the V18-Silver paper test

The latest 45-minute wait-for-entry test showed repeated cycles with:

```text
prepared=45
historyOk=45
candidates=0
tradeEligible=0
dataQuality=NoSelection
safety=CloseOnly
No orders sent
```

This means:

1. IBKR history was available.
2. Sailor could prepare the first 45 symbols from the Excel list.
3. The current scanner did not create any candidate that survived its hard filters and positive-score rule.
4. Because no candidate existed, `ScanListMemoryStore` retained no trade-eligible symbols.
5. Because there were no trade-eligible symbols, the paper conduct loop was correctly blocked.
6. Because no conduct loop ran, no paper orders were submitted.

This is a safe result, but it is too restrictive for discovery. A points-only scanner would have returned the best available symbols, even if they were weak, and would have made it clear why they were weak through penalties and evidence.

---

## 6. Target architecture: 100% points-based scanner, 0% scanner blocks

### 6.1 Design principle

Every symbol with at least minimally usable data should receive a score. The scanner must not say “no candidate” just because one factor fails. Instead:

```text
hard filter failed      -> negative points / penalty
strong factor passed    -> positive points
missing optional data   -> small penalty + evidence flag
missing critical data   -> candidate row with DataState=NotReady, not hidden
```

### 6.2 Proposed new architecture

```text
Universe symbols
        |
        v
History / realtime preparation
        |
        v
PointsScanner
        |
        +--> build SymbolScoreInput
        |       - latest OHLCV
        |       - previous OHLCV
        |       - EMA9 / SMA20 / SMA200 / VWAP
        |       - VolumeAverage20 / volumeRatio
        |       - momentum windows
        |       - range / candle body / gap / extension
        |       - optional L1/L2 snapshot
        |
        +--> Score LONG side
        |       - add points for favorable factors
        |       - subtract points for unfavorable factors
        |       - no scanner block
        |
        +--> Score SHORT side
        |       - add points for favorable factors
        |       - subtract points for unfavorable factors
        |       - no scanner block
        |
        +--> choose best side per symbol
        |
        v
PointsScannerCandidate list
        |
        +--> sorted by final score
        +--> top N retained even if score is low, if command allows
        +--> report includes all positive/negative factors
        |
        v
ScanListMemoryStore retains top N symbols
        |
        v
Conduct runtime watches selected symbols
        |
        +--> paper/live safety gates still hard
        +--> conduct exact-entry trigger may still wait
        |
        v
Order intent only when runtime/conduct says enter and safety gates allow
```

### 6.3 Scanner result categories

Each candidate should have both a numeric score and a status:

| Status | Meaning | Can be retained? | Can be used for order entry? |
|---|---|---:|---:|
| `Ready` | Data is sufficient and score is valid. | yes | yes, subject to conduct/runtime gates |
| `WeakReady` | Data is sufficient but score is below preferred threshold. | yes, if no stronger symbols | configurable |
| `WatchOnly` | Symbol has some useful evidence but missing important optional fields. | yes | no direct order until upgraded |
| `NotReady` | Critical data missing. | report only | no |

This keeps “0% scanner blocks” for reporting/ranking, while still avoiding unsafe order entry when critical data is absent.

### 6.4 Candidate output should include positive and negative factors

Example target display:

```text
 1. ILLR | Side=SHORT | Score=62.5 | Status=Ready
    + volume ratio 1.84 => +14.0
    + red bar body 0.42% => +8.0
    + below VWAP 0.31% => +6.5
    + short momentum 0.22% => +7.0
    - duplicate historical minute detected => -5.0
    - L2 unavailable => -2.0 advisory
```

### 6.5 Separate scanner score from conduct trigger

The scanner should answer:

> “Which symbols are best to watch and in which direction?”

The conduct strategy should answer:

> “Is this exact second/bar the correct moment to submit an order?”

The current system mixes these concepts. The points scanner should select good watch candidates even if the conduct trigger is not yet active.

---

## 7. Proposed points model V1

### 7.1 Score scale

Use a human-readable 0–100+ score, but allow negative scores internally.

| Score band | Meaning |
|---:|---|
| `>= 75` | Strong candidate. |
| `60–74.99` | Good candidate. |
| `45–59.99` | Watch candidate. |
| `25–44.99` | Weak candidate, keep only if universe is thin. |
| `< 25` | Low priority, report only unless command asks for fallback candidates. |

### 7.2 Data availability points

| Factor | Points |
|---|---:|
| Has at least `ScannerMinimumBars` | +10 |
| Has at least 20 bars but below profile minimum | +5 |
| Has less than 20 bars | -20 and `NotReady` |
| Latest candle timestamp is fresh | +8 |
| Latest candle stale but usable for backtest/local mode | -8 |
| Duplicate minute resolved | -3 to -8 depending count |
| Missing VWAP | -4 |
| Missing SMA200 | -2 advisory |
| Missing VolumeAverage20 | -8 |

### 7.3 Price/liquidity points

| Factor | Points |
|---|---:|
| Price inside preferred range | +8 |
| Price below minimum but tradable | -15 |
| Price above maximum but tradable | -10 |
| Latest volume >= minimum | +10 |
| Latest volume below minimum | proportional penalty down to -20 |
| Volume ratio >= 2.0 | +15 |
| Volume ratio 1.0–2.0 | +5 to +15 |
| Volume ratio 0.5–1.0 | -5 to +5 |
| Volume ratio < 0.5 | -15 |

### 7.4 Directional trend points

For LONG:

| Factor | Points |
|---|---:|
| EMA9 > SMA20 | +8 |
| EMA9 below SMA20 | -8 |
| Close > VWAP | +8 |
| Close below VWAP | -8 |
| Close > SMA200 | +4 |
| Close below SMA200 | -4 |
| Momentum positive over lookback | +0 to +20 scaled |
| Momentum negative over lookback | -0 to -20 scaled |

For SHORT, invert the same relationships:

| Factor | Points |
|---|---:|
| EMA9 < SMA20 | +8 |
| EMA9 above SMA20 | -8 |
| Close < VWAP | +8 |
| Close above VWAP | -8 |
| Close < SMA200 | +4 |
| Close above SMA200 | -4 |
| Momentum negative over lookback | +0 to +20 scaled |
| Momentum positive over lookback | -0 to -20 scaled |

### 7.5 V18-Silver / selective-short points

V18-Silver should receive a dedicated additive model so it can rank both near-short and near-long opportunities:

| V18 factor | LONG points | SHORT points |
|---|---:|---:|
| Green bar / red bar in selected direction | +10 for green LONG | +10 for red SHORT |
| Opposite candle color | -6 | -6 |
| Bar-to-bar momentum >= 0.10% in direction | +12 | +12 |
| VWAP reversion setup close to VWAP | +8 | +8 |
| Choppy-shield setup active | +6 | +6 |
| VWAP extension within 2.0% | +6 | +6 |
| VWAP extension above 2.0% | penalty scaled, not block | penalty scaled, not block |
| Volume >= 35k | +8 | +8 |
| VolumeRatio >= 0.70 | +8 | +8 |
| VolumeRatio below 0.70 | scaled penalty | scaled penalty |

### 7.6 L1/L2 advisory points

| Factor | Points |
|---|---:|
| L1 spread <= max spread | +6 |
| L1 spread > max spread | scaled penalty, e.g. -1 per 10 bps above max |
| Liquidity score >= minimum | +6 |
| Liquidity below minimum | scaled penalty |
| Book imbalance supports side | +5 |
| Book imbalance adverse | -5 to -15 |
| L2 unavailable | -2 advisory, not block |
| Synthetic snapshot | 0 or advisory penalty only |

### 7.7 Final side selection

For each symbol:

```text
longScore = base + long factors + long L1/L2 factors
shortScore = base + short factors + short L1/L2 factors
selectedSide = side with max score
finalScore = max(longScore, shortScore)
```

If the profile `SideMode` is LongOnly or ShortOnly, still calculate both for report/debug, but mark the disabled side as `SideDisabledByProfile` and do not use it for selected order side.

---

## 8. New implementation description

### 8.1 New scanner model classes

Add a new namespace or subfolder:

```text
src/Sailor.App/Backtest/Scanner/Points/
```

Proposed files:

| New file | Purpose |
|---|---|
| `PointsScanner.cs` | Main points-based scanner service. |
| `PointsScannerCandidate.cs` | Candidate model with score, side, status, factors. |
| `PointsScannerSideScore.cs` | LONG/SHORT per-side score details. |
| `PointsScannerFactor.cs` | Individual factor record: name, points, value, reason. |
| `PointsScannerSettings.cs` | Configurable score weights and thresholds. |
| `PointsScannerStatus.cs` | `Ready`, `WeakReady`, `WatchOnly`, `NotReady`. |
| `PointsScannerMode.cs` | `LegacyBlocks`, `PointsOnly`, `HybridCompare`. |
| `PointsScannerReportWriter.cs` | Detailed CSV/MD report writer. |

### 8.2 Backward compatibility

Add a scanner mode option:

```text
--scanner-mode legacy-blocks
--scanner-mode points-only
--scanner-mode hybrid-compare
```

Default should initially be:

```text
legacy-blocks
```

Then after validation:

```text
hybrid-compare
```

Finally:

```text
points-only
```

### 8.3 Runtime behavior in `points-only`

In `points-only` mode:

1. The scanner returns a ranked row for every prepared symbol with usable data.
2. `TopCount` selects the best N by points.
3. `ScanListMemoryStore` retains the selected top N even if no perfect setup exists.
4. Candidate status controls whether runtime can trade immediately:
   - `Ready`: can be watched for entry and may be used by conduct.
   - `WeakReady`: can be watched, but order entry can be configurable.
   - `WatchOnly`: shown/reported but no order entry.
   - `NotReady`: report only.
5. Paper order routing still requires reconciliation matched and safety normal.

### 8.4 Report changes

Add new report columns:

| Column | Purpose |
|---|---|
| `ScannerMode` | legacy / points-only / hybrid. |
| `CandidateStatus` | Ready, WeakReady, WatchOnly, NotReady. |
| `SelectedSide` | LONG or SHORT. |
| `FinalScore` | Final points score. |
| `LongScore` | Long side score. |
| `ShortScore` | Short side score. |
| `PositivePoints` | Sum positive points. |
| `NegativePoints` | Sum penalties. |
| `TopPositiveFactors` | Human-readable reasons. |
| `TopNegativeFactors` | Human-readable penalties. |
| `WouldHaveBeenBlockedByLegacy` | True/false. |
| `LegacyBlockReasons` | Reasons old scanner would discard it. |
| `CanTradeNow` | Ready + runtime data state. |
| `CanWatch` | Ready/WeakReady/WatchOnly. |

### 8.5 Key design rule

The new scanner must never return `candidates=0` when prepared history exists. It should return at least the top N ranked symbols unless all prepared symbols are truly data-unusable.

Expected output after transformation:

```text
prepared=45 historyOk=45 candidates=10 tradeEligible=10 scannerMode=points-only
```

Even if all candidates are weak, the output should be:

```text
candidates=10
candidateStatus=WeakReady or WatchOnly
orders may still wait for conduct trigger
```

---

## 9. Detailed step-by-step implementation plan

### Step 1 — Freeze current behavior with tests

1. Add regression test fixtures using a known local data set.
2. Verify current legacy scanner returns `candidates=0` for the latest V18-Silver case.
3. Save expected current report outputs as baseline evidence.
4. Do not change order routing yet.

Deliverables:

```text
src/Sailor.App.Tests/Scanner/LegacyScannerBehaviorTests.cs
```

Acceptance:

```text
legacy scanner still behaves exactly as before when scanner-mode=legacy-blocks
```

### Step 2 — Add scanner mode enum/config

1. Add `ScannerMode` property to `PaperScannerOptions`.
2. Parse command option `--scanner-mode`.
3. Supported values:
   - `legacy-blocks`
   - `points-only`
   - `hybrid-compare`
4. Add default setting under `Sailor.Scanner.DefaultMode` or runtime scanner config.

Files:

```text
src/Sailor.App/Scanner/Runtime/PaperScannerOptions.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
src/Sailor.App/Configuration/SailorAppSettings.cs
src/Sailor.App/appsettings.json
```

Acceptance:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --scanner-mode legacy-blocks
```

### Step 3 — Add points factor model

Create:

```text
PointsScannerFactor.cs
PointsScannerSideScore.cs
PointsScannerCandidate.cs
PointsScannerStatus.cs
```

Example records:

```csharp
public sealed record PointsScannerFactor(
    string Code,
    string Description,
    decimal Points,
    string RawValue,
    string Category);

public sealed record PointsScannerSideScore(
    string Side,
    decimal Score,
    IReadOnlyList<PointsScannerFactor> Factors,
    IReadOnlyList<string> LegacyBlockReasons);
```

Acceptance:

```text
candidate can explain exactly why it gained/lost points
```

### Step 4 — Build `PointsScannerSettings`

1. Add default score weights.
2. Allow profile-specific overrides later.
3. Keep V1 constants in code first to reduce config complexity.

Fields:

```text
MinimumBarsFullPoints
MinimumBarsPartialPoints
PriceRangePoints
VolumePoints
VolumeRatioPoints
MomentumWeight
EmaTrendPoints
VwapPositionPoints
Sma200Points
CandleColorPoints
VwapExtensionPenalty
L1SpreadPoints
LiquidityPoints
BookImbalancePoints
ReadyThreshold
WeakReadyThreshold
WatchOnlyThreshold
```

Acceptance:

```text
all weights centralized; no magic score constants spread through scanner code
```

### Step 5 — Implement `PointsScanner`

1. Load bars the same way current scanner does.
2. Calculate indicators using `TechnicalIndicatorCalculator.Calculate`.
3. Create a `SymbolScoreInput` with latest bar, previous bar, indicators, lookback close.
4. Score LONG side.
5. Score SHORT side.
6. Select side based on highest enabled score.
7. Return a candidate even if score is low.
8. Return a report-only candidate if data is partially missing.

Acceptance:

```text
For 45 prepared symbols with history, scanner returns up to top 10 candidates instead of zero.
```

### Step 6 — Preserve legacy block reasons as evidence

Inside the points scanner, evaluate the old block conditions but do not discard the symbol.

Example:

```text
LegacyBlock: latest volume 12,000 < 35,000
Penalty: -10.5 points
```

Acceptance:

```text
Hybrid report can show: old scanner would block, points scanner ranks anyway.
```

### Step 7 — Add V18-Silver scoring module

1. Add V18-specific factor group.
2. Include:
   - red/green candle direction,
   - bar-to-bar momentum,
   - VWAP reversion distance,
   - choppy-shield range/momentum,
   - max VWAP extension penalty,
   - volume and volume ratio.
3. Use profile name to activate V18 weighting.

Files:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerV18SilverScoring.cs
```

Acceptance:

```text
v18-silver can rank selective-short candidates even when current hard setup does not fully pass
```

### Step 8 — Integrate points scanner into `PaperScannerRunner`

Update `PaperScannerRunner.RunAsync`:

```text
if scanner-mode=legacy-blocks:
    use SailorScanner
if scanner-mode=points-only:
    use PointsScanner
if scanner-mode=hybrid-compare:
    run both and report comparison; route selected symbols from legacy until approved
```

Acceptance:

```text
existing commands keep working; new commands can activate points scanner
```

### Step 9 — Update candidate models and reports

Either extend `PaperScannerCandidate` or add a parallel `PaperPointsScannerCandidate`.

Recommended: extend `PaperScannerCandidate` carefully with optional points fields to minimize runtime changes.

Add CSV/MD report columns:

```text
ScannerMode, Status, SelectedSide, FinalScore, LongScore, ShortScore,
PositivePoints, NegativePoints, TopPositiveFactors, TopNegativeFactors,
LegacyBlockReasons
```

Acceptance:

```text
scanner CSV report explains every selected symbol, not only accepted symbols
```

### Step 10 — Update scan-list retention logic

Update `ScanListMemoryStore.RetainTradeCandidates` to understand status:

| Status | Retain? | Trade eligible? |
|---|---:|---:|
| `Ready` | yes | yes |
| `WeakReady` | yes | configurable |
| `WatchOnly` | yes | no by default |
| `NotReady` | optional | no |

Add options:

```text
--points-min-trade-score 45
--points-allow-weak-entry false
--points-retain-watch-only true
```

Acceptance:

```text
tradeEligible no longer becomes 0 simply because one hard scanner setup failed
```

### Step 11 — Update wait-for-scan-entry behavior

SAILOR-043 currently waits until at least one `tradeEligible` symbol appears. With points scanner:

1. If `Ready` candidates exist, start conduct loop.
2. If only `WeakReady` exists and weak entry is disabled, keep waiting.
3. If only `WatchOnly` exists, keep rescanning.
4. Print top watch candidates even while waiting.

Acceptance:

```text
operator can see the best 10 symbols and why Sailor is still waiting
```

### Step 12 — Add scanner-only command for diagnostics

Add a command that does not start conduct:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth
```

Output should include all factors.

Acceptance:

```text
operator can debug why V18-Silver gives no entry without starting paper order routing
```

### Step 13 — Add hybrid-comparison audit report

When `--scanner-mode hybrid-compare` is used, write:

```text
logs/Paper/Scanner/points_vs_legacy_YYYYMMDD_HHMMSS.csv
logs/Paper/Scanner/points_vs_legacy_YYYYMMDD_HHMMSS.md
```

Comparison columns:

```text
Symbol
LegacyCandidate yes/no
LegacyBlockReasons
PointsRank
PointsScore
PointsStatus
SelectedSide
WouldTradeByPoints
WouldTradeByLegacy
```

Acceptance:

```text
we can measure how many opportunities the legacy blocks remove
```

### Step 14 — Update paper certification report

Add fields:

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
```

Acceptance:

```text
paper report shows if scanner selection was points-based and why orders did or did not happen
```

### Step 15 — Update live pilot gate

Live should remain conservative:

1. Live read-only can show points-only rankings.
2. Live dry-run can use points-only selected symbol.
3. Live send-orders requires:
   - `scannerMode=points-only`,
   - paper report passed,
   - reconciliation matched,
   - selected symbol status `Ready`,
   - final score >= configured live threshold,
   - max notional gates,
   - operator confirmation.

Acceptance:

```text
points scanner improves selection but does not weaken live safety
```

### Step 16 — Add tests for “no candidates becomes top N weak/watch candidates”

Test the exact problem case:

1. Use Excel list with 81 symbols.
2. Mock or use cached history for 45 symbols.
3. Legacy mode returns `candidates=0`.
4. Points-only mode returns `candidates=10`.
5. Report shows penalties instead of silent discard.

Acceptance:

```text
The current no-order day becomes explainable and ranked, not invisible.
```

### Step 17 — Add operator commands

Paper scan only:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth
```

Paper wait-for-entry with points:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --send-orders --iterations 2700 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --wait-for-scan-entry --scan-entry-target 10 --scan-entry-wait-seconds 2700 --scan-refresh-seconds 300 --scanner-mode points-only --points-min-trade-score 45
```

Hybrid compare:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --dry-run --iterations 600 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --wait-for-scan-entry --scan-entry-target 10 --scan-entry-wait-seconds 600 --scan-refresh-seconds 60 --scanner-mode hybrid-compare
```

### Step 18 — Documentation update

Update:

```text
docs/2026-06-28_sailor_runtime_contracts_command_skeleton.md
docs/2026-06-29_sailor_scan_default_memory_candles_audit.md
```

Add this new audit file as the design source for SAILOR-044.

Acceptance:

```text
operator has a clear explanation of points scanner commands and reports
```

---

## 10. Migration strategy

### Phase A — Observe only

- Implement points scanner.
- Run `hybrid-compare` only.
- Do not let points scanner route orders.
- Compare old vs new rankings for several sessions.

### Phase B — Paper dry-run

- Enable `points-only` for paper dry-run only.
- Verify conduct loop produces reasonable intents.
- Verify reports show correct reasons and no unexpected entries.

### Phase C — Paper send-orders with 1 share

- Use `--quantity 1`.
- Use `--points-min-trade-score` conservative threshold, e.g. 60.
- Allow only `Ready` candidates.
- Keep `--no-depth` if L2 is unreliable, but use L1 where available.

### Phase D — Paper multi-symbol gradual scale

- Start top 1.
- Then top 3.
- Then top 5.
- Only then top 10.
- Keep max position notional and reconciliation gates.

### Phase E — Live read-only and live dry-run

- Live read-only points ranking.
- Live dry-run one symbol.
- No live send-orders until repeated paper reports are clean.

---

## 11. Acceptance criteria for SAILOR-044

A SAILOR-044 implementation should be accepted only when all of these pass:

1. `dotnet clean` succeeds.
2. `dotnet build` succeeds.
3. IBApi build succeeds.
4. `legacy-blocks` mode output is unchanged.
5. `points-only` returns candidates when history is usable.
6. V18-Silver with the Excel list returns up to 10 ranked symbols even when old scanner returns zero.
7. Reports show positive and negative factors.
8. No hidden hard scanner blocks remain in points-only mode.
9. Runtime safety gates still block real orders when reconciliation is not matched.
10. Paper `--send-orders` still sends no orders if there are no `Ready` candidates above threshold.
11. Paper `--send-orders` uses only selected side and quantity/risk gates.
12. Paper report includes scanner mode and points evidence.
13. Live read-only shows points ranking without creating an order router.
14. Live run remains blocked by live pilot gates unless explicit live conditions are met.

---

## 12. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Points scanner selects too many weak candidates | Use `points-min-trade-score` and `Ready` status. |
| Operator confuses watch candidates with trade-ready candidates | Report `Status`, `CanWatch`, and `CanTradeNow` separately. |
| Weak candidates generate too many paper orders | Start with dry-run, then `--quantity 1`, top 1, then top 3. |
| Removing scanner blocks weakens safety | Keep broker/reconciliation/order safety gates hard. |
| L2 unreliable blocks good symbols | Treat L2 as advisory points unless `RequireSnapshotForEntry=true`. |
| Conduct still blocks orders after points selection | This is acceptable; scanner ranks candidates, conduct times entries. Add diagnostics to explain conduct holds. |
| Old reports become incompatible | Add columns but preserve existing core fields. |

---

## 13. Recommended first implementation milestone

Recommended next milestone:

```text
SAILOR-044 — Points scanner model and hybrid comparison mode
```

Scope:

1. Add scanner mode enum/options.
2. Add points candidate/factor models.
3. Implement points-only scoring for current generic scanner factors.
4. Add hybrid comparison report.
5. Do not change order routing yet.

This isolates the risky conceptual change from the order-routing path.

---

## 14. Recommended second implementation milestone

```text
SAILOR-045 — V18-Silver points scoring and paper wait-for-entry integration
```

Scope:

1. Add V18-Silver scoring module.
2. Connect points status to `ScanListMemoryStore`.
3. Add `--points-min-trade-score`.
4. Allow paper dry-run to use points candidates.
5. Keep paper send-orders disabled unless explicitly requested and reconciliation is matched.

---

## 15. Recommended third implementation milestone

```text
SAILOR-046 — Points scanner certification and live read-only evidence
```

Scope:

1. Add points evidence to paper certification report.
2. Add points evidence to live scan-list read-only.
3. Add live pilot blocked-by-default points evidence.
4. Keep live send-orders unchanged and blocked by existing live gates.

---

## 16. Summary decision

The current scanner is safe but too block-heavy. It can produce no candidates even when IBKR history is available for all prepared symbols. The next scanner should be points-first:

```text
Current:
condition fails -> symbol disappears

Target:
condition fails -> symbol receives penalty, remains visible, ranked, explainable
```

The new scanner should therefore produce ranked candidates for every data-usable symbol, and only runtime safety should remain hard-blocking. This gives the operator a useful top 10 watch list, improves paper testing, and prevents silent `NoSelection` sessions where the system technically worked but produced no actionable explanation beyond `candidates=0`.

---

## 10. SAILOR-045 implementation — Steps 1 to 5 foundation

Date: 2026-06-29  
Status: implemented foundation; runtime default remains safe `legacy-blocks`.

### 10.1 Step 1 — Legacy behavior freeze

The first implementation keeps the current scanner behavior as the default:

```text
scannerMode=legacy-blocks
```

This means all existing commands continue to use `SailorScanner` and the current hard-filter behavior unless the operator explicitly asks for another mode. The legacy mode is the baseline for future comparison and keeps the current `candidates=0` behavior available for regression checks.

Manual regression commands:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth --max-symbols 45 --scanner-mode legacy-blocks
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --no-depth --max-symbols 45 --scanner-mode legacy-blocks
```

Expected legacy behavior is unchanged: a symbol must still survive all hard legacy filters before it can become a candidate.

### 10.2 Step 2 — Scanner mode enum/config

Added scanner mode support:

```text
legacy-blocks
points-only
hybrid-compare
```

New code:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerMode.cs
```

Updated runtime option model:

```text
src/Sailor.App/Scanner/Runtime/PaperScannerOptions.cs
```

Updated configuration:

```json
"Scanner": {
  "DefaultTopCount": 20,
  "DefaultMode": "legacy-blocks"
}
```

New command option:

```text
--scanner-mode legacy-blocks|points-only|hybrid-compare
```

The option is parsed by scan, scan-list, paper run scan-list selection, and live scan-list selection paths. The default remains `legacy-blocks`.

### 10.3 Step 3 — Points factor model

Added the points model foundation:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerFactor.cs
src/Sailor.App/Backtest/Scanner/Points/PointsScannerSideScore.cs
src/Sailor.App/Backtest/Scanner/Points/PointsScannerCandidate.cs
src/Sailor.App/Backtest/Scanner/Points/PointsScannerStatus.cs
```

Each points candidate now has:

```text
FinalScore
LongScore
ShortScore
SelectedSide
Status
PositivePoints
NegativePoints
LegacyBlockReasons
TopPositiveFactors
TopNegativeFactors
```

The candidate can also be converted back to the existing `ScannerCandidate` model so the runtime can consume it without changing conduct/order-routing code.

### 10.4 Step 4 — PointsScannerSettings

Added centralized score weights:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerSettings.cs
```

The first implementation keeps weights in code, as planned, to reduce configuration complexity while the model is validated. Main categories:

```text
data availability
price range
volume
volume ratio
lookback momentum
EMA trend
VWAP position
SMA200 position
V18-Silver candle color
V18-Silver bar momentum
V18-Silver VWAP reversion/extension
V18-Silver choppy shield
```

### 10.5 Step 5 — PointsScanner

Added:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScanner.cs
```

The points scanner:

1. Loads bars using the existing `CsvBacktestDataProvider`.
2. Calculates indicators using `TechnicalIndicatorCalculator.Calculate`.
3. Scores LONG and SHORT sides independently.
4. Converts former hard scanner blocks into penalties and `LegacyBlockReasons`.
5. Adds positive points for favorable factors.
6. Selects the best enabled side.
7. Returns ranked candidates even when the legacy scanner would have returned no candidate.

Runtime integration added for:

```text
--scanner-mode points-only
```

When explicitly used, `PaperScannerRunner` uses `PointsScanner` and writes candidate rows through the existing scanner report pipeline. Default behavior remains unchanged.

`hybrid-compare` is currently parsed and logged, but routing still uses legacy mode until the dedicated hybrid comparison report is implemented in the later audit steps.

### 10.6 SAILOR-045 smoke-test commands

Default legacy behavior:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth --max-symbols 45 --scanner-mode legacy-blocks
```

Points-only local-cache scan:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth --max-symbols 45 --scanner-mode points-only
```

Points-only IBKR paper scan-list observation:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-list 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --no-depth --max-symbols 45 --scanner-mode points-only --wait-seconds 15
```

Points-only paper run dry-run only:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --dry-run --iterations 60 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --scanner-mode points-only
```

Paper send-orders with `points-only` should wait until later steps complete the trade-eligibility/status controls. Use dry-run for SAILOR-045 validation.

---

## 17. SAILOR-048 implementation — Steps 15 to 18 completion

Date: 2026-06-29  
Status: implemented final scanner-side points workflow and live safety hardening.

### 17.1 Step 15 — Live pilot gate

SAILOR-048 adds a dedicated live points pilot gate. The gate is advisory for live read-only and live dry-run, but mandatory for live send-orders.

For live send-orders, all existing SAILOR live gates still apply, plus:

```text
scannerMode=points-only
selected scan-list symbol exists
selected points status=Ready
selected points score >= --points-min-trade-score
```

This means points selection can improve live pilot symbol selection, but it cannot weaken broker safety. Live orders remain blocked unless the paper certification, account match, max notional, operator confirmation, scan-list data quality, and live reconciliation gates also pass.

### 17.2 Step 16 — Regression/self-test command

SAILOR-048 adds:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points-test 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --no-depth --wait-seconds 15
```

The command runs a legacy-blocks pass and a points-only pass, starts no conduct loop, and sends no orders. It reports whether points-only produced ranked candidate evidence where legacy blocks would otherwise remove or reduce the candidate set.

Optional strict historical check:

```powershell
--expect-legacy-zero
```

### 17.3 Step 17 — Final operator commands

Paper diagnostics:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --points-min-trade-score 45 --no-depth --wait-seconds 15
```

Paper wait-for-entry:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --send-orders --iterations 2700 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --wait-for-scan-entry --scan-entry-target 10 --scan-entry-wait-seconds 2700 --scan-refresh-seconds 300 --scanner-mode points-only --points-min-trade-score 45
```

Hybrid comparison:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode hybrid-compare --no-depth --wait-seconds 15
```

Live read-only points ranking:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --max-symbols 45 --scanner-mode points-only --no-depth --read-only --wait-seconds 15
```

Live dry-run one-symbol pilot:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run 1m v18-silver 1 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --max-notional 100 --confirm-live --operator-watching-tws --dry-run --local-cache --no-depth --iterations 5 --scanner-mode points-only --points-min-trade-score 45
```

### 17.4 Step 18 — Documentation

SAILOR-048 updates:

```text
docs/2026-06-28_sailor_runtime_contracts_command_skeleton.md
docs/2026-06-29_sailor_scan_default_memory_candles_audit.md
docs/2026-06-29_sailor_points_scanner_steps_15_18_notes.md
```

The audit remains the design source for the scanner migration.

## 18. SAILOR-049 implementation — Common points scoring for all strategies

Date: 2026-06-30

SAILOR-049 makes the strongest scanner-side points scoring common to all strategy profiles.

The earlier SAILOR-046 implementation introduced a V18-Silver-specific scoring module. That proved the points model worked, but it meant V18-Silver received richer factor evidence than other profiles.

SAILOR-049 replaces that runtime behavior with a shared module:

```text
src/Sailor.App/Backtest/Scanner/Points/PointsScannerCommonStrategyScoring.cs
```

The main scanner now applies these common factors to every resolved `SailorStrategyProfile`:

```text
PROFILE_CANDLE_COLOR / PROFILE_CANDLE_COLOR_ADVERSE
PROFILE_BAR_MOMENTUM / PROFILE_BAR_MOMENTUM_MISSING
PROFILE_VWAP_REVERSION
PROFILE_VWAP_EXTENSION_OK / PROFILE_VWAP_EXTENSION_HIGH / PROFILE_VWAP_MISSING
PROFILE_BODY_CONTROLLED / PROFILE_BODY_EXTENDED
PROFILE_VOL_RATIO_OK / PROFILE_VOL_RATIO_LOW / PROFILE_VOL_RATIO_NOT_REQUIRED
```

The factors are common, but not blind: they use the active profile's own settings such as `EntryMomentumPercent`, `MinimumVolumeRatio`, `SideMode`, and indicator requirements. Therefore `v18-silver`, `v21-15minutes`, `harvester-conduct-v3`, `harvester-conduct-v9`, and configured profiles are all scored through the same points framework while preserving their profile-specific thresholds.

`PointsScannerV18SilverScoring` remains only as a compatibility shim and delegates to the common scorer. It should not be extended with new V18-only factors unless a future audit explicitly asks for a strategy-specific overlay.

### 18.1 Strategy profiles to validate with the common scanner

The common points scanner should be tested against every built-in scan-list strategy profile, not only V18-Silver. The purpose of this validation is to prove that each profile produces explainable `PROFILE_*` factor evidence and that the scanner selection layer can rank candidates without reverting to legacy hard scanner blocks.

Canonical strategy/profile names to test:

| Profile name | Scanner validation command pattern | Notes |
|---|---|---|
| `v18-silver` | `paper scan-points 1m v18-silver 10 ... --scanner-mode points-only` | Original V18 selective-short validation profile. |
| `v21-15minutes` | `paper scan-points 1m v21-15minutes 10 ... --scanner-mode points-only` | 15-minute profile; already used to confirm common `PROFILE_*` factors outside V18. |
| `v22-15minutes` | `paper scan-points 1m v22-15minutes 10 ... --scanner-mode points-only` | 15-minute profile variant. |
| `v23-5minutes` | `paper scan-points 1m v23-5minutes 10 ... --scanner-mode points-only` | 5-minute profile variant. |
| `v24-5minutes` | `paper scan-points 1m v24-5minutes 10 ... --scanner-mode points-only` | 5-minute profile variant; also mapped by the `harvester-conduct-v9` alias for conduct creation. |
| `v16-sqzbreakout` | `paper scan-points 1m v16-sqzbreakout 10 ... --scanner-mode points-only` | Squeeze-breakout conduct profile. |
| `v13` | `paper scan-points 1m v13 10 ... --scanner-mode points-only` | Built-in conduct profile. |
| `v12` | `paper scan-points 1m v12 10 ... --scanner-mode points-only` | Built-in conduct profile. |
| `v10-hybrid` | `paper scan-points 1m v10-hybrid 10 ... --scanner-mode points-only` | Hybrid conduct profile. |
| `v17-hybridflow` | `paper scan-points 1m v17-hybridflow 10 ... --scanner-mode points-only` | Hybrid-flow conduct profile. |
| `v2-conduct` | `paper scan-points 1m v2-conduct 10 ... --scanner-mode points-only` | V2 conduct-flow profile. |
| `v1-first` | `paper scan-points 1m v1-first 10 ... --scanner-mode points-only` | First conduct profile. |
| `v19-purplecloud` | `paper scan-points 1m v19-purplecloud 10 ... --scanner-mode points-only` | Purple-cloud conduct profile. |
| `v15-shortcap` | `paper scan-points 1m v15-shortcap 10 ... --scanner-mode points-only` | Short-cap profile. |
| `v14-smallcap` | `paper scan-points 1m v14-smallcap 10 ... --scanner-mode points-only` | Small-cap profile. |
| `v20-gen001-choppyshield` | `paper scan-points 1m v20-gen001-choppyshield 10 ... --scanner-mode points-only` | Choppy-shield profile. |
| `conduct-v3` | `paper scan-points 1m conduct-v3 10 ... --scanner-mode points-only` | Conduct V3 / Catamaran canonical profile. |
| `sailor-conduct-v3` | `paper scan-points 1m sailor-conduct-v3 10 ... --scanner-mode points-only` | Alias/profile variant of Conduct V3. |
| `harvester-conduct-v3` | `paper scan-points 1m harvester-conduct-v3 10 ... --scanner-mode points-only` | Harvester V3 conduct profile. |
| `harvester-conduct-v9` | `paper scan-points 1m harvester-conduct-v9 10 ... --scanner-mode points-only` | Harvester V9 conduct profile alias; scanner should still emit common points evidence. |

Short aliases such as `v18`, `v21`, `v23`, `v24`, `v22`, `v16`, `v10`, `v17`, `v2`, `v1`, `v19`, `v15`, `v14`, `v20`, `conduct`, `catamaran`, `harvester-v3`, and `harvester-v9` normalize internally to the canonical names above. Scanner validation should use the canonical names in logs and documentation so reports are easy to compare.

Recommended PowerShell loop for scanner-only validation of all canonical profiles:

```powershell
$profiles = @(
  "v18-silver",
  "v21-15minutes",
  "v22-15minutes",
  "v23-5minutes",
  "v24-5minutes",
  "v16-sqzbreakout",
  "v13",
  "v12",
  "v10-hybrid",
  "v17-hybridflow",
  "v2-conduct",
  "v1-first",
  "v19-purplecloud",
  "v15-shortcap",
  "v14-smallcap",
  "v20-gen001-choppyshield",
  "conduct-v3",
  "sailor-conduct-v3",
  "harvester-conduct-v3",
  "harvester-conduct-v9"
)

foreach ($profile in $profiles) {
  Write-Host "=== Testing points scanner profile $profile ==="
  dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m $profile 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
}
```

Expected validation result for each profile:

```text
scannerMode=points-only
pointsCandidates > 0, when usable history exists
report=<scanner csv path>
candidate explanations include PROFILE_* factor codes
No orders sent.
```

A profile can still produce `dataQuality=Blocked`, `staleSelected`, or `safety=CloseOnly` if history/market-data/reconciliation evidence is not clean. That is not a common-scanner failure. It only means the runtime correctly prevents entry routing while keeping scanner diagnostics available.

Validation commands:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --no-depth --wait-seconds 15
```

Expected result: top candidate explanations contain `PROFILE_*` factor codes for both profiles, proving the common scoring layer is active outside V18-Silver.
