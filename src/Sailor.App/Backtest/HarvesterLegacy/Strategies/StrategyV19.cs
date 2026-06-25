using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// StrategyV19 â€” Retained Purple Cloud Breakdown Profile
//
// Retained default on the active 52-symbol small-cap basket:
//   - short-only
//   - breakout / breakdown continuation only
//   - disables pullback and long branches by default
//
// The broader bidirectional and basket-specific small-cap variants overtraded
// and lost materially more on the current active basket.
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

public sealed class V19Config
{
    // â”€â”€ Account / Position Sizing â”€â”€
    public double RiskPerTradeDollars { get; init; } = 20.0;
    public double AccountSize { get; init; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; init; } = 0.18;
    public int MaxShares { get; init; } = 6_500;
    public double MinRiskPerShare { get; init; } = 0.05;

    // â”€â”€ Direction â”€â”€
    public bool AllowLong { get; init; } = false;
    public bool AllowShort { get; init; } = true;
    public bool UseNextBarOpenEntry { get; init; } = true;
    public bool PullbackEnabled { get; init; } = false;
    public bool BreakoutEnabled { get; init; } = true;

    // â”€â”€ Throttle â”€â”€
    public int CooldownBars { get; init; } = 8;
    public int MaxSignalsPerDay { get; init; } = 3;
    public int ShortMaxSignalsPerDay { get; init; } = 1;

    // â”€â”€ Filters â”€â”€
    public double MinPrice { get; init; } = 0.3;
    public double MaxPrice { get; init; } = 700.0;
    public double RvolMin { get; init; } = 0.80;
    public double StrongRvolMin { get; init; } = 1.10;
    public double SpreadZMax { get; init; } = 3.5;
    public double L2LiquidityMin { get; init; } = 0.0;

    // â”€â”€ Time Windows â”€â”€
    public int MarketOpenMinute { get; init; } = 570;
    public int SkipFirstNMinutes { get; init; } = 12;
    public int LastEntryMinuteBeforeClose { get; init; } = 50;
    public int ShortEarliestMinuteEt { get; init; } = 610;
    public (int Start, int End)[] EntryWindows { get; init; } = [(582, 930)];

    // â”€â”€ Purple Cloud (dual-EMA cloud) â”€â”€
    // Lead1 = avg(EMA9, EMA21);   Lead2 = avg(EMA50, SMMA145)
    // Cloud bullish when Lead1 > Lead2, bearish otherwise.
    public double CloudMinSeparationAtr { get; init; } = 0.08;

    // â”€â”€ EMA Difference momentum (EMA8 âˆ’ EMA21) â”€â”€
    public int EmaDiffShortPeriod { get; init; } = 8;
    public int EmaDiffLongPeriod { get; init; } = 21;
    public double EmaDiffMinLong { get; init; } = 0.02;
    public double EmaDiffMaxShort { get; init; } = -0.02;

    // â”€â”€ SMMA(145) trend filter â”€â”€
    public int SmmaPeriod { get; init; } = 145;

    // â”€â”€ ATR Bands (volatility envelope) â”€â”€
    public int AtrBandsPeriod { get; init; } = 14;
    public double AtrBandsMultiplierUpper { get; init; } = 2.0;
    public double AtrBandsMultiplierLower { get; init; } = 2.0;
    // Guard: reject entries already beyond the ATR band
    public double AtrBandExtensionGuardAtr { get; init; } = 0.15;

    // â”€â”€ Botify ATR stop â”€â”€
    public double BotifyStopAtrMultiplier { get; init; } = 1.35;
    public double MinStopCents { get; init; } = 5.0;

    // â”€â”€ Macro Regime Score (simplified X-Trend-inspired) â”€â”€
    // Score 0-100: low = risk-on, high = risk-off.
    // Uses: RSI, ADX, BB bandwidth, Donchian position, MFI.
    public double MacroRiskOffThreshold { get; init; } = 72.0;
    public bool EnableMacroFilter { get; init; } = true;

    // â”€â”€ Trend / Momentum Confluence â”€â”€
    public double AdxMin { get; init; } = 14.0;
    public double AdxMax { get; init; } = 55.0;
    public double LongRsiMin { get; init; } = 44.0;
    public double LongRsiMax { get; init; } = 70.0;
    public double ShortRsiMin { get; init; } = 30.0;
    public double ShortRsiMax { get; init; } = 56.0;
    public double LongDcPctMin { get; init; } = 0.45;
    public double ShortDcPctMax { get; init; } = 0.55;

    // â”€â”€ Pullback Setup â”€â”€
    public int PullbackLookbackBars { get; init; } = 14;
    public double PullbackToCloudEdgeAtr { get; init; } = 0.40;
    public double PullbackToVwapAtr { get; init; } = 0.35;

    // â”€â”€ Breakout Setup â”€â”€
    public int BreakoutLookbackBars { get; init; } = 18;
    public double BreakoutBufferAtr { get; init; } = 0.06;
    public double MaxBreakoutExtensionAtr { get; init; } = 0.55;

    // â”€â”€ Exit Profile â”€â”€
    public double HardStopR { get; init; } = 1.00;
    public double BreakevenR { get; init; } = 0.65;
    public double TrailR { get; init; } = 0.15;
    public double GivebackPct { get; init; } = 0.60;
    public double GivebackUsdCap { get; init; } = 22.0;
    public double Tp1R { get; init; } = 0.73;
    public double Tp2R { get; init; } = 2.04;
    public int MaxHoldBars { get; init; } = 50;
    public double SlippageCents { get; init; } = 0.8;
    public double CommissionPerShare { get; init; } = 0.005;

    // â”€â”€ Diagnostics â”€â”€
    public bool EnableDiagnostics { get; init; } = false;
    public string DiagnosticsLabel { get; init; } = "retained-breakout";
}

public sealed class StrategyV19 : BacktestStrategyBase
{
    private readonly V19Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;
    private readonly V19Diagnostics _diag;

    // Pre-computed custom indicator arrays (computed once per GenerateSignals call).
    private double[] _smma145 = [];
    private double[] _ema8 = [];
    private double[] _ema21Custom = [];
    private double[] _cloudLead1 = [];
    private double[] _cloudLead2 = [];
    private double[] _atrBandUpper = [];
    private double[] _atrBandLower = [];
    private double[] _emaDiff = [];

    public StrategyV19(V19Config? cfg = null)
    {
        _cfg = cfg ?? new V19Config();
        if (!_cfg.AllowLong && !_cfg.AllowShort)
            throw new InvalidOperationException("V19: at least one direction must be enabled.");
        if (!_cfg.PullbackEnabled && !_cfg.BreakoutEnabled)
            throw new InvalidOperationException("V19: at least one setup family must be enabled.");
        _diag = new V19Diagnostics(
            _cfg.DiagnosticsLabel,
            _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.18,
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
            EmaTrailBufferAtr = 0.16,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = 0.45,
            PeakGivebackActivateR = 0.30,
            FlattenOnStagnation = true,
            StagnationBars = 8,
            StagnationMinPeakR = 0.14,
            StagnationMaxAdverseR = -0.08,
        };
    }

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
        ComputeCustomIndicators(triggerBars);

        var signals = new List<BacktestSignal>();
        var daySignalCounts = new Dictionary<DateOnly, int>();
        var dayShortSignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBar = -10_000;

        int startIndex = Math.Max(_cfg.BreakoutLookbackBars + 2, _cfg.SmmaPeriod + 2);
        for (int i = startIndex; i < triggerBars.Length - 1; i++)
        {
            var row = triggerBars[i];
            _diag.RawScanned++;

            // â”€â”€ Core data gate â”€â”€
            if (!HasCoreData(row, i))
            {
                _diag.Reject("core-data-missing");
                continue;
            }

            double price = row.Bar.Close;
            double atr = row.Atr14;

            // â”€â”€ Price filter â”€â”€
            if (price < _cfg.MinPrice || price > _cfg.MaxPrice)
            {
                _diag.Reject("price");
                continue;
            }

            // â”€â”€ Relative volume â”€â”€
            if (row.Rvol < _cfg.RvolMin)
            {
                _diag.Reject("rvol");
                continue;
            }

            // â”€â”€ Spread health â”€â”€
            if (!double.IsNaN(row.SpreadZ) && row.SpreadZ > _cfg.SpreadZMax)
            {
                _diag.Reject("spread");
                continue;
            }

            // â”€â”€ L2 liquidity â”€â”€
            if (!double.IsNaN(row.L2Liquidity) && row.L2Liquidity < _cfg.L2LiquidityMin)
            {
                _diag.Reject("l2-liquidity");
                continue;
            }

            // â”€â”€ Time gates â”€â”€
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

            // â”€â”€ Cooldown â”€â”€
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

            // â”€â”€ ADX trend-strength gate â”€â”€
            if (row.Adx < _cfg.AdxMin || row.Adx > _cfg.AdxMax)
            {
                _diag.Reject("adx");
                continue;
            }

            // â”€â”€ Macro regime filter (X-Trend inspired) â”€â”€
            if (_cfg.EnableMacroFilter)
            {
                double macroRisk = ComputeMacroRiskScore(row);
                if (macroRisk > _cfg.MacroRiskOffThreshold)
                {
                    _diag.Reject("macro-risk-off");
                    continue;
                }
            }

            if (IsMarketRegimeBlocked(row))
            {
                _diag.Reject("market-regime-blocked");
                continue;
            }

            // â”€â”€ Try sub-strategies â”€â”€
            BacktestSignal? signal = null;
            if (_cfg.AllowLong)
            {
                if (_cfg.PullbackEnabled)
                {
                    signal = TryCloudPullbackLong(triggerBars, i, atr);
                }

                if (signal is null && _cfg.BreakoutEnabled)
                {
                    signal = TryCloudBreakoutLong(triggerBars, i, atr);
                }
            }

            if (signal is null
                && _cfg.AllowShort
                && minuteEt >= _cfg.ShortEarliestMinuteEt
                && dayShortSignalCounts.GetValueOrDefault(dayEt) < _cfg.ShortMaxSignalsPerDay)
            {
                if (_cfg.PullbackEnabled)
                {
                    signal = TryCloudPullbackShort(triggerBars, i, atr);
                }

                if (signal is null && _cfg.BreakoutEnabled)
                {
                    signal = TryCloudBreakoutShort(triggerBars, i, atr);
                }
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
        var result = ExitEngine.SimulateTrade(signal, triggerBars, ApplySelfLearningExitOverrides(_exitCfg, signal.SubStrategy));
        if (result != null)
        {
            _diag.ObserveTrade(result);
        }

        return result;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Custom Indicator Computation
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private void ComputeCustomIndicators(EnrichedBar[] bars)
    {
        int n = bars.Length;
        double[] closes = new double[n];
        for (int i = 0; i < n; i++)
            closes[i] = bars[i].Bar.Close;

        // EMA(8) for EMA-difference momentum
        _ema8 = ComputeEma(closes, _cfg.EmaDiffShortPeriod);

        // EMA(21) â€” separate computation for EMA-difference (EnrichedBar.Ema21 is used too)
        _ema21Custom = ComputeEma(closes, _cfg.EmaDiffLongPeriod);

        // EMA Difference = EMA(8) âˆ’ EMA(21)
        _emaDiff = new double[n];
        for (int i = 0; i < n; i++)
            _emaDiff[i] = _ema8[i] - _ema21Custom[i];

        // SMMA(145) â€” Smoothed Moving Average (Wilder's method: alpha = 1/period)
        _smma145 = ComputeSmma(closes, _cfg.SmmaPeriod);

        // Purple Cloud leads:
        //   Lead1 = avg(EMA9, EMA21) â€” fast ribbon
        //   Lead2 = avg(EMA50, SMMA145) â€” slow ribbon
        _cloudLead1 = new double[n];
        _cloudLead2 = new double[n];
        for (int i = 0; i < n; i++)
        {
            _cloudLead1[i] = (bars[i].Ema9 + bars[i].Ema21) / 2.0;
            _cloudLead2[i] = (bars[i].Ema50 + _smma145[i]) / 2.0;
        }

        // ATR Bands = EMA21 Â± multiplier * ATR14
        _atrBandUpper = new double[n];
        _atrBandLower = new double[n];
        for (int i = 0; i < n; i++)
        {
            double basis = bars[i].Ema21;
            double atr = bars[i].Atr14;
            _atrBandUpper[i] = basis + _cfg.AtrBandsMultiplierUpper * atr;
            _atrBandLower[i] = basis - _cfg.AtrBandsMultiplierLower * atr;
        }
    }

    /// <summary>Standard EMA: alpha = 2 / (period + 1).</summary>
    private static double[] ComputeEma(double[] series, int period)
    {
        double[] result = new double[series.Length];
        if (series.Length == 0) return result;
        double alpha = 2.0 / (period + 1);
        result[0] = series[0];
        for (int i = 1; i < series.Length; i++)
            result[i] = alpha * series[i] + (1.0 - alpha) * result[i - 1];
        return result;
    }

    /// <summary>
    /// Smoothed Moving Average (Wilder's method): alpha = 1 / period.
    /// Same family as ATR's smoothing â€” slower than EMA of same period.
    /// </summary>
    private static double[] ComputeSmma(double[] series, int period)
    {
        double[] result = new double[series.Length];
        if (series.Length < period) return result;

        // Seed with SMA of first `period` bars
        double sum = 0;
        for (int i = 0; i < period; i++)
        {
            sum += series[i];
            result[i] = double.NaN;
        }

        result[period - 1] = sum / period;

        // Wilder smoothing from period onward
        for (int i = period; i < series.Length; i++)
            result[i] = (result[i - 1] * (period - 1) + series[i]) / period;

        return result;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Macro Regime Score (X-Trend Macro Command Center â€” Adapted)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //
    // Simplified to work with the indicators already on each EnrichedBar.
    // Score 0â€“100.  Low = risk-on (favorable for entries).  High = risk-off.
    //
    //  Component         Weight   Description
    //  â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ â”€â”€â”€â”€â”€â”€   â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  Inflation proxy    0.25    BB bandwidth: extreme expansion â‡’ risk
    //  Rate proxy         0.20    ADX level: very high trend â‡’ risk
    //  Momentum drain     0.20    RSI deviation from 50 (extremes â‡’ risk)
    //  Velocity proxy     0.20    Donchian position: strongly pinned â‡’ risk
    //  Flow health        0.15    MFI extremes â‡’ risk
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private static double ComputeMacroRiskScore(EnrichedBar row)
    {
        // BB bandwidth expansion (>6% is stressed)
        double bwScore = double.IsNaN(row.BbBandwidth)
            ? 50.0
            : Clamp01(row.BbBandwidth / 0.08) * 100.0;

        // ADX: very high or very low both imply regime stress
        double adxScore = double.IsNaN(row.Adx)
            ? 50.0
            : Clamp01(Math.Abs(row.Adx - 25) / 30.0) * 100.0;

        // RSI extremity
        double rsiScore = double.IsNaN(row.Rsi14)
            ? 50.0
            : Clamp01(Math.Abs(row.Rsi14 - 50) / 30.0) * 100.0;

        // Donchian position â€” pinned near 0 or 1 is risky for new entries
        double dcScore = double.IsNaN(row.DcPct)
            ? 50.0
            : Clamp01(Math.Abs(row.DcPct - 0.5) / 0.45) * 100.0;

        // MFI extremes
        double mfiScore = double.IsNaN(row.Mfi14)
            ? 50.0
            : Clamp01(Math.Abs(row.Mfi14 - 50) / 35.0) * 100.0;

        return bwScore * 0.25
             + adxScore * 0.20
             + rsiScore * 0.20
             + dcScore * 0.20
             + mfiScore * 0.15;
    }

    private static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Sub-Strategies
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// Long pullback inside a bullish purple cloud:
    ///   - Cloud bullish (Lead1 > Lead2) with separation
    ///   - Price pulled back to the fast cloud edge (Lead1) or VWAP
    ///   - EMA difference (8âˆ’21) positive â‡’ momentum with trend
    ///   - Price above SMMA(145) â‡’ long-term trend intact
    ///   - Price not extended beyond ATR upper band
    ///   - Reclaim candle: close above EMA9 and VWAP, bullish body
    /// </summary>
    private BacktestSignal? TryCloudPullbackLong(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];

        // Cloud bullish with minimum separation
        double lead1 = _cloudLead1[i];
        double lead2 = _cloudLead2[i];
        if (double.IsNaN(lead1) || double.IsNaN(lead2))
            return null;
        if (lead1 - lead2 < _cfg.CloudMinSeparationAtr * atr)
            return null;

        // SMMA(145) trend filter â€” price above long-term baseline
        double smma = _smma145[i];
        if (double.IsNaN(smma) || row.Bar.Close < smma)
            return null;

        // EMA difference momentum â€” must be positive
        if (_emaDiff[i] < _cfg.EmaDiffMinLong * atr)
            return null;

        // ATR band extension guard â€” not already at upper extreme
        if (row.Bar.Close > _atrBandUpper[i] + _cfg.AtrBandExtensionGuardAtr * atr)
            return null;

        // Pullback toward cloud fast edge (Lead1) or VWAP
        bool pullback = row.Bar.Low <= lead1 + (_cfg.PullbackToCloudEdgeAtr * atr)
            || Math.Abs(row.Bar.Low - row.Vwap) <= (_cfg.PullbackToVwapAtr * atr);
        if (!pullback)
            return null;

        // Directional momentum confluence
        if (row.Rsi14 < _cfg.LongRsiMin || row.Rsi14 > _cfg.LongRsiMax)
            return null;
        if (row.MacdHist < bars[i - 1].MacdHist)
            return null;
        if (row.DcPct < _cfg.LongDcPctMin)
            return null;

        // Reclaim candle
        if (row.Bar.Close < row.Ema9 || row.Bar.Close < row.Vwap || row.Bar.Close <= row.Bar.Open)
            return null;

        double anchor = LowestLow(bars, i - _cfg.PullbackLookbackBars, i);
        return MakeSignal(bars, i, TradeSide.Long, anchor, "V19_PL_PULL");
    }

    /// <summary>
    /// Short pullback inside a bearish purple cloud:
    ///   - Cloud bearish (Lead2 > Lead1)
    ///   - Price rallied back to the fast cloud edge (Lead1) or VWAP
    ///   - EMA difference negative â‡’ downward momentum
    ///   - Price below SMMA(145)
    ///   - Price not extended beyond ATR lower band
    ///   - Rejection candle: close below EMA9 and VWAP, bearish body
    /// </summary>
    private BacktestSignal? TryCloudPullbackShort(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];

        // Cloud bearish with separation
        double lead1 = _cloudLead1[i];
        double lead2 = _cloudLead2[i];
        if (double.IsNaN(lead1) || double.IsNaN(lead2))
            return null;
        if (lead2 - lead1 < _cfg.CloudMinSeparationAtr * atr)
            return null;

        // SMMA(145) â€” price below long-term baseline
        double smma = _smma145[i];
        if (double.IsNaN(smma) || row.Bar.Close > smma)
            return null;

        // EMA difference negative
        if (_emaDiff[i] > _cfg.EmaDiffMaxShort * atr)
            return null;

        // ATR band extension guard â€” not at lower extreme
        if (row.Bar.Close < _atrBandLower[i] - _cfg.AtrBandExtensionGuardAtr * atr)
            return null;

        // Rally toward cloud fast edge (Lead1) or VWAP
        bool bounce = row.Bar.High >= lead1 - (_cfg.PullbackToCloudEdgeAtr * atr)
            || Math.Abs(row.Bar.High - row.Vwap) <= (_cfg.PullbackToVwapAtr * atr);
        if (!bounce)
            return null;

        // Momentum
        if (row.Rsi14 < _cfg.ShortRsiMin || row.Rsi14 > _cfg.ShortRsiMax)
            return null;
        if (row.MacdHist > bars[i - 1].MacdHist)
            return null;
        if (row.DcPct > _cfg.ShortDcPctMax)
            return null;

        // Rejection candle
        if (row.Bar.Close > row.Ema9 || row.Bar.Close > row.Vwap || row.Bar.Close >= row.Bar.Open)
            return null;

        double anchor = HighestHigh(bars, i - _cfg.PullbackLookbackBars, i);
    return MakeSignal(bars, i, TradeSide.Short, anchor, "V19_PS_PULL");
    }

    /// <summary>
    /// Long breakout through a flattening or newly-bullish cloud:
    ///   - Close breaks above the slow cloud edge (Lead2) and prior range high
    ///   - EMA difference accelerating positive
    ///   - SMMA(145) is flat-to-rising
    ///   - Strong relative volume
    /// </summary>
    private BacktestSignal? TryCloudBreakoutLong(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];

        double lead2 = _cloudLead2[i];
        if (double.IsNaN(lead2))
            return null;

        // Cloud breakout: close must be above slow cloud and cloud just turned or is thin
        double lead1 = _cloudLead1[i];
        if (double.IsNaN(lead1))
            return null;
        // Require price to be above lead2 (breaking upward through the cloud)
        if (row.Bar.Close < lead2)
            return null;

        // SMMA(145) â€” at least flat or rising
        double smma = _smma145[i];
        double smmaPrev = i > 0 ? _smma145[i - 1] : double.NaN;
        if (double.IsNaN(smma) || double.IsNaN(smmaPrev) || smma < smmaPrev - 0.001)
            return null;

        // Prior range breakout
        double priorHigh = HighestHigh(bars, i - _cfg.BreakoutLookbackBars, i - 1);
        if (row.Bar.Close < priorHigh - (_cfg.BreakoutBufferAtr * atr))
            return null;
        if (row.Bar.Close > priorHigh + (_cfg.MaxBreakoutExtensionAtr * atr))
            return null;

        // ATR band guard
        if (row.Bar.Close > _atrBandUpper[i] + _cfg.AtrBandExtensionGuardAtr * atr)
            return null;

        // EMA difference positive and accelerating
        if (_emaDiff[i] < _cfg.EmaDiffMinLong * atr)
            return null;
        if (i > 0 && _emaDiff[i] < _emaDiff[i - 1])
            return null;

        // Confirmation: strong volume, above VWAP, MACD positive
        if (row.Rvol < _cfg.StrongRvolMin)
            return null;
        if (row.Bar.Close < row.Vwap)
            return null;
        if (row.MacdHist <= 0)
            return null;

        double anchor = LowestLow(bars, i - _cfg.PullbackLookbackBars, i);
        return MakeSignal(bars, i, TradeSide.Long, anchor, "V19_PL_BREAK");
    }

    /// <summary>
    /// Short breakdown through a flattening or newly-bearish cloud:
    ///   - Close breaks below the slow cloud edge (Lead2) and prior range low
    ///   - EMA difference accelerating negative
    ///   - SMMA(145) flat-to-falling
    ///   - Strong relative volume
    /// </summary>
    private BacktestSignal? TryCloudBreakoutShort(EnrichedBar[] bars, int i, double atr)
    {
        var row = bars[i];

        double lead2 = _cloudLead2[i];
        if (double.IsNaN(lead2))
            return null;

        double lead1 = _cloudLead1[i];
        if (double.IsNaN(lead1))
            return null;
        // Price must be below lead2 (breaking down through cloud)
        if (row.Bar.Close > lead2)
            return null;

        // SMMA(145) â€” at least flat or falling
        double smma = _smma145[i];
        double smmaPrev = i > 0 ? _smma145[i - 1] : double.NaN;
        if (double.IsNaN(smma) || double.IsNaN(smmaPrev) || smma > smmaPrev + 0.001)
            return null;

        // Prior range breakdown
        double priorLow = LowestLow(bars, i - _cfg.BreakoutLookbackBars, i - 1);
        if (row.Bar.Close > priorLow + (_cfg.BreakoutBufferAtr * atr))
            return null;
        if (row.Bar.Close < priorLow - (_cfg.MaxBreakoutExtensionAtr * atr))
            return null;

        // ATR band guard
        if (row.Bar.Close < _atrBandLower[i] - _cfg.AtrBandExtensionGuardAtr * atr)
            return null;

        // EMA difference negative and accelerating
        if (_emaDiff[i] > _cfg.EmaDiffMaxShort * atr)
            return null;
        if (i > 0 && _emaDiff[i] > _emaDiff[i - 1])
            return null;

        // Confirmation
        if (row.Rvol < _cfg.StrongRvolMin)
            return null;
        if (row.Bar.Close > row.Vwap)
            return null;
        if (row.MacdHist >= 0)
            return null;

        double anchor = HighestHigh(bars, i - _cfg.PullbackLookbackBars, i);
        return MakeSignal(bars, i, TradeSide.Short, anchor, "V19_PS_BREAK");
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Signal Builder â€” Botify-style ATR stop
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

        // Botify-style ATR stop: multiplier * ATR from anchor, clamped to minimum
        double stopDist = Math.Max(atr * _cfg.BotifyStopAtrMultiplier, _cfg.MinStopCents / 100.0);
        stopDist = ApplySelfLearningStopMultiplier(stopDist);
        double stopPrice = side == TradeSide.Long
            ? Math.Min(entryPrice - _cfg.MinRiskPerShare, anchor - (0.12 * atr))
            : Math.Max(entryPrice + _cfg.MinRiskPerShare, anchor + (0.12 * atr));

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
            return null;

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
            MtfMomentum: "PurpleCloud",
            SubStrategy: subStrategy);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Helpers
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private bool HasCoreData(EnrichedBar row, int i)
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
            && !double.IsNaN(row.Adx)
            && !double.IsNaN(_smma145[i]);
    }

    private static double HighestHigh(EnrichedBar[] bars, int start, int end)
    {
        double value = double.MinValue;
        for (int i = Math.Max(0, start); i <= Math.Min(end, bars.Length - 1); i++)
            value = Math.Max(value, bars[i].Bar.High);
        return value;
    }

    private static double LowestLow(EnrichedBar[] bars, int start, int end)
    {
        double value = double.MaxValue;
        for (int i = Math.Max(0, start); i <= Math.Min(end, bars.Length - 1); i++)
            value = Math.Min(value, bars[i].Bar.Low);
        return value;
    }

    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V19");

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Diagnostics
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private sealed class V19Diagnostics
    {
        private readonly string _label;
        private readonly bool _enabled;
        private readonly Dictionary<string, int> _rejections = new(StringComparer.OrdinalIgnoreCase);

        public int RawScanned;
        public int Accepted;
        public int AcceptedLong;
        public int AcceptedShort;

        public V19Diagnostics(string label, bool enabled)
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
            Console.WriteLine($"[V19-DIAG:{_label}] scanned={RawScanned} accepted={Accepted} long={AcceptedLong} short={AcceptedShort}");
            if (_rejections.Count > 0)
            {
                Console.WriteLine($"[V19-DIAG:{_label}] rejects {string.Join(", ", _rejections.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}"))}");
            }
        }
    }
}

