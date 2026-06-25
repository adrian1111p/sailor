using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// S2 "ApexFlow" â€” Trend-Pullback Continuation with Order-Flow Confirmation
//
// COMPLETE REFACTOR (2026-05-30): StrategyV2 was historically the worst row on the
// compare scoreboard (a thin Conduct adapter wrapper, ~-$574 on the small-cap basket).
// It is now a genuine, self-contained, best-in-class day-trading strategy designed
// to beat V16-SqzBreakout (the only previously-profitable strategy, +$191.96 / PF 2.16).
//
// THESIS: The most durable intraday edge on this universe is *trend-continuation on
//         a shallow pullback*, confirmed by live *order-flow* (OFI + book imbalance).
//         This deliberately stacks the four signal families that the self-learning
//         analytics scored with a POSITIVE learned edge on the same basket:
//           â€¢ V9_L1L2 (order-flow)        prefer  +5.67 bps
//           â€¢ VWAP_REVERSION (vwap ctx)   prefer  +5.25 bps
//           â€¢ V12_MOMENTUM (continuation) hold    +10.51 bps
//           â€¢ V13_ORB_PULLBACK (pullback) hold    +5.56 bps
//
// ENTRY (long; mirrored for short):
//   1. Established up-trend: EMA9 > EMA21 (â‰¥ EMA50), Supertrend up, ADX â‰¥ min, +DI > -DI.
//   2. Shallow pullback: price retraced toward EMA9 / VWAP without breaking trend,
//      with a measurable pullback depth (real retrace, not chasing).
//   3. Resumption: bullish resumption candle closing in the upper part of its range.
//   4. Oscillator reset turning back up: RSI rising inside a supportive band,
//      Stoch %K > %D, MACD histogram momentum turning in trend direction.
//   5. ORDER-FLOW confirmation: OFI signal in-trend + favourable L2/L0 book imbalance.
//   6. Multi-factor confluence score â‰¥ threshold; HTF bias aligned; not over-extended.
//
// EXIT: reuses the shared ExitEngine elite cascade (the proven V16 conduct): TP1
//       partial scale-out + ATR-buffered breakeven, EMA9 dynamic trail, ATR trailing
//       TP2 with continuation extension, peak-giveback, stagnation flatten, micro-trail,
//       price-tier floors, and the MA-extension + L2-flip flatten.
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>
/// Configuration record for <see cref="StrategyV2"/> "ApexFlow" â€” trend-pullback
/// continuation with order-flow confirmation. Every tunable dimension (risk/sizing,
/// direction, cooldown, price/liquidity gates, time windows, trend/pullback detection,
/// oscillator reset bands, order-flow thresholds, confluence scoring, HTF bias, and the
/// full exit cascade) is exposed here. Defaults represent the optimised small-cap 1-minute
/// parameter set tuned to beat V16 on the capped compare basket.
/// </summary>
public sealed record class V2Config
{
    // â”€â”€ Risk / Position sizing â”€â”€

    /// <summary>Fixed dollar risk per trade used to size positions.</summary>
    public double RiskPerTradeDollars { get; init; } = 24.0;

    /// <summary>Notional account equity for max-position-pct calculations.</summary>
    public double AccountSize { get; init; } = 25_000.0;

    /// <summary>Maximum single-position notional as a fraction of <see cref="AccountSize"/>.</summary>
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.18;

    /// <summary>Hard cap on share count regardless of risk math.</summary>
    public int MaxShares { get; init; } = 6_500;

    /// <summary>Minimum allowable risk-per-share to avoid dust-sized stops ($0.05 nickel floor).</summary>
    public double MinRiskPerShare { get; init; } = 0.05;

    /// <summary>Minimum enriched bars required before scanning for entries.
    /// Backtest defaults keep the validated 55-bar indicator warmup; live/paper may lower this through
    /// <c>CreateV2</c> because startup slices commonly have 28-30 usable small bars and must still be able
    /// to create orders instead of returning permanent A005:v2-no-signal.</summary>
    public int MinimumSignalHistoryBars { get; init; } = 55;

    // â”€â”€ Entry direction â”€â”€

    /// <summary>Allow long (buy) continuation entries.</summary>
    public bool AllowLong { get; init; } = true;

    /// <summary>Allow short (sell) continuation entries.</summary>
    public bool AllowShort { get; init; } = true;

    /// <summary>When true, entry price is the open of the bar following the signal bar (next-bar market order).</summary>
    public bool UseNextBarOpenEntry { get; init; } = true;

    // â”€â”€ Cooldown / signals-per-day â”€â”€

    /// <summary>Minimum bars between accepted signals (prevents cluster entries).
    /// THROUGHPUT REFACTOR (2026-06-05): lowered 2â†’1 so back-to-back continuation
    /// signals are not artificially throttled in fast / harsh tape.</summary>
    public int CooldownBars { get; init; } = 1;

    /// <summary>Maximum number of signals accepted per trading day (per symbol).
    /// THROUGHPUT REFACTOR (2026-06-05): raised 6â†’14. In harsh conditions the goal is to
    /// conduct as many quality-confluence trades as the day offers, not to cap the dog inside
    /// a small yard. The confluence floor + elite exit cascade remain the quality guardrails.</summary>
    public int MaxSignalsPerDay { get; init; } = 14;

    // â”€â”€ Price / liquidity gate â”€â”€

    /// <summary>Minimum close price to consider (filters penny stocks).</summary>
    public double MinPrice { get; init; } = 0.3;

    /// <summary>Maximum close price to consider.</summary>
    public double MaxPrice { get; init; } = 700.0;

    /// <summary>Minimum relative volume required.
    /// THROUGHPUT REFACTOR: 0.95â†’0.75 so merely-average-volume continuations still qualify.</summary>
    public double RvolMin { get; init; } = 0.75;

    /// <summary>Minimum L2 order-book liquidity score for entry eligibility.
    /// THROUGHPUT REFACTOR: 15â†’8 so thinner-but-tradable books are not hard-blocked.</summary>
    public double L2LiquidityMin { get; init; } = 8.0;

    /// <summary>Maximum bid-ask spread z-score; rejects illiquid tickers.
    /// THROUGHPUT REFACTOR: 2.5â†’3.2 to tolerate the wider spreads of harsh tape.</summary>
    public double SpreadZMax { get; init; } = 3.2;

    /// <summary>Minimum volume acceleration; rejects drying-up flow.</summary>
    public double MinVolAccel { get; init; } = -0.15;

    // â”€â”€ Time window â”€â”€

    /// <summary>Market open expressed as minute-of-day ET (570 = 9:30 AM).</summary>
    public int MarketOpenMinute { get; init; } = 570;

    /// <summary>Skip the first N minutes after open to avoid volatile open prints.
    /// THROUGHPUT REFACTOR: 8â†’5 to reclaim the early-session continuation moves.</summary>
    public int SkipFirstNMinutes { get; init; } = 5;

    /// <summary>No entries within this many minutes before market close.
    /// THROUGHPUT REFACTOR: 30â†’18 so late-day trends remain tradable (exits still protect).</summary>
    public int LastEntryMinuteBeforeClose { get; init; } = 18;

    /// <summary>Allowed entry windows as (Start, End) minute-of-day ET pairs.
    /// THROUGHPUT REFACTOR: widened (576,945)â†’(575,957) toward a near-full session.</summary>
    public (int Start, int End)[] EntryWindows { get; init; } = [(575, 957)];

    // â”€â”€ Trend qualification â”€â”€

    /// <summary>Minimum ADX required for a tradable trend (filters range-bound chop).
    /// THROUGHPUT REFACTOR: 14â†’10 so early/soft trends are not rejected as chop.</summary>
    public double AdxMin { get; init; } = 10.0;

    /// <summary>Maximum ADX allowed (avoids parabolic exhaustion zones).
    /// THROUGHPUT REFACTOR: 55â†’65 to keep strong-but-not-blowoff trends eligible.</summary>
    public double AdxMax { get; init; } = 65.0;

    /// <summary>Require EMA50 alignment (EMA9 &gt; EMA21 &gt; EMA50 for longs) in addition to EMA9/EMA21.
    /// THROUGHPUT REFACTOR: now OFF by default â€” EMA50 stack instead contributes to the confluence
    /// score (factor #1) rather than hard-blocking entries.</summary>
    public bool RequireEma50Alignment { get; init; } = false;

    /// <summary>Require Supertrend direction to agree with the trade side.
    /// THROUGHPUT REFACTOR: now OFF by default â€” Supertrend instead contributes to the confluence
    /// score (factor #2). Holistic confluence + exits replace the single-factor hard block.</summary>
    public bool RequireSupertrendAlignment { get; init; } = false;

    /// <summary>Require +DI/-DI directional agreement with the trade side.
    /// THROUGHPUT REFACTOR: now OFF by default â€” +DI/-DI energy instead contributes to confluence
    /// (factor #3). The base EMA9/EMA21 ordering still enforces the micro-trend direction.</summary>
    public bool RequireDiAlignment { get; init; } = false;

    // â”€â”€ Pullback detection â”€â”€

    /// <summary>Bars to look back when measuring the prior trend leg and the pullback depth.</summary>
    public int PullbackLookback { get; init; } = 10;

    /// <summary>Maximum distance (in ATRs) the resumption bar close may sit from the EMA9 anchor.
    /// THROUGHPUT REFACTOR: 0.90â†’1.25 so deeper-but-valid pullbacks still qualify.</summary>
    public double PullbackMaxDistAtr { get; init; } = 1.25;

    /// <summary>Minimum pullback depth (in ATRs) from the recent leg extreme â€” ensures a real retrace.
    /// THROUGHPUT REFACTOR: 0.38â†’0.22 so shallow continuations are tradable, not rejected.</summary>
    public double PullbackMinDepthAtr { get; init; } = 0.22;

    /// <summary>Maximum pullback depth (in ATRs) â€” rejects a full trend reversal masquerading as a pullback.</summary>
    public double PullbackMaxDepthAtr { get; init; } = 3.0;

    // â”€â”€ Momentum-breakout continuation (complementary setup) â”€â”€

    /// <summary>Enable the complementary momentum-breakout continuation entry (fires on different bars than pullbacks).
    /// Default OFF: on the small-cap basket the pullback-resumption edge is materially cleaner; breakout
    /// continuations diluted profitability. Retained as an opt-in experimental path.</summary>
    public bool UseMomentumBreakout { get; init; } = false;

    /// <summary>Lookback window for the breakout base high/low.</summary>
    public int BreakoutLookback { get; init; } = 12;

    /// <summary>Minimum relative volume required to validate a momentum breakout.</summary>
    public double BreakoutMinRvol { get; init; } = 1.15;

    /// <summary>ATR margin by which the close must clear the breakout base to confirm.</summary>
    public double BreakoutBufferAtr { get; init; } = 0.05;

    /// <summary>Maximum distance (in ATRs) the breakout close may sit beyond the base â€” rejects late chases.</summary>
    public double BreakoutMaxExtensionAtr { get; init; } = 1.10;

    /// <summary>Maximum base height (in ATRs) over the breakout lookback â€” only fire breakouts out of a
    /// reasonably coiled / consolidated base (squeeze-style). Wide, sloppy bases are rejected because
    /// their breakouts are statistically far more likely to be false. Set very high to disable.</summary>
    public double BreakoutMaxBaseAtr { get; init; } = 4.0;

    /// <summary>Minimum confluence score required specifically for momentum-breakout entries.
    /// Breakouts are inherently lower win-rate than pullbacks, so they must clear a higher bar.
    /// Null falls back to the side's normal confluence requirement.</summary>
    public int? BreakoutMinConfluenceScore { get; init; }

    // â”€â”€ Resumption candle â”€â”€

    /// <summary>Minimum close-location within the resumption-bar range (0â€“1).</summary>
    public double ResumptionMinCloseLocationPct { get; init; } = 0.55;

    /// <summary>Maximum adverse-wick share of the resumption-bar range (0â€“1).</summary>
    public double ResumptionMaxAdverseWickPct { get; init; } = 0.45;

    // â”€â”€ Oscillator reset bands (longs) â”€â”€

    /// <summary>RSI lower bound for long resumption entries.</summary>
    public double RsiLongMin { get; init; } = 40.0;

    /// <summary>RSI upper bound for long resumption entries (rejects overbought chase).</summary>
    public double RsiLongMax { get; init; } = 72.0;

    /// <summary>Money Flow Index floor for longs (confirms buying pressure).</summary>
    public double MfiLongMin { get; init; } = 35.0;

    /// <summary>Williams %R ceiling for longs.</summary>
    public double WillRLongMax { get; init; } = -20.0;

    // â”€â”€ Oscillator reset bands (shorts) â”€â”€

    /// <summary>RSI upper bound for short resumption entries.</summary>
    public double RsiShortMax { get; init; } = 60.0;

    /// <summary>RSI lower bound for short resumption entries (rejects oversold chase).</summary>
    public double RsiShortMin { get; init; } = 28.0;

    /// <summary>Money Flow Index ceiling for shorts (confirms selling pressure).</summary>
    public double MfiShortMax { get; init; } = 65.0;

    /// <summary>Williams %R floor for shorts.</summary>
    public double WillRShortMin { get; init; } = -80.0;

    // â”€â”€ Order-flow confirmation â”€â”€

    /// <summary>Require L2 order-flow confirmation before entry.
    /// THROUGHPUT REFACTOR: now OFF by default â€” order-flow still contributes to the confluence
    /// score (factor #11), but harsh-tape bars with missing/unfavourable L2 are no longer hard-blocked.</summary>
    public bool RequireOrderFlowConfirmation { get; init; } = false;

    /// <summary>Minimum OFI signal for longs (negative = mild selling tolerated).</summary>
    public double OfiMinLong { get; init; } = -0.10;

    /// <summary>Maximum OFI signal for shorts (positive = mild buying tolerated).</summary>
    public double OfiMaxShort { get; init; } = 0.10;

    /// <summary>Minimum bid-side imbalance ratio for long entries (bids outweigh asks).</summary>
    public double ImbalanceMinLong { get; init; } = 0.80;

    /// <summary>Maximum ask-side imbalance ratio for short entries (asks outweigh bids).</summary>
    public double ImbalanceMaxShort { get; init; } = 1.25;

    // â”€â”€ Confluence scoring â”€â”€

    /// <summary>Minimum multi-factor confluence score (out of ~11) required to accept a signal.
    /// THROUGHPUT REFACTOR: 6â†’4. With the single-factor hard gates (EMA50/Supertrend/DI/HTF/order-flow)
    /// now folded into this score, a 4-of-11 holistic floor keeps quality while letting far more
    /// trades through than five independent veto gates ever did.</summary>
    public int MinConfluenceScore { get; init; } = 4;

    /// <summary>Optional confluence requirement applied only to long entries.</summary>
    public int? LongMinConfluenceScore { get; init; }

    /// <summary>Optional confluence requirement applied only to short entries.
    /// THROUGHPUT REFACTOR: 7â†’5 (shorts still demand one extra confirmation over longs).</summary>
    public int? ShortMinConfluenceScore { get; init; } = 5;

    // â”€â”€ HTF bias â”€â”€

    /// <summary>When true, higher-timeframe bias must agree (or be neutral) for entry.
    /// THROUGHPUT REFACTOR: now OFF by default â€” HTF disagreement no longer hard-blocks; the
    /// intraday confluence floor carries the directional-quality burden.</summary>
    public bool RequireHtfBias { get; init; } = false;

    /// <summary>Allow entry when HTF bias is neutral.</summary>
    public bool AllowNeutralHtf { get; init; } = true;

    /// <summary>Minimum confluence score required when trading against the HTF bias.
    /// THROUGHPUT REFACTOR: 7â†’5 to match the relaxed score floors.</summary>
    public int WeakCounterTrendMinScore { get; init; } = 5;

    // â”€â”€ Over-extension guard â”€â”€

    /// <summary>Maximum distance (in ATRs) the entry close may sit beyond EMA9 in the trade direction.
    /// THROUGHPUT REFACTOR: 1.6â†’2.2 so strong continuations are not rejected as "extended".</summary>
    public double MaxExtensionAtr { get; init; } = 2.2;

    /// <summary>Maximum RSI allowed for long entries before the over-extension guard blocks them.</summary>
    public double LongMaxEntryRsi { get; init; } = 80.0;

    /// <summary>Minimum RSI allowed for short entries before the over-extension guard blocks them.</summary>
    public double ShortMinEntryRsi { get; init; } = 20.0;

    // â”€â”€ Indecision-bar filter â”€â”€

    /// <summary>Reject resumption bars that are indecision candles (tiny body / range).</summary>
    public bool RejectIndecisionBar { get; init; } = true;

    /// <summary>Body-to-range ratio below which a bar is classified as indecision.
    /// THROUGHPUT REFACTOR: 0.22â†’0.15 so only genuine dojis are rejected, not modest-body bars.</summary>
    public double IndecisionBarMaxBodyPct { get; init; } = 0.15;

    // â”€â”€ Stop placement â”€â”€

    /// <summary>ATR buffer applied beyond the pullback swing extreme when placing the stop.</summary>
    public double StopBufferAtr { get; init; } = 0.15;

    /// <summary>Minimum stop distance expressed in ATRs.</summary>
    public double MinStopAtr { get; init; } = 0.35;

    /// <summary>Maximum stop distance expressed in ATRs.</summary>
    public double MaxStopAtr { get; init; } = 1.5;

    // â”€â”€ Exit cascade (elite conduct â€” mirrors the proven V16 profile) â”€â”€

    /// <summary>Hard stop distance in R-multiples.</summary>
    public double HardStopR { get; init; } = 0.55;

    /// <summary>R-multiple at which the stop is moved to breakeven.
    /// QUALITY REFACTOR (2026-06-05 v2): 0.50â†’0.42. Reaching BE sooner converts more would-be
    /// HardStop losers (the report's dominant loss bucket) into scratches once a trade has shown edge,
    /// without throttling entries â€” trade count is unchanged.</summary>
    public double BreakevenR { get; init; } = 0.42;

    /// <summary>Trailing stop distance in R-multiples once breakeven has been reached.</summary>
    public double TrailR { get; init; } = 0.15;

    /// <summary>Maximum giveback from peak unrealised P&amp;L as a fraction.</summary>
    public double GivebackPct { get; init; } = 0.60;

    /// <summary>Absolute dollar cap on giveback from peak unrealised P&amp;L.
    /// QUALITY REFACTOR (2026-06-05 v2): 28â†’20. Locks in more of each winner's peak so faders give
    /// back less; lifts profit-factor on the existing trade population rather than cutting trades.</summary>
    public double GivebackUsdCap { get; init; } = 20.0;

    /// <summary>First profit target in R-multiples (partial exit).</summary>
    public double Tp1R { get; init; } = 1.16;

    /// <summary>Second profit target in R-multiples (full exit of remainder).</summary>
    public double Tp2R { get; init; } = 2.20;

    /// <summary>Maximum number of bars to hold a position before time-based exit.</summary>
    public int MaxHoldBars { get; init; } = 45;

    /// <summary>When true, reaching TP1 tightens the stop to breakeven.</summary>
    public bool Tp1TightenToBe { get; init; } = true;

    /// <summary>Fraction of original size to scale out at TP1.</summary>
    public double Tp1PartialClosePct { get; init; } = 0.40;

    /// <summary>ATR-buffered breakeven offset applied when TP1 tightens the stop to BE.</summary>
    public double Tp1BreakevenBufferAtr { get; init; } = 0.05;

    /// <summary>Enable EMA9-based dynamic trailing stop after breakeven.</summary>
    public bool UseEmaTrail { get; init; } = true;

    /// <summary>ATR buffer beyond the EMA9 trail line.</summary>
    public double EmaTrailBufferAtr { get; init; } = 0.12;

    /// <summary>Enable ATR-based trailing TP2 with directional-continuation extension.</summary>
    public bool UseTrailingTp2 { get; init; } = true;

    /// <summary>ATR multiplier for the trailing TP2 distance.</summary>
    public double TrailingTp2AtrMultiplier { get; init; } = 0.55;

    /// <summary>Enable the L1/L2-decision check on the two-opposite-bars flatten path.</summary>
    public bool UseL1L2DecisionOnOppositeBarsFlatten { get; init; } = true;

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

    /// <summary>Enable price-tier-aware micro trailing stop (tighter for sub-$5 names).</summary>
    public bool UsePriceTierMicroTrail { get; init; } = true;

    /// <summary>Enable price-tier stop floor.</summary>
    public bool UsePriceTierStopFloor { get; init; } = true;

    /// <summary>Enable MA-extension + L2-flip flatten exit.</summary>
    public bool UseMaExtensionL2Flip { get; init; } = true;

    /// <summary>Minimum R in unrealised gain before MA-extension exit can fire.</summary>
    public double MaExtensionMinR { get; init; } = 0.30;

    /// <summary>ATR distance threshold for detecting price extension beyond the moving average.</summary>
    public double MaExtensionAtrThreshold { get; init; } = 1.50;

    /// <summary>Simulated slippage deducted at entry/exit (cents per share).</summary>
    public double SlippageCents { get; init; } = 1.0;

    /// <summary>Commission charged per share for P&amp;L calculation.</summary>
    public double CommissionPerShare { get; init; } = 0.005;

    /// <summary>When false, ignore adaptive V3 exit overrides and use the native exit config as-is.</summary>
    public bool RespectSelfLearningExitOverrides { get; init; } = true;

    // â”€â”€ Diagnostics â”€â”€

    /// <summary>Emit per-run diagnostic rejection summaries when set (or via env var).</summary>
    public bool EnableDiagnostics { get; init; } = false;

    /// <summary>Label prefix prepended to diagnostic output lines.</summary>
    public string DiagnosticsLabel { get; init; } = "V2-ApexFlow";
}

/// <summary>
/// Strategy V2 "ApexFlow" â€” trend-pullback continuation entries confirmed by live
/// order-flow (OFI + book imbalance), routed through the shared elite exit cascade.
/// Replaces the legacy worst-performing V2 Conduct adapter wrapper.
/// </summary>
public sealed class StrategyV2 : BacktestStrategyBase
{
    private const string SetupKey = "V2_FLOW";

    private readonly V2Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    /// <summary>Initialises a new ApexFlow strategy with the given (or default) configuration.</summary>
    public StrategyV2(V2Config? cfg = null)
    {
        _cfg = cfg ?? new V2Config();
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
        };
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Signal generation
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var diag = new FlowDiagnostics(_cfg.DiagnosticsLabel,
            _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());

        int warmup = Math.Max(_cfg.PullbackLookback + 2, _cfg.MinimumSignalHistoryBars);
        if (triggerBars.Length < warmup)
        {
            diag.PrintSummary();
            return Array.Empty<BacktestSignal>();
        }

        var accepted = new List<BacktestSignal>();
        var daySignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBarIndex = -10_000;

        for (int i = warmup; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atr = row.Atr14;

            if (double.IsNaN(atr) || atr <= 0) continue;
            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Rsi14)) continue;

            diag.RawScanned++;

            if (i - lastAcceptedBarIndex < _cfg.CooldownBars) continue;

            // â”€â”€ time gate â”€â”€
            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes) { diag.Reject("before-entry-start"); continue; }
            if (minuteEt > 960 - _cfg.LastEntryMinuteBeforeClose) { diag.Reject("too-close-to-close"); continue; }
            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows)) { diag.Reject("outside-entry-window"); continue; }

            // â”€â”€ core filters â”€â”€
            if (!PassesCoreFilters(row, out var coreReason)) { diag.Reject($"core:{coreReason}"); continue; }
            if (IsMarketRegimeBlocked(row)) { diag.Reject("market-regime-blocked"); continue; }

            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            int countForDay = daySignalCounts.GetValueOrDefault(dayEt);
            if (countForDay >= _cfg.MaxSignalsPerDay) { diag.Reject("max-signals-per-day"); continue; }

            // â”€â”€ trend qualification â”€â”€
            TradeSide? side = QualifyTrend(row);
            if (side == null) { diag.Reject("no-trend"); continue; }
            if (side == TradeSide.Long && !_cfg.AllowLong) { diag.Reject("long-disabled"); continue; }
            if (side == TradeSide.Short && !_cfg.AllowShort) { diag.Reject("short-disabled"); continue; }

            // â”€â”€ setup structure: pullback-resumption OR momentum-breakout continuation â”€â”€
            bool isPullback = DetectPullbackResumption(triggerBars, i, side.Value, atr, out double swingExtreme, out var pullbackReason);
            bool isBreakout = false;
            if (!isPullback)
            {
                if (!_cfg.UseMomentumBreakout ||
                    !DetectMomentumBreakout(triggerBars, i, side.Value, atr, out swingExtreme, out var breakoutReason))
                {
                    diag.Reject($"setup:{pullbackReason}"); continue;
                }
                isBreakout = true;
            }

            // â”€â”€ resumption candle quality â”€â”€
            if (!IsResumptionCandle(row, side.Value)) { diag.Reject("weak-resumption-candle"); continue; }

            if (_cfg.RejectIndecisionBar)
            {
                double body = Math.Abs(row.Bar.Close - row.Bar.Open);
                double range = row.Bar.High - row.Bar.Low;
                if (range > 0 && body / range <= _cfg.IndecisionBarMaxBodyPct) { diag.Reject("indecision-bar"); continue; }
            }

            // â”€â”€ over-extension guard â”€â”€
            if (IsOverExtended(row, side.Value, atr)) { diag.Reject("over-extended"); continue; }

            diag.RawSignals++;

            // â”€â”€ order-flow confirmation â”€â”€
            if (_cfg.RequireOrderFlowConfirmation && !PassesOrderFlow(row, side.Value))
            {
                diag.Reject("order-flow-unfavorable"); continue;
            }

            // â”€â”€ confluence scoring â”€â”€
            int confluence = ComputeConfluenceScore(triggerBars, i, side.Value);
            int requiredScore = side == TradeSide.Long
                ? _cfg.LongMinConfluenceScore ?? _cfg.MinConfluenceScore
                : _cfg.ShortMinConfluenceScore ?? _cfg.MinConfluenceScore;
            if (isBreakout && _cfg.BreakoutMinConfluenceScore is int breakoutScore)
                requiredScore = Math.Max(requiredScore, breakoutScore);

            // â”€â”€ HTF bias â”€â”€
            string htfBias = ComputeHtfBias(row.Bar.Timestamp, bars15m, bars1h, bars1d);
            if (!PassesHtfBias(side.Value, htfBias, confluence, out var htfReason))
            {
                diag.Reject($"htf:{htfReason}"); continue;
            }

            if (confluence < requiredScore) { diag.Reject($"confluence:{confluence}<{requiredScore}"); continue; }

            if (IsSelfLearningBlocked(SetupKey)) { diag.Reject("self-learning-blocked"); continue; }

            // â”€â”€ entry / stop / size â”€â”€
            double entryPrice = _cfg.UseNextBarOpenEntry && i + 1 < triggerBars.Length
                ? triggerBars[i + 1].Bar.Open
                : row.Bar.Close;

            double rawDist = ComputeStopDistance(entryPrice, swingExtreme, side.Value, atr);
            double adjDist = ApplySelfLearningStopMultiplier(rawDist);
            double stopPrice = side.Value == TradeSide.Long ? entryPrice - adjDist : entryPrice + adjDist;
            double riskPerShare = Math.Abs(entryPrice - stopPrice);
            if (riskPerShare < _cfg.MinRiskPerShare || riskPerShare < 0.001) { diag.Reject("risk-too-small"); continue; }

            int posSize = BacktestHelpers.ComputePositionSize(
                entryPrice, riskPerShare, _cfg.RiskPerTradeDollars,
                _cfg.AccountSize, _cfg.MaxPositionNotionalPctOfAccount, _cfg.MaxShares);
            posSize = ApplySelfLearningPositionSize(posSize, SetupKey);
            if (posSize <= 0) { diag.Reject("position-size-zero"); continue; }

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
                SetupKey,
                confluence));

            diag.Accepted++;
            daySignalCounts[dayEt] = countForDay + 1;
            lastAcceptedBarIndex = i;
        }

        diag.PrintSummary();
        return accepted;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var exitCfg = _cfg.RespectSelfLearningExitOverrides
            ? ApplySelfLearningExitOverrides(_exitCfg, signal.SubStrategy)
            : _exitCfg;

        return ExitEngine.SimulateTrade(signal, triggerBars, exitCfg);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Entry helpers
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Qualify an established intraday trend on the current bar. Returns the trade side or null.</summary>
    private TradeSide? QualifyTrend(EnrichedBar row)
    {
        bool longTrend = row.Ema9 > row.Ema21;
        bool shortTrend = row.Ema9 < row.Ema21;

        if (_cfg.RequireEma50Alignment && !double.IsNaN(row.Ema50))
        {
            longTrend = longTrend && row.Ema21 >= row.Ema50;
            shortTrend = shortTrend && row.Ema21 <= row.Ema50;
        }

        if (_cfg.RequireSupertrendAlignment)
        {
            longTrend = longTrend && row.StDirection == 1;
            shortTrend = shortTrend && row.StDirection == -1;
        }

        if (!double.IsNaN(row.Adx))
        {
            bool adxOk = row.Adx >= _cfg.AdxMin && row.Adx <= _cfg.AdxMax;
            longTrend = longTrend && adxOk;
            shortTrend = shortTrend && adxOk;
        }
        else
        {
            return null;
        }

        if (_cfg.RequireDiAlignment && !double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi))
        {
            longTrend = longTrend && row.PlusDi > row.MinusDi;
            shortTrend = shortTrend && row.MinusDi > row.PlusDi;
        }

        if (longTrend && _cfg.AllowLong) return TradeSide.Long;
        if (shortTrend && _cfg.AllowShort) return TradeSide.Short;
        return null;
    }

    /// <summary>
    /// Detect a shallow pullback into the EMA9 anchor that is now resuming in the trend
    /// direction. Reports the pullback swing extreme used for stop placement.
    /// </summary>
    private bool DetectPullbackResumption(
        EnrichedBar[] bars, int idx, TradeSide side, double atr,
        out double swingExtreme, out string reason)
    {
        swingExtreme = double.NaN;
        int start = Math.Max(0, idx - _cfg.PullbackLookback);
        var row = bars[idx];

        if (double.IsNaN(row.Ema9)) { reason = "no-ema"; return false; }

        // Resumption bar must sit close to a dynamic support anchor (a pullback, not a chase).
        // Accept the nearest of EMA9 / EMA21 / VWAP so retraces to any of these qualify.
        double distToAnchor = Math.Abs(row.Bar.Close - row.Ema9) / atr;
        if (!double.IsNaN(row.Ema21))
            distToAnchor = Math.Min(distToAnchor, Math.Abs(row.Bar.Close - row.Ema21) / atr);
        if (!double.IsNaN(row.Vwap))
            distToAnchor = Math.Min(distToAnchor, Math.Abs(row.Bar.Close - row.Vwap) / atr);
        if (distToAnchor > _cfg.PullbackMaxDistAtr) { reason = "too-far-from-ema"; return false; }

        if (side == TradeSide.Long)
        {
            double legHigh = double.MinValue;
            double pullbackLow = double.MaxValue;
            for (int j = start; j <= idx; j++)
            {
                legHigh = Math.Max(legHigh, bars[j].Bar.High);
                pullbackLow = Math.Min(pullbackLow, bars[j].Bar.Low);
            }
            swingExtreme = pullbackLow;

            double depth = (legHigh - pullbackLow) / atr;
            if (depth < _cfg.PullbackMinDepthAtr) { reason = "shallow-leg"; return false; }
            if (depth > _cfg.PullbackMaxDepthAtr) { reason = "leg-too-deep"; return false; }

            // Price should be turning back up off the pullback.
            if (row.Bar.Close <= bars[idx - 1].Bar.Close) { reason = "not-resuming"; return false; }
        }
        else
        {
            double legLow = double.MaxValue;
            double pullbackHigh = double.MinValue;
            for (int j = start; j <= idx; j++)
            {
                legLow = Math.Min(legLow, bars[j].Bar.Low);
                pullbackHigh = Math.Max(pullbackHigh, bars[j].Bar.High);
            }
            swingExtreme = pullbackHigh;

            double depth = (pullbackHigh - legLow) / atr;
            if (depth < _cfg.PullbackMinDepthAtr) { reason = "shallow-leg"; return false; }
            if (depth > _cfg.PullbackMaxDepthAtr) { reason = "leg-too-deep"; return false; }

            if (row.Bar.Close >= bars[idx - 1].Bar.Close) { reason = "not-resuming"; return false; }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Detect a momentum-breakout continuation: in an established trend, the current bar
    /// breaks the recent N-bar base extreme with elevated volume, without being a late chase.
    /// Reports the breakout base extreme used for stop placement.
    /// </summary>
    private bool DetectMomentumBreakout(
        EnrichedBar[] bars, int idx, TradeSide side, double atr,
        out double swingExtreme, out string reason)
    {
        swingExtreme = double.NaN;
        var row = bars[idx];

        if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.BreakoutMinRvol) { reason = "breakout-low-rvol"; return false; }

        int start = Math.Max(0, idx - _cfg.BreakoutLookback);
        double buffer = _cfg.BreakoutBufferAtr * atr;

        if (side == TradeSide.Long)
        {
            double baseHigh = double.MinValue;
            double baseLow = double.MaxValue;
            for (int j = start; j < idx; j++)
            {
                baseHigh = Math.Max(baseHigh, bars[j].Bar.High);
                baseLow = Math.Min(baseLow, bars[j].Bar.Low);
            }
            swingExtreme = baseLow;

            if ((baseHigh - baseLow) / atr > _cfg.BreakoutMaxBaseAtr) { reason = "breakout-base-too-wide"; return false; }
            if (row.Bar.Close < baseHigh + buffer) { reason = "no-breakout"; return false; }
            if ((row.Bar.Close - baseHigh) / atr > _cfg.BreakoutMaxExtensionAtr) { reason = "breakout-late-chase"; return false; }
        }
        else
        {
            double baseLow = double.MaxValue;
            double baseHigh = double.MinValue;
            for (int j = start; j < idx; j++)
            {
                baseLow = Math.Min(baseLow, bars[j].Bar.Low);
                baseHigh = Math.Max(baseHigh, bars[j].Bar.High);
            }
            swingExtreme = baseHigh;

            if ((baseHigh - baseLow) / atr > _cfg.BreakoutMaxBaseAtr) { reason = "breakout-base-too-wide"; return false; }
            if (row.Bar.Close > baseLow - buffer) { reason = "no-breakout"; return false; }
            if ((baseLow - row.Bar.Close) / atr > _cfg.BreakoutMaxExtensionAtr) { reason = "breakout-late-chase"; return false; }
        }

        reason = string.Empty;
        return true;
    }
    private bool IsResumptionCandle(EnrichedBar row, TradeSide side)
    {
        double range = row.Bar.High - row.Bar.Low;
        if (range <= 0) return false;
        double closeLoc = (row.Bar.Close - row.Bar.Low) / range; // 0 = at low, 1 = at high

        if (side == TradeSide.Long)
        {
            if (row.Bar.Close <= row.Bar.Open) return false;
            if (closeLoc < _cfg.ResumptionMinCloseLocationPct) return false;
            double upperWick = (row.Bar.High - row.Bar.Close) / range;
            return upperWick <= _cfg.ResumptionMaxAdverseWickPct;
        }
        else
        {
            if (row.Bar.Close >= row.Bar.Open) return false;
            if (1.0 - closeLoc < _cfg.ResumptionMinCloseLocationPct) return false;
            double lowerWick = (row.Bar.Close - row.Bar.Low) / range;
            return lowerWick <= _cfg.ResumptionMaxAdverseWickPct;
        }
    }

    /// <summary>Reject entries that are already over-extended away from the EMA9 anchor.</summary>
    private bool IsOverExtended(EnrichedBar row, TradeSide side, double atr)
    {
        if (!double.IsNaN(row.Ema9))
        {
            double ext = (row.Bar.Close - row.Ema9) / atr;
            if (side == TradeSide.Long && ext > _cfg.MaxExtensionAtr) return true;
            if (side == TradeSide.Short && -ext > _cfg.MaxExtensionAtr) return true;
        }

        if (!double.IsNaN(row.Rsi14))
        {
            if (side == TradeSide.Long && row.Rsi14 > _cfg.LongMaxEntryRsi) return true;
            if (side == TradeSide.Short && row.Rsi14 < _cfg.ShortMinEntryRsi) return true;
        }

        return false;
    }

    /// <summary>Order-flow confirmation: OFI in-trend plus a favourable book imbalance.</summary>
    private bool PassesOrderFlow(EnrichedBar row, TradeSide side)
    {
        if (side == TradeSide.Long)
        {
            if (!double.IsNaN(row.OfiSignal) && row.OfiSignal < _cfg.OfiMinLong) return false;
            if (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio < _cfg.ImbalanceMinLong) return false;
            if (!double.IsNaN(row.L0ImbalanceRatio) && row.L0ImbalanceRatio < _cfg.ImbalanceMinLong) return false;
        }
        else
        {
            if (!double.IsNaN(row.OfiSignal) && row.OfiSignal > _cfg.OfiMaxShort) return false;
            if (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio > _cfg.ImbalanceMaxShort) return false;
            if (!double.IsNaN(row.L0ImbalanceRatio) && row.L0ImbalanceRatio > _cfg.ImbalanceMaxShort) return false;
        }
        return true;
    }

    /// <summary>
    /// Multi-factor continuation confluence score (0â€“11). Each confirming factor adds one point:
    /// EMA50 stack, Supertrend, ADX/DI energy, RSI reset turning, Stoch turning, MACD-hist turning,
    /// MFI pressure, Williams %R, VWAP trend context, Donchian position, and order-flow strength.
    /// </summary>
    private int ComputeConfluenceScore(EnrichedBar[] bars, int idx, TradeSide side)
    {
        var row = bars[idx];
        var prev = bars[idx - 1];
        int score = 0;

        // 1. EMA50 stack alignment
        if (!double.IsNaN(row.Ema50))
        {
            if (side == TradeSide.Long && row.Ema9 > row.Ema21 && row.Ema21 > row.Ema50) score++;
            else if (side == TradeSide.Short && row.Ema9 < row.Ema21 && row.Ema21 < row.Ema50) score++;
        }

        // 2. Supertrend aligned
        if ((side == TradeSide.Long && row.StDirection == 1) || (side == TradeSide.Short && row.StDirection == -1)) score++;

        // 3. ADX directional energy
        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.AdxMin && !double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi))
        {
            if (side == TradeSide.Long && row.PlusDi > row.MinusDi) score++;
            else if (side == TradeSide.Short && row.MinusDi > row.PlusDi) score++;
        }

        // 4. RSI reset turning back in trend direction
        if (!double.IsNaN(row.Rsi14) && !double.IsNaN(prev.Rsi14))
        {
            if (side == TradeSide.Long && row.Rsi14 >= _cfg.RsiLongMin && row.Rsi14 <= _cfg.RsiLongMax && row.Rsi14 > prev.Rsi14) score++;
            else if (side == TradeSide.Short && row.Rsi14 <= _cfg.RsiShortMax && row.Rsi14 >= _cfg.RsiShortMin && row.Rsi14 < prev.Rsi14) score++;
        }

        // 5. Stochastic turning
        if (!double.IsNaN(row.StochK) && !double.IsNaN(row.StochD))
        {
            if (side == TradeSide.Long && row.StochK > row.StochD) score++;
            else if (side == TradeSide.Short && row.StochK < row.StochD) score++;
        }

        // 6. MACD histogram momentum turning
        if (!double.IsNaN(row.MacdHist) && !double.IsNaN(prev.MacdHist))
        {
            double delta = row.MacdHist - prev.MacdHist;
            if (side == TradeSide.Long && delta > 0) score++;
            else if (side == TradeSide.Short && delta < 0) score++;
        }

        // 7. MFI pressure
        if (!double.IsNaN(row.Mfi14))
        {
            if (side == TradeSide.Long && row.Mfi14 >= _cfg.MfiLongMin) score++;
            else if (side == TradeSide.Short && row.Mfi14 <= _cfg.MfiShortMax) score++;
        }

        // 8. Williams %R
        if (!double.IsNaN(row.WillR14))
        {
            if (side == TradeSide.Long && row.WillR14 >= _cfg.WillRLongMax) score++;
            else if (side == TradeSide.Short && row.WillR14 <= _cfg.WillRShortMin) score++;
        }

        // 9. VWAP trend context (price on the trend side of VWAP)
        if (!double.IsNaN(row.Vwap))
        {
            if (side == TradeSide.Long && row.Bar.Close >= row.Vwap) score++;
            else if (side == TradeSide.Short && row.Bar.Close <= row.Vwap) score++;
        }

        // 10. Donchian position
        if (!double.IsNaN(row.DcPct))
        {
            if (side == TradeSide.Long && row.DcPct >= 0.55) score++;
            else if (side == TradeSide.Short && row.DcPct <= 0.45) score++;
        }

        // 11. Order-flow strength
        if (!double.IsNaN(row.OfiSignal) && !double.IsNaN(row.ImbalanceRatio))
        {
            if (side == TradeSide.Long && row.OfiSignal > 0 && row.ImbalanceRatio >= _cfg.ImbalanceMinLong) score++;
            else if (side == TradeSide.Short && row.OfiSignal < 0 && row.ImbalanceRatio <= _cfg.ImbalanceMaxShort) score++;
        }

        return score;
    }

    /// <summary>Stop distance from the pullback swing extreme, clamped to the configured ATR band.</summary>
    private double ComputeStopDistance(double entryPrice, double swingExtreme, TradeSide side, double atr)
    {
        double buffer = _cfg.StopBufferAtr * atr;
        double dist;
        if (side == TradeSide.Long)
        {
            double stop = swingExtreme - buffer;
            dist = entryPrice - stop;
        }
        else
        {
            double stop = swingExtreme + buffer;
            dist = stop - entryPrice;
        }

        double minDist = _cfg.MinStopAtr * atr;
        double maxDist = _cfg.MaxStopAtr * atr;
        return Math.Clamp(dist, minDist, maxDist);
    }

    private bool PassesCoreFilters(EnrichedBar row, out string reason)
    {
        if (row.Bar.Close < _cfg.MinPrice || row.Bar.Close > _cfg.MaxPrice) { reason = "price"; return false; }
        if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin) { reason = "rvol"; return false; }
        if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin) { reason = "liquidity"; return false; }
        if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax) { reason = "spread"; return false; }
        if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.MinVolAccel) { reason = "vol-accel"; return false; }
        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Rsi14)) { reason = "indicators"; return false; }
        reason = string.Empty;
        return true;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // HTF bias (15m â†’ 1h â†’ 1d EMA9/EMA21 trend agreement)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string ComputeHtfBias(DateTime ts, EnrichedBar[]? bars15m, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        // Use the highest available timeframe; fall back to lower frames.
        foreach (var frame in new[] { bars1d, bars1h, bars15m })
        {
            var bias = FrameBias(ts, frame);
            if (bias != "neutral") return bias;
        }
        return "neutral";
    }

    private static string FrameBias(DateTime ts, EnrichedBar[]? frame)
    {
        if (frame == null || frame.Length == 0) return "neutral";
        EnrichedBar? last = null;
        for (int i = 0; i < frame.Length; i++)
        {
            if (frame[i].Bar.Timestamp <= ts) last = frame[i];
            else break;
        }
        if (last == null || double.IsNaN(last.Ema9) || double.IsNaN(last.Ema21)) return "neutral";
        if (last.Ema9 > last.Ema21) return "bull";
        if (last.Ema9 < last.Ema21) return "bear";
        return "neutral";
    }

    private bool PassesHtfBias(TradeSide side, string htfBias, int confluence, out string reason)
    {
        reason = string.Empty;
        if (!_cfg.RequireHtfBias) return true;

        if (htfBias == "neutral")
        {
            if (_cfg.AllowNeutralHtf) return true;
            reason = "neutral-blocked";
            return false;
        }

        bool aligned = (side == TradeSide.Long && htfBias == "bull") || (side == TradeSide.Short && htfBias == "bear");
        if (aligned) return true;

        // Counter-trend: demand higher confluence.
        if (confluence >= _cfg.WeakCounterTrendMinScore) return true;
        reason = "counter-trend";
        return false;
    }

    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V2");

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Lightweight rejection diagnostics
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed class FlowDiagnostics(string label, bool enabled)
    {
        private readonly Dictionary<string, int> _rejects = new();
        public int RawScanned;
        public int RawSignals;
        public int Accepted;

        public void Reject(string reason)
        {
            if (!enabled) return;
            _rejects[reason] = _rejects.GetValueOrDefault(reason) + 1;
        }

        public void PrintSummary()
        {
            if (!enabled) return;
            Console.WriteLine($"[{label}] scanned={RawScanned} rawSignals={RawSignals} accepted={Accepted}");
            foreach (var kv in _rejects.OrderByDescending(k => k.Value))
                Console.WriteLine($"[{label}]   reject {kv.Key} = {kv.Value}");
        }
    }
}


