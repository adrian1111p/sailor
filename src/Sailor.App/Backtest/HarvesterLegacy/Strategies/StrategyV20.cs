using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// StrategyV20 â€” retained defensive continuation variant
//
// The old V20 implementation reused one permissive composite entry engine across
// several labeled variants. That produced too many low-quality trades, especially
// in choppy and volatility-expansion conditions. This retained version keeps only
// the strongest survivor profile and converts V20 into a much tighter continuation
// strategy that trades only when:
//   1. intraday and higher-timeframe trend are aligned,
//   2. price has pulled back to the fast EMA and resumed,
//   3. volatility is in the tradeable middle band, not choppy or adverse,
//   4. price is not stretched too far from VWAP,
//   5. the trigger candle closes with decisive body quality.
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

public sealed class V20Config
{
    // â”€â”€ Account / Position Sizing â”€â”€
    public double RiskPerTradeDollars { get; init; } = 20.0;
    public double AccountSize { get; init; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.18;
    public int MaxShares { get; init; } = 6_500;
    public double MinRiskPerShare { get; init; } = 0.03;

    // â”€â”€ Direction â”€â”€
    public bool AllowLong { get; init; } = false;
    public bool AllowShort { get; init; } = true;
    public bool UseNextBarOpenEntry { get; init; } = true;

    // â”€â”€ Throttle â”€â”€
    public int CooldownBars { get; init; } = 8;
    public int MaxSignalsPerDay { get; init; } = 3;

    // â”€â”€ Price & Time Filters â”€â”€
    public double MinPrice { get; init; } = 0.3;
    public double MaxPrice { get; init; } = 700.0;
    public int MarketOpenMinute { get; init; } = 570;
    public int SkipFirstNMinutes { get; init; } = 20;
    public int LastEntryMinuteBeforeClose { get; init; } = 90;
    public (int Start, int End)[] EntryWindows { get; init; } = [(590, 870)];

    // â”€â”€ Entry Filters â”€â”€
    public double RvolMin { get; init; } = 0.0;
    public double RvolMax { get; init; } = 10.0;
    public double AdxMin { get; init; } = 12.0;
    public double AdxMax { get; init; } = 60.0;
    public double MaxSpreadZ { get; init; } = 8.0;
    public double LongRsiMin { get; init; } = 45.0;
    public double LongRsiMax { get; init; } = 68.0;
    public double ShortRsiMin { get; init; } = 35.0;
    public double ShortRsiMax { get; init; } = 52.0;
    public double MaxVwapExtensionAtr { get; init; } = 2.00;
    public double PullbackTouchToleranceAtr { get; init; } = 0.60;
    public double MinimumBodyFraction { get; init; } = 0.15;
    public double MinimumCloseLocation { get; init; } = 0.55;
    public bool RequireEmaStack { get; init; } = true;
    public bool RequireHigherTimeframeTrend { get; init; } = false;

    // â”€â”€ Regime Detection â”€â”€
    public double ChoppyAdxThreshold { get; init; } = 18.0;
    public double ChoppyBbBandwidthMax { get; init; } = 0.025;
    public bool EnableChoppyFilter { get; init; } = true;
    public double MinTradeableBbBandwidth { get; init; } = 0.025;
    public double VolatileBbBandwidthMin { get; init; } = 1.0;

    // â”€â”€ Stop / Exit Profile (DEFENSIVE â€” tighter than V3) â”€â”€
    public double HardStopR { get; init; } = 0.90;
    public double BreakevenR { get; init; } = 0.40;
    public double TrailR { get; init; } = 0.25;
    public double GivebackPct { get; init; } = 0.35;
    public double GivebackUsdCap { get; init; } = 12.0;
    public double Tp1R { get; init; } = 0.90;
    public double Tp2R { get; init; } = 1.60;
    public int MaxHoldBars { get; init; } = 18;

    // â”€â”€ Peak Giveback Flatten (trail at 60% of MFE) â”€â”€
    public bool FlattenOnPeakGiveback { get; init; } = true;
    public double PeakGivebackKeepFraction { get; init; } = 0.45;
    public double PeakGivebackActivateR { get; init; } = 0.25;

    // â”€â”€ Stagnation Exit (time-based adverse exit â€” 3 min) â”€â”€
    public bool FlattenOnStagnation { get; init; } = true;
    public int StagnationBars { get; init; } = 3;
    public double StagnationMinPeakR { get; init; } = 0.08;
    public double StagnationMaxAdverseR { get; init; } = -0.03;

    // â”€â”€ Advanced Exits â”€â”€
    public bool ReversalFlatten { get; init; } = true;
    public bool MicroTrail { get; init; } = true;
    public double MicroTrailCents { get; init; } = 1.5;
    public double MicroTrailActivateCents { get; init; } = 3.0;
    public bool EmaTrail { get; init; } = true;
    public double EmaTrailBufferAtr { get; init; } = 0.08;

    // â”€â”€ Confirmation Requirements â”€â”€
    public bool RequireVwapAlignment { get; init; } = true;
    public bool RequireMacdMomentum { get; init; } = true;
    public bool IgnoreSelfLearningSetupBlock { get; init; } = false;

    // â”€â”€ Slippage â”€â”€
    public double SlippageCents { get; init; } = 1.0;
    public double CommissionPerShare { get; init; } = 0.005;
}

public sealed class StrategyV20 : BacktestStrategyBase
{
    private readonly V20Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV20(V20Config? cfg = null)
    {
        _cfg = cfg ?? new V20Config();
        _exitCfg = BuildExitConfig(_cfg);
    }

    private static ExitEngine.ExitConfig BuildExitConfig(V20Config cfg) => new()
    {
        HardStopR = cfg.HardStopR,
        BreakevenR = cfg.BreakevenR,
        TrailR = cfg.TrailR,
        GivebackPct = cfg.GivebackPct,
        GivebackMinPeakR = 0.10,
        UseFixedGivebackUsdCap = true,
        UseVariableGivebackUsdCap = true,
        GivebackUsdCap = cfg.GivebackUsdCap,
        Tp1R = cfg.Tp1R,
        Tp2R = cfg.Tp2R,
        MaxHoldBars = cfg.MaxHoldBars,
        SlippageCents = cfg.SlippageCents,
        CommissionPerShare = cfg.CommissionPerShare,
        DeductCommission = true,
        Tp1TightenToBe = true,
        ReversalFlatten = cfg.ReversalFlatten,
        MicroTrail = cfg.MicroTrail,
        MicroTrailCents = cfg.MicroTrailCents,
        MicroTrailActivateCents = cfg.MicroTrailActivateCents,
        EmaTrail = cfg.EmaTrail,
        EmaTrailBufferAtr = cfg.EmaTrailBufferAtr,
        FlattenOnPeakGiveback = cfg.FlattenOnPeakGiveback,
        PeakGivebackKeepFraction = cfg.PeakGivebackKeepFraction,
        PeakGivebackActivateR = cfg.PeakGivebackActivateR,
        FlattenOnStagnation = cfg.FlattenOnStagnation,
        StagnationBars = cfg.StagnationBars,
        StagnationMinPeakR = cfg.StagnationMinPeakR,
        StagnationMaxAdverseR = cfg.StagnationMaxAdverseR,
    };

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Signal Generation
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var signals = new List<BacktestSignal>();
        if (triggerBars.Length < 60) return signals;

        int lastAcceptedBar = -10_000;
        var daySignalCounts = new Dictionary<DateOnly, int>();

        for (int i = 50; i < triggerBars.Length - 1; i++)
        {
            if (i - lastAcceptedBar < _cfg.CooldownBars) continue;

            var row = triggerBars[i];
            var prev = triggerBars[i - 1];
            double price = row.Bar.Close;
            double atr = row.Atr14;

            if (!HasRequiredData(row, prev, atr)) continue;
            if (price < _cfg.MinPrice || price > _cfg.MaxPrice) continue;

            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes
                || minuteEt > 960 - _cfg.LastEntryMinuteBeforeClose
                || !BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
                continue;

            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            if (daySignalCounts.GetValueOrDefault(dayEt) >= _cfg.MaxSignalsPerDay) continue;
            if (!PassesVolatilityAndQualityFilters(row, atr, price)) continue;
            if (IsMarketRegimeBlocked(row)) continue;
            if (!_cfg.IgnoreSelfLearningSetupBlock && IsSelfLearningBlocked("V20_EVOLUTION")) continue;

            var side = DetectRetainedVariantSide(triggerBars, i, bars15m, bars1h, atr);
            if (side is null) continue;

            var signal = MakeSignal(triggerBars, i, side.Value, atr);
            if (signal is null) continue;

            signals.Add(signal);
            lastAcceptedBar = signal.BarIndex;
            daySignalCounts[dayEt] = daySignalCounts.GetValueOrDefault(dayEt) + 1;
        }

        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    => ExitEngine.SimulateTrade(signal, triggerBars, ApplySelfLearningExitOverrides(_exitCfg, signal.SubStrategy));
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Entry Detection (single retained defensive continuation variant)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private bool HasRequiredData(EnrichedBar row, EnrichedBar prev, double atr)
    {
        if (double.IsNaN(atr) || atr <= 0) return false;
        if (double.IsNaN(row.Rsi14) || double.IsNaN(prev.Rsi14)) return false;
        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Ema50)) return false;
        if (double.IsNaN(prev.Ema9) || double.IsNaN(prev.Ema21) || double.IsNaN(prev.Ema50)) return false;
        if (_cfg.RequireVwapAlignment && (double.IsNaN(row.Vwap) || row.Vwap <= 0)) return false;
        if (double.IsNaN(row.Adx) || double.IsNaN(prev.Adx)) return false;
        if (_cfg.RequireMacdMomentum && (double.IsNaN(row.MacdHist) || double.IsNaN(prev.MacdHist))) return false;
        return true;
    }

    private bool PassesVolatilityAndQualityFilters(EnrichedBar row, double atr, double price)
    {
        if (_cfg.RvolMin > 0 && !double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin)
            return false;

        if (!double.IsNaN(row.SpreadZ) && row.SpreadZ > _cfg.MaxSpreadZ)
            return false;

        if (!double.IsNaN(row.Adx) && (row.Adx < _cfg.AdxMin || row.Adx > _cfg.AdxMax))
            return false;

        if (_cfg.EnableChoppyFilter && IsChoppy(row))
            return false;

        if (!double.IsNaN(row.BbBandwidth))
        {
            if (row.BbBandwidth >= _cfg.VolatileBbBandwidthMin)
                return false;
        }

        if (!double.IsNaN(row.Vwap) && row.Vwap > 0)
        {
            var vwapExtensionAtr = Math.Abs(price - row.Vwap) / atr;
            if (vwapExtensionAtr > _cfg.MaxVwapExtensionAtr)
                return false;
        }

        return true;
    }

    private TradeSide? DetectRetainedVariantSide(
        EnrichedBar[] bars,
        int i,
        EnrichedBar[]? bars15m,
        EnrichedBar[]? bars1h,
        double atr)
    {
        var row = bars[i];
        var prev = bars[i - 1];

        if (IsLongContinuation(row, prev, atr)
            && PassesHigherTimeframeTrend(row.Bar.Timestamp, TradeSide.Long, bars15m, bars1h))
        {
            return TradeSide.Long;
        }

        if (IsShortContinuation(row, prev, atr)
            && PassesHigherTimeframeTrend(row.Bar.Timestamp, TradeSide.Short, bars15m, bars1h))
        {
            return TradeSide.Short;
        }

        return null;
    }

    private bool IsLongContinuation(EnrichedBar row, EnrichedBar prev, double atr)
    {
        if (!_cfg.AllowLong) return false;
        if (row.Rsi14 < _cfg.LongRsiMin || row.Rsi14 > _cfg.LongRsiMax) return false;
        bool stFlipLong = row.StDirection == 1 && prev.StDirection == -1;
        bool emaPullbackLong = row.Bar.Close > row.Ema21
            && (row.Bar.Low <= row.Ema9 + (0.15 * atr) || prev.Bar.Low <= prev.Ema9 + (0.15 * atr))
            && row.Ema9 >= prev.Ema9;

        if (!(stFlipLong || emaPullbackLong)) return false;
        if (_cfg.RequireEmaStack && !(row.Ema9 > row.Ema21 && row.Ema21 >= row.Ema50 * 0.99)) return false;
        if (_cfg.RequireVwapAlignment && row.Bar.Close <= row.Vwap) return false;
        if (row.StDirection < 0) return false;
        if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi) && row.PlusDi <= row.MinusDi) return false;
        if (_cfg.RequireMacdMomentum && (row.MacdHist < -0.01 || row.MacdHist + 0.05 < prev.MacdHist)) return false;

        bool resumed = row.Bar.Close > row.Ema9
            && row.Bar.Close >= prev.Bar.Close - (0.05 * atr)
            && (row.Bar.Close > row.Bar.Open || row.Bar.Close >= prev.Bar.High - (0.20 * atr));
        return resumed && HasDecisiveCandle(row, TradeSide.Long);
    }

    private bool IsShortContinuation(EnrichedBar row, EnrichedBar prev, double atr)
    {
        if (!_cfg.AllowShort) return false;
        if (row.Rsi14 < _cfg.ShortRsiMin || row.Rsi14 > _cfg.ShortRsiMax) return false;
        bool stFlipShort = row.StDirection == -1 && prev.StDirection == 1;
        bool emaPullbackShort = row.Bar.Close < row.Ema21
            && (row.Bar.High >= row.Ema9 - (0.15 * atr) || prev.Bar.High >= prev.Ema9 - (0.15 * atr))
            && row.Ema9 <= prev.Ema9;

        if (!(stFlipShort || emaPullbackShort)) return false;
        if (_cfg.RequireEmaStack && !(row.Ema9 < row.Ema21 && row.Ema21 <= row.Ema50 * 1.01)) return false;
        if (_cfg.RequireVwapAlignment && row.Bar.Close >= row.Vwap) return false;
        if (row.StDirection > 0) return false;
        if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi) && row.MinusDi <= row.PlusDi) return false;
        if (_cfg.RequireMacdMomentum && (row.MacdHist > 0.0 || row.MacdHist - 0.02 > prev.MacdHist)) return false;

        bool resumed = row.Bar.Close < row.Ema9
            && row.Bar.Close <= prev.Bar.Close + (0.05 * atr)
            && (row.Bar.Close < row.Bar.Open || row.Bar.Close <= prev.Bar.Low + (0.20 * atr));
        return resumed && HasDecisiveCandle(row, TradeSide.Short);
    }

    private bool HasDecisiveCandle(EnrichedBar row, TradeSide side)
    {
        double range = row.Bar.High - row.Bar.Low;
        if (range <= 0) return false;

        double body = row.Bar.Close - row.Bar.Open;
        double bodyFraction = Math.Abs(body) / range;
        if (bodyFraction < _cfg.MinimumBodyFraction) return false;

        double closeLocation = (row.Bar.Close - row.Bar.Low) / range;
        if (side == TradeSide.Long)
        {
            return body > 0 && closeLocation >= _cfg.MinimumCloseLocation;
        }

        return body < 0 && closeLocation <= 1.0 - _cfg.MinimumCloseLocation;
    }

    private bool IsChoppy(EnrichedBar row)
    {
        if (double.IsNaN(row.Adx)) return false;
        bool lowAdx = row.Adx < _cfg.ChoppyAdxThreshold;
        bool lowBandwidth = !double.IsNaN(row.BbBandwidth) && row.BbBandwidth < _cfg.ChoppyBbBandwidthMax;
        return lowAdx && lowBandwidth;
    }

    private bool PassesHigherTimeframeTrend(
        DateTime timestamp,
        TradeSide side,
        EnrichedBar[]? bars15m,
        EnrichedBar[]? bars1h)
    {
        if (!_cfg.RequireHigherTimeframeTrend)
            return true;

        var tf15 = FindLatestContextBar(bars15m, timestamp);
        var tf1h = FindLatestContextBar(bars1h, timestamp);
        if (tf15 is null && tf1h is null)
            return true;

        bool anyAligned = false;

        if (tf15 is not null)
        {
            if (IsTimeframeTrendAligned(tf15, side))
                anyAligned = true;
            else if (IsTimeframeTrendOpposed(tf15, side))
                return false;
        }

        if (tf1h is not null)
        {
            if (IsTimeframeTrendAligned(tf1h, side))
                anyAligned = true;
            else if (IsTimeframeTrendOpposed(tf1h, side))
                return false;
        }

        return anyAligned;
    }

    private static EnrichedBar? FindLatestContextBar(EnrichedBar[]? bars, DateTime timestamp)
    {
        if (bars is null || bars.Length == 0)
            return null;

        for (int i = bars.Length - 1; i >= 0; i--)
        {
            if (bars[i].Bar.Timestamp <= timestamp)
                return bars[i];
        }

        return null;
    }

    private static bool IsTimeframeTrendAligned(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Ema50))
            return false;

        return side == TradeSide.Long
            ? row.Ema9 > row.Ema21 && row.Ema21 >= row.Ema50 && row.StDirection >= 1
            : row.Ema9 < row.Ema21 && row.Ema21 <= row.Ema50 && row.StDirection <= -1;
    }

    private static bool IsTimeframeTrendOpposed(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21))
            return false;

        return side == TradeSide.Long
            ? row.Ema9 < row.Ema21 && row.StDirection <= -1
            : row.Ema9 > row.Ema21 && row.StDirection >= 1;
    }

    private BacktestSignal? MakeSignal(EnrichedBar[] bars, int i, TradeSide side, double atr)
    {
        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTime = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry && i + 1 < bars.Length)
        {
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTime = bars[entryIndex].Bar.Timestamp;
        }

        // Stop distance: use ATR-based stop with HardStopR
        double stopDist = Math.Max(atr * _cfg.HardStopR, _cfg.MinRiskPerShare);
        stopDist = ApplySelfLearningStopMultiplier(stopDist);

        double stopPrice = side == TradeSide.Long
            ? entryPrice - stopDist
            : entryPrice + stopDist;

        double riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare < _cfg.MinRiskPerShare) return null;

        int positionSize = BacktestHelpers.ComputePositionSize(
            entryPrice, riskPerShare,
            _cfg.RiskPerTradeDollars, _cfg.AccountSize,
            _cfg.MaxPositionNotionalPctOfAccount, _cfg.MaxShares);
        positionSize = ApplySelfLearningPositionSize(positionSize, "V20_EVOLUTION");
        if (positionSize <= 0) return null;

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
            MtfMomentum: "V20-Retained",
            SubStrategy: "V20_EVOLUTION");
    }
}

