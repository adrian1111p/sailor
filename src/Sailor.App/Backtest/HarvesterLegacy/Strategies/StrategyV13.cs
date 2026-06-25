using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

public sealed class V13Config
{
    public double RiskPerTradeDollars { get; set; } = 24.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;
    public int MaxShares { get; set; } = 6_500;
    public double MinRiskPerShare { get; set; } = 0.01;

    public bool UseNextBarOpenEntry { get; set; } = true;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
    public int MaxSignalsPerDay { get; set; } = 2;
    public int CooldownBars { get; set; } = 10;

    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;
    public double L2LiquidityMin { get; set; } = 20.0;
    public double SpreadZMax { get; set; } = 2.0;
    public double MinVolAccel { get; set; } = -0.05;
    public double RvolMin { get; set; } = 0.85;
    public double BreakoutRvolMin { get; set; } = 0.95;

    public int MarketOpenMinute { get; set; } = 570;
    public int OpeningRangeMinutes { get; set; } = 15;
    public bool AllowOpeningRangeFallback { get; set; } = true;
    public int OpeningRangeFallbackLatestMinute { get; set; } = 630;
    public int OpeningRangeFallbackMinBars { get; set; } = 6;
    public int SkipFirstNMinutes { get; set; } = 16;
    public (int Start, int End)[] EntryWindows { get; set; } =
        [(588, 690), (780, 920)];

    public double BreakoutConfirmAtr { get; set; } = 0.08;
    public int PullbackMinBars { get; set; } = 2;
    public int PullbackMaxBars { get; set; } = 9;
    public double PullbackTouchAtr { get; set; } = 0.12;
    public double VwapTouchAtr { get; set; } = 0.15;
    public double MaxExtensionFromEma9Atr { get; set; } = 0.55;
    public int SwingLookback { get; set; } = 6;

    public double AdxMin { get; set; } = 18.0;
    public double AdxMax { get; set; } = 45.0;
    public bool AllowSoftAdxPass { get; set; } = true;
    public double AdxSoftTolerance { get; set; } = 3.0;
    public bool RequireHtfBias { get; set; } = true;
    public bool UseDirectionalStrengthHtfOverride { get; set; } = true;
    public bool AllowWeakCounterTrendHtf { get; set; } = false;
    public bool AllowStrongCounterTrendHtf { get; set; } = false;
    public int WeakCounterTrendHtfScoreMin { get; set; } = 3;
    public int StrongCounterTrendHtfScoreMin { get; set; } = 4;
    public bool RequireMtfAlign { get; set; } = true;

    public double RsiLongMin { get; set; } = 45.0;
    public double RsiLongMax { get; set; } = 68.0;
    public double RsiShortMin { get; set; } = 32.0;
    public double RsiShortMax { get; set; } = 55.0;
    public bool AllowAdaptiveRsi { get; set; } = true;
    public double AdaptiveRsiTolerance { get; set; } = 4.0;

    public double HardStopR { get; set; } = 0.80;
    public double BreakevenR { get; set; } = 0.40;
    public double TrailR { get; set; } = 0.24;
    public double GivebackPct { get; set; } = 0.18;
    public double GivebackUsdCap { get; set; } = 24.0;
    public double Tp1R { get; set; } = 0.55;
    public double Tp2R { get; set; } = 1.05;
    public int MaxHoldBars { get; set; } = 20;
    public bool Tp1TightenToBe { get; set; } = true;
    public double PeakGivebackKeepFraction { get; set; } = 0.55;
    public double PeakGivebackActivateR { get; set; } = 0.45;
    public int StagnationBars { get; set; } = 5;
    public double StagnationMinPeakR { get; set; } = 0.20;
    public double StagnationMaxAdverseR { get; set; } = -0.10;

    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;

    // â”€â”€ P1: L2 entry filter â”€â”€
    public bool RequireL2EntryFilter { get; set; } = false;
    public double L2OfiMinLong { get; set; } = -0.10;
    public double L2OfiMaxShort { get; set; } = 0.10;
    public double L2ImbalanceMinLong { get; set; } = 0.70;
    public double L2ImbalanceMaxShort { get; set; } = 1.40;

    // â”€â”€ P2: Price-tier micro trail on winners â”€â”€
    public bool UsePriceTierMicroTrail { get; set; } = false;

    // â”€â”€ P3: Price-tier hard stop floor â”€â”€
    public bool UsePriceTierStopFloor { get; set; } = false;

    // â”€â”€ P4: 20MA extension + L2 flip exit â”€â”€
    public bool UseMaExtensionL2Flip { get; set; } = false;
    public double MaExtensionMinR { get; set; } = 0.30;
    public double MaExtensionAtrThreshold { get; set; } = 1.50;
    public bool UseL1L2DecisionOnOppositeBarsFlatten { get; set; } = false;

    public bool EnableDiagnostics { get; set; } = false;
    public string DiagnosticsLabel { get; set; } = "V13";
}

public sealed class StrategyV13 : BacktestStrategyBase
{
    private readonly V13Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;
    private readonly V3SignalCore _signalCore;

    public StrategyV13(V13Config? cfg = null)
    {
        _cfg = cfg ?? new V13Config();
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
            VwapStretchAtr = 1.10,
            VwapEnabled = true,
            BbEntryPctbLow = 0.18,
            BbEntryPctbHigh = 0.82,
            BbEnabled = true,
            SqueezeEnabled = true,
            SqueezeBars = 7,
            L2LiquidityMin = _cfg.L2LiquidityMin,
            SpreadZMax = _cfg.SpreadZMax,
            VolAccelMin = _cfg.MinVolAccel,
            RvolMin = _cfg.RvolMin,
            RsiOversold = Math.Min(_cfg.RsiLongMax, 42.0),
            RsiOverbought = Math.Max(_cfg.RsiShortMin, 58.0),
            RequireVolumeConfirm = true,
            HardStopR = _cfg.HardStopR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            BreakevenR = _cfg.BreakevenR,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            AllowLong = _cfg.AllowLong,
            AllowShort = _cfg.AllowShort,
        });

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
            ReversalFlatten = true,
            MicroTrail = true,
            MicroTrailCents = 1.5,
            MicroTrailActivateCents = 2.5,
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

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var diagnostics = new V13Diagnostics(_cfg.DiagnosticsLabel, _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());
        var rawSignals = _signalCore.GenerateSignals(triggerBars, bars5m, bars15m, bars1h, bars1d);
        diagnostics.RawSignals = rawSignals.Count;
        if (rawSignals.Count == 0)
        {
            diagnostics.PrintSummary();
            return rawSignals;
        }

        var dayContexts = BuildDayContexts(triggerBars);
        var accepted = new List<BacktestSignal>(rawSignals.Count);
        var daySignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBarIndex = -10_000;

        foreach (var signal in rawSignals.OrderBy(s => s.BarIndex))
        {
            if (signal.BarIndex - lastAcceptedBarIndex < _cfg.CooldownBars)
            {
                diagnostics.Reject("cooldown");
                continue;
            }

            int evalIndex = Math.Max(0, signal.BarIndex - (_cfg.UseNextBarOpenEntry ? 1 : 0));
            if (evalIndex >= triggerBars.Length)
            {
                diagnostics.Reject("eval-index-oob");
                continue;
            }

            var row = triggerBars[evalIndex];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0)
            {
                diagnostics.Reject("atr-invalid");
                continue;
            }

            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            if (!dayContexts.TryGetValue(dayEt, out var dayContext))
            {
                diagnostics.Reject("opening-range-missing");
                continue;
            }

            if (!TryPassCoreFilters(row, out var coreReason))
            {
                diagnostics.Reject($"core:{coreReason}");
                continue;
            }

            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
            {
                diagnostics.Reject("before-entry-start");
                continue;
            }

            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
            {
                diagnostics.Reject("outside-entry-window");
                continue;
            }

            int countForDay = daySignalCounts.GetValueOrDefault(dayEt);
            if (countForDay >= _cfg.MaxSignalsPerDay)
            {
                diagnostics.Reject("max-signals-per-day");
                continue;
            }

            string htfBias = ComputeHtfBias(row.Bar.Timestamp, bars15m, bars1h, bars1d);
            if (signal.Side == TradeSide.Long)
            {
                diagnostics.RawLongSignals++;

                // P1: L2 entry filter for longs
                if (_cfg.RequireL2EntryFilter && !PassesL2EntryFilter(row, TradeSide.Long))
                {
                    diagnostics.Reject("long:l2-unfavorable");
                    continue;
                }

                if (!TryPassLongFilters(triggerBars, evalIndex, dayContext, atr, row, htfBias, bars5m, bars15m, out var longReason))
                {
                    diagnostics.Reject($"long:{longReason}");
                    continue;
                }

                accepted.Add(signal with { SubStrategy = "V13_ORB_PULLBACK" });
                diagnostics.AcceptedLongSignals++;
            }
            else
            {
                diagnostics.RawShortSignals++;

                // P1: L2 entry filter for shorts
                if (_cfg.RequireL2EntryFilter && !PassesL2EntryFilter(row, TradeSide.Short))
                {
                    diagnostics.Reject("short:l2-unfavorable");
                    continue;
                }

                if (!TryPassShortFilters(triggerBars, evalIndex, dayContext, atr, row, htfBias, bars5m, bars15m, out var shortReason))
                {
                    diagnostics.Reject($"short:{shortReason}");
                    continue;
                }

                accepted.Add(signal with { SubStrategy = "V13_ORB_PULLBACK" });
                diagnostics.AcceptedShortSignals++;
            }

            daySignalCounts[dayEt] = countForDay + 1;
            lastAcceptedBarIndex = signal.BarIndex;
        }

        diagnostics.AcceptedSignals = accepted.Count;
        diagnostics.PrintSummary();
        return accepted;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private Dictionary<DateOnly, DayContext> BuildDayContexts(EnrichedBar[] triggerBars)
    {
        var result = new Dictionary<DateOnly, DayContext>();
        foreach (var day in BacktestHelpers.GroupByTradingDayEt(triggerBars))
        {
            var (orHigh, orLow, orEndIdx) = BacktestHelpers.ComputeOpeningRangeEt(
                day.StartIdx,
                day.EndIdx,
                triggerBars,
                _cfg.MarketOpenMinute,
                _cfg.OpeningRangeMinutes);

            if (orEndIdx < 0 || double.IsNaN(orHigh) || double.IsNaN(orLow))
            {
                if (_cfg.AllowOpeningRangeFallback
                    && TryBuildFallbackDayContext(day, triggerBars, out var fallbackContext))
                {
                    result[day.DateEt] = fallbackContext;
                }

                continue;
            }

            result[day.DateEt] = new DayContext(day.StartIdx, day.EndIdx, orHigh, orLow, orEndIdx);
        }

        return result;
    }

    private bool TryPassCoreFilters(EnrichedBar row, out string reason)
    {
        if (row.Bar.Close < _cfg.MinPrice || row.Bar.Close > _cfg.MaxPrice)
        {
            reason = "price";
            return false;
        }

        if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin)
        {
            reason = "rvol";
            return false;
        }

        if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin)
        {
            reason = "liquidity";
            return false;
        }

        if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax)
        {
            reason = "spread";
            return false;
        }

        if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.MinVolAccel)
        {
            reason = "vol-accel";
            return false;
        }

        if (double.IsNaN(row.Adx) || row.Adx < _cfg.AdxMin || row.Adx > _cfg.AdxMax)
        {
            if (!PassesSoftAdx(row))
            {
                reason = "adx";
                return false;
            }
        }

        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Vwap) || double.IsNaN(row.Rsi14))
        {
            reason = "indicators";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TryPassLongFilters(
        EnrichedBar[] triggerBars,
        int evalIndex,
        DayContext dayContext,
        double atr,
        EnrichedBar row,
        string htfBias,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m,
        out string reason)
    {
        if (!PassesLongHtfBias(row, htfBias))
        {
            reason = $"htf:{htfBias}";
            return false;
        }

        if (_cfg.RequireMtfAlign && !HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, TradeSide.Long))
        {
            reason = "mtf-align";
            return false;
        }

        if (!PassesLongRsi(row))
        {
            reason = "rsi";
            return false;
        }

        double orRange = Math.Max(0.01, dayContext.OrHigh - dayContext.OrLow);
        double regimeFloor = dayContext.OrLow + (0.35 * orRange);
        if (row.Ema9 < row.Ema21 && row.Bar.Close < regimeFloor)
        {
            reason = "regime-floor";
            return false;
        }

        double extensionAtr = (row.Bar.Close - row.Ema9) / atr;
        if (extensionAtr > _cfg.MaxExtensionFromEma9Atr)
        {
            reason = "extension";
            return false;
        }

        bool hasBreakout = TryFindLongBreakout(triggerBars, dayContext, evalIndex, out int breakoutIndex);
        if (!hasBreakout)
        {
            if (row.Bar.Close < regimeFloor)
            {
                reason = "no-breakout-regime";
                return false;
            }
        }

        if (hasBreakout)
        {
            int age = evalIndex - breakoutIndex;
            if (age > _cfg.PullbackMaxBars)
            {
                reason = "pullback-too-old";
                return false;
            }

            if (age >= _cfg.PullbackMinBars
                && !HasTouchedLongPullbackZone(triggerBars, breakoutIndex + 1, evalIndex, atr))
            {
                reason = "pullback-touch-missing";
                return false;
            }
        }

        var prev = triggerBars[Math.Max(0, evalIndex - 1)];
        if (row.Bar.Close < regimeFloor)
        {
            reason = "below-regime-floor";
            return false;
        }

        if (!(row.Bar.Close >= row.Bar.Open || row.Bar.Close > prev.Bar.Close))
        {
            reason = "trigger-candle";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TryPassShortFilters(
        EnrichedBar[] triggerBars,
        int evalIndex,
        DayContext dayContext,
        double atr,
        EnrichedBar row,
        string htfBias,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m,
        out string reason)
    {
        if (!PassesShortHtfBias(row, htfBias))
        {
            reason = $"htf:{htfBias}";
            return false;
        }

        if (_cfg.RequireMtfAlign && !HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, TradeSide.Short))
        {
            reason = "mtf-align";
            return false;
        }

        if (!PassesShortRsi(row))
        {
            reason = "rsi";
            return false;
        }

        double orRange = Math.Max(0.01, dayContext.OrHigh - dayContext.OrLow);
        double regimeCeiling = dayContext.OrHigh - (0.35 * orRange);
        if (row.Ema9 > row.Ema21 && row.Bar.Close > regimeCeiling)
        {
            reason = "regime-ceiling";
            return false;
        }

        double extensionAtr = (row.Ema9 - row.Bar.Close) / atr;
        if (extensionAtr > _cfg.MaxExtensionFromEma9Atr)
        {
            reason = "extension";
            return false;
        }

        bool hasBreakout = TryFindShortBreakout(triggerBars, dayContext, evalIndex, out int breakoutIndex);
        if (!hasBreakout)
        {
            if (row.Bar.Close > regimeCeiling)
            {
                reason = "no-breakout-regime";
                return false;
            }
        }

        if (hasBreakout)
        {
            int age = evalIndex - breakoutIndex;
            if (age > _cfg.PullbackMaxBars)
            {
                reason = "pullback-too-old";
                return false;
            }

            if (age >= _cfg.PullbackMinBars
                && !HasTouchedShortPullbackZone(triggerBars, breakoutIndex + 1, evalIndex, atr))
            {
                reason = "pullback-touch-missing";
                return false;
            }
        }

        var prev = triggerBars[Math.Max(0, evalIndex - 1)];
        if (row.Bar.Close > regimeCeiling)
        {
            reason = "above-regime-ceiling";
            return false;
        }

        if (!(row.Bar.Close <= row.Bar.Open || row.Bar.Close < prev.Bar.Close))
        {
            reason = "trigger-candle";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool PassesLongHtfBias(EnrichedBar row, string htfBias)
    {
        if (!_cfg.RequireHtfBias)
        {
            return true;
        }

        if (htfBias is "BULL" or "STRONG_BULL" or "NEUTRAL")
        {
            return true;
        }

        int directionalScore = ComputeDirectionalStrengthScore(row, TradeSide.Long);
        if (_cfg.AllowStrongCounterTrendHtf && htfBias == "STRONG_BEAR")
        {
            return _cfg.UseDirectionalStrengthHtfOverride
                ? directionalScore >= _cfg.StrongCounterTrendHtfScoreMin
                : row.Adx >= _cfg.AdxMin + 4.0
                    && row.Ema9 >= row.Ema21
                    && row.Bar.Close >= row.Vwap
                    && row.Bar.Close >= row.Ema9;
        }

        if (!_cfg.AllowWeakCounterTrendHtf || htfBias != "BEAR")
        {
            return false;
        }

        return _cfg.UseDirectionalStrengthHtfOverride
            ? directionalScore >= _cfg.WeakCounterTrendHtfScoreMin
            : row.Adx >= _cfg.AdxMin + 2.0
                && row.Ema9 >= row.Ema21
                && row.Bar.Close >= row.Ema9;
    }

    private bool PassesShortHtfBias(EnrichedBar row, string htfBias)
    {
        if (!_cfg.RequireHtfBias)
        {
            return true;
        }

        if (htfBias is "BEAR" or "STRONG_BEAR" or "NEUTRAL")
        {
            return true;
        }

        int directionalScore = ComputeDirectionalStrengthScore(row, TradeSide.Short);
        if (_cfg.AllowStrongCounterTrendHtf && htfBias == "STRONG_BULL")
        {
            return _cfg.UseDirectionalStrengthHtfOverride
                ? directionalScore >= _cfg.StrongCounterTrendHtfScoreMin
                : row.Adx >= _cfg.AdxMin + 4.0
                    && row.Ema9 <= row.Ema21
                    && row.Bar.Close <= row.Vwap
                    && row.Bar.Close <= row.Ema9;
        }

        if (!_cfg.AllowWeakCounterTrendHtf || htfBias != "BULL")
        {
            return false;
        }

        return _cfg.UseDirectionalStrengthHtfOverride
            ? directionalScore >= _cfg.WeakCounterTrendHtfScoreMin
            : row.Adx >= _cfg.AdxMin + 2.0
                && row.Ema9 <= row.Ema21
                && row.Bar.Close <= row.Ema9;
    }

    private int ComputeDirectionalStrengthScore(EnrichedBar row, TradeSide side)
    {
        int score = 0;

        if (side == TradeSide.Long)
        {
            if (row.Ema9 >= row.Ema21) score++;
            if (row.Bar.Close >= row.Ema9) score++;
            if (row.Bar.Close >= row.Vwap) score++;
            if (row.Bar.Close >= row.Bar.Open) score++;
            if (!double.IsNaN(row.Rsi14) && row.Rsi14 >= _cfg.RsiLongMin - _cfg.AdaptiveRsiTolerance) score++;
        }
        else
        {
            if (row.Ema9 <= row.Ema21) score++;
            if (row.Bar.Close <= row.Ema9) score++;
            if (row.Bar.Close <= row.Vwap) score++;
            if (row.Bar.Close <= row.Bar.Open) score++;
            if (!double.IsNaN(row.Rsi14) && row.Rsi14 <= _cfg.RsiShortMax + _cfg.AdaptiveRsiTolerance) score++;
        }

        if (PassesSoftAdx(row))
        {
            score++;
        }

        return score;
    }

    private bool PassesLongRsi(EnrichedBar row)
    {
        if (row.Rsi14 >= _cfg.RsiLongMin && row.Rsi14 <= _cfg.RsiLongMax)
        {
            return true;
        }

        if (!_cfg.AllowAdaptiveRsi)
        {
            return false;
        }

        double min = _cfg.RsiLongMin - _cfg.AdaptiveRsiTolerance;
        double max = _cfg.RsiLongMax + Math.Max(1.0, _cfg.AdaptiveRsiTolerance * 0.5);
        if (row.Rsi14 < min || row.Rsi14 > max)
        {
            return false;
        }

        return row.Ema9 >= row.Ema21
            && row.Bar.Close >= row.Vwap
            && row.Bar.Close >= row.Ema9;
    }

    private bool PassesShortRsi(EnrichedBar row)
    {
        if (row.Rsi14 >= _cfg.RsiShortMin && row.Rsi14 <= _cfg.RsiShortMax)
        {
            return true;
        }

        if (!_cfg.AllowAdaptiveRsi)
        {
            return false;
        }

        double min = _cfg.RsiShortMin - Math.Max(1.0, _cfg.AdaptiveRsiTolerance * 0.5);
        double max = _cfg.RsiShortMax + _cfg.AdaptiveRsiTolerance;
        if (row.Rsi14 < min || row.Rsi14 > max)
        {
            return false;
        }

        return row.Ema9 <= row.Ema21
            && row.Bar.Close <= row.Vwap
            && row.Bar.Close <= row.Ema9;
    }

    private bool PassesSoftAdx(EnrichedBar row)
    {
        if (!_cfg.AllowSoftAdxPass || double.IsNaN(row.Adx) || row.Adx > _cfg.AdxMax)
        {
            return false;
        }

        if (row.Adx >= _cfg.AdxMin)
        {
            return true;
        }

        if (row.Adx < _cfg.AdxMin - _cfg.AdxSoftTolerance
            || double.IsNaN(row.Atr14)
            || row.Atr14 <= 0
            || double.IsNaN(row.Ema9)
            || double.IsNaN(row.Ema21)
            || double.IsNaN(row.Vwap))
        {
            return false;
        }

        double emaSpreadAtr = Math.Abs(row.Ema9 - row.Ema21) / row.Atr14;
        double vwapDistanceAtr = Math.Abs(row.Bar.Close - row.Vwap) / row.Atr14;
        return emaSpreadAtr >= 0.05 && vwapDistanceAtr >= 0.02;
    }

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

    private bool TryBuildFallbackDayContext(
        BacktestHelpers.DayGroup day,
        EnrichedBar[] triggerBars,
        out DayContext context)
    {
        context = default!;

        int firstRegularIdx = -1;
        int firstRegularMinute = -1;
        for (int i = day.StartIdx; i < day.EndIdx; i++)
        {
            int minute = TradingTime.GetMinuteOfDayEt(triggerBars[i].Bar.Timestamp);
            if (minute < _cfg.MarketOpenMinute)
            {
                continue;
            }

            firstRegularIdx = i;
            firstRegularMinute = minute;
            break;
        }

        if (firstRegularIdx < 0 || firstRegularMinute > _cfg.OpeningRangeFallbackLatestMinute)
        {
            return false;
        }

        int fallbackEndMinute = firstRegularMinute + _cfg.OpeningRangeMinutes;
        double orHigh = double.MinValue;
        double orLow = double.MaxValue;
        int includedBars = 0;
        int lastIncludedIdx = -1;

        for (int i = firstRegularIdx; i < day.EndIdx; i++)
        {
            int minute = TradingTime.GetMinuteOfDayEt(triggerBars[i].Bar.Timestamp);
            if (minute > fallbackEndMinute && includedBars >= _cfg.OpeningRangeFallbackMinBars)
            {
                break;
            }

            orHigh = Math.Max(orHigh, triggerBars[i].Bar.High);
            orLow = Math.Min(orLow, triggerBars[i].Bar.Low);
            includedBars++;
            lastIncludedIdx = i;
        }

        if (includedBars < _cfg.OpeningRangeFallbackMinBars
            || orHigh == double.MinValue
            || orLow == double.MaxValue
            || lastIncludedIdx < 0)
        {
            return false;
        }

        context = new DayContext(day.StartIdx, day.EndIdx, orHigh, orLow, Math.Min(lastIncludedIdx + 1, day.EndIdx));
        return true;
    }

    private bool TryFindLongBreakout(EnrichedBar[] bars, DayContext dayContext, int evalIndex, out int breakoutIndex)
    {
        breakoutIndex = -1;
        int start = Math.Max(dayContext.OrEndIdx, dayContext.StartIdx);
        int end = Math.Min(evalIndex, dayContext.EndIdx - 1);
        double orRange = Math.Max(0.01, dayContext.OrHigh - dayContext.OrLow);
        double regimeFloor = dayContext.OrLow + (0.60 * orRange);

        for (int i = start; i <= end; i++)
        {
            var row = bars[i];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0 || double.IsNaN(row.Rvol))
            {
                continue;
            }

            if (row.Rvol >= Math.Max(_cfg.RvolMin, _cfg.BreakoutRvolMin - 0.10)
                && row.Ema9 >= row.Ema21
                && (row.Bar.High >= dayContext.OrHigh - (0.05 * atr)
                    || row.Bar.Close >= regimeFloor))
            {
                breakoutIndex = i;
            }
        }

        return breakoutIndex >= 0;
    }

    private bool TryFindShortBreakout(EnrichedBar[] bars, DayContext dayContext, int evalIndex, out int breakoutIndex)
    {
        breakoutIndex = -1;
        int start = Math.Max(dayContext.OrEndIdx, dayContext.StartIdx);
        int end = Math.Min(evalIndex, dayContext.EndIdx - 1);
        double orRange = Math.Max(0.01, dayContext.OrHigh - dayContext.OrLow);
        double regimeCeiling = dayContext.OrHigh - (0.60 * orRange);

        for (int i = start; i <= end; i++)
        {
            var row = bars[i];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0 || double.IsNaN(row.Rvol))
            {
                continue;
            }

            if (row.Rvol >= Math.Max(_cfg.RvolMin, _cfg.BreakoutRvolMin - 0.10)
                && row.Ema9 <= row.Ema21
                && (row.Bar.Low <= dayContext.OrLow + (0.05 * atr)
                    || row.Bar.Close <= regimeCeiling))
            {
                breakoutIndex = i;
            }
        }

        return breakoutIndex >= 0;
    }

    private bool HasTouchedLongPullbackZone(EnrichedBar[] bars, int start, int end, double atr)
    {
        for (int i = Math.Max(1, start); i <= end; i++)
        {
            var row = bars[i];
            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Vwap))
            {
                continue;
            }

            bool emaTouch = row.Bar.Low <= row.Ema9 + (_cfg.PullbackTouchAtr * atr);
            bool vwapTouch = row.Bar.Low <= row.Vwap + (_cfg.VwapTouchAtr * atr);
            if (emaTouch || vwapTouch)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasTouchedShortPullbackZone(EnrichedBar[] bars, int start, int end, double atr)
    {
        for (int i = Math.Max(1, start); i <= end; i++)
        {
            var row = bars[i];
            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Vwap))
            {
                continue;
            }

            bool emaTouch = row.Bar.High >= row.Ema9 - (_cfg.PullbackTouchAtr * atr);
            bool vwapTouch = row.Bar.High >= row.Vwap - (_cfg.VwapTouchAtr * atr);
            if (emaTouch || vwapTouch)
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeHtfBias(DateTime ts, params EnrichedBar[]?[] frames)
    {
        int scoreSum = 0;
        int scoreCount = 0;

        foreach (var bars in frames)
        {
            if (bars == null || bars.Length < 2)
            {
                continue;
            }

            int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
            if (idx < 1)
            {
                continue;
            }

            var row = bars[idx];
            var prev = bars[idx - 1];
            if (double.IsNaN(row.Ema21) || double.IsNaN(row.Ema50) || double.IsNaN(row.MacdHist))
            {
                continue;
            }

            int score = 0;
            score += row.Ema21 > row.Ema50 ? 1 : -1;
            score += row.Bar.Close > row.Ema21 ? 1 : -1;
            score += row.MacdHist >= 0 ? 1 : -1;
            score += row.Ema21 >= prev.Ema21 ? 1 : -1;
            if (!double.IsNaN(row.Adx) && row.Adx >= 20 && !double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi))
            {
                score += row.PlusDi >= row.MinusDi ? 1 : -1;
            }

            scoreSum += score;
            scoreCount++;
        }

        if (scoreCount == 0)
        {
            return "NEUTRAL";
        }

        double avg = (double)scoreSum / scoreCount;
        if (avg >= 2.5)
        {
            return "STRONG_BULL";
        }

        if (avg >= 1.0)
        {
            return "BULL";
        }

        if (avg <= -2.5)
        {
            return "STRONG_BEAR";
        }

        if (avg <= -1.0)
        {
            return "BEAR";
        }

        return "NEUTRAL";
    }

    private static bool HasMtfAlignment(DateTime ts, EnrichedBar[]? bars5m, EnrichedBar[]? bars15m, TradeSide side)
    {
        return FrameAligned(bars5m, ts, side) && FrameAligned(bars15m, ts, side);

        static bool FrameAligned(EnrichedBar[]? bars, DateTime ts, TradeSide side)
        {
            if (bars == null || bars.Length == 0)
            {
                return true;
            }

            int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
            if (idx < 0)
            {
                return true;
            }

            var row = bars[idx];
            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.MacdHist))
            {
                return false;
            }

            return side == TradeSide.Long
                ? row.Ema9 > row.Ema21 && row.Bar.Close >= row.Ema21 && row.MacdHist >= 0
                : row.Ema9 < row.Ema21 && row.Bar.Close <= row.Ema21 && row.MacdHist <= 0;
        }
    }

    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V13");

    private sealed class V13Diagnostics
    {
        private readonly string _label;
        private readonly bool _enabled;
        private readonly Dictionary<string, int> _rejections = new(StringComparer.OrdinalIgnoreCase);

        public V13Diagnostics(string label, bool enabled)
        {
            _label = label;
            _enabled = enabled;
        }

        public int RawSignals { get; set; }
        public int AcceptedSignals { get; set; }
        public int RawLongSignals { get; set; }
        public int RawShortSignals { get; set; }
        public int AcceptedLongSignals { get; set; }
        public int AcceptedShortSignals { get; set; }

        public void Reject(string reason)
        {
            if (!_rejections.TryAdd(reason, 1))
            {
                _rejections[reason]++;
            }
        }

        public void PrintSummary()
        {
            if (!_enabled)
            {
                return;
            }

            var topReasons = _rejections
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .Select(kvp => $"{kvp.Key}={kvp.Value}");

            Console.WriteLine($"[V13-DIAG:{_label}] raw={RawSignals} accepted={AcceptedSignals} rawLong={RawLongSignals} rawShort={RawShortSignals} acceptedLong={AcceptedLongSignals} acceptedShort={AcceptedShortSignals}");
            Console.WriteLine($"[V13-DIAG:{_label}] rejects {string.Join(", ", topReasons)}");
        }
    }

    private sealed record DayContext(int StartIdx, int EndIdx, double OrHigh, double OrLow, int OrEndIdx);
}
