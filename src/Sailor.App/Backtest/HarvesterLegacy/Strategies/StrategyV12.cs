using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// V12 â€” Adaptive Multi-Regime  Confluence  Strategy
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//
// Key innovations over V13 (ORB-pullback-only):
//
// 1. CONFLUENCE SCORING â€” every trade must pass a multi-factor confirmation
//    score (OFI, Supertrend, Stochastic, MFI, Williams %R, DPO, EMA stack,
//    VWAP, BB position).  More confirmations â†’ higher win-rate.
//
// 2. THREE SUB-STRATEGIES selected by volatility regime:
//    â€¢ MOMENTUM  (high-vol) â€” Donchian channel breakout + Supertrend + OFI
//    â€¢ REVERSION (low-vol)  â€” BB bounce + Stochastic crossover + MFI divergence
//    â€¢ TREND_CONTINUATION   â€” EMA pullback + VWAP reversion + OR context
//
// 3. ADAPTIVE EXITS â€” exit config varies per sub-strategy and per volatility
//    regime.  Reversion trades get tighter stops + faster breakeven; momentum
//    trades get wider stops + micro-trail.
//
// 4. ORDER FLOW (OFI) â€” first strategy to integrate OfiCum / OfiSignal,
//    SpreadZ and L2Liquidity as scored confirmation factors.
//
// 5. INDICATORS PREVIOUSLY UNUSED â€” Supertrend, DonchianChannels, StochK/D,
//    MFI14, WillR14, Dpo20, Sma200 â€” none are used by V13.
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class V12Config
{
    // â”€â”€ Risk / sizing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double RiskPerTradeDollars { get; set; } = 26.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;
    public int MaxShares { get; set; } = 6_500;
    public double MinRiskPerShare { get; set; } = 0.01;

    // â”€â”€ General â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public bool UseNextBarOpenEntry { get; set; } = true;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
    public int MaxSignalsPerDay { get; set; } = 3;
    public int CooldownBars { get; set; } = 8;

    // â”€â”€ Price / liquidity / spread â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;
    public double L2LiquidityMin { get; set; } = 20.0;
    public double SpreadZMax { get; set; } = 2.0;
    public double MinVolAccel { get; set; } = -0.10;
    public double RvolMin { get; set; } = 0.80;

    // â”€â”€ Session timing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public int MarketOpenMinute { get; set; } = 570;   // 09:30 ET
    public int OpeningRangeMinutes { get; set; } = 15;
    public int SkipFirstNMinutes { get; set; } = 16;
    public (int Start, int End)[] EntryWindows { get; set; } =
        [(582, 940)];

    // â”€â”€ Confluence thresholds â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public int MinConfluenceMomentum { get; set; } = 6;
    public int MinConfluenceReversion { get; set; } = 7;
    public int MinConfluenceContinuation { get; set; } = 6;

    // â”€â”€ Volatility regime (BB bandwidth percentile) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double BbBandwidthHighThreshold { get; set; } = 0.06;
    public double BbBandwidthLowThreshold { get; set; } = 0.025;

    // â”€â”€ Momentum sub-strategy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double DonchianBreakoutPct { get; set; } = 0.85;  // DcPct level for breakout
    public double MomentumRvolMin { get; set; } = 0.95;

    // â”€â”€ Reversion sub-strategy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double BbPctBLongEntry { get; set; } = 0.20;
    public double BbPctBShortEntry { get; set; } = 0.80;
    public double StochOversold { get; set; } = 30.0;
    public double StochOverbought { get; set; } = 70.0;

    // â”€â”€ Continuation sub-strategy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double EmaPullbackMaxAtr { get; set; } = 0.60;
    public double VwapPullbackMaxAtr { get; set; } = 0.70;

    // â”€â”€ ADX â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double AdxMin { get; set; } = 12.0;
    public double AdxMax { get; set; } = 55.0;

    // â”€â”€ RSI zones â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double RsiLongMin { get; set; } = 35.0;
    public double RsiLongMax { get; set; } = 75.0;
    public double RsiShortMin { get; set; } = 25.0;
    public double RsiShortMax { get; set; } = 65.0;

    // â”€â”€ HTF / MTF â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public bool RequireHtfBias { get; set; } = true;
    public bool AllowCounterTrendWithHighConfluence { get; set; } = true;
    public int CounterTrendMinConfluence { get; set; } = 8;
    public bool RequireMtfAlign { get; set; } = false;

    // â”€â”€ Exit â€” Momentum â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double MomentumHardStopR { get; set; } = 0.95;
    public double MomentumBreakevenR { get; set; } = 0.50;
    public double MomentumTrailR { get; set; } = 0.30;
    public double MomentumGivebackPct { get; set; } = 0.22;
    public double MomentumTp1R { get; set; } = 0.65;
    public double MomentumTp2R { get; set; } = 1.30;
    public int MomentumMaxHoldBars { get; set; } = 25;

    // â”€â”€ Exit â€” Reversion â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double ReversionHardStopR { get; set; } = 0.65;
    public double ReversionBreakevenR { get; set; } = 0.30;
    public double ReversionTrailR { get; set; } = 0.18;
    public double ReversionGivebackPct { get; set; } = 0.15;
    public double ReversionTp1R { get; set; } = 0.45;
    public double ReversionTp2R { get; set; } = 0.85;
    public int ReversionMaxHoldBars { get; set; } = 15;

    // â”€â”€ Exit â€” Continuation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double ContinuationHardStopR { get; set; } = 0.80;
    public double ContinuationBreakevenR { get; set; } = 0.40;
    public double ContinuationTrailR { get; set; } = 0.24;
    public double ContinuationGivebackPct { get; set; } = 0.20;
    public double ContinuationTp1R { get; set; } = 0.55;
    public double ContinuationTp2R { get; set; } = 1.10;
    public int ContinuationMaxHoldBars { get; set; } = 22;

    // â”€â”€ Costs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
    public double GivebackUsdCap { get; set; } = 26.0;

    // â”€â”€ Diagnostics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public bool EnableDiagnostics { get; set; } = false;
    public string DiagnosticsLabel { get; set; } = "V12";
}

/// <summary>
/// Phase 6.15 â€” FROZEN strategy. Retained for historical/regression comparison and explicit selection only;
/// excluded from the default/active comparison plans. Superseded by Conduct-V3. Trade conduct is unchanged.
/// </summary>
[FrozenStrategy(supersededBy: "Conduct-V3", reason: "Superseded by Conduct-V3 / later V-line strategies.")]
public sealed class StrategyV12 : BacktestStrategyBase
{
    private readonly V12Config _cfg;
    private readonly V3SignalCore _signalCore;

    // Per-sub-strategy exit configs, built once in ctor.
    private readonly ExitEngine.ExitConfig _exitMomentum;
    private readonly ExitEngine.ExitConfig _exitReversion;
    private readonly ExitEngine.ExitConfig _exitContinuation;

    // Cached day contexts (opening-range data).
    private Dictionary<DateOnly, DayContext>? _dayContexts;

    public StrategyV12(V12Config? cfg = null)
    {
        _cfg = cfg ?? new V12Config();

        _signalCore = new V3SignalCore(new V3SignalCoreConfig
        {
            RiskPerTradeDollars = _cfg.RiskPerTradeDollars,
            AccountSize = _cfg.AccountSize,
            MaxPositionNotionalPctOfAccount = _cfg.MaxPositionNotionalPctOfAccount,
            MaxShares = _cfg.MaxShares,
            MinRiskPerShare = _cfg.MinRiskPerShare,
            MinPrice = _cfg.MinPrice,
            MaxPrice = _cfg.MaxPrice,
            UseNextBarOpenEntry = _cfg.UseNextBarOpenEntry,
            VwapStretchAtr = 1.20,
            VwapEnabled = true,
            BbEntryPctbLow = 0.15,
            BbEntryPctbHigh = 0.85,
            BbEnabled = true,
            SqueezeEnabled = true,
            SqueezeBars = 6,
            L2LiquidityMin = _cfg.L2LiquidityMin,
            SpreadZMax = _cfg.SpreadZMax,
            VolAccelMin = _cfg.MinVolAccel,
            RvolMin = _cfg.RvolMin,
            RsiOversold = 38.0,
            RsiOverbought = 62.0,
            RequireVolumeConfirm = true,
            HardStopR = _cfg.ContinuationHardStopR,
            TrailR = _cfg.ContinuationTrailR,
            GivebackPct = _cfg.ContinuationGivebackPct,
            Tp1R = _cfg.ContinuationTp1R,
            Tp2R = _cfg.ContinuationTp2R,
            BreakevenR = _cfg.ContinuationBreakevenR,
            MaxHoldBars = _cfg.ContinuationMaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            AllowLong = _cfg.AllowLong,
            AllowShort = _cfg.AllowShort,
        });

        _exitMomentum = BuildExitConfig(
            _cfg.MomentumHardStopR, _cfg.MomentumBreakevenR, _cfg.MomentumTrailR,
            _cfg.MomentumGivebackPct, _cfg.MomentumTp1R, _cfg.MomentumTp2R,
            _cfg.MomentumMaxHoldBars,
            microTrail: true, emaTrail: false, reversalFlatten: true,
            peakGivebackKeep: 0.50, peakGivebackActivateR: 0.50,
            stagnationBars: 6, stagnationMinPeakR: 0.25);

        _exitReversion = BuildExitConfig(
            _cfg.ReversionHardStopR, _cfg.ReversionBreakevenR, _cfg.ReversionTrailR,
            _cfg.ReversionGivebackPct, _cfg.ReversionTp1R, _cfg.ReversionTp2R,
            _cfg.ReversionMaxHoldBars,
            microTrail: false, emaTrail: true, reversalFlatten: true,
            peakGivebackKeep: 0.60, peakGivebackActivateR: 0.30,
            stagnationBars: 4, stagnationMinPeakR: 0.15);

        _exitContinuation = BuildExitConfig(
            _cfg.ContinuationHardStopR, _cfg.ContinuationBreakevenR, _cfg.ContinuationTrailR,
            _cfg.ContinuationGivebackPct, _cfg.ContinuationTp1R, _cfg.ContinuationTp2R,
            _cfg.ContinuationMaxHoldBars,
            microTrail: true, emaTrail: false, reversalFlatten: true,
            peakGivebackKeep: 0.55, peakGivebackActivateR: 0.40,
            stagnationBars: 5, stagnationMinPeakR: 0.20);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  GenerateSignals â€” main entry point
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var diag = new V12Diagnostics(_cfg.DiagnosticsLabel,
            _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());

        // Step 1: Get raw signals from V3_1 core (VWAP / BB / Squeeze).
        var rawSignals = _signalCore.GenerateSignals(triggerBars, bars5m, bars15m, bars1h, bars1d);
        diag.RawSignals = rawSignals.Count;
        if (rawSignals.Count == 0)
        {
            diag.PrintSummary();
            return rawSignals;
        }

        // Step 2: Build opening-range context per day.
        _dayContexts = BuildDayContexts(triggerBars);

        // Step 3: Filter signals through confluence + regime system.
        var accepted = new List<BacktestSignal>(rawSignals.Count);
        var daySignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBarIndex = -10_000;

        foreach (var signal in rawSignals.OrderBy(s => s.BarIndex))
        {
            if (signal.BarIndex - lastAcceptedBarIndex < _cfg.CooldownBars)
            {
                diag.Reject("cooldown");
                continue;
            }

            int evalIndex = Math.Max(0, signal.BarIndex - (_cfg.UseNextBarOpenEntry ? 1 : 0));
            if (evalIndex >= triggerBars.Length)
            {
                diag.Reject("eval-idx-oob");
                continue;
            }

            var row = triggerBars[evalIndex];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0)
            {
                diag.Reject("atr-invalid");
                continue;
            }

            // Core quality filters.
            if (!PassesCoreFilters(row, out var coreReason))
            {
                diag.Reject($"core:{coreReason}");
                continue;
            }

            // Timing filters.
            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
            {
                diag.Reject("before-entry-start");
                continue;
            }

            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
            {
                diag.Reject("outside-entry-window");
                continue;
            }

            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            int countForDay = daySignalCounts.GetValueOrDefault(dayEt);
            if (countForDay >= _cfg.MaxSignalsPerDay)
            {
                diag.Reject("max-signals-per-day");
                continue;
            }

            // HTF bias â€” allow counter-trend if confluence is very high.
            string htfBias = ComputeHtfBias(row.Bar.Timestamp, bars15m, bars1h, bars1d);
            bool htfPassed = !_cfg.RequireHtfBias || PassesHtfBias(signal.Side, htfBias);
            bool needsCounterTrendOverride = _cfg.RequireHtfBias && !htfPassed && _cfg.AllowCounterTrendWithHighConfluence;

            if (!htfPassed && !needsCounterTrendOverride)
            {
                diag.Reject($"htf:{htfBias}");
                continue;
            }

            // MTF alignment.
            if (_cfg.RequireMtfAlign && !HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, signal.Side))
            {
                diag.Reject("mtf-align");
                continue;
            }

            // RSI zone.
            if (!PassesRsi(row, signal.Side))
            {
                diag.Reject("rsi");
                continue;
            }

            // Determine volatility regime and select sub-strategy.
            var regime = ClassifyVolatilityRegime(row);
            var subStrategy = SelectSubStrategy(row, signal.Side, regime, triggerBars, evalIndex, atr);
            if (subStrategy == SubStrategyType.None)
            {
                diag.Reject("no-sub-strategy");
                continue;
            }

            // Confluence scoring.
            int score = ComputeConfluenceScore(row, signal.Side, subStrategy, triggerBars, evalIndex, atr, bars5m);
            int required = subStrategy switch
            {
                SubStrategyType.Momentum => _cfg.MinConfluenceMomentum,
                SubStrategyType.Reversion => _cfg.MinConfluenceReversion,
                SubStrategyType.Continuation => _cfg.MinConfluenceContinuation,
                _ => 10
            };

            // Counter-trend trades need higher confluence to compensate.
            int adjustedRequired = needsCounterTrendOverride
                ? Math.Max(required, _cfg.CounterTrendMinConfluence)
                : required;

            if (score < adjustedRequired)
            {
                diag.Reject($"confluence:{subStrategy}:{score}<{adjustedRequired}");
                continue;
            }

            string label = $"V12_{subStrategy.ToString().ToUpperInvariant()}";
            accepted.Add(signal with { SubStrategy = label });

            daySignalCounts[dayEt] = countForDay + 1;
            lastAcceptedBarIndex = signal.BarIndex;

            if (signal.Side == TradeSide.Long) diag.AcceptedLongSignals++;
            else diag.AcceptedShortSignals++;
        }

        diag.AcceptedSignals = accepted.Count;
        diag.PrintSummary();
        return accepted;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  SimulateTrade â€” route to sub-strategy-specific exit config
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var exitCfg = signal.SubStrategy switch
        {
            _ when signal.SubStrategy.Contains("MOMENTUM", StringComparison.Ordinal) => _exitMomentum,
            _ when signal.SubStrategy.Contains("REVERSION", StringComparison.Ordinal) => _exitReversion,
            _ => _exitContinuation
        };

        return ExitEngine.SimulateTrade(signal, triggerBars, exitCfg);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Volatility Regime Classification
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private enum VolatilityRegime { Low, Normal, High }

    private VolatilityRegime ClassifyVolatilityRegime(EnrichedBar row)
    {
        double bw = row.BbBandwidth;
        if (double.IsNaN(bw))
            return VolatilityRegime.Normal;

        if (bw >= _cfg.BbBandwidthHighThreshold)
            return VolatilityRegime.High;

        if (bw <= _cfg.BbBandwidthLowThreshold)
            return VolatilityRegime.Low;

        return VolatilityRegime.Normal;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Sub-Strategy Selection
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private enum SubStrategyType { None, Momentum, Reversion, Continuation }

    private SubStrategyType SelectSubStrategy(
        EnrichedBar row, TradeSide side, VolatilityRegime regime,
        EnrichedBar[] bars, int evalIndex, double atr)
    {
        // MOMENTUM: high-vol regime + Donchian/EMA breakout + Supertrend aligned.
        if (regime == VolatilityRegime.High && IsMomentumSetup(row, side))
            return SubStrategyType.Momentum;

        // REVERSION: low-vol regime + BB extremes + stochastic/RSI agreement.
        if (regime == VolatilityRegime.Low && IsReversionSetup(row, side))
            return SubStrategyType.Reversion;

        // CONTINUATION: EMA pullback structure near EMA9/21 or VWAP.
        if (IsContinuationSetup(row, side, bars, evalIndex, atr))
            return SubStrategyType.Continuation;

        // Allow momentum in normal regime if setup is very strong.
        if (regime == VolatilityRegime.Normal && IsMomentumSetup(row, side)
            && !double.IsNaN(row.Rvol) && row.Rvol >= _cfg.MomentumRvolMin + 0.20)
            return SubStrategyType.Momentum;

        // Allow reversion in normal regime if stochastic signal is extreme.
        if (regime == VolatilityRegime.Normal && IsReversionSetup(row, side))
            return SubStrategyType.Reversion;

        // Allow momentum in any regime if setup matches.
        if (IsMomentumSetup(row, side))
            return SubStrategyType.Momentum;

        // Fallback: classify by regime â€” the V3_1 core already validated the
        // signal quality, so we trust it and apply regime-appropriate exits.
        if (regime == VolatilityRegime.High)
            return SubStrategyType.Momentum;
        if (regime == VolatilityRegime.Low)
            return SubStrategyType.Reversion;

        // Default fallback: continuation (most conservative exit config).
        return SubStrategyType.Continuation;
    }

    private bool IsMomentumSetup(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.MomentumRvolMin)
            return false;

        // Donchian breakout (if available).
        bool dcBreakout = !double.IsNaN(row.DcPct)
            && (side == TradeSide.Long ? row.DcPct >= _cfg.DonchianBreakoutPct
                                       : row.DcPct <= (1.0 - _cfg.DonchianBreakoutPct));

        // Supertrend direction (if available).
        bool stAligned = double.IsNaN(row.Supertrend)
            || (side == TradeSide.Long ? row.StDirection >= 1 : row.StDirection <= -1);

        // EMA momentum as alternative to Donchian.
        bool emaMomentum = !double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21)
            && (side == TradeSide.Long ? row.Ema9 > row.Ema21 && row.Bar.Close > row.Ema9
                                       : row.Ema9 < row.Ema21 && row.Bar.Close < row.Ema9);

        return (dcBreakout || emaMomentum) && stAligned;
    }

    private bool IsReversionSetup(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.BbPctB))
            return false;

        // BB extremes (required).
        bool bbExtreme = side == TradeSide.Long
            ? row.BbPctB <= _cfg.BbPctBLongEntry
            : row.BbPctB >= _cfg.BbPctBShortEntry;
        if (!bbExtreme) return false;

        // Stochastic confirmation (if available) or RSI as fallback.
        if (!double.IsNaN(row.StochK))
            return side == TradeSide.Long
                ? row.StochK <= _cfg.StochOversold
                : row.StochK >= _cfg.StochOverbought;

        // Fallback: use RSI for oversold/overbought.
        if (!double.IsNaN(row.Rsi14))
            return side == TradeSide.Long ? row.Rsi14 <= 38.0 : row.Rsi14 >= 62.0;

        return false;
    }

    private bool IsContinuationSetup(EnrichedBar row, TradeSide side, EnrichedBar[] bars, int evalIndex, double atr)
    {
        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Vwap))
            return false;

        if (side == TradeSide.Long)
        {
            if (row.Ema9 < row.Ema21)
                return false;

            double pullbackFromEma = (row.Ema9 - row.Bar.Low) / atr;
            double pullbackFromVwap = (row.Vwap - row.Bar.Low) / atr;
            bool nearEma = pullbackFromEma <= _cfg.EmaPullbackMaxAtr && pullbackFromEma >= -0.05;
            bool nearVwap = pullbackFromVwap <= _cfg.VwapPullbackMaxAtr && pullbackFromVwap >= -0.05;

            return nearEma || nearVwap;
        }
        else
        {
            if (row.Ema9 > row.Ema21)
                return false;

            double pullbackFromEma = (row.Bar.High - row.Ema9) / atr;
            double pullbackFromVwap = (row.Bar.High - row.Vwap) / atr;
            bool nearEma = pullbackFromEma <= _cfg.EmaPullbackMaxAtr && pullbackFromEma >= -0.05;
            bool nearVwap = pullbackFromVwap <= _cfg.VwapPullbackMaxAtr && pullbackFromVwap >= -0.05;

            return nearEma || nearVwap;
        }
    }

    private bool IsStochasticExtreme(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.StochK))
            return false;

        return side == TradeSide.Long
            ? row.StochK <= _cfg.StochOversold - 5.0
            : row.StochK >= _cfg.StochOverbought + 5.0;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Confluence Scoring â€” up to 12 factors
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private int ComputeConfluenceScore(
        EnrichedBar row, TradeSide side, SubStrategyType subStrategy,
        EnrichedBar[] bars, int evalIndex, double atr,
        EnrichedBar[]? bars5m)
    {
        int score = 0;
        bool isLong = side == TradeSide.Long;

        // 1. EMA stack alignment (EMA9 vs EMA21 vs EMA50).
        if (!double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21))
        {
            bool aligned = isLong ? row.Ema9 > row.Ema21 : row.Ema9 < row.Ema21;
            if (aligned) score++;

            if (!double.IsNaN(row.Ema50))
            {
                bool fullStack = isLong ? row.Ema21 > row.Ema50 : row.Ema21 < row.Ema50;
                if (fullStack) score++;
            }
        }

        // 2. VWAP position.
        if (!double.IsNaN(row.Vwap))
        {
            if ((isLong && row.Bar.Close >= row.Vwap) || (!isLong && row.Bar.Close <= row.Vwap))
                score++;
        }

        // 3. Supertrend direction.
        if (!double.IsNaN(row.Supertrend))
        {
            if ((isLong && row.StDirection >= 1) || (!isLong && row.StDirection <= -1))
                score++;
        }

        // 4. Order Flow Imbalance â€” OfiCum direction.
        if (!double.IsNaN(row.OfiCum))
        {
            if ((isLong && row.OfiCum > 0) || (!isLong && row.OfiCum < 0))
                score++;
        }

        // 5. Stochastic position (not overbought for longs, not oversold for shorts).
        if (!double.IsNaN(row.StochK))
        {
            bool stochOk = isLong
                ? row.StochK < _cfg.StochOverbought
                : row.StochK > _cfg.StochOversold;
            if (stochOk) score++;
        }

        // 6. Money Flow Index.
        if (!double.IsNaN(row.Mfi14))
        {
            if ((isLong && row.Mfi14 >= 45.0) || (!isLong && row.Mfi14 <= 55.0))
                score++;
        }

        // 7. Williams %R â€” not at exhaustion against trade direction.
        if (!double.IsNaN(row.WillR14))
        {
            bool willOk = isLong ? row.WillR14 > -85.0 : row.WillR14 < -15.0;
            if (willOk) score++;
        }

        // 8. DPO (Detrended Price Oscillator) direction.
        if (!double.IsNaN(row.Dpo20))
        {
            if ((isLong && row.Dpo20 > 0) || (!isLong && row.Dpo20 < 0))
                score++;
        }

        // 9. ADX trendiness.
        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.AdxMin && row.Adx <= _cfg.AdxMax)
        {
            score++;
        }

        // 10. Volume confirmation (RVOL + VolAccel).
        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin
            && !double.IsNaN(row.VolAccel) && row.VolAccel >= _cfg.MinVolAccel)
        {
            score++;
        }

        // 11. Directional Index alignment (DI+/DI-).
        if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi))
        {
            if ((isLong && row.PlusDi > row.MinusDi) || (!isLong && row.MinusDi > row.PlusDi))
                score++;
        }

        // 12. SMA200 trend context â€” price above for longs, below for shorts.
        if (!double.IsNaN(row.Sma200))
        {
            if ((isLong && row.Bar.Close > row.Sma200) || (!isLong && row.Bar.Close < row.Sma200))
                score++;
        }

        return score;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Core Filters (price, liquidity, spread, volatility, indicators)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private bool PassesCoreFilters(EnrichedBar row, out string reason)
    {
        if (row.Bar.Close < _cfg.MinPrice || row.Bar.Close > _cfg.MaxPrice)
        { reason = "price"; return false; }

        if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin)
        { reason = "liquidity"; return false; }

        if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax)
        { reason = "spread"; return false; }

        if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin)
        { reason = "rvol"; return false; }

        if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.MinVolAccel)
        { reason = "vol-accel"; return false; }

        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21)
            || double.IsNaN(row.Vwap) || double.IsNaN(row.Rsi14))
        { reason = "indicators"; return false; }

        reason = string.Empty;
        return true;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  RSI Filter
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private bool PassesRsi(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.Rsi14))
            return false;

        return side == TradeSide.Long
            ? row.Rsi14 >= _cfg.RsiLongMin && row.Rsi14 <= _cfg.RsiLongMax
            : row.Rsi14 >= _cfg.RsiShortMin && row.Rsi14 <= _cfg.RsiShortMax;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  HTF Bias (multi-timeframe trend direction)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private bool PassesHtfBias(TradeSide side, string htfBias)
    {
        if (side == TradeSide.Long)
            return htfBias is "BULL" or "STRONG_BULL" or "NEUTRAL";
        else
            return htfBias is "BEAR" or "STRONG_BEAR" or "NEUTRAL";
    }

    private static string ComputeHtfBias(DateTime ts, params EnrichedBar[]?[] frames)
    {
        int scoreSum = 0;
        int scoreCount = 0;

        foreach (var bars in frames)
        {
            if (bars is not { Length: >= 2 })
                continue;

            int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
            if (idx < 1)
                continue;

            var row = bars[idx];
            var prev = bars[idx - 1];
            if (double.IsNaN(row.Ema21) || double.IsNaN(row.Ema50) || double.IsNaN(row.MacdHist))
                continue;

            int score = 0;
            score += row.Ema21 > row.Ema50 ? 1 : -1;
            score += row.Bar.Close > row.Ema21 ? 1 : -1;
            score += row.MacdHist >= 0 ? 1 : -1;
            score += row.Ema21 >= prev.Ema21 ? 1 : -1;

            if (!double.IsNaN(row.Adx) && row.Adx >= 20
                && !double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi))
            {
                score += row.PlusDi >= row.MinusDi ? 1 : -1;
            }

            // V12 addition: incorporate Supertrend from HTF when available.
            if (!double.IsNaN(row.Supertrend))
            {
                score += row.StDirection >= 1 ? 1 : -1;
            }

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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  MTF Alignment (5m + 15m frames must agree)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private static bool HasMtfAlignment(DateTime ts, EnrichedBar[]? bars5m, EnrichedBar[]? bars15m, TradeSide side)
    {
        return FrameAligned(bars5m, ts, side) && FrameAligned(bars15m, ts, side);

        static bool FrameAligned(EnrichedBar[]? bars, DateTime ts, TradeSide side)
        {
            if (bars is not { Length: > 0 })
                return true;

            int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
            if (idx < 0)
                return true;

            var row = bars[idx];
            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.MacdHist))
                return false;

            return side == TradeSide.Long
                ? row.Ema9 > row.Ema21 && row.Bar.Close >= row.Ema21 && row.MacdHist >= 0
                : row.Ema9 < row.Ema21 && row.Bar.Close <= row.Ema21 && row.MacdHist <= 0;
        }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Day Context (opening range)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private Dictionary<DateOnly, DayContext> BuildDayContexts(EnrichedBar[] triggerBars)
    {
        var result = new Dictionary<DateOnly, DayContext>();
        foreach (var day in BacktestHelpers.GroupByTradingDayEt(triggerBars))
        {
            var (orHigh, orLow, orEndIdx) = BacktestHelpers.ComputeOpeningRangeEt(
                day.StartIdx, day.EndIdx, triggerBars,
                _cfg.MarketOpenMinute, _cfg.OpeningRangeMinutes);

            if (orEndIdx >= 0 && !double.IsNaN(orHigh) && !double.IsNaN(orLow))
            {
                result[day.DateEt] = new DayContext(day.StartIdx, day.EndIdx, orHigh, orLow, orEndIdx);
            }
        }

        return result;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Exit Config Builder
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private ExitEngine.ExitConfig BuildExitConfig(
        double hardStopR, double breakevenR, double trailR,
        double givebackPct, double tp1R, double tp2R, int maxHoldBars,
        bool microTrail, bool emaTrail, bool reversalFlatten,
        double peakGivebackKeep, double peakGivebackActivateR,
        int stagnationBars, double stagnationMinPeakR)
    {
        return new ExitEngine.ExitConfig
        {
            HardStopR = hardStopR,
            BreakevenR = breakevenR,
            TrailR = trailR,
            GivebackPct = givebackPct,
            GivebackMinPeakR = 0.20,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = tp1R,
            Tp2R = tp2R,
            MaxHoldBars = maxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
            ReversalFlatten = reversalFlatten,
            MicroTrail = microTrail,
            MicroTrailCents = 1.8,
            MicroTrailActivateCents = 3.0,
            EmaTrail = emaTrail,
            EmaTrailBufferAtr = 0.12,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = peakGivebackKeep,
            PeakGivebackActivateR = peakGivebackActivateR,
            FlattenOnStagnation = true,
            StagnationBars = stagnationBars,
            StagnationMinPeakR = stagnationMinPeakR,
            StagnationMaxAdverseR = -0.08,
        };
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Diagnostics
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V12");

    private sealed class V12Diagnostics
    {
        private readonly string _label;
        private readonly bool _enabled;
        private readonly Dictionary<string, int> _rejections = new(StringComparer.OrdinalIgnoreCase);

        public V12Diagnostics(string label, bool enabled) { _label = label; _enabled = enabled; }

        public int RawSignals { get; set; }
        public int AcceptedSignals { get; set; }
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

            Console.WriteLine($"[V12-DIAG:{_label}] raw={RawSignals} accepted={AcceptedSignals} long={AcceptedLongSignals} short={AcceptedShortSignals}");
            Console.WriteLine($"[V12-DIAG:{_label}] rejects: {string.Join(", ", topReasons)}");
        }
    }

    private sealed record DayContext(int StartIdx, int EndIdx, double OrHigh, double OrLow, int OrEndIdx);
}

