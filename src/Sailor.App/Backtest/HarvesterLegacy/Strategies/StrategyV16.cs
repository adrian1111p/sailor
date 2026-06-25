using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// S16 "Squeeze Breakout" â€” Bollinger/Keltner squeeze-release with
// multi-oscillator confluence scoring.
//
// THESIS: When Bollinger Bands compress inside Keltner Channels for several
//         bars (low volatility squeeze), energy builds.  When the squeeze
//         releases (BB expands past KC), enter in the breakout direction
//         only if a multi-factor confluence score confirms the move.
//
// KEY INNOVATIONS vs V13 (ORB pullback):
//   â€¢ Volatility-regime entry (squeezeâ†’release) instead of pattern-based ORB
//   â€¢ Multi-oscillator scoring (RSI + Stochastic + Williams %R + MFI)
//   â€¢ Keltner Channel & Bollinger Band squeeze detection
//   â€¢ Donchian channel breakout confirmation
//   â€¢ VWAP mean-reversion context
//   â€¢ DPO cycle-position awareness
//   â€¢ Supertrend trend alignment
//   â€¢ MACD histogram momentum-turn detection
//   â€¢ All P2/P3/P4 exit features from tight-r
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>
/// Configuration record for StrategyV16 "Squeeze Breakout".
/// <para>
/// Controls every tunable dimension of the strategy: risk/sizing, entry direction,
/// cooldown, price/liquidity gates, time windows, squeeze detection thresholds,
/// multi-oscillator confluence scoring, HTF bias rules, L2 order-flow filters,
/// exit cascade parameters, and P2/P3/P4 exit features.
/// </para>
/// <para>
/// Default values represent the optimised parameter set from backtest comparison
/// runs (small-cap universe, 1-minute bars). Override individual properties via
/// object initialiser or dedicated factory methods.
/// </para>
/// </summary>
public sealed record class V16Config
{
    // â”€â”€ Risk / Position sizing â”€â”€

    /// <summary>Fixed dollar risk per trade used to size positions (default $24).</summary>
    public double RiskPerTradeDollars { get; init; } = 24.0;

    /// <summary>Notional account equity for max-position-pct calculations.</summary>
    public double AccountSize { get; init; } = 25_000.0;

    /// <summary>Maximum single-position notional as a fraction of <see cref="AccountSize"/> (18%).</summary>
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.18;

    /// <summary>Hard cap on share count regardless of risk math.</summary>
    public int MaxShares { get; init; } = 6_500;

    /// <summary>Optional per-trade USD loss cap that simulates the live per-symbol market flatten guard.</summary>
    public double SymbolLossFlattenUsd { get; init; }

    /// <summary>Minimum allowable risk-per-share to avoid dust-sized stops ($0.05 = nickel floor).</summary>
    public double MinRiskPerShare { get; init; } = 0.05;

    // â”€â”€ Controlled-variant adaptive recovery levers â”€â”€

    /// <summary>When true, intraday context adjusts confluence and size without banning any hour bucket.</summary>
    public bool EnableTimeContextAdaptation { get; init; } = false;

    /// <summary>Extra score required during late-session trading for the time-context variant.</summary>
    public int TimeContextLateSessionScoreBoost { get; init; } = 1;

    /// <summary>Position-size multiplier applied during late-session trading for the time-context variant.</summary>
    public double TimeContextLateSessionPositionScale { get; init; } = 0.75;

    /// <summary>Optional position-size multiplier used during the opening impulse window.</summary>
    public double TimeContextOpeningPositionScale { get; init; } = 1.0;

    /// <summary>Specific hard-environment UTC hours that remain tradable but demand stronger evidence and smaller size.</summary>
    public int[] TimeContextWeakHoursUtc { get; init; } = [];

    /// <summary>Extra score required during configured weak hours.</summary>
    public int TimeContextWeakHourScoreBoost { get; init; } = 1;

    /// <summary>Position-size multiplier applied during configured weak hours.</summary>
    public double TimeContextWeakHourPositionScale { get; init; } = 0.75;

    /// <summary>When true, weak microstructure reduces size and demands stronger evidence instead of excluding the symbol.</summary>
    public bool EnableDepthContextAdaptation { get; init; } = false;

    /// <summary>Extra score required when spread/liquidity/order-flow context is fragile.</summary>
    public int DepthContextPoorBookScoreBoost { get; init; } = 1;

    /// <summary>Position-size multiplier applied when the order book is fragile.</summary>
    public double DepthContextPoorBookPositionScale { get; init; } = 0.75;

    /// <summary>When true, size adapts to ATR/price regime while every symbol remains eligible.</summary>
    public bool EnableVolatilityNormalizedSizing { get; init; } = false;

    /// <summary>ATR/price ratio above which the variant scales down risk.</summary>
    public double VolatilityNormalizedHighAtrPct { get; init; } = 0.08;

    /// <summary>ATR/price ratio below which the variant trims size to avoid oversizing quiet names.</summary>
    public double VolatilityNormalizedLowAtrPct { get; init; } = 0.012;

    /// <summary>Position-size multiplier applied in high-volatility regimes.</summary>
    public double VolatilityNormalizedHighAtrPositionScale { get; init; } = 0.72;

    /// <summary>Position-size multiplier applied in very low-volatility regimes.</summary>
    public double VolatilityNormalizedLowAtrPositionScale { get; init; } = 0.84;

    /// <summary>When true, fragile symbol contexts demand more confluence rather than banning the symbol.</summary>
    public bool EnableSymbolContextAdaptation { get; init; } = false;

    /// <summary>Extra score required when the symbol context is fragile.</summary>
    public int SymbolContextFragileScoreBoost { get; init; } = 1;

    /// <summary>Position-size multiplier applied when the symbol context is fragile.</summary>
    public double SymbolContextFragilePositionScale { get; init; } = 0.75;

    /// <summary>When true, likely hard-stop contexts are pre-empted with extra confirmation, lower risk, and a slightly wider noise budget.</summary>
    public bool EnableHardStopPreemptAdaptation { get; init; } = false;

    /// <summary>Extra score required when adverse-selection pressure suggests elevated hard-stop risk.</summary>
    public int HardStopPreemptScoreBoost { get; init; } = 1;

    /// <summary>Position-size multiplier applied when hard-stop pressure is elevated.</summary>
    public double HardStopPreemptPositionScale { get; init; } = 0.65;

    /// <summary>Risk-budget multiplier applied when hard-stop pressure is elevated.</summary>
    public double HardStopPreemptRiskScale { get; init; } = 0.75;

    /// <summary>Stop-distance multiplier applied when hard-stop pressure is elevated.</summary>
    public double HardStopPreemptStopDistanceMultiplier { get; init; } = 1.15;

    /// <summary>Spread z-score threshold that counts as elevated adverse-selection pressure.</summary>
    public double HardStopPreemptSpreadZThreshold { get; init; } = 3.0;

    /// <summary>Minimum L2 liquidity score expected before the setup is treated as thin-depth pressure.</summary>
    public double HardStopPreemptThinLiquidityThreshold { get; init; } = 3.5;

    /// <summary>ATR ratio threshold that counts as a volatility spike.</summary>
    public double HardStopPreemptVolatilitySpikeAtrRatioThreshold { get; init; } = 1.35;

    /// <summary>Single-bar range, expressed in ATRs, that counts as a volatility spike.</summary>
    public double HardStopPreemptVolatilitySpikeBarRangeAtrThreshold { get; init; } = 1.60;

    // â”€â”€ Entry direction â”€â”€

    /// <summary>Allow long (buy) entries.</summary>
    public bool AllowLong { get; init; } = true;

    /// <summary>Allow short (sell) entries.</summary>
    public bool AllowShort { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, entry price is the open of the bar following the signal bar,
    /// simulating a next-bar market order rather than same-bar close fill.
    /// </summary>
    public bool UseNextBarOpenEntry { get; init; } = true;

    // â”€â”€ Cooldown / signals-per-day â”€â”€

    /// <summary>Minimum bars between accepted signals (prevents cluster entries).</summary>
    public int CooldownBars { get; init; } = 8;

    /// <summary>Maximum number of signals accepted per trading day.</summary>
    public int MaxSignalsPerDay { get; init; } = 3;

    // â”€â”€ Price / liquidity gate â”€â”€

    /// <summary>Minimum close price to consider (filters penny stocks).</summary>
    public double MinPrice { get; init; } = 0.3;

    /// <summary>Maximum close price to consider.</summary>
    public double MaxPrice { get; init; } = 700.0;

    /// <summary>Minimum relative volume (current bar vol / average vol) required.</summary>
    public double RvolMin { get; init; } = 0.80;

    /// <summary>Minimum L2 order-book liquidity score for entry eligibility.</summary>
    public double L2LiquidityMin { get; init; } = 15.0;

    /// <summary>Maximum bid-ask spread z-score; rejects illiquid tickers.</summary>
    public double SpreadZMax { get; init; } = 2.5;

    /// <summary>Minimum volume acceleration (rate of change of volume); rejects drying-up flow.</summary>
    public double MinVolAccel { get; init; } = -0.15;

    // â”€â”€ Time window â”€â”€

    /// <summary>Market open expressed as minute-of-day ET (570 = 9:30 AM).</summary>
    public int MarketOpenMinute { get; init; } = 570;

    /// <summary>Skip the first N minutes after open to avoid volatile open prints.</summary>
    public int SkipFirstNMinutes { get; init; } = 20;

    /// <summary>No entries within this many minutes before market close (90 = no last-90-min entries).</summary>
    public int LastEntryMinuteBeforeClose { get; init; } = 90;

    /// <summary>Allowed entry windows as (Start, End) minute-of-day ET pairs.</summary>
    public (int Start, int End)[] EntryWindows { get; init; } = [(590, 900)];

    // â”€â”€ Indecision-bar filter â”€â”€

    /// <summary>
    /// When <c>true</c>, reject signals whose signal bar is an indecision candle
    /// (doji/cross/spinning-top) â€” body is less than <see cref="IndecisionBarMaxBodyPct"/>
    /// of the bar's total range.  Prevents entries on ambiguous bars that frequently reverse.
    /// </summary>
    public bool RejectIndecisionBar { get; init; } = true;

    /// <summary>
    /// Maximum body-to-range ratio (0â€“1) below which a bar is classified as
    /// indecision.  Default 0.25 means a bar whose body is â‰¤ 25 % of its
    /// high-low range is rejected.
    /// </summary>
    public double IndecisionBarMaxBodyPct { get; init; } = 0.25;

    /// <summary>When true, long entries require a bullish breakout signal bar instead of any long-leaning release bar.</summary>
    public bool RequireBullishBreakoutBarForLongs { get; init; } = false;

    /// <summary>Minimum close location within the signal-bar range required for long breakout bars.</summary>
    public double LongBreakoutMinCloseLocationPct { get; init; } = 0.50;

    /// <summary>Maximum upper-wick share of the signal-bar range allowed for long breakout bars.</summary>
    public double LongBreakoutMaxUpperWickPct { get; init; } = 0.45;

    /// <summary>Minimum directional score required to classify a squeeze release as a breakout.</summary>
    public int BreakoutDirectionMinScore { get; init; } = 2;

    // â”€â”€ Squeeze detection â”€â”€

    /// <summary>Minimum consecutive bars where BB sits inside KC to qualify as a valid squeeze.</summary>
    public int SqueezeMinBars { get; init; } = 4;

    /// <summary>How many bars back from the release to search for the squeeze window.</summary>
    public int SqueezeLookback { get; init; } = 14;

    /// <summary>Entry must occur within this many bars after the squeeze releases.</summary>
    public int SqueezeReleaseMaxBars { get; init; } = 3;

    /// <summary>
    /// BB bandwidth rolling percentile threshold (0â€“1). Only squeezes where
    /// bandwidth was at or below this percentile qualify, ensuring genuinely
    /// low-volatility compression rather than a brief crossover.
    /// </summary>
    public double SqueezeBandwidthMaxPctile { get; init; } = 0.40;

    // â”€â”€ Direction confirmation â”€â”€

    /// <summary>Minimum ADX required for directional energy (filters range-bound chop).</summary>
    public double AdxMin { get; init; } = 12.0;

    /// <summary>Maximum ADX allowed (avoids parabolic exhaustion zones).</summary>
    public double AdxMax { get; init; } = 55.0;

    // â”€â”€ Multi-oscillator thresholds (longs) â”€â”€

    /// <summary>RSI lower bound for long entries (rejects deeply oversold).</summary>
    public double RsiLongMin { get; init; } = 40.0;

    /// <summary>RSI upper bound for long entries (rejects overbought).</summary>
    public double RsiLongMax { get; init; } = 75.0;

    /// <summary>Stochastic %K minimum for long entries (confirms upward momentum).</summary>
    public double StochLongMin { get; init; } = 20.0;

    /// <summary>Williams %R ceiling for longs; values above this are less oversold, confirming upside.</summary>
    public double WillRLongMax { get; init; } = -25.0;

    /// <summary>Money Flow Index floor for longs (confirms buying pressure).</summary>
    public double MfiLongMin { get; init; } = 30.0;

    // â”€â”€ Multi-oscillator thresholds (shorts) â”€â”€

    /// <summary>RSI lower bound for short entries.</summary>
    public double RsiShortMin { get; init; } = 25.0;

    /// <summary>RSI upper bound for short entries (rejects extreme reads).</summary>
    public double RsiShortMax { get; init; } = 60.0;

    /// <summary>Stochastic %K ceiling for short entries (confirms downward momentum).</summary>
    public double StochShortMax { get; init; } = 80.0;

    /// <summary>Williams %R floor for shorts; values below this are less overbought, confirming downside.</summary>
    public double WillRShortMin { get; init; } = -75.0;

    /// <summary>Money Flow Index ceiling for shorts (confirms selling pressure).</summary>
    public double MfiShortMax { get; init; } = 70.0;

    // â”€â”€ Confluence scoring â”€â”€

    /// <summary>
    /// Minimum multi-factor confluence score (out of ~10 possible) required to
    /// accept a signal. Each factor (RSI, Stoch, WillR, MFI, MACD, ADX, VWAP,
    /// DPO, Donchian, L2 OFI) contributes 0 or 1 point.
    /// </summary>
    public int MinConfluenceScore { get; init; } = 4;

    /// <summary>Optional higher confluence requirement applied only to long entries.</summary>
    public int? LongMinConfluenceScore { get; init; } = 1;

    /// <summary>Optional higher confluence requirement applied only to short entries.</summary>
    public int? ShortMinConfluenceScore { get; init; }

    // â”€â”€ HTF bias â”€â”€

    /// <summary>When <c>true</c>, higher-timeframe bias must agree (or be neutral/weak) for entry.</summary>
    public bool RequireHtfBias { get; init; } = true;

    /// <summary>Allow entry when HTF bias is NEUTRAL (neither bullish nor bearish).</summary>
    public bool AllowNeutralHtf { get; init; } = true;

    /// <summary>Allow entry against a weak HTF trend if confluence is high enough.</summary>
    public bool AllowWeakCounterTrendHtf { get; init; } = true;

    /// <summary>Minimum confluence score required when trading against the HTF bias.</summary>
    public int WeakCounterTrendMinScore { get; init; } = 5;

    // â”€â”€ L2 / OFI entry filter â”€â”€

    /// <summary>Require L2 order-flow confirmation before entry.</summary>
    public bool RequireL2EntryFilter { get; init; } = true;

    /// <summary>Require L2 order-flow confirmation only for long entries while leaving shorts unchanged.</summary>
    public bool RequireL2EntryFilterForLongsOnly { get; init; } = false;

    /// <summary>Minimum Order Flow Imbalance signal for longs (negative = mild selling tolerated).</summary>
    public double L2OfiMinLong { get; init; } = -0.15;

    /// <summary>Maximum OFI signal for shorts (positive = mild buying tolerated).</summary>
    public double L2OfiMaxShort { get; init; } = 0.15;

    /// <summary>Minimum bid-side imbalance ratio for long entries (bids outweigh asks).</summary>
    public double L2ImbalanceMinLong { get; init; } = 0.65;

    /// <summary>Maximum ask-side imbalance ratio for short entries (asks outweigh bids).</summary>
    public double L2ImbalanceMaxShort { get; init; } = 1.45;

    // â”€â”€ Exit parameters (tight-r based + extended hold) â”€â”€

    /// <summary>Hard stop distance in R-multiples. Position is closed immediately if hit.</summary>
    public double HardStopR { get; init; } = 0.55;

    /// <summary>R-multiple at which the stop is moved to breakeven.</summary>
    public double BreakevenR { get; init; } = 0.50;

    /// <summary>Trailing stop distance in R-multiples once breakeven has been reached.</summary>
    public double TrailR { get; init; } = 0.15;

    /// <summary>Maximum giveback from peak unrealised P&amp;L as a fraction (60 %).</summary>
    public double GivebackPct { get; init; } = 0.60;

    /// <summary>Absolute dollar cap on giveback from peak unrealised P&amp;L.</summary>
    public double GivebackUsdCap { get; init; } = 28.0;

    /// <summary>First profit target in R-multiples (partial exit).</summary>
    public double Tp1R { get; init; } = 1.16;

    /// <summary>Second profit target in R-multiples (full exit of remainder).</summary>
    public double Tp2R { get; init; } = 2.10;

    /// <summary>Maximum number of bars to hold a position before time-based exit.</summary>
    public int MaxHoldBars { get; init; } = 45;

    /// <summary>When <c>true</c>, reaching TP1 tightens the stop to breakeven.</summary>
    public bool Tp1TightenToBe { get; init; } = true;

    /// <summary>Fraction of peak unrealised gain to keep; exit if giveback exceeds this.</summary>
    public double PeakGivebackKeepFraction { get; init; } = 0.45;

    /// <summary>R-multiple above which the peak-giveback exit becomes active.</summary>
    public double PeakGivebackActivateR { get; init; } = 0.70;

    /// <summary>Number of bars of sideways drift before triggering a stagnation exit.</summary>
    public int StagnationBars { get; init; } = 9;

    /// <summary>Minimum peak R required before stagnation exit logic activates.</summary>
    public double StagnationMinPeakR { get; init; } = 0.30;

    /// <summary>Maximum adverse R tolerated during the stagnation window.</summary>
    public double StagnationMaxAdverseR { get; init; } = -0.12;

    // â”€â”€ Indicator-driven trade conduct (post-entry) â€” added 2026-04-26 â”€â”€

    /// <summary>
    /// Fraction of original size to scale out at TP1 (0.0 = no scale, 1.0 = full close).
    /// 0.40 locks 40 % of position at TP1, lets the remainder run on the trail/TP2 path.
    /// Smooths the equity curve (improves Sharpe) without sacrificing the upside tail.
    /// </summary>
    public double Tp1PartialClosePct { get; init; } = 0.40;

    /// <summary>
    /// ATR-buffered breakeven offset applied when TP1 tightens the stop to BE.
    /// 0.05 = TP1 BE sits 5 % of an ATR above (long) / below (short) entry, preventing
    /// stop-out on the immediate post-TP1 noise candle.
    /// </summary>
    public double Tp1BreakevenBufferAtr { get; init; } = 0.05;

    /// <summary>
    /// Enable EMA9-based dynamic trailing stop after breakeven activates.
    /// Uses the bar's <c>Ema9</c> indicator as the trail anchor, offset by
    /// <see cref="EmaTrailBufferAtr"/> ATRs. This is an indicator-driven trail that
    /// adapts to trend curvature (tighter in low vol, wider in high vol).
    /// </summary>
    public bool UseEmaTrail { get; init; } = true;

    /// <summary>ATR buffer below (long) / above (short) the EMA9 trail line.</summary>
    public double EmaTrailBufferAtr { get; init; } = 0.12;

    /// <summary>
    /// Enable ATR-based trailing TP2 with directional-continuation profit extension.
    /// When peak R reaches <see cref="Tp2R"/> and continuation still confirms (candle + L1 + L2),
    /// the engine arms an ATR-distance trail (<see cref="TrailingTp2AtrMultiplier"/> Ã— ATR) and
    /// defers the exit while the trend persists. Flattens on the first bar where continuation
    /// no longer holds. Captures more of the right-tail winners that previously gave back to TP2.
    /// </summary>
    public bool UseTrailingTp2 { get; init; } = true;

    /// <summary>ATR multiplier for the trailing TP2 distance.</summary>
    public double TrailingTp2AtrMultiplier { get; init; } = 0.55;

    /// <summary>
    /// Enable L1/L2-decision check on the two-opposite-bars flatten path.
    /// Adds an order-flow-aware confirmation before flattening on opposing candles,
    /// reducing premature exits during shake-out wicks while still cutting losers
    /// when the order book actually flips.
    /// </summary>
    public bool UseL1L2DecisionOnOppositeBarsFlatten { get; init; } = true;

    /// <summary>Simulated slippage deducted at entry/exit (cents per share).</summary>
    public double SlippageCents { get; init; } = 1.0;

    /// <summary>Commission charged per share for P&amp;L calculation.</summary>
    public double CommissionPerShare { get; init; } = 0.005;

    // â”€â”€ P2/P3/P4 features â”€â”€

    /// <summary>Enable price-tier-aware micro trailing stop (tighter trail for sub-$5 stocks).</summary>
    public bool UsePriceTierMicroTrail { get; init; } = true;

    /// <summary>Enable price-tier stop floor (prevents stop from being too tight on low-priced names).</summary>
    public bool UsePriceTierStopFloor { get; init; } = true;

    /// <summary>Enable MA-extension + L2-flip exit: flatten when price overextends past EMA and L2 flips.</summary>
    public bool UseMaExtensionL2Flip { get; init; } = true;

    /// <summary>Minimum R in unrealised gain before MA-extension exit can fire.</summary>
    public double MaExtensionMinR { get; init; } = 0.30;

    /// <summary>ATR distance threshold for detecting price extension beyond the moving average.</summary>
    public double MaExtensionAtrThreshold { get; init; } = 1.50;

    // â”€â”€ Diagnostics â”€â”€

    /// <summary>Emit per-run diagnostic summaries to console (rejection counts, signal stats).</summary>
    public bool EnableDiagnostics { get; init; } = false;

    /// <summary>Label prefix prepended to diagnostic output lines.</summary>
    public string DiagnosticsLabel { get; init; } = "V16";

    /// <summary>When false, ignore adaptive V3 exit overrides and use the variant's native exit config as-is.</summary>
    public bool RespectSelfLearningExitOverrides { get; init; } = true;

    /// <summary>Bars to block the next same-symbol long after a low-excursion long hard stop. Zero disables the lockout.</summary>
    public int LongRetryLockoutBarsAfterLowExcursionHardStop { get; init; } = 0;

    /// <summary>Maximum peak R that still counts as a low-excursion hard stop for long retry lockout.</summary>
    public double LongRetryLockoutMaxPeakR { get; init; } = 0.25;

    /// <summary>Bars to block the next same-symbol short after a low-excursion short hard stop. Zero disables the lockout.</summary>
    public int ShortRetryLockoutBarsAfterLowExcursionHardStop { get; init; } = 0;

    /// <summary>Maximum peak R that still counts as a low-excursion hard stop for short retry lockout.</summary>
    public double ShortRetryLockoutMaxPeakR { get; init; } = 0.25;

    /// <summary>Reject long continuation entries when the prior bar was already an extreme expansion impulse.</summary>
    public bool RejectOverextendedLongContinuationForLongs { get; init; } = false;

    /// <summary>Maximum RSI allowed for long continuation entries before the overextension guard blocks them.</summary>
    public double LongContinuationMaxRsi { get; init; } = 85.0;

    /// <summary>Minimum prior-bar range, in ATRs, required to classify the setup as an extreme continuation impulse.</summary>
    public double LongContinuationPriorBarMinRangeAtr { get; init; } = 2.5;

    /// <summary>Minimum prior-bar RVOL required before the overextended long continuation guard can trigger.</summary>
    public double LongContinuationPriorBarMinRvol { get; init; } = 4.0;
}

/// <summary>
/// Strategy V16 "Squeeze Breakout" â€” a Bollinger Band / Keltner Channel squeeze-release
/// entry strategy with 10-factor multi-oscillator confluence scoring.
/// <para>
/// <b>Entry thesis:</b> When Bollinger Bands compress inside Keltner Channels for
/// <see cref="V16Config.SqueezeMinBars"/> consecutive bars, volatility energy builds.
/// Once the BB expands past KC (squeeze releases), the strategy determines the
/// breakout direction via BB-mid position, MACD histogram, EMA alignment, and
/// Supertrend. A 10-factor confluence score (RSI, Stochastic, Williams %R, MFI,
/// MACD momentum, ADX directional energy, VWAP context, DPO cycle, Donchian
/// breakout, L2 order-flow imbalance) must reach <see cref="V16Config.MinConfluenceScore"/>
/// before the signal is accepted.
/// </para>
/// <para>
/// <b>Exit model:</b> Tight-r based cascade â€” hard stop â†’ breakeven lock â†’ trailing
/// stop â†’ TP1/TP2 targets â€” plus peak-giveback, stagnation, max-hold-bar,
/// price-tier micro-trail, and MA-extension L2-flip exits.
/// </para>
/// <para>
/// Designed for the <b>small-cap universe</b> where squeeze patterns and order-flow
/// imbalances are most predictive. Ranked #1 in backtest comparison on small-cap.
/// </para>
/// </summary>
public sealed partial class StrategyV16 : BacktestStrategyBase, IBacktestPostTradeSignalGate
{
    private readonly V16Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    private readonly record struct AdaptiveEntryControls(
        int ScoreBoost,
        double PositionScale,
        double RiskScale,
        double StopDistanceMultiplier);

    /// <summary>
    /// Initialises a new V16 Squeeze Breakout strategy with the given (or default) configuration.
    /// Maps <see cref="V16Config"/> properties onto the shared <see cref="ExitEngine.ExitConfig"/> record.
    /// </summary>
    /// <param name="cfg">Optional configuration override; <c>null</c> uses default values.</param>
    public StrategyV16(V16Config? cfg = null)
    {
        _cfg = cfg ?? StrategyV16VariantFactory.CreateVariant(null);
        if (!_cfg.AllowLong || !_cfg.AllowShort)
            throw new InvalidOperationException("V16: single-direction configs are not allowed. Both AllowLong and AllowShort must be true.");

        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.25,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = _cfg.Tp1TightenToBe,
            Tp1PartialClosePct = _cfg.Tp1PartialClosePct,
            Tp1BreakevenBufferAtr = _cfg.Tp1BreakevenBufferAtr,
            ReversalFlatten = true,
            MicroTrail = true,
            MicroTrailCents = 1.5,
            MicroTrailActivateCents = 2.5,
            EmaTrail = _cfg.UseEmaTrail,
            EmaTrailBufferAtr = _cfg.EmaTrailBufferAtr,
            UseTrailingTp2 = _cfg.UseTrailingTp2,
            TrailingTp2AtrMultiplier = _cfg.TrailingTp2AtrMultiplier,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = _cfg.PeakGivebackKeepFraction,
            PeakGivebackActivateR = _cfg.PeakGivebackActivateR,
            FlattenOnStagnation = true,
            StagnationBars = _cfg.StagnationBars,
            StagnationMinPeakR = _cfg.StagnationMinPeakR,
            StagnationMaxAdverseR = _cfg.StagnationMaxAdverseR,
            UsePriceTierMicroTrail = _cfg.UsePriceTierMicroTrail,
            UsePriceTierStopFloor = _cfg.UsePriceTierStopFloor,
            UseMaExtensionL2Flip = _cfg.UseMaExtensionL2Flip,
            MaExtensionMinR = _cfg.MaExtensionMinR,
            MaExtensionAtrThreshold = _cfg.MaExtensionAtrThreshold,
            UseL1L2DecisionOnOppositeBarsFlatten = _cfg.UseL1L2DecisionOnOppositeBarsFlatten,
            SymbolLossFlattenUsd = _cfg.SymbolLossFlattenUsd,
        };
    }

    public BacktestSignalRetryLockout? GetRetryLockout(BacktestSignal signal, BacktestTradeResult trade)
    {
        if (signal.Side != trade.Side
            || trade.ExitReason != ExitReason.HardStop
            || (trade.Side == TradeSide.Long && trade.PeakR > _cfg.LongRetryLockoutMaxPeakR)
            || (trade.Side == TradeSide.Short && trade.PeakR > _cfg.ShortRetryLockoutMaxPeakR))
        {
            return null;
        }

        var lockoutBars = trade.Side == TradeSide.Long
            ? _cfg.LongRetryLockoutBarsAfterLowExcursionHardStop
            : _cfg.ShortRetryLockoutBarsAfterLowExcursionHardStop;

        if (lockoutBars <= 0)
        {
            return null;
        }

        return new BacktestSignalRetryLockout(
            trade.Side,
            trade.ExitBar + 1 + lockoutBars);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Signal generation â€” scan every bar for squeeze-release + confluence
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Scans all <paramref name="triggerBars"/> for squeeze-release events, then
    /// scores each release with the 10-factor confluence model.  Accepted signals
    /// are filtered by cooldown, time gates, price/liquidity, HTF bias, and L2 flow.
    /// </summary>
    /// <param name="triggerBars">Primary 1-minute enriched bars.</param>
    /// <param name="bars5m">Optional 5-minute timeframe bars (unused in V16 but required by interface).</param>
    /// <param name="bars15m">Optional 15-minute bars for HTF bias calculation.</param>
    /// <param name="bars1h">Optional 1-hour bars for HTF bias calculation.</param>
    /// <param name="bars1d">Optional daily bars for HTF bias calculation.</param>
    /// <returns>List of accepted entry signals ordered by bar index.</returns>
    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var diag = new V16Diagnostics(_cfg.DiagnosticsLabel,
            _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());

        if (triggerBars.Length < _cfg.SqueezeLookback + 5)
        {
            diag.PrintSummary();
            return Array.Empty<BacktestSignal>();
        }

        // Pre-compute per-bar squeeze state for fast lookup
        var squeezeState = ComputeSqueezeStates(triggerBars);

        // Pre-compute BB bandwidth percentile ranks over rolling 50-bar window
        var bwPctile = ComputeBandwidthPercentiles(triggerBars, 50);

        var accepted = new List<BacktestSignal>();
        var daySignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBarIndex = -10_000;

        for (int i = _cfg.SqueezeLookback + 2; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atr = row.Atr14;

            // â”€â”€ quick-reject on NaN / invalid â”€â”€
            if (double.IsNaN(atr) || atr <= 0) continue;
            if (double.IsNaN(row.BbUpper) || double.IsNaN(row.BbLower)) continue;
            if (double.IsNaN(row.KcUpper) || double.IsNaN(row.KcLower)) continue;

            diag.RawScanned++;

            // â”€â”€ cooldown â”€â”€
            if (i - lastAcceptedBarIndex < _cfg.CooldownBars) continue;

            // â”€â”€ time gate â”€â”€
            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
            {
                diag.Reject("before-entry-start"); continue;
            }
            if (minuteEt > 960 - _cfg.LastEntryMinuteBeforeClose)
            {
                diag.Reject("too-close-to-close"); continue;
            }
            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
            {
                diag.Reject("outside-entry-window"); continue;
            }

            // â”€â”€ core filters (price, rvol, liquidity, spread, vol-accel) â”€â”€
            if (!PassesCoreFilters(row, out var coreReason))
            {
                diag.Reject($"core:{coreReason}"); continue;
            }

            if (IsMarketRegimeBlocked(row))
            {
                diag.Reject("market-regime-blocked"); continue;
            }

            // â”€â”€ max signals per day â”€â”€
            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            int countForDay = daySignalCounts.GetValueOrDefault(dayEt);
            if (countForDay >= _cfg.MaxSignalsPerDay)
            {
                diag.Reject("max-signals-per-day"); continue;
            }

            // â”€â”€ squeeze detection â”€â”€
            // We need: squeeze was active for SqueezeMinBars within the lookback,
            // and the squeeze released within SqueezeReleaseMaxBars before current bar.
            if (!DetectSqueezeRelease(squeezeState, bwPctile, i, out int squeezeEndBar))
            {
                diag.Reject("no-squeeze-release"); continue;
            }

            // â”€â”€ determine direction from breakout â”€â”€
            TradeSide? side = DetermineBreakoutDirection(triggerBars, i, squeezeEndBar);
            if (side == null)
            {
                diag.Reject("no-direction"); continue;
            }
            if (side == TradeSide.Long && !_cfg.AllowLong) { diag.Reject("long-disabled"); continue; }
            if (side == TradeSide.Short && !_cfg.AllowShort) { diag.Reject("short-disabled"); continue; }
            if (side == TradeSide.Long
                && _cfg.RequireBullishBreakoutBarForLongs
                && !IsBullishBreakoutCandle(row, _cfg.LongBreakoutMinCloseLocationPct, _cfg.LongBreakoutMaxUpperWickPct))
            {
                diag.Reject("long:weak-breakout-bar"); continue;
            }
            if (side == TradeSide.Long
                && _cfg.RejectOverextendedLongContinuationForLongs
                && IsOverextendedLongContinuation(triggerBars, i))
            {
                diag.Reject("long:overextended-continuation"); continue;
            }

            diag.RawSignals++;
            if (side == TradeSide.Long) diag.RawLongSignals++;
            else diag.RawShortSignals++;

            // â”€â”€ confluence scoring â”€â”€
            int confluence = ComputeConfluenceScore(triggerBars, i, side.Value, bars5m, bars15m, bars1h, bars1d);
            int requiredScore = side == TradeSide.Long
                ? _cfg.LongMinConfluenceScore ?? _cfg.MinConfluenceScore
                : _cfg.ShortMinConfluenceScore ?? _cfg.MinConfluenceScore;

            var adaptiveControls = BuildAdaptiveEntryControls(triggerBars, i, side.Value);
            requiredScore += adaptiveControls.ScoreBoost;

            // HTF bias check â€” if counter-trend, demand higher confluence
            string htfBias = ComputeHtfBias(row.Bar.Timestamp, bars15m, bars1h, bars1d);
            if (!PassesHtfBias(side.Value, htfBias, confluence, out var htfReason))
            {
                diag.Reject($"{(side == TradeSide.Long ? "long" : "short")}:htf:{htfReason}"); continue;
            }

            if (confluence < requiredScore)
            {
                diag.Reject($"confluence:{confluence}<{requiredScore}"); continue;
            }

            // â”€â”€ L2 entry filter â”€â”€
            bool requireL2EntryFilter = _cfg.RequireL2EntryFilter
                || (_cfg.RequireL2EntryFilterForLongsOnly && side == TradeSide.Long);
            if (requireL2EntryFilter && !PassesL2EntryFilter(row, side.Value))
            {
                diag.Reject($"{(side == TradeSide.Long ? "long" : "short")}:l2-unfavorable"); continue;
            }

            // â”€â”€ indecision-bar (doji/cross) filter â”€â”€
            if (_cfg.RejectIndecisionBar)
            {
                double body = Math.Abs(row.Bar.Close - row.Bar.Open);
                double range = row.Bar.High - row.Bar.Low;
                if (range > 0 && body / range <= _cfg.IndecisionBarMaxBodyPct)
                {
                    diag.Reject("indecision-bar"); continue;
                }
            }

            // â”€â”€ self-learning setup block â”€â”€
            if (IsSelfLearningBlocked("V16_SQZ"))
            {
                diag.Reject("self-learning-blocked"); continue;
            }

            // â”€â”€ compute stop and entry price â”€â”€
            double entryPrice = _cfg.UseNextBarOpenEntry && i + 1 < triggerBars.Length
                ? triggerBars[i + 1].Bar.Open
                : row.Bar.Close;

            double stopPrice = ComputeStopPrice(triggerBars, i, squeezeEndBar, side.Value, atr);
            // Apply self-learning stop multiplier to the risk distance
            double rawDist = Math.Abs(entryPrice - stopPrice);
            rawDist *= adaptiveControls.StopDistanceMultiplier;
            double adjDist = ApplySelfLearningStopMultiplier(rawDist);
            if (adjDist != rawDist)
                stopPrice = side.Value == TradeSide.Long ? entryPrice - adjDist : entryPrice + adjDist;
            else
                stopPrice = side.Value == TradeSide.Long ? entryPrice - rawDist : entryPrice + rawDist;
            double riskPerShare = Math.Abs(entryPrice - stopPrice);
            if (riskPerShare < _cfg.MinRiskPerShare || riskPerShare < 0.001)
            {
                diag.Reject("risk-too-small"); continue;
            }

            int posSize = BacktestHelpers.ComputePositionSize(
                entryPrice,
                riskPerShare,
                _cfg.RiskPerTradeDollars * adaptiveControls.RiskScale,
                _cfg.AccountSize,
                _cfg.MaxPositionNotionalPctOfAccount,
                _cfg.MaxShares);
            posSize = (int)Math.Floor(posSize * adaptiveControls.PositionScale);
            posSize = ApplySelfLearningPositionSize(posSize, "V16_SQZ");
            if (posSize <= 0)
            {
                diag.Reject("position-size-zero"); continue;
            }

            int signalBar = _cfg.UseNextBarOpenEntry ? i + 1 : i;
            if (signalBar >= triggerBars.Length) { diag.Reject("signal-bar-oob"); continue; }

            accepted.Add(new BacktestSignal(
                signalBar,
                triggerBars[signalBar].Bar.Timestamp,
                side.Value,
                entryPrice,
                stopPrice,
                riskPerShare,
                posSize,
                atr,
                HtfBias.Neutral,
                "",
                "V16_SQZ",
                confluence
            ));

            if (side == TradeSide.Long) diag.AcceptedLongSignals++;
            else diag.AcceptedShortSignals++;

            daySignalCounts[dayEt] = countForDay + 1;
            lastAcceptedBarIndex = i;
        }

        diag.AcceptedSignals = accepted.Count;
        diag.PrintSummary();
        return accepted;
    }

    /// <summary>
    /// Simulates a trade from the given signal through the exit cascade engine,
    /// returning final P&amp;L, exit reason, hold duration, and R-multiple.
    /// </summary>
    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var exitCfg = _cfg.RespectSelfLearningExitOverrides
            ? ApplySelfLearningExitOverrides(_exitCfg, signal.SubStrategy)
            : _exitCfg;

        return ExitEngine.SimulateTrade(signal, triggerBars, exitCfg);
    }
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Squeeze detection helpers
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// For each bar, true when both BB bands fit inside the KC bands (classic TTM Squeeze).
    /// </summary>
    private static bool[] ComputeSqueezeStates(EnrichedBar[] bars)
    {
        var result = new bool[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            var b = bars[i];
            if (double.IsNaN(b.BbUpper) || double.IsNaN(b.BbLower)
                || double.IsNaN(b.KcUpper) || double.IsNaN(b.KcLower))
            {
                result[i] = false;
                continue;
            }

            result[i] = b.BbUpper < b.KcUpper && b.BbLower > b.KcLower;
        }
        return result;
    }

    /// <summary>
    /// Compute rolling percentile rank of BB bandwidth over the given window.
    /// Returns values 0..1 where 0 = lowest bandwidth in window.
    /// </summary>
    private static double[] ComputeBandwidthPercentiles(EnrichedBar[] bars, int window)
    {
        var result = new double[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            if (double.IsNaN(bars[i].BbBandwidth) || i < window)
            {
                result[i] = 0.5;
                continue;
            }
            double current = bars[i].BbBandwidth;
            int belowCount = 0;
            int total = 0;
            for (int j = i - window; j < i; j++)
            {
                if (!double.IsNaN(bars[j].BbBandwidth))
                {
                    if (bars[j].BbBandwidth <= current) belowCount++;
                    total++;
                }
            }
            result[i] = total > 0 ? (double)belowCount / total : 0.5;
        }
        return result;
    }

    /// <summary>
    /// Check if a squeeze was active in the lookback window and recently released.
    /// Returns true + the bar index where the squeeze ended.
    /// </summary>
    /// <summary>
    /// Detects whether a qualifying squeeze recently released.
    /// <para>Walks backward from <paramref name="currentBar"/> up to
    /// <see cref="V16Config.SqueezeReleaseMaxBars"/> bars. If a run of
    /// at least <see cref="V16Config.SqueezeMinBars"/> in-squeeze bars is
    /// found whose BB-bandwidth percentile is below
    /// <see cref="V16Config.SqueezeBandwidthMaxPctile"/>, returns <c>true</c>
    /// and sets <paramref name="squeezeEndBar"/> to the last squeeze bar.</para>
    /// </summary>
    private bool DetectSqueezeRelease(bool[] squeezeState, double[] bwPctile, int currentBar, out int squeezeEndBar)
    {
        squeezeEndBar = -1;

        // Current bar must NOT be in squeeze (squeeze has released)
        if (squeezeState[currentBar]) return false;

        // Find the most recent qualifying squeeze within SqueezeReleaseMaxBars.
        // A newer partial squeeze fragment should not hide an earlier valid release.
        int searchStart = Math.Max(0, currentBar - _cfg.SqueezeReleaseMaxBars);
        for (int i = currentBar - 1; i >= searchStart; i--)
        {
            if (squeezeState[i])
            {
                // Found squeeze end at bar i. Now verify it lasted SqueezeMinBars
                int squeezeBars = 0;
                for (int j = i; j >= Math.Max(0, i - _cfg.SqueezeLookback); j--)
                {
                    if (squeezeState[j]) squeezeBars++;
                    else break;
                }

                if (squeezeBars >= _cfg.SqueezeMinBars)
                {
                    // Additional quality check: bandwidth should be relatively low
                    if (bwPctile[i] <= _cfg.SqueezeBandwidthMaxPctile)
                    {
                        squeezeEndBar = i;
                        return true;
                    }
                }

                // Skip this squeeze run and keep searching for an earlier one.
                while (i - 1 >= searchStart && squeezeState[i - 1])
                    i--;
            }
        }
        return false;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Direction determination
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// After a squeeze release, determine the breakout direction from price action
    /// and MACD histogram momentum.
    /// </summary>
    private TradeSide? DetermineBreakoutDirection(EnrichedBar[] bars, int currentBar, int squeezeEndBar)
    {
        var row = bars[currentBar];

        // Price broke above/below BB mid (SMA20) coming out of squeeze
        bool aboveBbMid = row.Bar.Close > row.BbMid;
        bool belowBbMid = row.Bar.Close < row.BbMid;

        // MACD histogram direction
        bool macdBullish = !double.IsNaN(row.MacdHist) && row.MacdHist > 0;
        bool macdBearish = !double.IsNaN(row.MacdHist) && row.MacdHist < 0;

        // Check if MACD hist is turning (momentum building) â€” adds extra weight
        bool macdTurningBull = false, macdTurningBear = false;
        if (currentBar > 0 && !double.IsNaN(row.MacdHist) && !double.IsNaN(bars[currentBar - 1].MacdHist))
        {
            double histDelta = row.MacdHist - bars[currentBar - 1].MacdHist;
            macdTurningBull = histDelta > 0;
            macdTurningBear = histDelta < 0;
        }

        // EMA alignment
        bool ema9AboveEma21 = !double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21) && row.Ema9 > row.Ema21;

        // Supertrend direction
        bool stBullish = !double.IsNaN(row.StDirection) && row.StDirection > 0;
        bool stBearish = !double.IsNaN(row.StDirection) && row.StDirection < 0;

        // Score long vs short direction
        int longScore = 0, shortScore = 0;

        if (aboveBbMid) longScore += 2; else if (belowBbMid) shortScore += 2;
        if (macdBullish) longScore++; else if (macdBearish) shortScore++;
        if (macdTurningBull) longScore++; else if (macdTurningBear) shortScore++;
        if (ema9AboveEma21) longScore++; else shortScore++;
        if (stBullish) longScore++; else if (stBearish) shortScore++;

        // Need clear directional bias (at least 3-point margin)
        int minimumDirectionalScore = Math.Max(1, _cfg.BreakoutDirectionMinScore);
        if (longScore >= minimumDirectionalScore && longScore > shortScore) return TradeSide.Long;
        if (shortScore >= minimumDirectionalScore && shortScore > longScore) return TradeSide.Short;

        return null; // ambiguous â€” skip
    }

    private static bool IsBullishBreakoutCandle(EnrichedBar row, double minCloseLocationPct, double maxUpperWickPct)
    {
        double range = row.Bar.High - row.Bar.Low;
        if (range <= 0)
        {
            return false;
        }

        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        double closeLocationPct = (row.Bar.Close - row.Bar.Low) / range;
        return row.Bar.Close > row.Bar.Open
            && closeLocationPct >= minCloseLocationPct
            && upperWick / range <= maxUpperWickPct;
    }

    private bool IsOverextendedLongContinuation(EnrichedBar[] bars, int currentBar)
    {
        int entryBar = _cfg.UseNextBarOpenEntry ? currentBar + 1 : currentBar;
        if (entryBar <= 0 || entryBar >= bars.Length)
        {
            return false;
        }

        var row = bars[entryBar];
        var previousBar = bars[entryBar - 1];

        if (double.IsNaN(row.Rsi14) || row.Rsi14 < _cfg.LongContinuationMaxRsi)
        {
            return false;
        }

        if (double.IsNaN(previousBar.Rvol) || previousBar.Rvol < _cfg.LongContinuationPriorBarMinRvol)
        {
            return false;
        }

        if (double.IsNaN(row.Atr14) || row.Atr14 <= 0)
        {
            return false;
        }

        double previousRangeAtr = (previousBar.Bar.High - previousBar.Bar.Low) / row.Atr14;
        if (previousRangeAtr < _cfg.LongContinuationPriorBarMinRangeAtr)
        {
            return false;
        }

        return row.Bar.Close >= previousBar.Bar.Close;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Multi-factor confluence scoring
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Score 0..10+ based on how many independent indicator families confirm the signal.
    /// Each point represents a different technical concept, reducing false-signal risk.
    /// </summary>
    private int ComputeConfluenceScore(
        EnrichedBar[] bars, int idx, TradeSide side,
        EnrichedBar[]? bars5m, EnrichedBar[]? bars15m, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var row = bars[idx];
        int score = 0;

        // â”€â”€ 1. RSI in supportive range â”€â”€
        if (!double.IsNaN(row.Rsi14))
        {
            if (side == TradeSide.Long && row.Rsi14 >= _cfg.RsiLongMin && row.Rsi14 <= _cfg.RsiLongMax)
                score++;
            else if (side == TradeSide.Short && row.Rsi14 >= _cfg.RsiShortMin && row.Rsi14 <= _cfg.RsiShortMax)
                score++;
        }

        // â”€â”€ 2. Stochastic in supportive range â”€â”€
        if (!double.IsNaN(row.StochK) && !double.IsNaN(row.StochD))
        {
            if (side == TradeSide.Long && row.StochK >= _cfg.StochLongMin && row.StochK > row.StochD)
                score++;
            else if (side == TradeSide.Short && row.StochK <= _cfg.StochShortMax && row.StochK < row.StochD)
                score++;
        }

        // â”€â”€ 3. Williams %R in supportive range â”€â”€
        if (!double.IsNaN(row.WillR14))
        {
            if (side == TradeSide.Long && row.WillR14 >= _cfg.WillRLongMax)
                score++;
            else if (side == TradeSide.Short && row.WillR14 <= _cfg.WillRShortMin)
                score++;
        }

        // â”€â”€ 4. MFI (volume-weighted RSI) in supportive range â”€â”€
        if (!double.IsNaN(row.Mfi14))
        {
            if (side == TradeSide.Long && row.Mfi14 >= _cfg.MfiLongMin)
                score++;
            else if (side == TradeSide.Short && row.Mfi14 <= _cfg.MfiShortMax)
                score++;
        }

        // â”€â”€ 5. MACD histogram momentum turning in direction â”€â”€
        if (idx > 0 && !double.IsNaN(row.MacdHist) && !double.IsNaN(bars[idx - 1].MacdHist))
        {
            double histDelta = row.MacdHist - bars[idx - 1].MacdHist;
            if (side == TradeSide.Long && histDelta > 0)
                score++;
            else if (side == TradeSide.Short && histDelta < 0)
                score++;
        }

        // â”€â”€ 6. ADX showing directional energy â”€â”€
        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.AdxMin && row.Adx <= _cfg.AdxMax)
        {
            if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi))
            {
                if (side == TradeSide.Long && row.PlusDi > row.MinusDi)
                    score++;
                else if (side == TradeSide.Short && row.MinusDi > row.PlusDi)
                    score++;
            }
        }

        // â”€â”€ 7. VWAP context (mean-reversion bias) â”€â”€
        if (!double.IsNaN(row.Vwap))
        {
            double vwapDist = (row.Bar.Close - row.Vwap) / Math.Max(0.01, row.Atr14);
            // Long: near or below VWAP (room to revert up); Short: near or above VWAP
            if (side == TradeSide.Long && vwapDist <= 0.5)
                score++;
            else if (side == TradeSide.Short && vwapDist >= -0.5)
                score++;
        }

        // â”€â”€ 8. DPO cycle position â”€â”€
        if (!double.IsNaN(row.Dpo20))
        {
            // DPO > 0 = price above detrended average â†’ bullish cycle
            if (side == TradeSide.Long && row.Dpo20 > 0)
                score++;
            else if (side == TradeSide.Short && row.Dpo20 < 0)
                score++;
        }

        // â”€â”€ 9. Donchian channel breakout confirmation â”€â”€
        if (!double.IsNaN(row.DcUpper) && !double.IsNaN(row.DcLower) && !double.IsNaN(row.DcPct))
        {
            // Long: close in upper 40% of Donchian; Short: close in lower 40%
            if (side == TradeSide.Long && row.DcPct >= 0.60)
                score++;
            else if (side == TradeSide.Short && row.DcPct <= 0.40)
                score++;
        }

        // â”€â”€ 10. L2 order flow (OFI + imbalance) â”€â”€
        if (!double.IsNaN(row.OfiSignal) && !double.IsNaN(row.ImbalanceRatio))
        {
            if (side == TradeSide.Long && row.OfiSignal > 0 && row.ImbalanceRatio >= 0.85)
                score++;
            else if (side == TradeSide.Short && row.OfiSignal < 0 && row.ImbalanceRatio <= 1.15)
                score++;
        }

        return score;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Stop-price computation
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Place stop at the squeeze range extreme (the tightest point of the Bollinger Band
    /// during the squeeze), buffered by 0.15 Ã— ATR.  This is a natural vol-based stop.
    /// Falls back to 1-ATR stop if squeeze range is too tight.
    /// </summary>
    private double ComputeStopPrice(EnrichedBar[] bars, int currentBar, int squeezeEndBar, TradeSide side, double atr)
    {
        // Find the tightest BB band extreme during the squeeze
        int searchStart = Math.Max(0, squeezeEndBar - _cfg.SqueezeLookback);
        double extremeHigh = double.MinValue;
        double extremeLow = double.MaxValue;

        for (int i = searchStart; i <= squeezeEndBar; i++)
        {
            if (!double.IsNaN(bars[i].Bar.High)) extremeHigh = Math.Max(extremeHigh, bars[i].Bar.High);
            if (!double.IsNaN(bars[i].Bar.Low)) extremeLow = Math.Min(extremeLow, bars[i].Bar.Low);
        }

        double buffer = 0.15 * atr;
        double entryPrice = bars[currentBar].Bar.Close;

        if (side == TradeSide.Long)
        {
            double squeezeStop = extremeLow - buffer;
            double atrStop = entryPrice - atr;
            // Use whichever is tighter but not too tight
            double stop = Math.Max(squeezeStop, atrStop);
            // Ensure minimum risk of 0.3 ATR
            if (entryPrice - stop < 0.3 * atr) stop = entryPrice - 0.3 * atr;
            return stop;
        }
        else
        {
            double squeezeStop = extremeHigh + buffer;
            double atrStop = entryPrice + atr;
            double stop = Math.Min(squeezeStop, atrStop);
            if (stop - entryPrice < 0.3 * atr) stop = entryPrice + 0.3 * atr;
            return stop;
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Filter helpers
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Gate check on price range, relative volume, L2 liquidity, spread z-score,
    /// volume acceleration, and indicator availability. Rejects bars that fail any threshold.
    /// </summary>
    private bool PassesCoreFilters(EnrichedBar row, out string reason)
    {
        if (row.Bar.Close < _cfg.MinPrice || row.Bar.Close > _cfg.MaxPrice)
        { reason = "price"; return false; }

        if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin)
        { reason = "rvol"; return false; }

        if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin)
        { reason = "liquidity"; return false; }

        if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax)
        { reason = "spread"; return false; }

        if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.MinVolAccel)
        { reason = "vol-accel"; return false; }

        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Rsi14))
        { reason = "indicators"; return false; }

        reason = string.Empty;
        return true;
    }

    private AdaptiveEntryControls BuildAdaptiveEntryControls(EnrichedBar[] bars, int idx, TradeSide side)
    {
        var row = bars[idx];
        int scoreBoost = 0;
        double positionScale = 1.0;
        double riskScale = 1.0;
        double stopDistanceMultiplier = 1.0;

        if (_cfg.EnableTimeContextAdaptation)
        {
            int hourUtc = row.Bar.Timestamp.Hour;
            if (_cfg.TimeContextWeakHoursUtc.Contains(hourUtc))
            {
                scoreBoost += _cfg.TimeContextWeakHourScoreBoost;
                positionScale *= _cfg.TimeContextWeakHourPositionScale;
            }
            else if (hourUtc >= 18)
            {
                scoreBoost += _cfg.TimeContextLateSessionScoreBoost;
                positionScale *= _cfg.TimeContextLateSessionPositionScale;
            }
            else if (hourUtc <= 14)
            {
                positionScale *= _cfg.TimeContextOpeningPositionScale;
            }
        }

        if (_cfg.EnableDepthContextAdaptation && IsFragileBookContext(row, side))
        {
            scoreBoost += _cfg.DepthContextPoorBookScoreBoost;
            positionScale *= _cfg.DepthContextPoorBookPositionScale;
        }

        if (_cfg.EnableVolatilityNormalizedSizing)
        {
            double atrPct = row.Atr14 > 0 && row.Bar.Close > 0
                ? row.Atr14 / row.Bar.Close
                : double.NaN;

            if (!double.IsNaN(atrPct))
            {
                if (atrPct >= _cfg.VolatilityNormalizedHighAtrPct)
                {
                    positionScale *= _cfg.VolatilityNormalizedHighAtrPositionScale;
                    riskScale *= 0.90;
                }
                else if (atrPct <= _cfg.VolatilityNormalizedLowAtrPct)
                {
                    positionScale *= _cfg.VolatilityNormalizedLowAtrPositionScale;
                    riskScale *= 0.95;
                }
            }
        }

        if (_cfg.EnableSymbolContextAdaptation && IsFragileSymbolContext(row))
        {
            scoreBoost += _cfg.SymbolContextFragileScoreBoost;
            positionScale *= _cfg.SymbolContextFragilePositionScale;
        }

        if (_cfg.EnableHardStopPreemptAdaptation)
        {
            int pressure = ComputeHardStopPressure(bars, idx, side);
            if (pressure >= 2)
            {
                scoreBoost += _cfg.HardStopPreemptScoreBoost + (pressure >= 3 ? 1 : 0);
                positionScale *= _cfg.HardStopPreemptPositionScale;
                riskScale *= _cfg.HardStopPreemptRiskScale;
                stopDistanceMultiplier = Math.Max(stopDistanceMultiplier, _cfg.HardStopPreemptStopDistanceMultiplier);
            }
        }

        return new AdaptiveEntryControls(
            scoreBoost,
            Math.Clamp(positionScale, 0.20, 1.10),
            Math.Clamp(riskScale, 0.20, 1.10),
            Math.Clamp(stopDistanceMultiplier, 1.0, 1.35));
    }

    private bool IsFragileBookContext(EnrichedBar row, TradeSide side)
    {
        bool weakLiquidity = !double.IsNaN(row.L2Liquidity) && row.L2Liquidity > 0 && row.L2Liquidity < 4.0;
        bool wideSpread = !double.IsNaN(row.SpreadZ) && row.SpreadZ >= 3.0;
        bool adverseFlow = side == TradeSide.Long
            ? (!double.IsNaN(row.OfiSignal) && row.OfiSignal < -0.05) || (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio < 0.95)
            : (!double.IsNaN(row.OfiSignal) && row.OfiSignal > 0.05) || (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio > 1.05);

        return weakLiquidity || wideSpread || adverseFlow;
    }

    private static bool IsFragileSymbolContext(EnrichedBar row)
    {
        bool microPrice = row.Bar.Close <= 1.50;
        bool weakRvol = !double.IsNaN(row.Rvol) && row.Rvol < 0.40;
        bool wideSpread = !double.IsNaN(row.SpreadZ) && row.SpreadZ >= 3.5;
        return microPrice || weakRvol || wideSpread;
    }

    private int ComputeHardStopPressure(EnrichedBar[] bars, int idx, TradeSide side)
    {
        var row = bars[idx];
        int pressure = 0;

        if (!double.IsNaN(row.SpreadZ) && row.SpreadZ >= _cfg.HardStopPreemptSpreadZThreshold)
        {
            pressure++;
        }

        if (!double.IsNaN(row.VolAccel) && row.VolAccel < 0)
        {
            pressure++;
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol < 0.35)
        {
            pressure++;
        }

        if (!double.IsNaN(row.L2Liquidity) && row.L2Liquidity < _cfg.HardStopPreemptThinLiquidityThreshold)
        {
            pressure++;
        }

        if (IsVolatilitySpike(bars, idx))
        {
            pressure++;
        }

        if (HasAdverseMicrotrend(row, side))
        {
            pressure++;
        }

        if (side == TradeSide.Long)
        {
            if ((!double.IsNaN(row.OfiSignal) && row.OfiSignal < -0.10)
                || (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio < 0.92))
            {
                pressure++;
            }
        }
        else if ((!double.IsNaN(row.OfiSignal) && row.OfiSignal > 0.10)
            || (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio > 1.08))
        {
            pressure++;
        }

        return pressure;
    }

    private bool IsVolatilitySpike(EnrichedBar[] bars, int idx)
    {
        var row = bars[idx];
        bool rangeSpike = !double.IsNaN(row.Atr14)
            && row.Atr14 > 0
            && (row.Bar.High - row.Bar.Low) / row.Atr14 >= _cfg.HardStopPreemptVolatilitySpikeBarRangeAtrThreshold;

        if (rangeSpike)
        {
            return true;
        }

        if (idx < 5 || double.IsNaN(row.Atr14) || row.Atr14 <= 0)
        {
            return false;
        }

        double atrSum = 0.0;
        int atrCount = 0;
        for (int i = Math.Max(0, idx - 10); i < idx; i++)
        {
            if (!double.IsNaN(bars[i].Atr14) && bars[i].Atr14 > 0)
            {
                atrSum += bars[i].Atr14;
                atrCount++;
            }
        }

        if (atrCount == 0)
        {
            return false;
        }

        double atrRatio = row.Atr14 / (atrSum / atrCount);
        return atrRatio >= _cfg.HardStopPreemptVolatilitySpikeAtrRatioThreshold;
    }

    private static bool HasAdverseMicrotrend(EnrichedBar row, TradeSide side)
    {
        if (side == TradeSide.Long)
        {
            return (!double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21) && row.Ema9 < row.Ema21)
                || (!double.IsNaN(row.Vwap) && row.Bar.Close < row.Vwap)
                || (!double.IsNaN(row.MacdHist) && row.MacdHist < 0)
                || (!double.IsNaN(row.Sma20) && row.Bar.Close < row.Sma20)
                || (!double.IsNaN(row.StDirection) && row.StDirection < 0);
        }

        return (!double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21) && row.Ema9 > row.Ema21)
            || (!double.IsNaN(row.Vwap) && row.Bar.Close > row.Vwap)
            || (!double.IsNaN(row.MacdHist) && row.MacdHist > 0)
            || (!double.IsNaN(row.Sma20) && row.Bar.Close > row.Sma20)
            || (!double.IsNaN(row.StDirection) && row.StDirection > 0);
    }

    /// <summary>
    /// Confirms order-flow alignment: OFI and imbalance ratio must support
    /// the proposed <paramref name="side"/>. If L2 data is absent, passes by default.
    /// </summary>
    private bool PassesL2EntryFilter(EnrichedBar row, TradeSide side)
    {
        double ofi = row.OfiSignal;
        double imbalance = row.ImbalanceRatio;
        if (double.IsNaN(ofi) || double.IsNaN(imbalance))
            return true; // pass if no L2 data available

        if (side == TradeSide.Long)
            return ofi >= _cfg.L2OfiMinLong && imbalance >= _cfg.L2ImbalanceMinLong;
        else
            return ofi <= _cfg.L2OfiMaxShort && imbalance <= _cfg.L2ImbalanceMaxShort;
    }

    /// <summary>
    /// Validates higher-timeframe directional bias. Allows aligned trends, neutral
    /// conditions, and weak counter-trend entries when confluence exceeds
    /// <see cref="V16Config.WeakCounterTrendMinScore"/>.
    /// </summary>
    private bool PassesHtfBias(TradeSide side, string htfBias, int confluence, out string reason)
    {
        reason = string.Empty;

        if (!_cfg.RequireHtfBias) return true;

        if (side == TradeSide.Long)
        {
            if (htfBias is "BULL" or "STRONG_BULL") return true;
            if (htfBias == "NEUTRAL" && _cfg.AllowNeutralHtf) return true;
            if (htfBias is "BEAR" && _cfg.AllowWeakCounterTrendHtf
                && confluence >= _cfg.WeakCounterTrendMinScore)
                return true;
            reason = htfBias;
            return false;
        }
        else
        {
            if (htfBias is "BEAR" or "STRONG_BEAR") return true;
            if (htfBias == "NEUTRAL" && _cfg.AllowNeutralHtf) return true;
            if (htfBias is "BULL" && _cfg.AllowWeakCounterTrendHtf
                && confluence >= _cfg.WeakCounterTrendMinScore)
                return true;
            reason = htfBias;
            return false;
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // HTF bias (reused pattern from V13)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Computes a composite higher-timeframe bias string (STRONG_BULL, BULL, NEUTRAL,
    /// BEAR, STRONG_BEAR) by averaging EMA/MACD/DI scores across 15m, 1h, and 1d frames.
    /// </summary>
    private static string ComputeHtfBias(DateTime ts, params EnrichedBar[]?[] frames)
    {
        int scoreSum = 0;
        int scoreCount = 0;

        foreach (var bars in frames)
        {
            if (bars == null || bars.Length < 2) continue;

            int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
            if (idx < 1) continue;

            var row = bars[idx];
            var prev = bars[idx - 1];
            if (double.IsNaN(row.Ema21) || double.IsNaN(row.Ema50) || double.IsNaN(row.MacdHist))
                continue;

            int score = 0;
            score += row.Ema21 > row.Ema50 ? 1 : -1;
            score += row.Bar.Close > row.Ema21 ? 1 : -1;
            score += row.MacdHist >= 0 ? 1 : -1;
            score += row.Ema21 >= prev.Ema21 ? 1 : -1;
            if (!double.IsNaN(row.Adx) && row.Adx >= 20 && !double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi))
                score += row.PlusDi >= row.MinusDi ? 1 : -1;

            scoreSum += score;
            scoreCount++;
        }

        if (scoreCount == 0) return "NEUTRAL";

        double avg = (double)scoreSum / scoreCount;
        if (avg >= 2.5) return "STRONG_BULL";
        if (avg >= 1.0) return "BULL";
        if (avg <= -2.5) return "STRONG_BEAR";
        if (avg <= -1.0) return "BEAR";
        return "NEUTRAL";
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Diagnostics & environment
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Checks the <c>HARVESTER_V16_DIAGNOSTICS</c> environment variable to enable
    /// diagnostic output at runtime without recompilation.
    /// </summary>
    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V16");

    /// <summary>
    /// Internal diagnostics tracker that accumulates rejection reasons and signal
    /// counts during a <see cref="GenerateSignals"/> run, then optionally prints
    /// a summary to the console.
    /// </summary>
    private sealed class V16Diagnostics
    {
        private readonly string _label;
        private readonly bool _enabled;
        private readonly Dictionary<string, int> _rejections = new(StringComparer.OrdinalIgnoreCase);

        public V16Diagnostics(string label, bool enabled)
        {
            _label = label;
            _enabled = enabled;
        }

        public int RawScanned { get; set; }
        public int RawSignals { get; set; }
        public int AcceptedSignals { get; set; }
        public int RawLongSignals { get; set; }
        public int RawShortSignals { get; set; }
        public int AcceptedLongSignals { get; set; }
        public int AcceptedShortSignals { get; set; }

        public void Reject(string reason)
        {
            if (!_rejections.TryAdd(reason, 1))
                _rejections[reason]++;
        }

        public void PrintSummary()
        {
            if (!_enabled) return;

            var topReasons = _rejections
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .Select(kvp => $"{kvp.Key}={kvp.Value}");

            Console.WriteLine($"[V16-DIAG:{_label}] scanned={RawScanned} rawSignals={RawSignals} accepted={AcceptedSignals} " +
                $"rawLong={RawLongSignals} rawShort={RawShortSignals} acceptLong={AcceptedLongSignals} acceptShort={AcceptedShortSignals}");
            Console.WriteLine($"[V16-DIAG:{_label}] rejects {string.Join(", ", topReasons)}");
        }
    }
}

