using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

public sealed class V17Config
{
    public double RiskPerTradeDollars { get; init; } = 22.0;
    public double AccountSize { get; init; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.18;
    public int MaxShares { get; init; } = 6_500;
    public double MinRiskPerShare { get; init; } = 0.05;

    public bool AllowLong { get; init; } = true;
    public bool AllowShort { get; init; } = true;
    public bool UseNextBarOpenEntry { get; init; } = true;

    public int CooldownBars { get; init; } = 12;
    public int MaxSignalsPerDay { get; init; } = 2;
    public int ShortMaxSignalsPerDay { get; init; } = 1;

    public double MinPrice { get; init; } = 0.3;
    public double MaxPrice { get; init; } = 700.0;
    public double ShortMinPrice { get; init; } = 0.3;
    public double SpreadZMax { get; init; } = 3.0;
    public double L2LiquidityMin { get; init; } = 10.0;

    public int MarketOpenMinute { get; init; } = 570;
    public int SkipFirstNMinutes { get; init; } = 15;
    public int ShortEarliestMinuteEt { get; init; } = 615;
    public int LastEntryMinuteBeforeClose { get; init; } = 75;
    public (int Start, int End)[] EntryWindows { get; init; } = [(585, 885)];

    public int TrendScoreMin { get; init; } = 5;
    public int TrendLeadMin { get; init; } = 2;
    public bool RequireNonNeutralStructureForTrend { get; init; } = true;
    public double NeutralBandwidthMax { get; init; } = 0.06;
    public double NeutralDcDistancePct { get; init; } = 0.18;

    public double RvolMin { get; init; } = 1.05;
    public double StrongRvolMin { get; init; } = 1.35;
    public double MinNormalizedBarRangeAtr { get; init; } = 0.45;
    public double MaxNormalizedBarRangeAtr { get; init; } = 2.20;
    public double MinVolAccel { get; init; } = 0.00;

    public int RangeFilterLookback { get; init; } = 12;
    public double RangeBreakoutConfirmAtr { get; init; } = 0.16;
    public double PullbackToTrendAtr { get; init; } = 0.25;
    public double LongVwapReclaimToleranceAtr { get; init; } = 0.05;
    public double LongMaxBreakoutExtensionAtr { get; init; } = 0.35;
    public double LongMaxVwapExtensionAtr { get; init; } = 0.55;
    public double TrendEmaToleranceAtr { get; init; } = 0.00;
    public double TrendDcMidToleranceAtr { get; init; } = 0.00;
    public bool RequireLongSupertrendSupport { get; init; } = false;
    public double LongSupertrendToleranceAtr { get; init; } = 0.08;
    public bool RequireLongAdxStrength { get; init; } = false;
    public double LongMinAdx { get; init; } = 16.0;
    public double LongMinPlusDiEdge { get; init; } = 4.0;
    public bool UseLongAdxCeiling { get; init; } = false;
    public double LongMaxAdx { get; init; } = 45.0;
    public bool RequireLongRsiBand { get; init; } = false;
    public double LongMinRsi14 { get; init; } = 48.0;
    public double LongMaxRsi14 { get; init; } = 68.0;
    public bool RequireLongMfiBand { get; init; } = false;
    public double LongMinMfi14 { get; init; } = 50.0;
    public double LongMaxMfi14 { get; init; } = 80.0;
    public bool RequireLongStochTrend { get; init; } = false;
    public double LongMinStochK { get; init; } = 45.0;
    public double LongMaxStochK { get; init; } = 85.0;
    public bool UseLongKeltnerExtensionGuard { get; init; } = false;
    public double LongMaxKeltnerExtensionAtr { get; init; } = 0.20;
    public double LongMinCloseLocationPct { get; init; } = 0.55;

    public int AtrPeriod { get; init; } = 14;
    public double AtrUpperMultiplier { get; init; } = 3.0;
    public double AtrLowerMultiplier { get; init; } = 2.0;

    public int BuySetupLookbackBars { get; init; } = 24;
    public int BuySetupPullbackBars { get; init; } = 3;
    public int OneTwoThreeLookbackBars { get; init; } = 12;
    public int BreakoutLookbackBars { get; init; } = 12;
    public bool EnableLongBreakoutSetup { get; init; } = true;
    public bool EnableLongOneTwoThreeSetup { get; init; } = true;
    public bool EnableLongBuySetup { get; init; } = true;
    public bool RequireBullishBreakoutCandle { get; init; } = false;
    public double LongBreakoutMinCloseLocationPct { get; init; } = 0.65;
    public double LongBreakoutMaxUpperWickPct { get; init; } = 0.25;
    public bool RequireBullishReversalPullbackCandle { get; init; } = false;

    public int L1L2ConfirmMinScore { get; init; } = 4;
    public int ShortL1L2ConfirmMinScore { get; init; } = 5;
    public int BuySetupLongExtraConfirmScore { get; init; } = 1;
    public int BuySetupShortExtraConfirmScore { get; init; } = 1;
    public double LongImbalanceMin { get; init; } = 1.05;
    public double ShortImbalanceMax { get; init; } = 0.95;
    public double DeepLongImbalanceMin { get; init; } = 1.15;
    public double DeepShortImbalanceMax { get; init; } = 0.85;
    public double PositiveOfiMin { get; init; } = 0.05;
    public double NegativeOfiMax { get; init; } = -0.05;
    public double BidAskSizeRatioLongMin { get; init; } = 1.10;
    public double BidAskSizeRatioShortMax { get; init; } = 0.90;
    public double ShortRiskPerTradeMultiplier { get; init; } = 0.55;
    public double ShortUpperWickMinPct { get; init; } = 0.25;
    public double ShortVwapRejectAtr { get; init; } = 0.05;
    public double ShortFailedBreakoutToleranceAtr { get; init; } = 0.10;
    public int ShortFailedBreakoutLookbackBars { get; init; } = 6;
    public double ShortMaxChaseBelowLowerBandAtr { get; init; } = 0.35;

    public double HardStopR { get; init; } = 1.20;
    public double BreakevenR { get; init; } = 0.60;
    public double TrailR { get; init; } = 0.15;
    public double GivebackPct { get; init; } = 0.60;
    public double GivebackUsdCap { get; init; } = 20.0;
    public double Tp1R { get; init; } = 0.73;
    public double Tp2R { get; init; } = 2.04;
    public int MaxHoldBars { get; init; } = 40;
    public double SlippageCents { get; init; } = 1.0;
    public double CommissionPerShare { get; init; } = 0.005;
    public double StopAtrMultiplierTrend { get; init; } = 1.25;
    public double StopAtrMultiplierNeutral { get; init; } = 0.85;
    public double StopAtrMultiplierShort { get; init; } = 0.95;
    public double MinStopCents { get; init; } = 4.0;
    public double LongStopAnchorBufferAtr { get; init; } = 0.30;
    public double LongStopEma21BufferAtr { get; init; } = 0.20;
    public double LongStopVwapBufferAtr { get; init; } = 0.25;
    public bool UseLongLowAtrDefensiveSizing { get; init; } = true;
    public double LongLowAtrToPriceThreshold { get; init; } = 0.018;
    public double LongLowAtrRiskMultiplier { get; init; } = 0.70;
    public double LongLowAtrMaxPositionNotionalPctOfAccount { get; init; } = 0.12;
    public bool UseLongHighBandwidthRiskTaper { get; init; } = true;
    public double LongHighBandwidthThreshold { get; init; } = 0.055;
    public double LongHighBandwidthRiskMultiplier { get; init; } = 0.75;

    public bool UseBreakoutLongExitProfile { get; init; } = false;
    public double BreakoutLongBreakevenR { get; init; } = 0.35;
    public double BreakoutLongTrailR { get; init; } = 0.15;
    public double BreakoutLongGivebackPct { get; init; } = 0.60;
    public double BreakoutLongTp1R { get; init; } = 0.73;
    public double BreakoutLongTp2R { get; init; } = 2.04;
    public int BreakoutLongMaxHoldBars { get; init; } = 18;
    public bool BreakoutLongFlattenOnEntryLossCross { get; init; } = true;
    public double BreakoutLongEntryLossBufferCents { get; init; } = 1.0;
    public double BreakoutLongMicroTrailCents { get; init; } = 0.8;
    public double BreakoutLongMicroTrailActivateCents { get; init; } = 1.5;
    public double BreakoutLongPeakGivebackKeepFraction { get; init; } = 0.30;
    public double BreakoutLongPeakGivebackActivateR { get; init; } = 0.20;
    public int BreakoutLongStagnationBars { get; init; } = 4;
    public double BreakoutLongStagnationMinPeakR { get; init; } = 0.12;
    public double BreakoutLongStagnationMaxAdverseR { get; init; } = -0.03;
    public bool BreakoutLongReversalFlatten { get; init; } = true;
    public bool BreakoutLongEmaTrail { get; init; } = true;
    public double BreakoutLongEmaTrailBufferAtr { get; init; } = 0.20;
    public double BreakoutLongGivebackMinPeakR { get; init; } = 0.20;

    public bool EnableDiagnostics { get; init; } = false;
    public string DiagnosticsLabel { get; init; } = "V17";
    public bool IgnoreSelfLearningSetupBlock { get; init; } = false;
}

public sealed class StrategyV17 : BacktestStrategyBase
{
    private enum TrendRegime
    {
        Up,
        Down,
        Neutral,
    }

    private readonly V17Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;
    private readonly ExitEngine.ExitConfig _breakoutLongExitCfg;
    private readonly V17Diagnostics _diag;

    public StrategyV17(V17Config? cfg = null)
    {
        _cfg = cfg ?? new V17Config();
        if (!_cfg.AllowLong || !_cfg.AllowShort)
            throw new InvalidOperationException("V17: single-direction configs are not allowed. Both AllowLong and AllowShort must be true.");
        _diag = new V17Diagnostics(
            _cfg.DiagnosticsLabel,
            _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());
        _exitCfg = BuildExitConfig(
            breakevenR: _cfg.BreakevenR,
            trailR: _cfg.TrailR,
            givebackPct: _cfg.GivebackPct,
            tp1R: _cfg.Tp1R,
            tp2R: _cfg.Tp2R,
            maxHoldBars: _cfg.MaxHoldBars,
            flattenOnEntryLossCross: false,
            entryLossBufferCents: 0.0,
            microTrailCents: 1.0,
            microTrailActivateCents: 2.0,
            peakGivebackKeepFraction: 0.50,
            peakGivebackActivateR: 0.35,
            stagnationBars: 8,
            stagnationMinPeakR: 0.15,
            stagnationMaxAdverseR: -0.10);
        _breakoutLongExitCfg = BuildExitConfig(
            breakevenR: _cfg.BreakoutLongBreakevenR,
            trailR: _cfg.BreakoutLongTrailR,
            givebackPct: _cfg.BreakoutLongGivebackPct,
            tp1R: _cfg.BreakoutLongTp1R,
            tp2R: _cfg.BreakoutLongTp2R,
            maxHoldBars: _cfg.BreakoutLongMaxHoldBars,
            flattenOnEntryLossCross: _cfg.BreakoutLongFlattenOnEntryLossCross,
            entryLossBufferCents: _cfg.BreakoutLongEntryLossBufferCents,
            microTrailCents: _cfg.BreakoutLongMicroTrailCents,
            microTrailActivateCents: _cfg.BreakoutLongMicroTrailActivateCents,
            peakGivebackKeepFraction: _cfg.BreakoutLongPeakGivebackKeepFraction,
            peakGivebackActivateR: _cfg.BreakoutLongPeakGivebackActivateR,
            stagnationBars: _cfg.BreakoutLongStagnationBars,
            stagnationMinPeakR: _cfg.BreakoutLongStagnationMinPeakR,
            stagnationMaxAdverseR: _cfg.BreakoutLongStagnationMaxAdverseR,
            reversalFlatten: _cfg.BreakoutLongReversalFlatten,
            emaTrail: _cfg.BreakoutLongEmaTrail,
            emaTrailBufferAtr: _cfg.BreakoutLongEmaTrailBufferAtr,
            givebackMinPeakR: _cfg.BreakoutLongGivebackMinPeakR);
    }

    private ExitEngine.ExitConfig BuildExitConfig(
        double breakevenR,
        double trailR,
        double givebackPct,
        double tp1R,
        double tp2R,
        int maxHoldBars,
        bool flattenOnEntryLossCross,
        double entryLossBufferCents,
        double microTrailCents,
        double microTrailActivateCents,
        double peakGivebackKeepFraction,
        double peakGivebackActivateR,
        int stagnationBars,
        double stagnationMinPeakR,
        double stagnationMaxAdverseR,
        bool reversalFlatten = true,
        bool emaTrail = true,
        double emaTrailBufferAtr = 0.20,
        double givebackMinPeakR = 0.20)
        {
        return new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = breakevenR,
            TrailR = trailR,
            GivebackPct = givebackPct,
            GivebackMinPeakR = givebackMinPeakR,
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
            MicroTrail = true,
            MicroTrailCents = microTrailCents,
            MicroTrailActivateCents = microTrailActivateCents,
            EmaTrail = emaTrail,
            EmaTrailBufferAtr = emaTrailBufferAtr,
            FlattenOnEntryLossCross = flattenOnEntryLossCross,
            EntryLossBufferCents = entryLossBufferCents,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = peakGivebackKeepFraction,
            PeakGivebackActivateR = peakGivebackActivateR,
            FlattenOnStagnation = true,
            StagnationBars = stagnationBars,
            StagnationMinPeakR = stagnationMinPeakR,
            StagnationMaxAdverseR = stagnationMaxAdverseR,
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

        int startIndex = Math.Max(30, Math.Max(_cfg.RangeFilterLookback, _cfg.BuySetupLookbackBars) + 5);
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

            if (!PassNormalizedVolumeFilter(row, atr))
            {
                _diag.Reject("normalized-volume-filter");
                continue;
            }

            if (IsMarketRegimeBlocked(row))
            {
                _diag.Reject("market-regime-blocked");
                continue;
            }

            var regime = DetermineTrendRegime(triggerBars, i);
            if (regime == TrendRegime.Down
                && dayShortSignalCounts.GetValueOrDefault(dayEt) >= _cfg.ShortMaxSignalsPerDay)
            {
                _diag.Reject("short:max-signals-per-day");
                continue;
            }

            BacktestSignal? signal;
            string rejectReason;

            if (regime == TrendRegime.Up && _cfg.AllowLong)
            {
                signal = TryLongSignal(triggerBars, i, regime, out rejectReason);
            }
            else if (regime == TrendRegime.Down && _cfg.AllowShort)
            {
                signal = TryShortSignal(triggerBars, i, regime, out rejectReason);
            }
            else
            {
                signal = null;
                rejectReason = regime switch
                {
                    TrendRegime.Up => "long-disabled",
                    TrendRegime.Down => "short-disabled",
                    _ => "neutral-regime",
                };
            }

            if (signal == null)
            {
                _diag.Reject(rejectReason);
                continue;
            }

            signals.Add(signal);
            _diag.AcceptedSignals++;
            if (signal.Side == TradeSide.Long) _diag.AcceptedLongSignals++;
            else _diag.AcceptedShortSignals++;
            lastAcceptedBar = i;
            daySignalCounts[dayEt] = daySignalCounts.GetValueOrDefault(dayEt) + 1;
            if (signal.Side == TradeSide.Short)
            {
                dayShortSignalCounts[dayEt] = dayShortSignalCounts.GetValueOrDefault(dayEt) + 1;
            }
        }

        _diag.PrintSummary();
        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var exitCfg = _cfg.UseBreakoutLongExitProfile
            && signal.Side == TradeSide.Long
            && string.Equals(signal.SubStrategy, "V17_BREAKOUT", StringComparison.Ordinal)
            ? _breakoutLongExitCfg
            : _exitCfg;

        exitCfg = ApplySelfLearningExitOverrides(exitCfg, signal.SubStrategy);

        var result = ExitEngine.SimulateTrade(signal, triggerBars, exitCfg);
        if (result != null)
        {
            _diag.ObserveTrade(result);
        }

        return result;
    }

    private bool HasCoreData(EnrichedBar row)
    {
        return !double.IsNaN(row.Atr14)
            && row.Atr14 > 0
            && !double.IsNaN(row.Ema9)
            && !double.IsNaN(row.Ema21)
            && !double.IsNaN(row.Ema50)
            && !double.IsNaN(row.Vwap)
            && !double.IsNaN(row.DcUpper)
            && !double.IsNaN(row.DcLower)
            && !double.IsNaN(row.DcMid)
            && !double.IsNaN(row.Rvol)
            && !double.IsNaN(row.BbBandwidth);
    }

    private bool PassNormalizedVolumeFilter(EnrichedBar row, double atr)
    {
        if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin)
        {
            return false;
        }

        if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.MinVolAccel)
        {
            return false;
        }

        double barRangeAtr = (row.Bar.High - row.Bar.Low) / Math.Max(0.01, atr);
        return barRangeAtr >= _cfg.MinNormalizedBarRangeAtr
            && barRangeAtr <= _cfg.MaxNormalizedBarRangeAtr;
    }

    private TrendRegime DetermineTrendRegime(EnrichedBar[] bars, int i)
    {
        var row = bars[i];
        var prev = bars[i - 1];

        int longScore = 0;
        int shortScore = 0;

        if (row.StDirection > 0) longScore++; else if (row.StDirection < 0) shortScore++;
        if (row.Ema9 > row.Ema21) longScore++; else shortScore++;
        if (row.Ema21 > row.Ema50) longScore++; else shortScore++;
        if (row.Bar.Close > row.Ema21) longScore++; else shortScore++;
        if (!double.IsNaN(row.MacdHist) && row.MacdHist > 0) longScore++; else if (!double.IsNaN(row.MacdHist)) shortScore++;
        if (row.Ema21 >= prev.Ema21) longScore++; else shortScore++;
        if (!double.IsNaN(row.DcPct) && row.DcPct >= 0.60) longScore++; else if (!double.IsNaN(row.DcPct) && row.DcPct <= 0.40) shortScore++;

        bool neutralStructure = row.BbBandwidth <= _cfg.NeutralBandwidthMax
            || (!double.IsNaN(row.DcPct) && Math.Abs(row.DcPct - 0.50) <= _cfg.NeutralDcDistancePct);

        bool allowTrendThroughNeutralStructure = !_cfg.RequireNonNeutralStructureForTrend || !neutralStructure;

        if (longScore >= _cfg.TrendScoreMin
            && longScore >= shortScore + _cfg.TrendLeadMin
            && allowTrendThroughNeutralStructure)
        {
            return TrendRegime.Up;
        }

        if (shortScore >= _cfg.TrendScoreMin
            && shortScore >= longScore + _cfg.TrendLeadMin
            && allowTrendThroughNeutralStructure)
        {
            return TrendRegime.Down;
        }

        return TrendRegime.Neutral;
    }

    private BacktestSignal? TryLongSignal(EnrichedBar[] bars, int i, TrendRegime regime, out string reason)
    {
        reason = string.Empty;
        var row = bars[i];
        var prev = bars[i - 1];
        double atr = row.Atr14;
        double upperAtrBand = row.Ema21 + (_cfg.AtrUpperMultiplier * atr);
        double priorHigh = HighestHigh(bars, i - _cfg.RangeFilterLookback, i - 1);
        double emaTolerance = _cfg.TrendEmaToleranceAtr * atr;
        double dcTolerance = _cfg.TrendDcMidToleranceAtr * atr;

        bool trendFilterPass = row.Bar.Close >= row.Ema21 - emaTolerance
            && row.Ema9 >= row.Ema21 - emaTolerance
            && row.Bar.Close >= row.DcMid - dcTolerance
            && row.Bar.Close <= upperAtrBand;
        if (!trendFilterPass)
        {
            reason = "long:trend-filter";
            return null;
        }

        bool rangeFilterPass = row.Bar.Close >= row.KcMid
            && row.Bar.Close >= priorHigh - (_cfg.RangeBreakoutConfirmAtr * atr)
            && row.Bar.Close >= row.Vwap - (_cfg.LongVwapReclaimToleranceAtr * atr)
            && row.Bar.Close <= priorHigh + (_cfg.LongMaxBreakoutExtensionAtr * atr)
            && row.Bar.Close <= row.Vwap + (_cfg.LongMaxVwapExtensionAtr * atr);
        if (!rangeFilterPass)
        {
            reason = "long:range-filter";
            return null;
        }

        bool qualityFilterPass = PassLongQualityFilter(row, prev, atr);
        if (!qualityFilterPass)
        {
            reason = "long:quality-filter";
            return null;
        }

        int l1l2Score = ScoreL1L2(row, TradeSide.Long);
        if (l1l2Score < _cfg.L1L2ConfirmMinScore)
        {
            reason = $"long:l1l2<{_cfg.L1L2ConfirmMinScore}";
            return null;
        }

        bool breakoutReady = _cfg.EnableLongBreakoutSetup && TryBreakoutLong(bars, i);
        bool oneTwoThreeReady = _cfg.EnableLongOneTwoThreeSetup && TryOneTwoThreeLong(bars, i);
        bool buySetupReady = _cfg.EnableLongBuySetup
            && TryBuySetupLong(bars, i)
            && l1l2Score >= _cfg.L1L2ConfirmMinScore + _cfg.BuySetupLongExtraConfirmScore;

        if (breakoutReady)
        {
            double swingLow = LowestLow(bars, i - _cfg.BuySetupLookbackBars / 2, i);
            return MakeSignal(bars, i, TradeSide.Long, swingLow, regime, "V17_BREAKOUT");
        }

        if (oneTwoThreeReady)
        {
            double swingLow = LowestLow(bars, i - _cfg.BuySetupLookbackBars / 2, i);
            return MakeSignal(bars, i, TradeSide.Long, swingLow, regime, "V17_123");
        }

        if (buySetupReady)
        {
            double swingLow = LowestLow(bars, i - _cfg.BuySetupLookbackBars / 2, i);
            return MakeSignal(bars, i, TradeSide.Long, swingLow, regime, "V17_BUYSETUP");
        }

        reason = "long:no-setup";
        return null;
    }

    private bool PassLongQualityFilter(EnrichedBar row, EnrichedBar prev, double atr)
    {
        bool supertrendAligned = !_cfg.RequireLongSupertrendSupport
            || (row.StDirection > 0
                && !double.IsNaN(row.Supertrend)
                && row.Bar.Close >= row.Supertrend - (_cfg.LongSupertrendToleranceAtr * atr));

        bool adxStrength = !_cfg.RequireLongAdxStrength
            || (!double.IsNaN(row.Adx)
                && row.Adx >= _cfg.LongMinAdx
                && !double.IsNaN(row.PlusDi)
                && !double.IsNaN(row.MinusDi)
                && row.PlusDi >= row.MinusDi + _cfg.LongMinPlusDiEdge
                && (double.IsNaN(prev.Adx) || row.Adx >= prev.Adx - 1.0));

        bool moneyFlowHealthy = !_cfg.RequireLongMfiBand
            || (!double.IsNaN(row.Mfi14)
                && row.Mfi14 >= _cfg.LongMinMfi14
                && row.Mfi14 <= _cfg.LongMaxMfi14);

        bool rsiHealthy = !_cfg.RequireLongRsiBand
            || (!double.IsNaN(row.Rsi14)
                && row.Rsi14 >= _cfg.LongMinRsi14
                && row.Rsi14 <= _cfg.LongMaxRsi14);

        bool adxNotExhausted = !_cfg.UseLongAdxCeiling
            || double.IsNaN(row.Adx)
            || row.Adx <= _cfg.LongMaxAdx;

        bool stochAligned = !_cfg.RequireLongStochTrend
            || (!double.IsNaN(row.StochK)
                && !double.IsNaN(row.StochD)
                && row.StochK >= row.StochD
                && row.StochK >= _cfg.LongMinStochK
                && row.StochK <= _cfg.LongMaxStochK);

        bool notOverextended = !_cfg.UseLongKeltnerExtensionGuard
            || double.IsNaN(row.KcUpper)
            || row.Bar.Close <= row.KcUpper + (_cfg.LongMaxKeltnerExtensionAtr * atr);

        double range = row.Bar.High - row.Bar.Low;
        bool strongClose = range <= 0
            || (row.Bar.Close > row.Bar.Open
                && row.Bar.Close >= row.Bar.Low + (range * _cfg.LongMinCloseLocationPct));

        return supertrendAligned
            && adxStrength
            && adxNotExhausted
            && rsiHealthy
            && moneyFlowHealthy
            && stochAligned
            && notOverextended
            && strongClose;
    }

    private BacktestSignal? TryShortSignal(EnrichedBar[] bars, int i, TrendRegime regime, out string reason)
    {
        reason = string.Empty;
        var row = bars[i];
        double atr = row.Atr14;
        double lowerAtrBand = row.Ema21 - (_cfg.AtrLowerMultiplier * atr);
        int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
        double emaTolerance = _cfg.TrendEmaToleranceAtr * atr;
        double dcTolerance = _cfg.TrendDcMidToleranceAtr * atr;

        if (row.Bar.Close < _cfg.ShortMinPrice || minuteEt < _cfg.ShortEarliestMinuteEt)
        {
            reason = "short:time-price-gate";
            return null;
        }

        bool trendFilterPass = row.Bar.Close <= row.Ema21 + emaTolerance
            && row.Ema9 <= row.Ema21 + emaTolerance
            && row.Bar.Close <= row.DcMid + dcTolerance
            && row.StDirection < 0
            && row.MinusDi > row.PlusDi
            && row.Rsi14 <= 45.0
            && row.MacdHist < 0
            && row.DcPct <= 0.35
            && row.Bar.Close >= lowerAtrBand;
        if (!trendFilterPass)
        {
            reason = "short:trend-filter";
            return null;
        }

        if (lowerAtrBand - row.Bar.Close > _cfg.ShortMaxChaseBelowLowerBandAtr * atr)
        {
            reason = "short:overshoot";
            return null;
        }

        bool rangeFilterPass = row.Bar.Close <= row.KcMid
            && row.Bar.Close <= LowestLow(bars, i - _cfg.RangeFilterLookback, i - 1) - (_cfg.RangeBreakoutConfirmAtr * atr)
            && row.Bar.Close <= row.Vwap - (_cfg.PullbackToTrendAtr * atr);
        if (!rangeFilterPass)
        {
            reason = "short:range-filter";
            return null;
        }

        int l1l2Score = ScoreL1L2(row, TradeSide.Short);
        if (l1l2Score < _cfg.ShortL1L2ConfirmMinScore)
        {
            reason = $"short:l1l2<{_cfg.ShortL1L2ConfirmMinScore}";
            return null;
        }

        bool breakdownReady = TryBreakdownShort(bars, i);
        bool oneTwoThreeReady = TryOneTwoThreeShort(bars, i);
        bool sellSetupReady = TrySellSetupShort(bars, i)
            && l1l2Score >= _cfg.ShortL1L2ConfirmMinScore + _cfg.BuySetupShortExtraConfirmScore;
        bool vwapReject = IsShortVwapReject(row, atr);
        bool failedBreakout = HasRecentFailedBreakout(bars, i, atr);
        bool rejectionCandle = IsBearishReversalBar(row) && HasUpperWickPct(row, _cfg.ShortUpperWickMinPct);

        if ((breakdownReady && (failedBreakout || vwapReject))
            || (oneTwoThreeReady && rejectionCandle && failedBreakout)
            || (sellSetupReady && vwapReject && rejectionCandle))
        {
            double swingHigh = HighestHigh(bars, i - _cfg.BuySetupLookbackBars / 2, i);
            return MakeSignal(bars, i, TradeSide.Short, swingHigh, regime, "V17_TREND");
        }

        reason = "short:no-setup";
        return null;
    }

    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V17");

    private bool TryBuySetupLong(EnrichedBar[] bars, int i)
    {
        if (i < _cfg.BuySetupLookbackBars + _cfg.BuySetupPullbackBars)
        {
            return false;
        }

        int start = i - _cfg.BuySetupLookbackBars;
        int peakIndex = start;
        double peakHigh = bars[start].Bar.High;
        for (int j = start + 1; j < i; j++)
        {
            if (bars[j].Bar.High >= peakHigh)
            {
                peakHigh = bars[j].Bar.High;
                peakIndex = j;
            }
        }

        if (peakIndex <= start || peakIndex >= i - _cfg.BuySetupPullbackBars)
        {
            return false;
        }

        double rallyLow = LowestLow(bars, start, peakIndex);
        double pullbackLow = LowestLow(bars, peakIndex + 1, i - 1);
        double rallyRange = peakHigh - rallyLow;
        if (rallyRange <= 0)
        {
            return false;
        }

        double retracementPct = (peakHigh - pullbackLow) / rallyRange;
        bool retracementOk = retracementPct >= 0.35 && retracementPct <= 0.65;
        bool lowerHighPullback = HasLowerHighPullback(bars, peakIndex + 1, i - 1);
        bool reclaim = bars[i].Bar.Close > bars[i - 1].Bar.High && bars[i].Bar.Close > bars[i].Ema9;
        bool volumeOk = bars[i].Rvol >= _cfg.StrongRvolMin || bars[i].Bar.Volume > bars[i - 1].Bar.Volume;

        return retracementOk && lowerHighPullback && reclaim && volumeOk;
    }

    private bool TrySellSetupShort(EnrichedBar[] bars, int i)
    {
        if (i < _cfg.BuySetupLookbackBars + _cfg.BuySetupPullbackBars)
        {
            return false;
        }

        int start = i - _cfg.BuySetupLookbackBars;
        int troughIndex = start;
        double troughLow = bars[start].Bar.Low;
        for (int j = start + 1; j < i; j++)
        {
            if (bars[j].Bar.Low <= troughLow)
            {
                troughLow = bars[j].Bar.Low;
                troughIndex = j;
            }
        }

        if (troughIndex <= start || troughIndex >= i - _cfg.BuySetupPullbackBars)
        {
            return false;
        }

        double rallyHigh = HighestHigh(bars, start, troughIndex);
        double bounceHigh = HighestHigh(bars, troughIndex + 1, i - 1);
        double dropRange = rallyHigh - troughLow;
        if (dropRange <= 0)
        {
            return false;
        }

        double bouncePct = (bounceHigh - troughLow) / dropRange;
        bool retracementOk = bouncePct >= 0.35 && bouncePct <= 0.65;
        bool higherLowBounce = HasHigherLowBounce(bars, troughIndex + 1, i - 1);
        bool reject = bars[i].Bar.Close < bars[i - 1].Bar.Low && bars[i].Bar.Close < bars[i].Ema9;
        bool volumeOk = bars[i].Rvol >= _cfg.StrongRvolMin || bars[i].Bar.Volume > bars[i - 1].Bar.Volume;

        return retracementOk && higherLowBounce && reject && volumeOk;
    }

    private bool TryBreakoutLong(EnrichedBar[] bars, int i)
    {
        if (i < _cfg.BreakoutLookbackBars)
        {
            return false;
        }

        double highestPriorHigh = HighestHigh(bars, i - _cfg.BreakoutLookbackBars, i - 1);
        var row = bars[i];
        bool bullishBreakoutCandle = !_cfg.RequireBullishBreakoutCandle
            || IsBullishBreakoutCandle(row, _cfg.LongBreakoutMinCloseLocationPct, _cfg.LongBreakoutMaxUpperWickPct);
        return row.Bar.Close > highestPriorHigh
            && row.Rvol >= _cfg.StrongRvolMin
            && row.Bar.Close > row.Vwap
            && row.MacdHist > bars[i - 1].MacdHist
            && bullishBreakoutCandle;
    }

    private bool TryBreakdownShort(EnrichedBar[] bars, int i)
    {
        if (i < _cfg.BreakoutLookbackBars)
        {
            return false;
        }

        double lowestPriorLow = LowestLow(bars, i - _cfg.BreakoutLookbackBars, i - 1);
        var row = bars[i];
        return row.Bar.Close < lowestPriorLow
            && row.Rvol >= _cfg.StrongRvolMin
            && row.Bar.Close < row.Vwap
            && row.MacdHist < bars[i - 1].MacdHist;
    }

    private bool TryOneTwoThreeLong(EnrichedBar[] bars, int i)
    {
        if (i < _cfg.OneTwoThreeLookbackBars)
        {
            return false;
        }

        int start = i - _cfg.OneTwoThreeLookbackBars;
        int point1 = IndexOfLowestLow(bars, start, i - 3);
        if (point1 < start || point1 >= i - 2)
        {
            return false;
        }

        int point2 = IndexOfHighestHigh(bars, point1 + 1, i - 2);
        if (point2 <= point1 || point2 >= i - 1)
        {
            return false;
        }

        double point3Low = LowestLow(bars, point2 + 1, i - 1);
        bool higherLow = point3Low > bars[point1].Bar.Low;
        bool breakout = bars[i].Bar.Close > bars[point2].Bar.High;
        bool reversalConfirmed = !_cfg.RequireBullishReversalPullbackCandle
            || IsBullishReversalBar(bars[i], bars[i - 1]);
        return higherLow && breakout && reversalConfirmed;
    }

    private bool TryOneTwoThreeShort(EnrichedBar[] bars, int i)
    {
        if (i < _cfg.OneTwoThreeLookbackBars)
        {
            return false;
        }

        int start = i - _cfg.OneTwoThreeLookbackBars;
        int point1 = IndexOfHighestHigh(bars, start, i - 3);
        if (point1 < start || point1 >= i - 2)
        {
            return false;
        }

        int point2 = IndexOfLowestLow(bars, point1 + 1, i - 2);
        if (point2 <= point1 || point2 >= i - 1)
        {
            return false;
        }

        double point3High = HighestHigh(bars, point2 + 1, i - 1);
        bool lowerHigh = point3High < bars[point1].Bar.High;
        bool breakdown = bars[i].Bar.Close < bars[point2].Bar.Low;
        return lowerHigh && breakdown;
    }

    private int ScoreL1L2(EnrichedBar row, TradeSide side)
    {
        int score = 0;
        double mid = (!double.IsNaN(row.BidPrice) && !double.IsNaN(row.AskPrice))
            ? (row.BidPrice + row.AskPrice) / 2.0
            : double.NaN;

        if (side == TradeSide.Long)
        {
            if (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio >= _cfg.LongImbalanceMin) score++;
            if (!double.IsNaN(row.DeepImbalanceRatio) && row.DeepImbalanceRatio >= _cfg.DeepLongImbalanceMin) score++;
            if (!double.IsNaN(row.OfiSignal) && row.OfiSignal >= _cfg.PositiveOfiMin) score++;
            if (row.AskSize > 0 && row.BidSize > 0 && row.BidSize / row.AskSize >= _cfg.BidAskSizeRatioLongMin) score++;
            if (!double.IsNaN(row.DepthWeightedMid) && !double.IsNaN(mid) && row.DepthWeightedMid >= mid) score++;
            if (!double.IsNaN(row.LastPrice) && !double.IsNaN(mid) && row.LastPrice >= mid) score++;
        }
        else
        {
            if (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio <= _cfg.ShortImbalanceMax) score++;
            if (!double.IsNaN(row.DeepImbalanceRatio) && row.DeepImbalanceRatio <= _cfg.DeepShortImbalanceMax) score++;
            if (!double.IsNaN(row.OfiSignal) && row.OfiSignal <= _cfg.NegativeOfiMax) score++;
            if (row.AskSize > 0 && row.BidSize > 0 && row.BidSize / row.AskSize <= _cfg.BidAskSizeRatioShortMax) score++;
            if (!double.IsNaN(row.DepthWeightedMid) && !double.IsNaN(mid) && row.DepthWeightedMid <= mid) score++;
            if (!double.IsNaN(row.LastPrice) && !double.IsNaN(mid) && row.LastPrice <= mid) score++;
        }

        return score;
    }

    private BacktestSignal? MakeSignal(EnrichedBar[] bars, int i, TradeSide side, double anchor, TrendRegime regime, string subStrategy)
    {
        if (!_cfg.IgnoreSelfLearningSetupBlock && IsSelfLearningBlocked(subStrategy)) return null;

        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTs = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry && i + 1 < bars.Length)
        {
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTs = bars[entryIndex].Bar.Timestamp;
        }

        double atr = bars[i].Atr14;
        double stopAtrMultiplier = side == TradeSide.Short
            ? _cfg.StopAtrMultiplierShort
            : regime == TrendRegime.Neutral
                ? _cfg.StopAtrMultiplierNeutral
                : _cfg.StopAtrMultiplierTrend;

        double stopDist = Math.Max(
            atr * stopAtrMultiplier,
            _cfg.MinStopCents / 100.0);
        stopDist = ApplySelfLearningStopMultiplier(stopDist);

        double stopPrice;
        if (side == TradeSide.Long)
        {
            double naturalStop = Math.Min(
                anchor - (_cfg.LongStopAnchorBufferAtr * atr),
                Math.Min(
                    bars[i].Ema21 - (_cfg.LongStopEma21BufferAtr * atr),
                    bars[i].Vwap - (_cfg.LongStopVwapBufferAtr * atr)));
            stopPrice = Math.Min(entryPrice - _cfg.MinRiskPerShare, naturalStop);
            if (entryPrice - stopPrice < stopDist)
            {
                stopPrice = entryPrice - stopDist;
            }
        }
        else
        {
            double naturalStop = anchor + (0.20 * atr);
            stopPrice = Math.Max(entryPrice + _cfg.MinRiskPerShare, naturalStop);
            if (stopPrice - entryPrice < stopDist)
            {
                stopPrice = entryPrice + stopDist;
            }
        }

        double riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare < _cfg.MinRiskPerShare)
        {
            riskPerShare = _cfg.MinRiskPerShare;
            stopPrice = side == TradeSide.Long
                ? entryPrice - riskPerShare
                : entryPrice + riskPerShare;
        }

        double riskBudget = side == TradeSide.Short
            ? _cfg.RiskPerTradeDollars * _cfg.ShortRiskPerTradeMultiplier
            : _cfg.RiskPerTradeDollars;
        double maxPositionNotionalPct = _cfg.MaxPositionNotionalPctOfAccount;

        if (side == TradeSide.Long)
        {
            double atrToPrice = entryPrice > 0 ? atr / entryPrice : double.NaN;
            if (_cfg.UseLongLowAtrDefensiveSizing
                && !double.IsNaN(atrToPrice)
                && atrToPrice > 0
                && atrToPrice <= _cfg.LongLowAtrToPriceThreshold)
            {
                riskBudget *= _cfg.LongLowAtrRiskMultiplier;
                maxPositionNotionalPct = Math.Min(maxPositionNotionalPct, _cfg.LongLowAtrMaxPositionNotionalPctOfAccount);
            }

            double bbBandwidth = bars[i].BbBandwidth;
            if (_cfg.UseLongHighBandwidthRiskTaper
                && !double.IsNaN(bbBandwidth)
                && bbBandwidth >= _cfg.LongHighBandwidthThreshold)
            {
                riskBudget *= _cfg.LongHighBandwidthRiskMultiplier;
            }
        }

        riskBudget = Math.Max(1.0, riskBudget);

        int posSize = BacktestHelpers.ComputePositionSize(
            entryPrice,
            riskPerShare,
            riskBudget,
            _cfg.AccountSize,
            maxPositionNotionalPct,
            _cfg.MaxShares);
        posSize = ApplySelfLearningPositionSize(posSize, subStrategy);

        if (posSize <= 0)
        {
            return null;
        }

        return new BacktestSignal(
            BarIndex: entryIndex,
            Timestamp: entryTs,
            Side: side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: posSize,
            AtrValue: atr,
            HtfTrend: regime switch
            {
                TrendRegime.Up => HtfBias.Bull,
                TrendRegime.Down => HtfBias.Bear,
                _ => HtfBias.Neutral,
            },
            MtfMomentum: regime.ToString(),
            SubStrategy: subStrategy);
    }

    private static bool IsBearishReversalBar(EnrichedBar row)
    {
        double range = row.Bar.High - row.Bar.Low;
        if (range <= 0)
        {
            return false;
        }

        double body = Math.Abs(row.Bar.Close - row.Bar.Open);
        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        return row.Bar.Close < row.Bar.Open && (upperWick / range >= 0.30 || body / range >= 0.45 || row.IsStar);
    }

    private static bool IsBullishReversalBar(EnrichedBar row, EnrichedBar prev)
    {
        double range = row.Bar.High - row.Bar.Low;
        if (range <= 0)
        {
            return false;
        }

        double body = Math.Abs(row.Bar.Close - row.Bar.Open);
        double lowerWick = Math.Min(row.Bar.Open, row.Bar.Close) - row.Bar.Low;
        bool hammerLike = row.IsHammer || (lowerWick / range >= 0.35 && row.Bar.Close > row.Bar.Open);
        bool bullishEngulfing = row.Bar.Close > row.Bar.Open
            && prev.Bar.Close < prev.Bar.Open
            && row.Bar.Open <= prev.Bar.Close
            && row.Bar.Close >= prev.Bar.Open;
        bool strongBullClose = row.Bar.Close > row.Bar.Open
            && row.Bar.Close >= row.Bar.Low + (range * 0.65)
            && row.Bar.Close > prev.Bar.High;

        return hammerLike || bullishEngulfing || strongBullClose || (row.IsBullishCandle && body / range >= 0.45);
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

    private static bool HasUpperWickPct(EnrichedBar row, double minPct)
    {
        double range = row.Bar.High - row.Bar.Low;
        if (range <= 0)
        {
            return false;
        }

        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        return upperWick / range >= minPct;
    }

    private bool IsShortVwapReject(EnrichedBar row, double atr)
    {
        return row.Bar.High >= row.Vwap
            && row.Bar.Close <= row.Vwap - (_cfg.ShortVwapRejectAtr * atr);
    }

    private bool HasRecentFailedBreakout(EnrichedBar[] bars, int i, double atr)
    {
        int lookbackStart = Math.Max(0, i - _cfg.ShortFailedBreakoutLookbackBars);
        double priorHigh = HighestHigh(bars, lookbackStart, i - 1);
        var row = bars[i];
        return row.Bar.High >= priorHigh - (_cfg.ShortFailedBreakoutToleranceAtr * atr)
            && row.Bar.Close <= priorHigh - (_cfg.ShortFailedBreakoutToleranceAtr * atr)
            && row.Bar.Close < row.Bar.Open;
    }

    private sealed class V17Diagnostics
    {
        private readonly string _label;
        private readonly bool _enabled;
        private readonly Dictionary<string, int> _rejections = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _losersByExit = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _losersBySetup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _lossPnlBySetup = new(StringComparer.OrdinalIgnoreCase);

        public V17Diagnostics(string label, bool enabled)
        {
            _label = label;
            _enabled = enabled;
        }

        public int RawScanned { get; set; }
        public int AcceptedSignals { get; set; }
        public int AcceptedLongSignals { get; set; }
        public int AcceptedShortSignals { get; set; }
        public int LosingTrades { get; private set; }
        public int WinningTrades { get; private set; }

        public void Reject(string reason)
        {
            if (!_rejections.TryAdd(reason, 1))
            {
                _rejections[reason]++;
            }
        }

        public void ObserveTrade(BacktestTradeResult trade)
        {
            if (trade.Pnl > 0)
            {
                WinningTrades++;
                return;
            }

            LosingTrades++;
            string exitKey = $"{trade.Side}:{trade.ExitReason}";
            if (!_losersByExit.TryAdd(exitKey, 1))
            {
                _losersByExit[exitKey]++;
            }

            string setup = string.IsNullOrWhiteSpace(trade.SubStrategy) ? "UNKNOWN" : trade.SubStrategy;
            if (!_losersBySetup.TryAdd(setup, 1))
            {
                _losersBySetup[setup]++;
            }

            if (!_lossPnlBySetup.TryAdd(setup, trade.Pnl))
            {
                _lossPnlBySetup[setup] += trade.Pnl;
            }
        }

        public void PrintSummary()
        {
            if (!_enabled) return;

            var rejectStr = string.Join(", ", _rejections
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));

            var loserExitStr = string.Join(", ", _losersByExit
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));

            var loserSetupStr = string.Join(", ", _lossPnlBySetup
                .OrderBy(kvp => kvp.Value)
                .Take(10)
                .Select(kvp => $"{kvp.Key}={kvp.Value:F2}/{_losersBySetup[kvp.Key]}"));

            Console.WriteLine($"[V17-DIAG:{_label}] scanned={RawScanned} accepted={AcceptedSignals} long={AcceptedLongSignals} short={AcceptedShortSignals} wins={WinningTrades} losers={LosingTrades}");
            Console.WriteLine($"[V17-DIAG:{_label}] rejects {rejectStr}");
            Console.WriteLine($"[V17-DIAG:{_label}] loser-exits {loserExitStr}");
            Console.WriteLine($"[V17-DIAG:{_label}] loser-setups {loserSetupStr}");
        }
    }

    private static bool HasLowerHighPullback(EnrichedBar[] bars, int start, int end)
    {
        if (end - start + 1 < 3)
        {
            return false;
        }

        int first = Math.Max(start, end - 2);
        return bars[first].Bar.High > bars[first + 1].Bar.High
            && bars[first + 1].Bar.High > bars[first + 2].Bar.High;
    }

    private static bool HasHigherLowBounce(EnrichedBar[] bars, int start, int end)
    {
        if (end - start + 1 < 3)
        {
            return false;
        }

        int first = Math.Max(start, end - 2);
        return bars[first].Bar.Low < bars[first + 1].Bar.Low
            && bars[first + 1].Bar.Low < bars[first + 2].Bar.Low;
    }

    private static double HighestHigh(EnrichedBar[] bars, int start, int end)
    {
        start = Math.Max(0, start);
        end = Math.Min(bars.Length - 1, end);
        double highest = double.MinValue;
        for (int i = start; i <= end; i++)
        {
            highest = Math.Max(highest, bars[i].Bar.High);
        }
        return highest;
    }

    private static double LowestLow(EnrichedBar[] bars, int start, int end)
    {
        start = Math.Max(0, start);
        end = Math.Min(bars.Length - 1, end);
        double lowest = double.MaxValue;
        for (int i = start; i <= end; i++)
        {
            lowest = Math.Min(lowest, bars[i].Bar.Low);
        }
        return lowest;
    }

    private static int IndexOfHighestHigh(EnrichedBar[] bars, int start, int end)
    {
        start = Math.Max(0, start);
        end = Math.Min(bars.Length - 1, end);
        int idx = start;
        double highest = bars[start].Bar.High;
        for (int i = start + 1; i <= end; i++)
        {
            if (bars[i].Bar.High >= highest)
            {
                highest = bars[i].Bar.High;
                idx = i;
            }
        }
        return idx;
    }

    private static int IndexOfLowestLow(EnrichedBar[] bars, int start, int end)
    {
        start = Math.Max(0, start);
        end = Math.Min(bars.Length - 1, end);
        int idx = start;
        double lowest = bars[start].Bar.Low;
        for (int i = start + 1; i <= end; i++)
        {
            if (bars[i].Bar.Low <= lowest)
            {
                lowest = bars[i].Bar.Low;
                idx = i;
            }
        }
        return idx;
    }
}
