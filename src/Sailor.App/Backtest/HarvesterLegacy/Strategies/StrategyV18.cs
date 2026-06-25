using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

public sealed class V18Config
{
    public double RiskPerTradeDollars { get; init; } = 20.0;
    public double AccountSize { get; init; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.18;
    public int MaxShares { get; init; } = 6_500;
    public double MinRiskPerShare { get; init; } = 0.05;

    public bool AllowLong { get; init; } = true;
    public bool AllowShort { get; init; } = true;
    public bool UseNextBarOpenEntry { get; init; } = true;

    public int CooldownBars { get; init; } = 6;
    public int MaxSignalsPerDay { get; init; } = 4;
    public int ShortMaxSignalsPerDay { get; init; } = 1;

    public double MinPrice { get; init; } = 0.3;
    public double MaxPrice { get; init; } = 700.0;
    public double RvolMin { get; init; } = 0.70;
    public double StrongRvolMin { get; init; } = 1.00;
    public double ShortStrongRvolMin { get; init; } = 1.15;
    public double SpreadZMax { get; init; } = 3.5;
    public double L2LiquidityMin { get; init; } = 0.0;

    public int MarketOpenMinute { get; init; } = 570;
    public int SkipFirstNMinutes { get; init; } = 10;
    public int LastEntryMinuteBeforeClose { get; init; } = 45;
    public int ShortEarliestMinuteEt { get; init; } = 620;
    public (int Start, int End)[] EntryWindows { get; init; } = [(590, 930)];

    public int PullbackLookbackBars { get; init; } = 12;
    public int BreakoutLookbackBars { get; init; } = 18;
    public double PullbackToEma21Atr { get; init; } = 0.45;
    public double PullbackToVwapAtr { get; init; } = 0.35;
    public double BreakoutBufferAtr { get; init; } = 0.08;
    public double MaxBreakoutExtensionAtr { get; init; } = 0.60;

    public double AdxMin { get; init; } = 12.0;
    public double AdxMax { get; init; } = 60.0;
    public double LongRsiMin { get; init; } = 48.0;
    public double LongRsiMax { get; init; } = 66.0;
    public double ShortRsiMin { get; init; } = 32.0;
    public double ShortRsiMax { get; init; } = 56.0;
    public double LongDcPctMin { get; init; } = 0.55;
    public double ShortDcPctMax { get; init; } = 0.45;

    public double HardStopR { get; init; } = 1.00;
    public double BreakevenR { get; init; } = 0.70;
    public double TrailR { get; init; } = 0.15;
    public double GivebackPct { get; init; } = 0.60;
    public double GivebackUsdCap { get; init; } = 24.0;
    public double Tp1R { get; init; } = 0.73;
    public double Tp2R { get; init; } = 2.04;
    public int MaxHoldBars { get; init; } = 45;
    public double SlippageCents { get; init; } = 0.8;
    public double CommissionPerShare { get; init; } = 0.005;

    public double StopAtrMultiplier { get; init; } = 1.15;
    public double MinStopCents { get; init; } = 5.0;

    public bool UseFastReEntryMode { get; init; } = true;
    public int FastReEntryBaselineSlopeBars { get; init; } = 30;
    public double FastReEntryBaselineMinSlopeAtr { get; init; } = 0.12;
    public double FastReEntryMinBaselineDistanceAtr { get; init; } = 0.10;
    public bool FastReEntryRequireSupertrendSupport { get; init; } = false;
    public double FastReEntrySupertrendToleranceAtr { get; init; } = 0.05;
    public bool FastReEntryRequireDirectionalStrength { get; init; } = true;
    public double FastReEntryMinPlusDiEdge { get; init; } = 2.0;
    public bool FastReEntryRequireMfiBand { get; init; } = true;
    public double FastReEntryMinMfi14 { get; init; } = 47.0;
    public double FastReEntryMaxMfi14 { get; init; } = 84.0;
    public bool FastReEntryUseKeltnerExtensionGuard { get; init; } = true;
    public double FastReEntryMaxKeltnerExtensionAtr { get; init; } = 0.40;
    public int FastReEntryPullbackBars { get; init; } = 16;
    public int FastReEntryReclaimLookbackBars { get; init; } = 8;
    public double FastReEntryPullbackToleranceAtr { get; init; } = 0.45;
    public double FastReEntryMinBreakoutAtr { get; init; } = 0.03;
    public double FastReEntryStopBufferAtr { get; init; } = 0.15;
    public int FastReEntryMaxHoldBars { get; init; } = 220;
    public bool UseFastReEntryShortMode { get; init; } = false;
    public bool FastReEntryShortRequireSupertrendResistance { get; init; } = true;
    public double FastReEntryShortSupertrendToleranceAtr { get; init; } = 0.05;
    public bool FastReEntryShortRequireDirectionalStrength { get; init; } = true;
    public double FastReEntryMinMinusDiEdge { get; init; } = 3.0;
    public bool FastReEntryShortRequireMfiBand { get; init; } = true;
    public double FastReEntryShortMinMfi14 { get; init; } = 18.0;
    public double FastReEntryShortMaxMfi14 { get; init; } = 58.0;
    public bool FastReEntryShortUseKeltnerExtensionGuard { get; init; } = true;
    public double FastReEntryShortMaxKeltnerExtensionAtr { get; init; } = 0.35;
    public double FastReEntryShortMinBaselineDistanceAtr { get; init; } = 0.06;
    public double FastReEntryShortMinBreakoutAtr { get; init; } = 0.05;

    public bool UseFastReEntryShortExitProfile { get; init; } = true;
    public double FastReEntryShortBreakevenR { get; init; } = 0.45;
    public double FastReEntryShortTrailR { get; init; } = 0.15;
    public double FastReEntryShortGivebackPct { get; init; } = 0.60;
    public double FastReEntryShortTp1R { get; init; } = 0.73;
    public double FastReEntryShortTp2R { get; init; } = 2.04;
    public int FastReEntryShortMaxHoldBars { get; init; } = 24;
    public double FastReEntryShortPeakGivebackKeepFraction { get; init; } = 0.40;
    public double FastReEntryShortPeakGivebackActivateR { get; init; } = 0.25;
    public int FastReEntryShortStagnationBars { get; init; } = 6;
    public double FastReEntryShortStagnationMinPeakR { get; init; } = 0.15;
    public double FastReEntryShortStagnationMaxAdverseR { get; init; } = -0.05;

    public bool EnableDiagnostics { get; init; } = false;
    public string DiagnosticsLabel { get; init; } = "V18";
}

public sealed class StrategyV18 : BacktestStrategyBase
{
    private readonly V18Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;
    private readonly ExitEngine.ExitConfig _fastShortExitCfg;
    private readonly V18Diagnostics _diag;

    public StrategyV18(V18Config? cfg = null)
    {
        _cfg = cfg ?? new V18Config();
        if (!_cfg.AllowLong || !_cfg.AllowShort)
            throw new InvalidOperationException("V18: single-direction configs are not allowed. Both AllowLong and AllowShort must be true.");
        _diag = new V18Diagnostics(
            _cfg.DiagnosticsLabel,
            _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.20,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
            ReversalFlatten = true,
            MicroTrail = true,
            MicroTrailCents = 1.0,
            MicroTrailActivateCents = 2.0,
            EmaTrail = true,
            EmaTrailBufferAtr = 0.18,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = 0.50,
            PeakGivebackActivateR = 0.35,
            FlattenOnStagnation = true,
            StagnationBars = 9,
            StagnationMinPeakR = 0.15,
            StagnationMaxAdverseR = -0.10,
        };
        _fastShortExitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.FastReEntryShortBreakevenR,
            TrailR = _cfg.FastReEntryShortTrailR,
            GivebackPct = _cfg.FastReEntryShortGivebackPct,
            GivebackMinPeakR = 0.15,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = _cfg.FastReEntryShortTp1R,
            Tp2R = _cfg.FastReEntryShortTp2R,
            MaxHoldBars = _cfg.FastReEntryShortMaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
            ReversalFlatten = true,
            MicroTrail = true,
            MicroTrailCents = 1.0,
            MicroTrailActivateCents = 2.0,
            EmaTrail = true,
            EmaTrailBufferAtr = 0.15,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = _cfg.FastReEntryShortPeakGivebackKeepFraction,
            PeakGivebackActivateR = _cfg.FastReEntryShortPeakGivebackActivateR,
            FlattenOnStagnation = true,
            StagnationBars = _cfg.FastReEntryShortStagnationBars,
            StagnationMinPeakR = _cfg.FastReEntryShortStagnationMinPeakR,
            StagnationMaxAdverseR = _cfg.FastReEntryShortStagnationMaxAdverseR,
        };
    }

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var signals = new List<BacktestSignal>();
        var daySignalCounts = new Dictionary<DateOnly, int>();
        var dayShortSignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBar = -10_000;

        int startIndex = Math.Max(_cfg.BreakoutLookbackBars + 2, _cfg.PullbackLookbackBars + 2);
        for (int i = startIndex; i < triggerBars.Length - 1; i++)
        {
            var row = triggerBars[i];
            _diag.RawScanned++;

            if (!HasCoreData(row))
            {
                _diag.Reject("core-data-missing");
                continue;
            }

            double price = row.Bar.Close;
            double atr = row.Atr14;
            if (price < _cfg.MinPrice || price > _cfg.MaxPrice)
            {
                _diag.Reject("price");
                continue;
            }

            if (row.Rvol < _cfg.RvolMin)
            {
                _diag.Reject("rvol");
                continue;
            }

            if (!double.IsNaN(row.SpreadZ) && row.SpreadZ > _cfg.SpreadZMax)
            {
                _diag.Reject("spread");
                continue;
            }

            if (!double.IsNaN(row.L2Liquidity) && row.L2Liquidity < _cfg.L2LiquidityMin)
            {
                _diag.Reject("l2-liquidity");
                continue;
            }

            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
            {
                _diag.Reject("before-entry-start");
                continue;
            }

            if (minuteEt > 960 - _cfg.LastEntryMinuteBeforeClose)
            {
                _diag.Reject("too-close-to-close");
                continue;
            }

            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
            {
                _diag.Reject("outside-entry-window");
                continue;
            }

            if (i - lastAcceptedBar < _cfg.CooldownBars)
            {
                _diag.Reject("cooldown");
                continue;
            }

            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            if (daySignalCounts.GetValueOrDefault(dayEt) >= _cfg.MaxSignalsPerDay)
            {
                _diag.Reject("max-signals-per-day");
                continue;
            }

            if (row.Adx < _cfg.AdxMin || row.Adx > _cfg.AdxMax)
            {
                _diag.Reject("adx");
                continue;
            }

            if (IsMarketRegimeBlocked(row))
            {
                _diag.Reject("market-regime-blocked");
                continue;
            }

            BacktestSignal? signal = null;
            if (_cfg.UseFastReEntryMode)
            {
                if (_cfg.AllowLong)
                {
                    signal = TryFastReEntryLong(triggerBars, i, atr);
                }

                if (signal is null
                    && _cfg.AllowShort
                    && _cfg.UseFastReEntryShortMode
                    && minuteEt >= _cfg.ShortEarliestMinuteEt
                    && dayShortSignalCounts.GetValueOrDefault(dayEt) < _cfg.ShortMaxSignalsPerDay)
                {
                    signal = TryFastReEntryShort(triggerBars, i, atr);
                }
            }
            else if (_cfg.AllowLong)
            {
                signal = TryTrendPullbackLong(triggerBars, i, atr) ?? TryRangeBreakoutLong(triggerBars, i, atr);
            }

            if (!_cfg.UseFastReEntryMode && signal is null && _cfg.AllowShort)
            {
                signal = TryTrendPullbackShort(triggerBars, i, atr) ?? TryRangeBreakoutShort(triggerBars, i, atr);
            }

            if (signal is null)
            {
                _diag.Reject("no-setup");
                continue;
            }

            signals.Add(signal);
            _diag.Accepted++;
            if (signal.Side == TradeSide.Long) _diag.AcceptedLong++;
            if (signal.Side == TradeSide.Short)
            {
                _diag.AcceptedShort++;
                dayShortSignalCounts[dayEt] = dayShortSignalCounts.GetValueOrDefault(dayEt) + 1;
            }
            lastAcceptedBar = signal.BarIndex;
            daySignalCounts[dayEt] = daySignalCounts.GetValueOrDefault(dayEt) + 1;
        }

        _diag.PrintSummary();
        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var result = _cfg.UseFastReEntryMode
            ? (signal.Side == TradeSide.Short && _cfg.UseFastReEntryShortExitProfile
                ? ExitEngine.SimulateTrade(signal, triggerBars, ApplySelfLearningExitOverrides(_fastShortExitCfg, signal.SubStrategy))
                : SimulateFastReEntryTrade(signal, triggerBars))
            : ExitEngine.SimulateTrade(signal, triggerBars, ApplySelfLearningExitOverrides(_exitCfg, signal.SubStrategy));
        if (result != null)
        {
            _diag.ObserveTrade(result);
        }

        return result;
    }

    private BacktestSignal? TryFastReEntryLong(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];
        if (double.IsNaN(row.Sma200) || i < _cfg.FastReEntryBaselineSlopeBars)
        {
            return null;
        }

        var baselineRow = bars[i - _cfg.FastReEntryBaselineSlopeBars];
        if (double.IsNaN(baselineRow.Sma200))
        {
            return null;
        }

        bool baselineUp = row.Sma200 >= baselineRow.Sma200 + (_cfg.FastReEntryBaselineMinSlopeAtr * atr);
        bool aboveBaseline = row.Bar.Close >= row.Sma200 + (_cfg.FastReEntryMinBaselineDistanceAtr * atr)
            && row.Ema9 > row.Ema21
            && row.Ema21 > row.Sma200;
        bool supertrendAligned = !_cfg.FastReEntryRequireSupertrendSupport
            || (row.StDirection == 1
                && !double.IsNaN(row.Supertrend)
                && row.Bar.Close >= row.Supertrend - (_cfg.FastReEntrySupertrendToleranceAtr * atr));
        bool directionalStrength = !_cfg.FastReEntryRequireDirectionalStrength
            || (!double.IsNaN(row.PlusDi)
                && !double.IsNaN(row.MinusDi)
                && row.PlusDi >= row.MinusDi + _cfg.FastReEntryMinPlusDiEdge);
        bool moneyFlowHealthy = !_cfg.FastReEntryRequireMfiBand
            || (!double.IsNaN(row.Mfi14)
                && row.Mfi14 >= _cfg.FastReEntryMinMfi14
                && row.Mfi14 <= _cfg.FastReEntryMaxMfi14);
        bool keltnerNotExtended = !_cfg.FastReEntryUseKeltnerExtensionGuard
            || double.IsNaN(row.KcUpper)
            || row.Bar.Close <= row.KcUpper + (_cfg.FastReEntryMaxKeltnerExtensionAtr * atr);
        bool recentPullback = HasRecentPullbackLong(bars, i, atr);
        double reclaimHigh = HighestHigh(bars, i - _cfg.FastReEntryReclaimLookbackBars, i - 1);
        bool reclaimBreakout = row.Bar.Close >= reclaimHigh + (_cfg.FastReEntryMinBreakoutAtr * atr)
            && row.Bar.Close > row.Bar.Open
            && row.Bar.Close >= row.Bar.Low + ((row.Bar.High - row.Bar.Low) * 0.55)
            && row.Bar.Close > row.Ema9
            && row.Bar.Close > row.Vwap
            && row.MacdHist >= bars[i - 1].MacdHist
            && row.DcPct >= _cfg.LongDcPctMin
            && row.Rvol >= _cfg.StrongRvolMin;

        if (!baselineUp
            || !aboveBaseline
            || !supertrendAligned
            || !directionalStrength
            || !moneyFlowHealthy
            || !keltnerNotExtended
            || !recentPullback
            || !reclaimBreakout)
        {
            return null;
        }

        double anchor = LowestLow(bars, i - _cfg.FastReEntryPullbackBars, i);
        return MakeSignal(bars, i, TradeSide.Long, anchor, "V18_FAST_REENTRY");
    }

    private BacktestSignal? TryFastReEntryShort(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];
        if (double.IsNaN(row.Sma200) || i < _cfg.FastReEntryBaselineSlopeBars)
        {
            return null;
        }

        var baselineRow = bars[i - _cfg.FastReEntryBaselineSlopeBars];
        if (double.IsNaN(baselineRow.Sma200))
        {
            return null;
        }

        bool baselineDown = row.Sma200 <= baselineRow.Sma200 - (_cfg.FastReEntryBaselineMinSlopeAtr * atr);
        bool belowBaseline = row.Bar.Close <= row.Sma200 - (_cfg.FastReEntryShortMinBaselineDistanceAtr * atr)
            && row.Ema9 < row.Ema21
            && row.Ema21 < row.Sma200;
        bool supertrendAligned = !_cfg.FastReEntryShortRequireSupertrendResistance
            || (row.StDirection == -1
                && !double.IsNaN(row.Supertrend)
                && row.Bar.Close <= row.Supertrend + (_cfg.FastReEntryShortSupertrendToleranceAtr * atr));
        bool directionalStrength = !_cfg.FastReEntryShortRequireDirectionalStrength
            || (!double.IsNaN(row.PlusDi)
                && !double.IsNaN(row.MinusDi)
                && row.MinusDi >= row.PlusDi + _cfg.FastReEntryMinMinusDiEdge);
        bool moneyFlowWeak = !_cfg.FastReEntryShortRequireMfiBand
            || (!double.IsNaN(row.Mfi14)
                && row.Mfi14 >= _cfg.FastReEntryShortMinMfi14
                && row.Mfi14 <= _cfg.FastReEntryShortMaxMfi14);
        bool keltnerNotOverextended = !_cfg.FastReEntryShortUseKeltnerExtensionGuard
            || double.IsNaN(row.KcLower)
            || row.Bar.Close >= row.KcLower - (_cfg.FastReEntryShortMaxKeltnerExtensionAtr * atr);
        bool recentBounce = HasRecentBounceShort(bars, i, atr);
        double reclaimLow = LowestLow(bars, i - _cfg.FastReEntryReclaimLookbackBars, i - 1);
        bool reclaimBreakdown = row.Bar.Close <= reclaimLow - (_cfg.FastReEntryShortMinBreakoutAtr * atr)
            && row.Bar.Close < row.Bar.Open
            && row.Bar.Close <= row.Bar.High - ((row.Bar.High - row.Bar.Low) * 0.55)
            && row.Bar.Close < row.Ema9
            && row.Bar.Close < row.Vwap
            && row.MacdHist <= bars[i - 1].MacdHist
            && row.DcPct <= _cfg.ShortDcPctMax
            && row.Rsi14 >= _cfg.ShortRsiMin
            && row.Rsi14 <= _cfg.ShortRsiMax
            && row.Rvol >= _cfg.ShortStrongRvolMin;

        if (!baselineDown
            || !belowBaseline
            || !supertrendAligned
            || !directionalStrength
            || !moneyFlowWeak
            || !keltnerNotOverextended
            || !recentBounce
            || !reclaimBreakdown)
        {
            return null;
        }

        double anchor = HighestHigh(bars, i - _cfg.FastReEntryPullbackBars, i);
        return MakeSignal(bars, i, TradeSide.Short, anchor, "V18_FAST_REENTRY");
    }

    private BacktestSignal? TryTrendPullbackLong(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];
        bool trendAligned = row.Ema9 >= row.Ema21 && row.Ema21 >= row.Ema50 && row.Bar.Close >= row.Ema21;
        bool momentumAligned = row.Rsi14 >= _cfg.LongRsiMin
            && row.Rsi14 <= _cfg.LongRsiMax
            && row.MacdHist >= bars[i - 1].MacdHist
            && row.DcPct >= _cfg.LongDcPctMin;
        bool pullbackReady = Math.Abs(row.Bar.Low - row.Ema21) <= (_cfg.PullbackToEma21Atr * atr)
            || Math.Abs(row.Bar.Low - row.Vwap) <= (_cfg.PullbackToVwapAtr * atr);
        bool reclaim = row.Bar.Close >= row.Ema9 && row.Bar.Close >= row.Vwap && row.Bar.Close > row.Bar.Open;

        if (!trendAligned || !momentumAligned || !pullbackReady || !reclaim)
        {
            return null;
        }

        double anchor = LowestLow(bars, i - _cfg.PullbackLookbackBars, i);
        return MakeSignal(bars, i, TradeSide.Long, anchor, "V18_SILVER_PULLBACK");
    }

    private BacktestSignal? TryTrendPullbackShort(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];
        bool trendAligned = row.Ema9 <= row.Ema21 && row.Ema21 <= row.Ema50 && row.Bar.Close <= row.Ema21;
        bool momentumAligned = row.Rsi14 >= _cfg.ShortRsiMin
            && row.Rsi14 <= _cfg.ShortRsiMax
            && row.MacdHist <= bars[i - 1].MacdHist
            && row.DcPct <= _cfg.ShortDcPctMax;
        bool pullbackReady = Math.Abs(row.Bar.High - row.Ema21) <= (_cfg.PullbackToEma21Atr * atr)
            || Math.Abs(row.Bar.High - row.Vwap) <= (_cfg.PullbackToVwapAtr * atr);
        bool reject = row.Bar.Close <= row.Ema9 && row.Bar.Close <= row.Vwap && row.Bar.Close < row.Bar.Open;

        if (!trendAligned || !momentumAligned || !pullbackReady || !reject)
        {
            return null;
        }

        double anchor = HighestHigh(bars, i - _cfg.PullbackLookbackBars, i);
        return MakeSignal(bars, i, TradeSide.Short, anchor, "V18_SILVER_PULLBACK");
    }

    private BacktestSignal? TryRangeBreakoutLong(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];
        double priorHigh = HighestHigh(bars, i - _cfg.BreakoutLookbackBars, i - 1);
        bool breakout = row.Bar.Close >= priorHigh - (_cfg.BreakoutBufferAtr * atr)
            && row.Bar.Close <= priorHigh + (_cfg.MaxBreakoutExtensionAtr * atr);
        bool confirm = row.Rvol >= _cfg.StrongRvolMin
            && row.Bar.Close > row.Vwap
            && row.MacdHist > 0
            && row.Ema21 >= row.Ema50;

        if (!breakout || !confirm)
        {
            return null;
        }

        double anchor = LowestLow(bars, i - _cfg.PullbackLookbackBars, i);
        return MakeSignal(bars, i, TradeSide.Long, anchor, "V18_SILVER_BREAKOUT");
    }

    private BacktestSignal? TryRangeBreakoutShort(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];
        double priorLow = LowestLow(bars, i - _cfg.BreakoutLookbackBars, i - 1);
        bool breakdown = row.Bar.Close <= priorLow + (_cfg.BreakoutBufferAtr * atr)
            && row.Bar.Close >= priorLow - (_cfg.MaxBreakoutExtensionAtr * atr);
        bool confirm = row.Rvol >= _cfg.StrongRvolMin
            && row.Bar.Close < row.Vwap
            && row.MacdHist < 0
            && row.Ema21 <= row.Ema50;

        if (!breakdown || !confirm)
        {
            return null;
        }

        double anchor = HighestHigh(bars, i - _cfg.PullbackLookbackBars, i);
        return MakeSignal(bars, i, TradeSide.Short, anchor, "V18_SILVER_BREAKOUT");
    }

    private BacktestSignal? MakeSignal(EnrichedBar[] bars, int i, TradeSide side, double anchor, string subStrategy)
    {
        if (IsSelfLearningBlocked(subStrategy)) return null;

        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTime = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry && i + 1 < bars.Length)
        {
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTime = bars[entryIndex].Bar.Timestamp;
        }

        double atr = bars[i].Atr14;
        double stopDist = Math.Max(atr * _cfg.StopAtrMultiplier, _cfg.MinStopCents / 100.0);
        stopDist = ApplySelfLearningStopMultiplier(stopDist);
        double stopPrice = side == TradeSide.Long
            ? Math.Min(entryPrice - _cfg.MinRiskPerShare, anchor - (0.15 * atr))
            : Math.Max(entryPrice + _cfg.MinRiskPerShare, anchor + (0.15 * atr));

        if (Math.Abs(entryPrice - stopPrice) < stopDist)
        {
            stopPrice = side == TradeSide.Long ? entryPrice - stopDist : entryPrice + stopDist;
        }

        double riskPerShare = Math.Max(Math.Abs(entryPrice - stopPrice), _cfg.MinRiskPerShare);
        int positionSize = BacktestHelpers.ComputePositionSize(
            entryPrice,
            riskPerShare,
            _cfg.RiskPerTradeDollars,
            _cfg.AccountSize,
            _cfg.MaxPositionNotionalPctOfAccount,
            _cfg.MaxShares);
        positionSize = ApplySelfLearningPositionSize(positionSize, subStrategy);

        if (positionSize <= 0)
        {
            return null;
        }

        return new BacktestSignal(
            BarIndex: entryIndex,
            Timestamp: entryTime,
            Side: side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: positionSize,
            AtrValue: atr,
            HtfTrend: HtfBias.Neutral,
            MtfMomentum: "Silver",
            SubStrategy: subStrategy);
    }

    private BacktestTradeResult SimulateFastReEntryTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var side = signal.Side;
        double entryPrice = signal.EntryPrice
            + (side == TradeSide.Long ? _cfg.SlippageCents / 100.0 : -_cfg.SlippageCents / 100.0);
        double stopPrice = signal.StopPrice;
        double riskPerShare = Math.Max(signal.RiskPerShare, _cfg.MinRiskPerShare);
        int positionSize = signal.PositionSize;

        int lastBarExclusive = Math.Min(signal.BarIndex + _cfg.FastReEntryMaxHoldBars + 1, triggerBars.Length);
        int exitBar = Math.Min(lastBarExclusive - 1, triggerBars.Length - 1);
        double exitPrice = triggerBars[exitBar].Bar.Close;
        DateTime exitTime = triggerBars[exitBar].Bar.Timestamp;
        ExitReason exitReason = ExitReason.TimeStop;
        bool exited = false;

        double peakPrice = entryPrice;
        double troughPrice = entryPrice;
        double trailingProtection = stopPrice;
        int consecutiveOppositeBars = 0;

        for (int j = signal.BarIndex + 1; j < lastBarExclusive; j++)
        {
            var row = triggerBars[j];
            var bar = row.Bar;
            if (side == TradeSide.Long)
            {
                peakPrice = Math.Max(peakPrice, bar.High);
            }
            else
            {
                troughPrice = Math.Min(troughPrice, bar.Low);
            }

            double atr = double.IsNaN(row.Atr14) || row.Atr14 <= 0 ? signal.AtrValue : row.Atr14;
            double baselineProtection = side == TradeSide.Long
                ? (!double.IsNaN(row.Sma200) ? row.Sma200 : double.MinValue)
                : (!double.IsNaN(row.Sma200) ? row.Sma200 : double.MaxValue);
            double stairProtection = side == TradeSide.Long
                ? (!double.IsNaN(row.DcLower) ? row.DcLower - (_cfg.FastReEntryStopBufferAtr * atr) : double.MinValue)
                : (!double.IsNaN(row.DcUpper) ? row.DcUpper + (_cfg.FastReEntryStopBufferAtr * atr) : double.MaxValue);

            if (side == TradeSide.Long)
            {
                trailingProtection = Math.Max(trailingProtection, Math.Max(baselineProtection, stairProtection));
                if (bar.Low <= trailingProtection)
                {
                    exitPrice = Math.Min(bar.Open, trailingProtection);
                    exitBar = j;
                    exitTime = bar.Timestamp;
                    exitReason = ExitReason.EmaTrail;
                    exited = true;
                    break;
                }

                if (ExitEngine.ShouldFlattenOnTwoOppositeBars(
                        side,
                        bar.Open,
                        bar.Close,
                        entryPrice,
                        trailingProtection,
                        ref consecutiveOppositeBars))
                {
                    exitPrice = bar.Close;
                    exitBar = j;
                    exitTime = bar.Timestamp;
                    exitReason = ExitReason.OppositeBarsFlatten;
                    exited = true;
                    break;
                }
            }
            else
            {
                trailingProtection = Math.Min(trailingProtection, Math.Min(baselineProtection, stairProtection));
                if (bar.High >= trailingProtection)
                {
                    exitPrice = Math.Max(bar.Open, trailingProtection);
                    exitBar = j;
                    exitTime = bar.Timestamp;
                    exitReason = ExitReason.EmaTrail;
                    exited = true;
                    break;
                }

                if (ExitEngine.ShouldFlattenOnTwoOppositeBars(
                        side,
                        bar.Open,
                        bar.Close,
                        entryPrice,
                        trailingProtection,
                        ref consecutiveOppositeBars))
                {
                    exitPrice = bar.Close;
                    exitBar = j;
                    exitTime = bar.Timestamp;
                    exitReason = ExitReason.OppositeBarsFlatten;
                    exited = true;
                    break;
                }
            }
        }

        if (!exited)
        {
            exitPrice = triggerBars[exitBar].Bar.Close;
            exitTime = triggerBars[exitBar].Bar.Timestamp;
        }

        double grossPnl = side == TradeSide.Long
            ? (exitPrice - entryPrice) * positionSize
            : (entryPrice - exitPrice) * positionSize;
        double commission = _cfg.CommissionPerShare * positionSize * 2.0;
        double pnl = grossPnl - commission;
        double pnlR = positionSize > 0 && riskPerShare > 0
            ? pnl / (riskPerShare * positionSize)
            : 0.0;
        double peakR = side == TradeSide.Long
            ? (peakPrice - entryPrice) / riskPerShare
            : (entryPrice - troughPrice) / riskPerShare;

        return new BacktestTradeResult(
            EntryBar: signal.BarIndex,
            EntryTime: signal.Timestamp,
            ExitBar: exitBar,
            ExitTime: exitTime,
            Side: side,
            EntryPrice: entryPrice,
            ExitPrice: exitPrice,
            StopPrice: stopPrice,
            PositionSize: positionSize,
            Pnl: pnl,
            PnlR: pnlR,
            ExitReason: exitReason,
            PeakR: peakR,
            BarsHeld: Math.Max(1, exitBar - signal.BarIndex),
            SubStrategy: signal.SubStrategy);
    }

    private bool HasRecentPullbackLong(EnrichedBar[] bars, int i, double atr)
    {
        int start = Math.Max(1, i - _cfg.FastReEntryPullbackBars);
        for (int j = start; j < i; j++)
        {
            var row = bars[j];
            bool touchedTrend = row.Bar.Low <= row.Ema21 + (_cfg.FastReEntryPullbackToleranceAtr * atr)
                || row.Bar.Low <= row.Vwap + (_cfg.FastReEntryPullbackToleranceAtr * atr)
                || row.Bar.Close <= row.Ema9;
            bool heldBaseline = double.IsNaN(row.Sma200) || row.Bar.Low >= row.Sma200 - (_cfg.FastReEntryPullbackToleranceAtr * atr);
            if (touchedTrend && heldBaseline)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasRecentBounceShort(EnrichedBar[] bars, int i, double atr)
    {
        int start = Math.Max(1, i - _cfg.FastReEntryPullbackBars);
        for (int j = start; j < i; j++)
        {
            var row = bars[j];
            bool touchedTrend = row.Bar.High >= row.Ema21 - (_cfg.FastReEntryPullbackToleranceAtr * atr)
                || row.Bar.High >= row.Vwap - (_cfg.FastReEntryPullbackToleranceAtr * atr)
                || row.Bar.Close >= row.Ema9;
            bool heldBaseline = double.IsNaN(row.Sma200) || row.Bar.High <= row.Sma200 + (_cfg.FastReEntryPullbackToleranceAtr * atr);
            if (touchedTrend && heldBaseline)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V18");

    private static bool HasCoreData(EnrichedBar row)
    {
        return !double.IsNaN(row.Atr14)
            && row.Atr14 > 0
            && !double.IsNaN(row.Ema9)
            && !double.IsNaN(row.Ema21)
            && !double.IsNaN(row.Ema50)
            && !double.IsNaN(row.Vwap)
            && !double.IsNaN(row.Rsi14)
            && !double.IsNaN(row.MacdHist)
            && !double.IsNaN(row.Rvol)
            && !double.IsNaN(row.DcPct)
            && !double.IsNaN(row.Adx);
    }

    private static double HighestHigh(EnrichedBar[] bars, int start, int end)
    {
        double value = double.MinValue;
        for (int i = Math.Max(0, start); i <= Math.Min(end, bars.Length - 1); i++)
        {
            value = Math.Max(value, bars[i].Bar.High);
        }

        return value;
    }

    private static double LowestLow(EnrichedBar[] bars, int start, int end)
    {
        double value = double.MaxValue;
        for (int i = Math.Max(0, start); i <= Math.Min(end, bars.Length - 1); i++)
        {
            value = Math.Min(value, bars[i].Bar.Low);
        }

        return value;
    }

    private sealed class V18Diagnostics
    {
        private readonly string _label;
        private readonly bool _enabled;
        private readonly Dictionary<string, int> _rejections = new(StringComparer.OrdinalIgnoreCase);

        public int RawScanned;
        public int Accepted;
        public int AcceptedLong;
        public int AcceptedShort;

        public V18Diagnostics(string label, bool enabled)
        {
            _label = label;
            _enabled = enabled;
        }

        public void Reject(string reason)
        {
            if (!_enabled) return;
            _rejections[reason] = _rejections.GetValueOrDefault(reason) + 1;
        }

        public void ObserveTrade(BacktestTradeResult trade)
        {
            if (!_enabled) return;
        }

        public void PrintSummary()
        {
            if (!_enabled) return;
            Console.WriteLine($"[V18-DIAG:{_label}] scanned={RawScanned} accepted={Accepted} long={AcceptedLong} short={AcceptedShort}");
            if (_rejections.Count > 0)
            {
                Console.WriteLine($"[V18-DIAG:{_label}] rejects {string.Join(", ", _rejections.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}"))}");
            }
        }
    }
}
