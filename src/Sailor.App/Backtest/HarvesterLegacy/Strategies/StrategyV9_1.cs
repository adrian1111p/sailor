using Sailor.App.Backtest.Engine;
using Harvester.App.Strategy;

namespace Sailor.App.Backtest.Strategies;

/// <summary>
/// V9 config (score-based L1/L2).
/// "_1" version includes:
/// - Exchange-time (America/New_York) aware entry windows
/// - Conservative missing-data policy (NaN fails critical filters used in scoring)
/// - Persistent cooldown based on time (works across repeated GenerateSignals calls)
/// - Directional MTF alignment (no ambiguous trendUp/trendDown boolean coupling)
/// - Optional next-bar-open entry modeling
/// - Position sizing caps using AccountSize
/// - Consistent commission deduction
/// </summary>
public sealed class V9Config_1
{
    public double RiskPerTradeDollars { get; set; } = 40.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;

    /// <summary>Cap position notional as % of AccountSize (0..1).</summary>
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;

    /// <summary>Absolute hard cap on shares.</summary>
    public int MaxShares { get; set; } = 6_500;

    /// <summary>Minimum risk per share to avoid unrealistic huge sizing when stop is too tight.</summary>
    public double MinRiskPerShare { get; set; } = 0.01;

    /// <summary>
    /// If true: signal computed on bar i close, but entry assumed at bar i+1 open (more realistic).
    /// If false: entry at bar i close (legacy behavior).
    /// </summary>
    public bool UseNextBarOpenEntry { get; set; } = true;

    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;

    public double RvolMin { get; set; } = 0.5;
    public double L2LiquidityMin { get; set; } = 10.0;
    public double SpreadZMax { get; set; } = 3.0;
    public double MinVolAccel { get; set; } = -0.50;
    public double OfiSignalThreshold { get; set; } = 0.00;

    public double PullbackToEma9Atr { get; set; } = 0.30;
    public double MaxVwapDistAtr { get; set; } = 0.80;
    public double AdxMin { get; set; } = 0.0;
    public double AdxMax { get; set; } = 0.0;
    public bool UseTrendFilter { get; set; } = true;
    public bool RequirePullback { get; set; } = true;
    public int MinEntryScore { get; set; } = 6;
    public int SwingLookback { get; set; } = 4;
    public bool UsePearlL1L2BorderlineGate { get; set; } = false;
    public int BorderlineScoreWindow { get; set; } = 1;
    public bool RequireStrictBookConfirmation { get; set; } = true;
    public int StrictBookConfirmationBonus { get; set; } = 1;
    public int RequiredConsecutiveTrendBarsBeforeEntry { get; set; } = 2;
    public bool EnableDiagnostics { get; set; } = false;
    public double L2ImbalanceMinForLong { get; set; } = 1.05;
    public double L2ImbalanceMaxForShort { get; set; } = 0.95;
    public double L2DeepImbalanceMinForLong { get; set; } = 1.10;
    public double L2DeepImbalanceMaxForShort { get; set; } = 0.90;
    public double L1SizeRatioMinForLong { get; set; } = 1.05;
    public double L1SizeRatioMaxForShort { get; set; } = 0.95;

    /// <summary>Cooldown in minutes for 1m trigger bars.</summary>
    public int CooldownBars { get; set; } = 2;

    public bool RequireHtfBias { get; set; } = true;
    public bool RequireMtfAlign { get; set; } = false;

    public int SkipFirstNMinutes { get; set; } = 5;

    /// <summary>Market open minute-of-day in ET. Default 09:30 => 570.</summary>
    public int MarketOpenMinute { get; set; } = 570;

    public (int Start, int End)[] EntryWindows { get; set; } =
        [(575, 690), (780, 955)];

    public double RsiMinLong { get; set; } = 36.0;
    public double RsiMaxLong { get; set; } = 72.0;
    public double RsiMinShort { get; set; } = 28.0;
    public double RsiMaxShort { get; set; } = 64.0;

    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 0.55;
    public double TrailR { get; set; } = 0.45;
    public double GivebackPct { get; set; } = 0.35;
    public bool UseFixedGivebackUsdCap { get; set; } = true;
    public double GivebackUsdCap { get; set; } = 30.0;
    public double Tp1R { get; set; } = 1.0;
    public double Tp2R { get; set; } = 2.1;
    public bool UseTrailingTp2 { get; set; } = false;
    public double TrailingTp2AtrMultiplier { get; set; } = 0.50;
    public int MaxHoldBars { get; set; } = 50;
    public bool UseL1L2DecisionOnOppositeBarsFlatten { get; set; } = false;

    public bool ReversalFlatten { get; set; } = true;
    public double MicroTrailCents { get; set; } = 2.5;
    public double MicroTrailActivateCents { get; set; } = 4.0;
    public bool FlattenOnEntryLossCross { get; set; } = false;
    public double EntryLossBufferCents { get; set; } = 0.0;
    public bool FlattenOnPeakGiveback { get; set; } = false;
    public double PeakGivebackKeepFraction { get; set; } = 0.50;
    public double PeakGivebackActivateR { get; set; } = 0.30;
    public bool UsePriceTierStopFloor { get; set; } = false;

    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

/// <summary>
/// Phase 6.15 â€” FROZEN strategy. Retained for historical/regression comparison and explicit selection only;
/// excluded from the default/active comparison plans. Superseded by Conduct-V3. Trade conduct is unchanged.
/// </summary>
[FrozenStrategy(supersededBy: "Conduct-V3", reason: "Early sealing-yacht lineage superseded by Conduct-V3 entry/exit conduct.")]
public sealed class StrategyV9_1 : BacktestStrategyBase, IBacktestDiagnosticsProvider
{
    private readonly V9Config_1 _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;
    private readonly V9Diagnostics _diagnostics = new();

    private bool DiagnosticsEnabled => _cfg.EnableDiagnostics || StrategyDiagnosticsEnvironment.IsEnabled("V9_1");

    // Persistent cooldown across repeated calls (live-like use)
    private DateTime _lastSignalEt = DateTime.MinValue;
    private DateTime _lastSeriesEndUtc = DateTime.MinValue;
    private string? _lastSeriesFingerprint;

    public StrategyV9_1(V9Config_1? cfg = null)
    {
        _cfg = cfg ?? new V9Config_1();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.20,
            UseFixedGivebackUsdCap = _cfg.UseFixedGivebackUsdCap,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            UseTrailingTp2 = _cfg.UseTrailingTp2,
            TrailingTp2AtrMultiplier = _cfg.TrailingTp2AtrMultiplier,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true, // fixed: deduct commission consistently
            Tp1TightenToBe = true,
            ReversalFlatten = _cfg.ReversalFlatten,
            MicroTrail = true,
            MicroTrailCents = _cfg.MicroTrailCents,
            MicroTrailActivateCents = _cfg.MicroTrailActivateCents,
            FlattenOnEntryLossCross = _cfg.FlattenOnEntryLossCross,
            EntryLossBufferCents = _cfg.EntryLossBufferCents,
            FlattenOnPeakGiveback = _cfg.FlattenOnPeakGiveback,
            PeakGivebackKeepFraction = _cfg.PeakGivebackKeepFraction,
            PeakGivebackActivateR = _cfg.PeakGivebackActivateR,
            UsePriceTierStopFloor = _cfg.UsePriceTierStopFloor,
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
        var signals = new List<BacktestSignal>();
        if (triggerBars.Length < 7) return signals;

        ResetCooldownForRewoundSeries(triggerBars);

        for (int i = 7; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            var prev = triggerBars[i - 1];
            var latestRejectReason = string.Empty;
            var latestRejectStage = string.Empty;

            if (DiagnosticsEnabled)
                _diagnostics.ObserveEvaluated();

            var rowEt = TradingTime.ToEt(row.Bar.Timestamp);
            if (_lastSignalEt != DateTime.MinValue)
            {
                // Cooldown interpreted as minutes for 1m trigger bars (works even if window slides).
                if ((rowEt - _lastSignalEt).TotalMinutes < _cfg.CooldownBars)
                {
                    RecordReject("pre-entry-guard", "cooldown-active");
                    continue;
                }
            }

            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0)
            {
                RecordReject("pre-entry-guard", "atr-invalid");
                continue;
            }

            if (row.Bar.Close < _cfg.MinPrice || row.Bar.Close > _cfg.MaxPrice)
            {
                RecordReject("pre-entry-guard", "price-outside-range");
                continue;
            }

            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
            {
                RecordReject("pre-entry-guard", "market-open-delay");
                continue;
            }
            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
            {
                RecordReject("pre-entry-guard", "entry-window-closed");
                continue;
            }

            // Compute HTF bias per-bar (no lookahead)
            string htfBias = ComputeHtfBias(row.Bar.Timestamp, bars1h, bars1d);

            // ---- Conservative missing-data policy for critical filters ----
            if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin)
            {
                RecordReject("shared-filter", "rvol-below-min");
                continue;
            }
            if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin)
            {
                RecordReject("shared-filter", "l2-liquidity-below-min");
                continue;
            }
            if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax)
            {
                RecordReject("shared-filter", "spread-z-too-high");
                continue;
            }
            if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.MinVolAccel)
            {
                RecordReject("shared-filter", "vol-accel-below-min");
                continue;
            }

            if (!double.IsNaN(row.Adx))
            {
                if (_cfg.AdxMin > 0 && row.Adx < _cfg.AdxMin)
                {
                    RecordReject("shared-filter", "adx-below-min");
                    continue;
                }

                if (_cfg.AdxMax > 0 && row.Adx > _cfg.AdxMax)
                {
                    RecordReject("shared-filter", "adx-above-max");
                    continue;
                }
            }

            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21))
            {
                RecordReject("shared-filter", "ema-missing");
                continue;
            }

            // VWAP distance guard (conservative: NaN VWAP fails this guard)
            if (double.IsNaN(row.Vwap) || row.Vwap <= 0)
            {
                RecordReject("shared-filter", "vwap-missing");
                continue;
            }
            double vwapDistAtr = Math.Abs(row.Bar.Close - row.Vwap) / atr;
            if (vwapDistAtr > _cfg.MaxVwapDistAtr)
            {
                RecordReject("shared-filter", "vwap-distance-too-far");
                continue;
            }

            bool ofiBull = !double.IsNaN(row.OfiSignal)
                ? row.OfiSignal >= _cfg.OfiSignalThreshold
                : row.Bar.Close >= prev.Bar.Close;

            bool ofiBear = !double.IsNaN(row.OfiSignal)
                ? row.OfiSignal <= -_cfg.OfiSignalThreshold
                : row.Bar.Close <= prev.Bar.Close;

            bool candleBull = row.Bar.Close > row.Bar.Open;
            bool candleBear = row.Bar.Close < row.Bar.Open;

            bool trendUp = _cfg.UseTrendFilter
                ? row.Ema9 > row.Ema21 && row.Bar.Close >= row.Ema21
                : row.Bar.Close >= row.Ema9;

            bool trendDown = _cfg.UseTrendFilter
                ? row.Ema9 < row.Ema21 && row.Bar.Close <= row.Ema21
                : row.Bar.Close <= row.Ema9;

            bool pullbackToFastMa = Math.Abs(row.Bar.Close - row.Ema9) / atr <= _cfg.PullbackToEma9Atr
                                    || (row.Bar.Low <= row.Ema9 && row.Bar.High >= row.Ema9);
            if (!_cfg.RequirePullback) pullbackToFastMa = true;

            // VWAP side checks (VWAP is guaranteed non-NaN above)
            bool vwapLongOk = row.Bar.Close >= row.Vwap;
            bool vwapShortOk = row.Bar.Close <= row.Vwap;

            // ---- Score model (keeps original structure, but NaNs no longer auto-pass) ----
            int longScore = 0;
            if (candleBull) longScore++;
            if (trendUp) longScore++;
            if (pullbackToFastMa) longScore++;
            if (vwapLongOk) longScore++;
            if (ofiBull) longScore++;
            longScore += 4; // rvolOk, liqOk, spreadOk, volAccelOk are guaranteed true by gates
            if (_cfg.UsePearlL1L2BorderlineGate)
                longScore += ComputeL1L2Bonus(row, TradeSide.Long);
            bool strictLongBookConfirmed = PassesStrictBookConfirmationGate(row, TradeSide.Long);
            if (EarnsStrictBookConfirmationBonus(row, TradeSide.Long))
                longScore += _cfg.StrictBookConfirmationBonus;
            int baseLongScore = longScore;

            int shortScore = 0;
            if (candleBear) shortScore++;
            if (trendDown) shortScore++;
            if (pullbackToFastMa) shortScore++;
            if (vwapShortOk) shortScore++;
            if (ofiBear) shortScore++;
            shortScore += 4;
            if (_cfg.UsePearlL1L2BorderlineGate)
                shortScore += ComputeL1L2Bonus(row, TradeSide.Short);
            bool strictShortBookConfirmed = PassesStrictBookConfirmationGate(row, TradeSide.Short);
            if (EarnsStrictBookConfirmationBonus(row, TradeSide.Short))
                shortScore += _cfg.StrictBookConfirmationBonus;
            int baseShortScore = shortScore;

            // Optional MTF alignment: now directional
            if (_cfg.RequireMtfAlign)
            {
                // We'll only check alignment for the direction we might take.
                // If both directions are plausible, each direction's check is applied in its branch below.
            }

            // ---- LONG branch ----
            if (_cfg.AllowLong
                && trendUp
                && longScore >= _cfg.MinEntryScore
                && (double.IsNaN(row.Rsi14) || (row.Rsi14 >= _cfg.RsiMinLong && row.Rsi14 <= _cfg.RsiMaxLong))
                && (!_cfg.RequireHtfBias || htfBias is "BULL" or "STRONG_BULL" or "NEUTRAL"))
            {
                if (!HasRequiredConsecutiveTrendBars(triggerBars, i, TradeSide.Long))
                {
                    latestRejectStage = "candidate-eligibility";
                    latestRejectReason = "long-consecutive-trend-bars";
                    goto TRY_SHORT;
                }

                if (_cfg.RequireStrictBookConfirmation && !strictLongBookConfirmed)
                {
                    latestRejectStage = "candidate-eligibility";
                    latestRejectReason = "long-strict-book-confirmation";
                    goto TRY_SHORT;
                }

                if (_cfg.UsePearlL1L2BorderlineGate
                    && baseLongScore <= _cfg.MinEntryScore + _cfg.BorderlineScoreWindow
                    && !PassesL1L2Gate(row, TradeSide.Long))
                {
                    latestRejectStage = "candidate-eligibility";
                    latestRejectReason = "long-borderline-l1l2";
                    goto TRY_SHORT;
                }

                if (_cfg.RequireMtfAlign && !HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, TradeSide.Long))
                {
                    latestRejectStage = "candidate-eligibility";
                    latestRejectReason = "long-mtf-alignment";
                    goto TRY_SHORT;
                }

                double swingLow = row.Bar.Low;
                for (int k = Math.Max(0, i - _cfg.SwingLookback); k <= i; k++)
                    swingLow = Math.Min(swingLow, triggerBars[k].Bar.Low);

                // Entry model
                int entryIndex = i;
                double entry = row.Bar.Close;
                DateTime entryTs = row.Bar.Timestamp;
                if (_cfg.UseNextBarOpenEntry)
                {
                    if (i + 1 >= triggerBars.Length) goto TRY_SHORT;
                    entryIndex = i + 1;
                    entry = triggerBars[entryIndex].Bar.Open;
                    entryTs = triggerBars[entryIndex].Bar.Timestamp;
                }

                double stopBySwing = swingLow - (0.05 * atr);
                double maxStop = entry - (_cfg.HardStopR * atr);
                double stop = Math.Max(stopBySwing, maxStop);

                double riskPerShare = entry - stop;
                if (riskPerShare > 0 && riskPerShare >= _cfg.MinRiskPerShare)
                {
                    int qty = BacktestHelpers.ComputePositionSize(entry, riskPerShare,
                        _cfg.RiskPerTradeDollars, _cfg.AccountSize, _cfg.MaxPositionNotionalPctOfAccount, _cfg.MaxShares);
                    if (qty > 0)
                    {
                        signals.Add(new BacktestSignal(
                            BarIndex: entryIndex,
                            Timestamp: entryTs,
                            Side: TradeSide.Long,
                            EntryPrice: entry,
                            StopPrice: stop,
                            RiskPerShare: riskPerShare,
                            PositionSize: qty,
                            AtrValue: atr,
                            HtfTrend: HtfBias.Bull,
                            MtfMomentum: _cfg.RequireMtfAlign ? "ALIGNED" : "N/A",
                            SubStrategy: "V9_L1L2",
                            EntryScore: longScore));
                        if (DiagnosticsEnabled)
                            _diagnostics.ObserveAccepted("V9_L1L2", longScore);
                        _lastSignalEt = TradingTime.ToEt(entryTs);
                        continue;
                    }

                    latestRejectStage = "entry-validity";
                    latestRejectReason = "long-position-size";
                }
                else
                {
                    latestRejectStage = "entry-validity";
                    latestRejectReason = "long-risk-per-share";
                }
            }
            else if (_cfg.AllowLong)
            {
                latestRejectStage = "candidate-selection";
                latestRejectReason = baseLongScore < _cfg.MinEntryScore ? "long-entry-score" : "long-not-qualified";
            }

            TRY_SHORT:

            // ---- SHORT branch ----
            if (_cfg.AllowShort
                && trendDown
                && shortScore >= _cfg.MinEntryScore
                && (double.IsNaN(row.Rsi14) || (row.Rsi14 >= _cfg.RsiMinShort && row.Rsi14 <= _cfg.RsiMaxShort))
                && (!_cfg.RequireHtfBias || htfBias is "BEAR" or "STRONG_BEAR" or "NEUTRAL"))
            {
                if (!HasRequiredConsecutiveTrendBars(triggerBars, i, TradeSide.Short))
                {
                    RecordReject("candidate-eligibility", "short-consecutive-trend-bars");
                    continue;
                }

                if (_cfg.RequireStrictBookConfirmation && !strictShortBookConfirmed)
                {
                    RecordReject("candidate-eligibility", "short-strict-book-confirmation");
                    continue;
                }

                if (_cfg.UsePearlL1L2BorderlineGate
                    && baseShortScore <= _cfg.MinEntryScore + _cfg.BorderlineScoreWindow
                    && !PassesL1L2Gate(row, TradeSide.Short))
                {
                    RecordReject("candidate-eligibility", "short-borderline-l1l2");
                    continue;
                }

                if (_cfg.RequireMtfAlign && !HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, TradeSide.Short))
                {
                    RecordReject("candidate-eligibility", "short-mtf-alignment");
                    continue;
                }

                double swingHigh = row.Bar.High;
                for (int k = Math.Max(0, i - _cfg.SwingLookback); k <= i; k++)
                    swingHigh = Math.Max(swingHigh, triggerBars[k].Bar.High);

                // Entry model
                int entryIndex = i;
                double entry = row.Bar.Close;
                DateTime entryTs = row.Bar.Timestamp;
                if (_cfg.UseNextBarOpenEntry)
                {
                    if (i + 1 >= triggerBars.Length) continue;
                    entryIndex = i + 1;
                    entry = triggerBars[entryIndex].Bar.Open;
                    entryTs = triggerBars[entryIndex].Bar.Timestamp;
                }

                double stopBySwing = swingHigh + (0.05 * atr);
                double maxStop = entry + (_cfg.HardStopR * atr);
                double stop = Math.Min(stopBySwing, maxStop);

                double riskPerShare = stop - entry;
                if (riskPerShare > 0 && riskPerShare >= _cfg.MinRiskPerShare)
                {
                    int qty = BacktestHelpers.ComputePositionSize(entry, riskPerShare,
                        _cfg.RiskPerTradeDollars, _cfg.AccountSize, _cfg.MaxPositionNotionalPctOfAccount, _cfg.MaxShares);
                    if (qty > 0)
                    {
                        signals.Add(new BacktestSignal(
                            BarIndex: entryIndex,
                            Timestamp: entryTs,
                            Side: TradeSide.Short,
                            EntryPrice: entry,
                            StopPrice: stop,
                            RiskPerShare: riskPerShare,
                            PositionSize: qty,
                            AtrValue: atr,
                            HtfTrend: HtfBias.Bear,
                            MtfMomentum: _cfg.RequireMtfAlign ? "ALIGNED" : "N/A",
                            SubStrategy: "V9_L1L2",
                            EntryScore: shortScore));
                        if (DiagnosticsEnabled)
                            _diagnostics.ObserveAccepted("V9_L1L2", shortScore);
                        _lastSignalEt = TradingTime.ToEt(entryTs);
                        continue;
                    }

                    RecordReject("entry-validity", "short-position-size");
                    continue;
                }

                RecordReject("entry-validity", "short-risk-per-share");
                continue;
            }

            RecordReject(
                string.IsNullOrWhiteSpace(latestRejectStage) ? "candidate-selection" : latestRejectStage,
                string.IsNullOrWhiteSpace(latestRejectReason) ? "no-direction-qualified" : latestRejectReason);
        }

        return signals;

        void RecordReject(string stage, string reason)
        {
            if (DiagnosticsEnabled)
                _diagnostics.ObserveReject(stage, reason);
        }
    }

    private void ResetCooldownForRewoundSeries(EnrichedBar[] triggerBars)
    {
        var firstBar = triggerBars[0].Bar;
        var lastBar = triggerBars[^1].Bar;
        var currentSeriesFingerprint = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{triggerBars.Length}|{firstBar.Timestamp:O}|{lastBar.Timestamp:O}|{firstBar.Close:F4}|{lastBar.Close:F4}");

        bool looksLikeDifferentHistoricalSeries = _lastSeriesEndUtc != DateTime.MinValue
            && lastBar.Timestamp <= _lastSeriesEndUtc
            && !string.Equals(currentSeriesFingerprint, _lastSeriesFingerprint, StringComparison.Ordinal);

        if (looksLikeDifferentHistoricalSeries)
        {
            _lastSignalEt = DateTime.MinValue;
        }

        _lastSeriesEndUtc = lastBar.Timestamp;
        _lastSeriesFingerprint = currentSeriesFingerprint;
    }



    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    public void ResetDiagnostics()
    {
        _diagnostics.Reset();
    }

    public IReadOnlyList<string> GetDiagnosticsSummaryLines()
    {
        return DiagnosticsEnabled
            ? _diagnostics.BuildSummaryLines("V9_1")
            : [];
    }



    private static string ComputeHtfBias(DateTime ts, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 2) continue;
            int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
            if (idx < 0) continue;
            var last = bars[idx];

            int s = 0;
            s += last.Ema21 > last.Ema50 ? 1 : -1;
            s += last.Bar.Close > last.Ema21 ? 1 : -1;
            s += last.MacdHist >= 0 ? 1 : -1;
            if (!double.IsNaN(last.Adx) && last.Adx > 20)
                s += last.PlusDi >= last.MinusDi ? 1 : -1;
            scores.Add(s);
        }

        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 2.5) return "STRONG_BULL";
        if (avg >= 1.0) return "BULL";
        if (avg <= -2.5) return "STRONG_BEAR";
        if (avg <= -1.0) return "BEAR";
        return "NEUTRAL";
    }

    private static bool HasMtfAlignment(DateTime ts, EnrichedBar[]? bars5m, EnrichedBar[]? bars15m, TradeSide direction)
    {
        bool ok5 = true;
        bool ok15 = true;

        if (bars5m != null && bars5m.Length > 0)
        {
            int i5 = BacktestHelpers.FindBarAtOrBefore(bars5m, ts);
            if (i5 >= 0)
            {
                var b = bars5m[i5];
                ok5 = direction == TradeSide.Long
                    ? !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 > b.Ema21 && b.MacdHist >= 0
                    : !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21 && b.MacdHist <= 0;
            }
        }

        if (bars15m != null && bars15m.Length > 0)
        {
            int i15 = BacktestHelpers.FindBarAtOrBefore(bars15m, ts);
            if (i15 >= 0)
            {
                var b = bars15m[i15];
                ok15 = direction == TradeSide.Long
                    ? !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 > b.Ema21 && b.MacdHist >= 0
                    : !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21 && b.MacdHist <= 0;
            }
        }

        return ok5 && ok15;
    }

    private bool HasRequiredConsecutiveTrendBars(EnrichedBar[] triggerBars, int signalIndex, TradeSide side)
    {
        var requiredBars = Math.Max(1, _cfg.RequiredConsecutiveTrendBarsBeforeEntry);
        if (requiredBars <= 1)
            return true;

        var firstIndex = signalIndex - requiredBars + 1;
        if (firstIndex < 0)
            return false;

        for (var index = signalIndex; index >= firstIndex; index--)
        {
            if (!DirectionalConfirmationEngine.HasExpectedTrendCandle(triggerBars[index], side))
                return false;
        }

        return true;
    }

    private static bool PassesStrictBookConfirmationGate(EnrichedBar row, TradeSide side)
    {
        DirectionalConfirmationEngine.TryGetL2DirectionalConfirmation(row, side, out var l2Available, out var l2Confirmed);
        return !l2Available || l2Confirmed;
    }

    private static bool EarnsStrictBookConfirmationBonus(EnrichedBar row, TradeSide side)
    {
        DirectionalConfirmationEngine.TryGetL1DirectionalConfirmation(row, side, out var l1Available, out var l1Confirmed);
        if (!l1Available || !l1Confirmed)
            return false;

        DirectionalConfirmationEngine.TryGetL2DirectionalConfirmation(row, side, out var l2Available, out var l2Confirmed);
        return !l2Available || l2Confirmed;
    }

    private int ComputeL1L2Bonus(EnrichedBar row, TradeSide side)
    {
        var bonus = 0;

        if (!double.IsNaN(row.SpreadPct) && row.SpreadPct < 0.80)
            bonus++;

        if (!double.IsNaN(row.BidSize) && !double.IsNaN(row.AskSize) && row.BidSize > 0 && row.AskSize > 0)
        {
            var sizeRatio = row.BidSize / row.AskSize;
            if (side == TradeSide.Long && sizeRatio >= 2.0)
                bonus++;
            if (side == TradeSide.Short && sizeRatio <= 0.5)
                bonus++;
        }

        if (!double.IsNaN(row.OfiSignal) && (side == TradeSide.Long ? row.OfiSignal > 0.15 : row.OfiSignal < -0.15))
            bonus++;

        if (!double.IsNaN(row.DeepImbalanceRatio) && row.DeepImbalanceRatio > 0)
        {
            if (side == TradeSide.Long && row.DeepImbalanceRatio >= _cfg.L2DeepImbalanceMinForLong)
                bonus++;
            if (side == TradeSide.Short && row.DeepImbalanceRatio <= _cfg.L2DeepImbalanceMaxForShort)
                bonus++;
        }

        return bonus;
    }

    private bool PassesL1L2Gate(EnrichedBar row, TradeSide side)
    {
        var anySignal = false;

        if (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio > 0)
        {
            anySignal = true;
            if (side == TradeSide.Long && row.ImbalanceRatio < _cfg.L2ImbalanceMinForLong)
                return false;
            if (side == TradeSide.Short && row.ImbalanceRatio > _cfg.L2ImbalanceMaxForShort)
                return false;
        }

        if (!double.IsNaN(row.DeepImbalanceRatio) && row.DeepImbalanceRatio > 0)
        {
            anySignal = true;
            if (side == TradeSide.Long && row.DeepImbalanceRatio < _cfg.L2DeepImbalanceMinForLong)
                return false;
            if (side == TradeSide.Short && row.DeepImbalanceRatio > _cfg.L2DeepImbalanceMaxForShort)
                return false;
        }

        if (!double.IsNaN(row.BidSize) && !double.IsNaN(row.AskSize) && row.BidSize > 0 && row.AskSize > 0)
        {
            anySignal = true;
            var sizeRatio = row.BidSize / row.AskSize;
            if (side == TradeSide.Long && sizeRatio < _cfg.L1SizeRatioMinForLong)
                return false;
            if (side == TradeSide.Short && sizeRatio > _cfg.L1SizeRatioMaxForShort)
                return false;
        }

        if (!double.IsNaN(row.BidPrice) && !double.IsNaN(row.AskPrice) && !double.IsNaN(row.LastPrice)
            && row.BidPrice > 0 && row.AskPrice > 0 && row.LastPrice > 0)
        {
            anySignal = true;
            var midPrice = (row.BidPrice + row.AskPrice) / 2.0;
            if (side == TradeSide.Long && row.LastPrice < midPrice)
                return false;
            if (side == TradeSide.Short && row.LastPrice > midPrice)
                return false;
        }

        if (!double.IsNaN(row.DepthWeightedMid) && !double.IsNaN(row.BidPrice) && !double.IsNaN(row.AskPrice)
            && row.BidPrice > 0 && row.AskPrice > 0)
        {
            anySignal = true;
            var midPrice = (row.BidPrice + row.AskPrice) / 2.0;
            if (side == TradeSide.Long && row.DepthWeightedMid < midPrice)
                return false;
            if (side == TradeSide.Short && row.DepthWeightedMid > midPrice)
                return false;
        }

        return anySignal;
    }

    private sealed class V9Diagnostics
    {
        private readonly Dictionary<string, int> _rejectCounts = new(StringComparer.OrdinalIgnoreCase);
        private int _evaluatedBars;
        private int _acceptedSignals;
        private double _acceptedScoreSum;

        public void Reset()
        {
            _rejectCounts.Clear();
            _evaluatedBars = 0;
            _acceptedSignals = 0;
            _acceptedScoreSum = 0;
        }

        public void ObserveEvaluated()
        {
            _evaluatedBars++;
        }

        public void ObserveReject(string stage, string reason)
        {
            var key = $"{stage}:{reason}";
            _rejectCounts[key] = _rejectCounts.GetValueOrDefault(key) + 1;
        }

        public void ObserveAccepted(string source, int entryScore)
        {
            _acceptedSignals++;
            _acceptedScoreSum += entryScore;
        }

        public IReadOnlyList<string> BuildSummaryLines(string label)
        {
            if (_evaluatedBars == 0)
                return [];

            var lines = new List<string>
            {
                $"diagnostics[{label}] evaluated-bars={_evaluatedBars} accepted={_acceptedSignals} avg-entry-score={(_acceptedSignals > 0 ? _acceptedScoreSum / _acceptedSignals : 0.0):F2}"
            };

            foreach (var reject in _rejectCounts.OrderByDescending(entry => entry.Value).Take(8))
            {
                lines.Add($"diag-reject {reject.Key}={reject.Value}");
            }

            return lines;
        }
    }



}




