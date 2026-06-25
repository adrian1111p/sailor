using Sailor.App.Backtest.Engine;
using Harvester.App.Strategy;

namespace Sailor.App.Backtest.Strategies;

/// <summary>
/// Conduct Strategy V3 â€” timestamp-aligned multi-timeframe trend/pullback strategy.
/// Improvements over V2:
/// - Removes HTF/MTF lookahead by aligning context bars to signal timestamp
/// - Uses shared ExitEngine (single source of truth for exits)
/// - Supports next-bar-open entry and capped position sizing
/// - Adds optional strict missing-data policy and MTF alignment gating
/// - Entry time windows and price filter for realistic market-hours-only trading
/// - Optional VWAP reversion and BB bounce alternate entries
/// </summary>
public sealed class ConductStrategyV3 : BacktestStrategyBase, IBacktestLifecycleStrategy
{
    private readonly StrategyConfig _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public ConductStrategyV3(StrategyConfig? cfg = null)
    {
        _cfg = cfg ?? new StrategyConfig();

        // Phase 3.9 â€” validate and log the resolved config. Non-throwing by default to preserve
        // behavior (constraint 1: never reject a config merely for opening more trades); genuinely
        // contradictory settings are surfaced as console warnings so they are visible in logs.
        var validation = StrategyConfigValidator.Validate(_cfg);
        Console.WriteLine($"[INFO] {validation.ResolvedSummary}");
        foreach (var error in validation.Errors)
        {
            Console.WriteLine($"[WARN] ConductStrategyV3 config error: {error}");
        }

        foreach (var warning in validation.Warnings)
        {
            Console.WriteLine($"[INFO] ConductStrategyV3 config warning: {warning}");
        }

        // Phase 3.10 â€” compose the shared ExitProfile as the single source of exit settings.
        var exitProfile = _cfg.ToExitProfile();

        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = exitProfile.HardStopR,
            BreakevenR = exitProfile.BreakevenR,
            TrailR = exitProfile.TrailR,
            GivebackPct = exitProfile.GivebackPct,
            GivebackMinPeakR = 0.0,
            UseFixedGivebackUsdCap = !exitProfile.UseNotionalGivebackCap && exitProfile.GivebackUsdCap > 0,
            UseNotionalGivebackCap = exitProfile.UseNotionalGivebackCap,
            UseVariableGivebackUsdCap = exitProfile.UseVariableGivebackUsdCap,
            UseTightTrailOnFixedGiveback = exitProfile.UseTightTrailOnFixedGiveback,
            GivebackPctOfNotional = exitProfile.GivebackPctOfNotional,
            GivebackUsdCap = exitProfile.GivebackUsdCap,
            Tp1R = exitProfile.Tp1R,
            Tp1PartialClosePct = exitProfile.Tp1ScalePct,
            Tp2R = exitProfile.Tp2R,
            UseContinuationTp2ScaleOut = exitProfile.UseContinuationTp2ScaleOut,
            ContinuationTp2ScalePct = exitProfile.ContinuationTp2ScalePct,
            UseTrailingTp2 = exitProfile.UseTrailingTp2,
            TrailingTp2AtrMultiplier = exitProfile.TrailingTp2AtrMultiplier,
            UseL1L2DecisionOnOppositeBarsFlatten = exitProfile.UseL1L2DecisionOnOppositeBarsFlatten,
            MaxHoldBars = exitProfile.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
            Tp1BreakevenBufferAtr = exitProfile.Tp1BreakevenBufferAtr,
        };
    }

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var triggerBars = bars1m;
        var signals = new List<BacktestSignal>();
        int minimumHistoryBars = Math.Max(StrategyConfig.MinimumRequiredSignalHistoryBars, _cfg.MinimumSignalHistoryBars);
        if (triggerBars.Length < minimumHistoryBars)
            return ReturnRawSignals(signals);

        int firstSignalIndex = Math.Max(1, _cfg.FirstSignalEvaluationIndex);

        int lastSignalBar = -10_000;
        int lastPullbackSignalBar = -10_000;

        for (int i = firstSignalIndex; i < triggerBars.Length; i++)
        {
            var preEntryGuard = EvaluatePreEntryGuard(triggerBars, i, lastSignalBar);
            if (!string.IsNullOrWhiteSpace(preEntryGuard.RejectReason))
            {
                if (string.Equals(preEntryGuard.RejectReason, "next-entry-bar-missing", StringComparison.OrdinalIgnoreCase))
                    break;

                continue;
            }

            var row = preEntryGuard.Row;
            var prev = triggerBars[i - 1];
            var ts = preEntryGuard.TimestampUtc;
            double price = preEntryGuard.Price;
            int entryIndex = preEntryGuard.EntryIndex;
            double atrVal = preEntryGuard.AtrValue;

            var htfBias = ComputeHtfBiasAt(ts, bars1h, bars1d);
            var mainCandidates = GetCandidateSides(row, prev, htfBias);
            bool appendedSignal = AppendSignalsForCandidates(
                OrderCandidatesForEvaluation(mainCandidates),
                signals,
                ref lastSignalBar,
                ref lastPullbackSignalBar,
                triggerBars,
                i,
                row,
                prev,
                ts,
                entryIndex,
                atrVal,
                htfBias,
                bars5m,
                bars15m);

            if (ShouldEvaluateAlternateEntries(mainCandidates, appendedSignal))
            {
                var alternateCandidates = new List<CandidateSide>(2);
                AddAlternateCandidates(alternateCandidates, row, price, atrVal, htfBias);
                AppendSignalsForCandidates(
                    OrderCandidatesForEvaluation(alternateCandidates),
                    signals,
                    ref lastSignalBar,
                    ref lastPullbackSignalBar,
                    triggerBars,
                    i,
                    row,
                    prev,
                    ts,
                    entryIndex,
                    atrVal,
                    htfBias,
                    bars5m,
                    bars15m);
            }
        }

        return ReturnRawSignals(signals);
    }

    internal ConductV3PreEntryGuardEvaluation? FindLatestRejectedPreEntryGuard(EnrichedBar[] bars1m)
    {
        ArgumentNullException.ThrowIfNull(bars1m);

        int minimumHistoryBars = Math.Max(StrategyConfig.MinimumRequiredSignalHistoryBars, _cfg.MinimumSignalHistoryBars);
        if (bars1m.Length < minimumHistoryBars)
        {
            return null;
        }

        int firstSignalIndex = Math.Max(1, _cfg.FirstSignalEvaluationIndex);
        int latestSignalIndex = _cfg.UseNextBarOpenEntry
            ? bars1m.Length - 2
            : bars1m.Length - 1;
        if (latestSignalIndex < firstSignalIndex)
        {
            return null;
        }

        var evaluation = EvaluatePreEntryGuard(bars1m, latestSignalIndex, lastSignalBar: -10_000);
        return string.IsNullOrWhiteSpace(evaluation.RejectReason)
            ? null
            : evaluation;
    }

    internal ConductV3NoSignalDiagnostic? FindLatestNoSignalDiagnostic(
        EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        ArgumentNullException.ThrowIfNull(bars1m);

        int minimumHistoryBars = Math.Max(StrategyConfig.MinimumRequiredSignalHistoryBars, _cfg.MinimumSignalHistoryBars);
        if (bars1m.Length < minimumHistoryBars)
        {
            return null;
        }

        int firstSignalIndex = Math.Max(1, _cfg.FirstSignalEvaluationIndex);
        int latestSignalIndex = _cfg.UseNextBarOpenEntry
            ? bars1m.Length - 2
            : bars1m.Length - 1;
        if (latestSignalIndex < firstSignalIndex)
        {
            return null;
        }

        var preEntryGuard = EvaluatePreEntryGuard(bars1m, latestSignalIndex, lastSignalBar: -10_000);
        if (!string.IsNullOrWhiteSpace(preEntryGuard.RejectReason))
        {
            return CreateNoSignalDiagnostic(
                preEntryGuard.SignalIndex,
                preEntryGuard.TimestampUtc,
                preEntryGuard.RejectReason,
                stage: "pre-entry-guard");
        }

        var row = preEntryGuard.Row;
        var prev = bars1m[latestSignalIndex - 1];
        var ts = preEntryGuard.TimestampUtc;
        double price = preEntryGuard.Price;
        int entryIndex = preEntryGuard.EntryIndex;
        double atrVal = preEntryGuard.AtrValue;
        var htfBias = ComputeHtfBiasAt(ts, bars1h, bars1d);
        var mainCandidates = OrderCandidatesForEvaluation(GetCandidateSides(row, prev, htfBias));

        if (mainCandidates.Count > 0)
        {
            var mainDiagnostic = FindNoSignalDiagnosticForCandidates(
                mainCandidates,
                bars1m,
                row,
                prev,
                ts,
                entryIndex,
                atrVal,
                bars5m,
                bars15m,
                candidateSet: "main");
            if (mainDiagnostic is null)
            {
                return null;
            }

            if (!ShouldEvaluateAlternateEntries(mainCandidates, appendedMainSignal: false))
            {
                return mainDiagnostic;
            }

            var alternateCandidates = new List<CandidateSide>(2);
            AddAlternateCandidates(alternateCandidates, row, price, atrVal, htfBias);
            return alternateCandidates.Count == 0
                ? mainDiagnostic
                : mainDiagnostic;
        }

        if (!ShouldEvaluateAlternateEntries(mainCandidates, appendedMainSignal: false))
        {
            return CreateNoSignalDiagnostic(
                latestSignalIndex,
                ts,
                rejectReason: "no-candidate-trigger",
                stage: "candidate-selection",
                candidateSet: "main");
        }

        var alternateCandidatesOnly = new List<CandidateSide>(2);
        AddAlternateCandidates(alternateCandidatesOnly, row, price, atrVal, htfBias);
        if (alternateCandidatesOnly.Count == 0)
        {
            return CreateNoSignalDiagnostic(
                latestSignalIndex,
                ts,
                rejectReason: "no-candidate-trigger",
                stage: "candidate-selection",
                candidateSet: "main-and-alternate");
        }

        return FindNoSignalDiagnosticForCandidates(
            OrderCandidatesForEvaluation(alternateCandidatesOnly),
            bars1m,
            row,
            prev,
            ts,
            entryIndex,
            atrVal,
            bars5m,
            bars15m,
            candidateSet: "alternate");
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => SimulateTradeLifecycle(signal, triggerBars).Trade;

    internal BacktestTradeLifecycleResult SimulateTradeLifecycle(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var exitConfig = ResolveExitConfig(signal.SubStrategy);
        return ExitEngine.SimulateTradeLifecycle(signal, triggerBars, exitConfig, CreateSelectedEntryIntent(signal, exitConfig, "1m"));
    }

    public BacktestSelectedEntryIntent CreateSelectedEntryIntent(BacktestSignal signal, string triggerTimeframe)
        => CreateSelectedEntryIntent(signal, ResolveExitConfig(signal.SubStrategy), triggerTimeframe);

    internal BacktestSelectedEntryIntent CreateSelectedEntryIntent(BacktestSignal signal)
        => CreateSelectedEntryIntent(signal, "1m");

    public BacktestTradeLifecycleResult SimulateAcceptedEntryIntent(BacktestSelectedEntryIntent selectedEntryIntent, EnrichedBar[] triggerBars)
    {
        var exitConfig = ResolveExitConfig(selectedEntryIntent.Signal.SubStrategy);
        return ExitEngine.SimulateTradeLifecycle(selectedEntryIntent.Signal, triggerBars, exitConfig, selectedEntryIntent);
    }

    private ExitEngine.ExitConfig ResolveExitConfig(string? subStrategy)
        => ApplySelfLearningExitOverrides(_exitCfg, subStrategy);

    private BacktestSelectedEntryIntent CreateSelectedEntryIntent(BacktestSignal signal, ExitEngine.ExitConfig exitConfig, string triggerTimeframe)
    {
        return new BacktestSelectedEntryIntent(
            IntentId: $"CONDUCT_V3::{signal.Timestamp:yyyyMMddHHmmss}::{signal.BarIndex}::{signal.Side}",
            Signal: signal,
            ExitProfile: ExitEngine.ToNormalizedExitProfile(exitConfig),
            LifecycleMetadata: new BacktestStrategyLifecycleMetadata(
                StrategyName: "ConductStrategy",
                StrategyVersion: "V3",
                Symbol: Symbol ?? string.Empty,
                TriggerTimeframe: string.IsNullOrWhiteSpace(triggerTimeframe) ? "1m" : triggerTimeframe,
                SubStrategy: signal.SubStrategy,
                EntryScore: signal.EntryScore));
    }

    private ConductV3PreEntryGuardEvaluation EvaluatePreEntryGuard(EnrichedBar[] triggerBars, int signalIndex, int lastSignalBar)
    {
        var row = triggerBars[signalIndex];
        var ts = row.Bar.Timestamp;
        int minuteEt = TradingTime.GetMinuteOfDayEt(ts);
        double price = row.Bar.Close;
        int entryIndex = _cfg.UseNextBarOpenEntry ? signalIndex + 1 : signalIndex;
        double atrVal = row.Atr14;

        string rejectReason = string.Empty;
        if (signalIndex - lastSignalBar < Math.Max(0, _cfg.CooldownBars))
        {
            rejectReason = "cooldown-active";
        }
        else if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
        {
            rejectReason = "market-open-delay";
        }
        else if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
        {
            rejectReason = "entry-window-closed";
        }
        else if (price < _cfg.MinPrice)
        {
            rejectReason = "price-below-min";
        }
        else if (price > _cfg.MaxPrice)
        {
            rejectReason = "price-above-max";
        }
        else if (entryIndex >= triggerBars.Length)
        {
            rejectReason = "next-entry-bar-missing";
        }
        else if (double.IsNaN(atrVal) || atrVal <= 0)
        {
            rejectReason = "atr-invalid";
        }
        else if (IsMarketRegimeBlocked(row))
        {
            rejectReason = "market-regime-blocked";
        }

        return new ConductV3PreEntryGuardEvaluation(signalIndex, row, ts, price, entryIndex, atrVal, rejectReason);
    }

    private ConductV3CandidateEligibilityEvaluation EvaluateCandidateEligibility(
        CandidateSide candidate,
        EnrichedBar row,
        EnrichedBar prev,
        double atrVal,
        DateTime ts,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m)
    {
        if (!ShouldBypassSelfLearningSetupBlock(candidate.Source) && IsSelfLearningBlocked(candidate.Source))
            return ConductV3CandidateEligibilityEvaluation.Reject("self-learning-blocked");

        var filterEvaluation = EvaluateIndicatorFilters(candidate, row, prev, atrVal, candidate.Side);
        if (!filterEvaluation.Accepted)
            return ConductV3CandidateEligibilityEvaluation.Reject(filterEvaluation.RejectReason);

        string mtfMomentum = ComputeMtfMomentumAt(ts, bars5m, bars15m, candidate.Side);
        if (_cfg.RequireMtfAlignment && mtfMomentum != "ALIGNED")
            return ConductV3CandidateEligibilityEvaluation.Reject("mtf-alignment-required");

        return ConductV3CandidateEligibilityEvaluation.Accept(mtfMomentum);
    }

    private ConductV3IndicatorFilterEvaluation EvaluateIndicatorFilters(CandidateSide candidate, EnrichedBar row, EnrichedBar prev, double atrVal, TradeSide side)
    {
        if (_cfg.StrictMissingDataChecks
            && (double.IsNaN(row.Rvol) || double.IsNaN(row.Rsi14) || double.IsNaN(row.Sma20)))
        {
            return ConductV3IndicatorFilterEvaluation.Reject("missing-required-indicator");
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin)
            return ConductV3IndicatorFilterEvaluation.Reject("rvol-below-min");

        if (!double.IsNaN(row.Adx))
        {
            if (_cfg.MinEntryAdx > 0 && row.Adx < _cfg.MinEntryAdx)
                return ConductV3IndicatorFilterEvaluation.Reject("adx-below-entry-min");

            if (_cfg.MaxEntryAdx > 0 && row.Adx > _cfg.MaxEntryAdx)
                return ConductV3IndicatorFilterEvaluation.Reject("adx-above-entry-max");

            if (IsAlternateCandidateSource(candidate.Source)
                && _cfg.AlternateEntryMaxAdx > 0
                && row.Adx > _cfg.AlternateEntryMaxAdx)
            {
                return ConductV3IndicatorFilterEvaluation.Reject("alternate-adx-too-high");
            }
        }

        if (!double.IsNaN(row.Rsi14))
        {
            var (rsiLow, rsiHigh) = side == TradeSide.Long
                ? _cfg.RsiLongRange
                : _cfg.RsiShortRange;
            if (row.Rsi14 < rsiLow || row.Rsi14 > rsiHigh)
                return ConductV3IndicatorFilterEvaluation.Reject("rsi-out-of-range");
        }
        else if (_cfg.StrictMissingDataChecks)
        {
            return ConductV3IndicatorFilterEvaluation.Reject("rsi-missing");
        }

        if (!double.IsNaN(row.Sma20) && atrVal > 0)
        {
            double maDist = (row.Bar.Close - row.Sma20) / atrVal;
            if (side == TradeSide.Long && maDist > _cfg.MaxMaDistAtr)
                return ConductV3IndicatorFilterEvaluation.Reject("sma20-distance-too-far");
            if (side == TradeSide.Short && maDist < -_cfg.MaxMaDistAtr)
                return ConductV3IndicatorFilterEvaluation.Reject("sma20-distance-too-far");
        }

        if (IsPullbackCandidateSource(candidate.Source))
        {
            if (_cfg.PullbackRvolMin > 0
                && !double.IsNaN(row.Rvol)
                && row.Rvol < _cfg.PullbackRvolMin)
            {
                return ConductV3IndicatorFilterEvaluation.Reject("pullback-rvol-below-min");
            }

            if (!double.IsNaN(row.BbPctB))
            {
                if (side == TradeSide.Long && row.BbPctB > _cfg.MainLongMaxBbPctB)
                    return ConductV3IndicatorFilterEvaluation.Reject("pullback-long-bb-too-high");

                if (side == TradeSide.Short && row.BbPctB < _cfg.MainShortMinBbPctB)
                    return ConductV3IndicatorFilterEvaluation.Reject("pullback-short-bb-too-low");
            }

            if (_cfg.MainEntryMaxVwapDeviationAtr > 0
                && TryGetVwapDistanceAtr(row, atrVal, out double pullbackVwapDistAtr)
                && Math.Abs(pullbackVwapDistAtr) > _cfg.MainEntryMaxVwapDeviationAtr)
            {
                return ConductV3IndicatorFilterEvaluation.Reject("pullback-vwap-distance-too-far");
            }
        }

        if (IsAlternateCandidateSource(candidate.Source))
        {
            if (_cfg.AlternateEntryRequireRsiExtreme && !IsAlternateRsiExtreme(row, side))
                return ConductV3IndicatorFilterEvaluation.Reject("alternate-rsi-not-extreme");

            if (_cfg.AlternateEntryRequireReversalCandle && !IsReversalCandle(row, prev, side))
                return ConductV3IndicatorFilterEvaluation.Reject("alternate-no-reversal-candle");

            if (_cfg.AlternateEntryMaxCountertrendMaDistAtr > 0
                && TryGetSma20DistanceAtr(row, atrVal, out double sma20DistAtr))
            {
                if (side == TradeSide.Long && sma20DistAtr < -_cfg.AlternateEntryMaxCountertrendMaDistAtr)
                    return ConductV3IndicatorFilterEvaluation.Reject("alternate-sma20-distance-too-far");

                if (side == TradeSide.Short && sma20DistAtr > _cfg.AlternateEntryMaxCountertrendMaDistAtr)
                    return ConductV3IndicatorFilterEvaluation.Reject("alternate-sma20-distance-too-far");
            }

            if (_cfg.AlternateEntryMaxVwapStretchAtr > 0
                && TryGetVwapDistanceAtr(row, atrVal, out double alternateVwapDistAtr)
                && Math.Abs(alternateVwapDistAtr) > _cfg.AlternateEntryMaxVwapStretchAtr)
            {
                return ConductV3IndicatorFilterEvaluation.Reject("alternate-vwap-stretch-too-far");
            }
        }

        return ConductV3IndicatorFilterEvaluation.Accept();
    }

    private List<CandidateSide> GetCandidateSides(EnrichedBar row, EnrichedBar prev, HtfBias htfBias)
    {
        var candidates = new List<CandidateSide>(2);

        if (htfBias is HtfBias.Bull or HtfBias.Neutral)
        {
            bool stFlipLong = row.StDirection == 1 && prev.StDirection == -1;
            bool emaPullbackLong = row.Bar.Close >= ResolvePullbackEma(row) && prev.Bar.Close < ResolvePullbackEma(prev);
            bool triggerLong = _cfg.RequireSupertrend ? stFlipLong : (stFlipLong || emaPullbackLong);

            if (triggerLong && row.Bar.Close > row.Ema21)
                candidates.Add(new CandidateSide(TradeSide.Long, ResolveMainTriggerScore(stFlipLong, emaPullbackLong), ResolveMainTriggerSource(stFlipLong, emaPullbackLong)));
        }

        if (htfBias is HtfBias.Bear or HtfBias.Neutral)
        {
            bool stFlipShort = row.StDirection == -1 && prev.StDirection == 1;
            bool emaPullbackShort = row.Bar.Close <= ResolvePullbackEma(row) && prev.Bar.Close > ResolvePullbackEma(prev);
            bool triggerShort = _cfg.RequireSupertrend ? stFlipShort : (stFlipShort || emaPullbackShort);

            if (triggerShort && row.Bar.Close < row.Ema21)
                candidates.Add(new CandidateSide(TradeSide.Short, ResolveMainTriggerScore(stFlipShort, emaPullbackShort), ResolveMainTriggerSource(stFlipShort, emaPullbackShort)));
        }

        return candidates;
    }

    private bool ShouldEvaluateAlternateEntries(IReadOnlyCollection<CandidateSide> mainCandidates, bool appendedMainSignal)
        => mainCandidates.Count == 0 || (_cfg.AllowAlternateEntriesAfterRejectedMainCandidates && !appendedMainSignal);

    private static IReadOnlyList<CandidateSide> OrderCandidatesForEvaluation(IReadOnlyList<CandidateSide> candidates)
        => candidates
            .OrderByDescending(candidate => candidate.TriggerScore)
            .ThenBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Side)
            .ToArray();

    private bool AppendSignalsForCandidates(
        IReadOnlyList<CandidateSide> candidates,
        List<BacktestSignal> signals,
        ref int lastSignalBar,
        ref int lastPullbackSignalBar,
        EnrichedBar[] triggerBars,
        int signalIndex,
        EnrichedBar row,
        EnrichedBar prev,
        DateTime ts,
        int entryIndex,
        double atrVal,
        HtfBias htfBias,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m)
    {
        bool appendedSignal = false;

        foreach (var candidate in candidates)
        {
            if (IsPullbackReentryCooldownActive(candidate, signalIndex, lastPullbackSignalBar))
                continue;

            var signal = TryBuildSignalForCandidate(candidate, triggerBars, row, prev, ts, entryIndex, atrVal, htfBias, bars5m, bars15m);
            if (signal is null)
                continue;

            lastSignalBar = AppendRawSignal(signals, signal);
            if (IsPullbackCandidateSource(candidate.Source))
                lastPullbackSignalBar = signalIndex;

            appendedSignal = true;
        }

        return appendedSignal;
    }

    private bool IsPullbackReentryCooldownActive(CandidateSide candidate, int signalIndex, int lastPullbackSignalBar)
        => IsPullbackCandidateSource(candidate.Source)
            && signalIndex - lastPullbackSignalBar < Math.Max(0, _cfg.PullbackReentryCooldownBars);

    private BacktestSignal? TryBuildSignalForCandidate(
        CandidateSide candidate,
        EnrichedBar[] triggerBars,
        EnrichedBar row,
        EnrichedBar prev,
        DateTime ts,
        int entryIndex,
        double atrVal,
        HtfBias htfBias,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m)
    {
        var candidateEvaluation = EvaluateCandidateEligibility(candidate, row, prev, atrVal, ts, bars5m, bars15m);
        if (!candidateEvaluation.Accepted)
            return null;

        var side = candidate.Side;
        string mtfMomentum = candidateEvaluation.MtfMomentum;

        var entryRow = triggerBars[entryIndex];
        if (_cfg.RequireL2EntryConfirmation && !PassesEntryConfirmation(entryRow, side))
            return null;

        var entryConstruction = BuildEntryConstruction(candidate, entryRow, atrVal);
        var entryValidity = EvaluateEntryValidity(candidate, entryConstruction);
        if (!entryValidity.Accepted)
            return null;

        double entryPrice = entryConstruction.EntryPrice;
        double stopPrice = entryConstruction.StopPrice;
        double riskPerShare = entryConstruction.RiskPerShare;
        int positionSize = entryConstruction.PositionSize;

        int entryScore = candidate.TriggerScore + ComputeEntryScore(candidate, row, prev, side, atrVal);
        if (_cfg.MinEntryScore > 0 && entryScore < _cfg.MinEntryScore)
            return null;

        return new BacktestSignal(
            BarIndex: entryIndex,
            Timestamp: triggerBars[entryIndex].Bar.Timestamp,
            Side: side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: positionSize,
            AtrValue: atrVal,
            HtfTrend: htfBias,
            MtfMomentum: mtfMomentum,
            SubStrategy: candidate.Source,
            EntryScore: entryScore);
    }

    private ConductV3NoSignalDiagnostic? FindNoSignalDiagnosticForCandidates(
        IReadOnlyList<CandidateSide> candidates,
        EnrichedBar[] triggerBars,
        EnrichedBar row,
        EnrichedBar prev,
        DateTime ts,
        int entryIndex,
        double atrVal,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m,
        string candidateSet)
    {
        if (candidates.Count == 0)
        {
            return CreateNoSignalDiagnostic(
                signalIndex: entryIndex,
                timestampUtc: ts,
                rejectReason: "no-candidate-trigger",
                stage: "candidate-selection",
                candidateSet: candidateSet);
        }

        foreach (var candidate in candidates)
        {
            var diagnostic = FindNoSignalDiagnosticForCandidate(
                candidate,
                triggerBars,
                row,
                prev,
                ts,
                entryIndex,
                atrVal,
                bars5m,
                bars15m,
                candidateSet);
            if (diagnostic is not null)
            {
                return diagnostic;
            }
        }

        return null;
    }

    private ConductV3NoSignalDiagnostic? FindNoSignalDiagnosticForCandidate(
        CandidateSide candidate,
        EnrichedBar[] triggerBars,
        EnrichedBar row,
        EnrichedBar prev,
        DateTime ts,
        int entryIndex,
        double atrVal,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m,
        string candidateSet)
    {
        var candidateEvaluation = EvaluateCandidateEligibility(candidate, row, prev, atrVal, ts, bars5m, bars15m);
        if (!candidateEvaluation.Accepted)
        {
            return CreateNoSignalDiagnostic(
                signalIndex: entryIndex,
                timestampUtc: ts,
                rejectReason: candidateEvaluation.RejectReason,
                stage: "candidate-eligibility",
                candidateSet: candidateSet,
                candidateSource: candidate.Source,
                candidateSide: candidate.Side);
        }

        var side = candidate.Side;
        var entryRow = triggerBars[entryIndex];
        if (_cfg.RequireL2EntryConfirmation && !PassesEntryConfirmation(entryRow, side))
        {
            return CreateNoSignalDiagnostic(
                signalIndex: entryIndex,
                timestampUtc: ts,
                rejectReason: "l2-entry-not-confirmed",
                stage: "l2-entry-confirmation",
                candidateSet: candidateSet,
                candidateSource: candidate.Source,
                candidateSide: candidate.Side);
        }

        var entryConstruction = BuildEntryConstruction(candidate, entryRow, atrVal);
        var entryValidity = EvaluateEntryValidity(candidate, entryConstruction);
        if (!entryValidity.Accepted)
        {
            return CreateNoSignalDiagnostic(
                signalIndex: entryIndex,
                timestampUtc: ts,
                rejectReason: entryValidity.RejectReason,
                stage: "entry-validity",
                candidateSet: candidateSet,
                candidateSource: candidate.Source,
                candidateSide: candidate.Side);
        }

        int entryScore = candidate.TriggerScore + ComputeEntryScore(candidate, row, prev, side, atrVal);
        if (_cfg.MinEntryScore > 0 && entryScore < _cfg.MinEntryScore)
        {
            return CreateNoSignalDiagnostic(
                signalIndex: entryIndex,
                timestampUtc: ts,
                rejectReason: "entry-score-below-min",
                stage: "entry-score",
                candidateSet: candidateSet,
                candidateSource: candidate.Source,
                candidateSide: candidate.Side);
        }

        return null;
    }

    private ConductV3EntryConstructionEvaluation BuildEntryConstruction(
        CandidateSide candidate,
        EnrichedBar entryRow,
        double atrVal)
    {
        double barEntryPrice = _cfg.UseNextBarOpenEntry
            ? entryRow.Bar.Open
            : entryRow.Bar.Close;
        double quoteEntryPrice = candidate.Side == TradeSide.Long
            ? entryRow.AskPrice
            : entryRow.BidPrice;
        double entryPrice = quoteEntryPrice > 0
            ? quoteEntryPrice
            : barEntryPrice;

        double stopDist = _cfg.HardStopR * atrVal;
        stopDist = ApplySelfLearningStopMultiplier(stopDist);
        double stopPrice = candidate.Side == TradeSide.Long
            ? entryPrice - stopDist
            : entryPrice + stopDist;
        double riskPerShare = Math.Abs(entryPrice - stopPrice);

        int positionSize = BacktestHelpers.ComputePositionSize(entryPrice, riskPerShare,
            _cfg.RiskPerTradeDollars, _cfg.AccountSize, _cfg.MaxPositionNotionalPctOfAccount, _cfg.MaxShares);
        positionSize = ApplySelfLearningPositionSize(positionSize, candidate.Source);

        return new ConductV3EntryConstructionEvaluation(entryPrice, stopPrice, riskPerShare, positionSize);
    }

    private ConductV3EntryValidityEvaluation EvaluateEntryValidity(CandidateSide candidate, ConductV3EntryConstructionEvaluation entryConstruction)
    {
        if (candidate.Source == "CONDUCT_ALT_VWAP_REVERSION"
            && candidate.Side == TradeSide.Long
            && _cfg.AlternateVwapLongMinRiskPerShare > 0
            && entryConstruction.RiskPerShare < _cfg.AlternateVwapLongMinRiskPerShare)
        {
            return ConductV3EntryValidityEvaluation.Reject("alternate-vwap-long-risk-per-share-below-min");
        }

        return EvaluateEntryValidity(entryConstruction);
    }

    internal ConductV3EntryValidityEvaluation EvaluateEntryValidity(ConductV3EntryConstructionEvaluation entryConstruction)
    {
        if (entryConstruction.RiskPerShare < _cfg.MinRiskPerShare)
            return ConductV3EntryValidityEvaluation.Reject("risk-per-share-below-min");

        if (entryConstruction.PositionSize <= 0)
            return ConductV3EntryValidityEvaluation.Reject("position-size-non-positive");

        return ConductV3EntryValidityEvaluation.Accept();
    }

    internal static int AppendRawSignal(List<BacktestSignal> signals, BacktestSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signals);

        signals.Add(signal);
        return signal.BarIndex;
    }

    internal static IReadOnlyList<BacktestSignal> ReturnRawSignals(List<BacktestSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return signals;
    }

    private void AddAlternateCandidates(
        List<CandidateSide> candidates,
        EnrichedBar row,
        double price,
        double atrVal,
        HtfBias htfBias)
    {
        if (_cfg.VwapReversionEnabled)
        {
            double vwapVal = row.Vwap;
            if (!double.IsNaN(vwapVal) && vwapVal > 0)
            {
                double distFromVwap = (price - vwapVal) / atrVal;
                if (distFromVwap < -_cfg.VwapStretchAtr && htfBias is HtfBias.Bull or HtfBias.Neutral)
                    AddCandidateOrRaiseScore(candidates, TradeSide.Long, _cfg.VwapReversionTriggerPoints, "CONDUCT_ALT_VWAP_REVERSION");
                else if (distFromVwap > _cfg.VwapStretchAtr && htfBias is HtfBias.Bear or HtfBias.Neutral)
                    AddCandidateOrRaiseScore(candidates, TradeSide.Short, _cfg.VwapReversionTriggerPoints, "CONDUCT_ALT_VWAP_REVERSION");
            }
        }

        if (_cfg.BbBounceEnabled)
        {
            double bbPctb = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
            if (bbPctb < _cfg.BbEntryPctbLow && htfBias is HtfBias.Bull or HtfBias.Neutral)
                AddCandidateOrRaiseScore(candidates, TradeSide.Long, _cfg.BbBounceTriggerPoints, "CONDUCT_ALT_BB_BOUNCE");
            else if (bbPctb > _cfg.BbEntryPctbHigh && htfBias is HtfBias.Bear or HtfBias.Neutral)
                AddCandidateOrRaiseScore(candidates, TradeSide.Short, _cfg.BbBounceTriggerPoints, "CONDUCT_ALT_BB_BOUNCE");
        }
    }

    private static void AddCandidateOrRaiseScore(List<CandidateSide> candidates, TradeSide side, int triggerScore, string source)
    {
        int existingIndex = candidates.FindIndex(candidate => candidate.Side == side);
        if (existingIndex >= 0)
        {
            if (triggerScore > candidates[existingIndex].TriggerScore)
                candidates[existingIndex] = candidates[existingIndex] with { TriggerScore = triggerScore, Source = source };

            return;
        }

        candidates.Add(new CandidateSide(side, triggerScore, source));
    }

    private double ResolvePullbackEma(EnrichedBar row)
        => _cfg.PullbackEmaPeriod <= 9 ? row.Ema9 : row.Ema21;

    private int ResolveMainTriggerScore(bool supertrendFlipTriggered, bool emaPullbackTriggered)
    {
        var score = 0;

        if (supertrendFlipTriggered)
            score = Math.Max(score, _cfg.SupertrendFlipTriggerPoints);

        if (emaPullbackTriggered)
            score = Math.Max(score, _cfg.EmaPullbackTriggerPoints);

        return score;
    }

    private static string ResolveMainTriggerSource(bool supertrendFlipTriggered, bool emaPullbackTriggered)
    {
        if (supertrendFlipTriggered && emaPullbackTriggered)
            return "CONDUCT_MAIN_HYBRID";
        if (supertrendFlipTriggered)
            return "CONDUCT_MAIN_SUPERTREND";
        if (emaPullbackTriggered)
            return "CONDUCT_MAIN_PULLBACK";

        return "CONDUCT_MAIN_UNKNOWN";
    }

    private HtfBias ComputeHtfBiasAt(DateTime ts, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        int scoreSum = 0;
        int scoreCount = 0;

        ComputeHtfBarScore(bars1h, ts, ref scoreSum, ref scoreCount);
        ComputeHtfBarScore(bars1d, ts, ref scoreSum, ref scoreCount);

        if (scoreCount == 0) return HtfBias.Neutral;

        double avg = (double)scoreSum / scoreCount;
        if (avg >= 1.5) return HtfBias.Bull;
        if (avg <= -1.5) return HtfBias.Bear;
        return HtfBias.Neutral;
    }

    private void ComputeHtfBarScore(EnrichedBar[]? bars, DateTime ts, ref int scoreSum, ref int scoreCount)
    {
        if (bars == null || bars.Length < 2) return;
        int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
        if (idx < 1) return;

        var last = bars[idx];
        var prev = bars[idx - 1];

        int emaSlope = last.Ema21 > prev.Ema21 ? 1 : -1;
        int diScore = 0;
        if (!double.IsNaN(last.Adx) && last.Adx > _cfg.AdxThreshold)
            diScore = last.PlusDi > last.MinusDi ? 1 : -1;
        int macdScore = 0;
        if (!double.IsNaN(last.MacdHist))
            macdScore = last.MacdHist > 0 ? 1 : -1;

        scoreSum += emaSlope + diScore + macdScore;
        scoreCount++;
    }

    private string ComputeMtfMomentumAt(
        DateTime ts,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m,
        TradeSide side)
    {
        int alignedCount = 0;
        int total = 0;

        CheckMtfAlignment(bars5m, ts, side, ref alignedCount, ref total);
        CheckMtfAlignment(bars15m, ts, side, ref alignedCount, ref total);

        if (total == 0)
            return "CONFLICTING";

        return alignedCount == total ? "ALIGNED" : "CONFLICTING";
    }

    private void CheckMtfAlignment(EnrichedBar[]? bars, DateTime ts, TradeSide side, ref int alignedCount, ref int total)
    {
        if (bars == null || bars.Length == 0) return;
        int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
        if (idx < 0) return;

        var last = bars[idx];
        total++;

        bool macdOk = side == TradeSide.Long ? last.MacdHist > 0 : last.MacdHist < 0;
        var (rsiLow, rsiHigh) = side == TradeSide.Long ? _cfg.RsiLongRange : _cfg.RsiShortRange;
        bool rsiOk = !double.IsNaN(last.Rsi14) && last.Rsi14 >= rsiLow && last.Rsi14 <= rsiHigh;

        if (macdOk && rsiOk) alignedCount++;
    }

    private bool PassesEntryConfirmation(EnrichedBar row, TradeSide side)
    {
        return _cfg.UseRichL2EntryConfirmation
            ? PassesRichL2EntryConfirmation(row, side)
            : PassesL2EntryConfirmation(row, side);
    }

    private bool PassesRichL2EntryConfirmation(EnrichedBar row, TradeSide side)
    {
        var anyRichSignal = false;

        if (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio > 0)
        {
            anyRichSignal = true;
            if (side == TradeSide.Long && row.ImbalanceRatio < _cfg.RichL2ImbalanceMinForLong)
                return false;
            if (side == TradeSide.Short && row.ImbalanceRatio > _cfg.RichL2ImbalanceMaxForShort)
                return false;
        }

        if (!double.IsNaN(row.DeepImbalanceRatio) && row.DeepImbalanceRatio > 0)
        {
            anyRichSignal = true;
            if (side == TradeSide.Long && row.DeepImbalanceRatio < _cfg.RichL2DeepImbalanceMinForLong)
                return false;
            if (side == TradeSide.Short && row.DeepImbalanceRatio > _cfg.RichL2DeepImbalanceMaxForShort)
                return false;
        }

        if (TryGetEntryMidPrice(row.BidPrice, row.AskPrice, out var midPrice)
            && !double.IsNaN(row.DepthWeightedMid)
            && row.DepthWeightedMid > 0)
        {
            anyRichSignal = true;
            if (side == TradeSide.Long && row.DepthWeightedMid < midPrice)
                return false;
            if (side == TradeSide.Short && row.DepthWeightedMid > midPrice)
                return false;
        }

        if (!double.IsNaN(row.BidSize) && !double.IsNaN(row.AskSize) && row.BidSize > 0 && row.AskSize > 0)
        {
            anyRichSignal = true;
            var sizeRatio = row.BidSize / row.AskSize;
            if (side == TradeSide.Long && sizeRatio < _cfg.RichL1SizeRatioMinForLong)
                return false;
            if (side == TradeSide.Short && sizeRatio > _cfg.RichL1SizeRatioMaxForShort)
                return false;
        }

        if (TryGetEntryMidPrice(row.BidPrice, row.AskPrice, out midPrice)
            && !double.IsNaN(row.LastPrice)
            && row.LastPrice > 0)
        {
            anyRichSignal = true;
            if (side == TradeSide.Long && row.LastPrice < midPrice)
                return false;
            if (side == TradeSide.Short && row.LastPrice > midPrice)
                return false;
        }

        return !anyRichSignal || PassesL2EntryConfirmation(row, side);
    }

    private static bool PassesL2EntryConfirmation(EnrichedBar row, TradeSide side)
    {
        DirectionalConfirmationEngine.TryGetL2DirectionalConfirmation(row, side, out var available, out var confirmed);
        return !available || confirmed;
    }

    private static bool TryGetEntryMidPrice(double bidPrice, double askPrice, out double midPrice)
    {
        if (!double.IsNaN(bidPrice) && !double.IsNaN(askPrice) && bidPrice > 0 && askPrice > 0)
        {
            midPrice = (bidPrice + askPrice) / 2.0;
            return true;
        }

        midPrice = double.NaN;
        return false;
    }

    private bool ShouldBypassSelfLearningSetupBlock(string source)
    {
        if (_cfg.IgnoreSelfLearningSetupBlock)
            return true;

        return _cfg.IgnoreSelfLearningSetupBlockSources.Any(configuredSource =>
            string.Equals(configuredSource, source, StringComparison.OrdinalIgnoreCase));
    }

    private int ComputeEntryScore(CandidateSide candidate, EnrichedBar row, EnrichedBar prev, TradeSide side, double atrVal)
    {
        var score = 0;

        if (!double.IsNaN(row.Sma20) && !double.IsNaN(prev.Sma20))
        {
            bool sma20Aligned = side == TradeSide.Long
                ? row.Sma20 > prev.Sma20
                : row.Sma20 < prev.Sma20;
            if (sma20Aligned)
            {
                score += _cfg.Sma20TrendAlignedPoints;
            }
        }

        if (IsMainCandidateSource(candidate.Source))
        {
            if (_cfg.MainBbFavorablePoints > 0 && IsMainBbLocationFavorable(row, side))
                score += _cfg.MainBbFavorablePoints;

            if (_cfg.MainVwapProximityPoints > 0 && IsMainVwapDistanceFavorable(row, atrVal))
                score += _cfg.MainVwapProximityPoints;
        }

        if (IsAlternateCandidateSource(candidate.Source))
        {
            if (_cfg.AlternateRsiExtremePoints > 0 && IsAlternateRsiExtreme(row, side))
                score += _cfg.AlternateRsiExtremePoints;

            if (_cfg.AlternateReversalCandlePoints > 0 && IsReversalCandle(row, prev, side))
                score += _cfg.AlternateReversalCandlePoints;

            if (_cfg.AlternateContainedStretchPoints > 0 && IsAlternateStretchContained(row, side, atrVal))
                score += _cfg.AlternateContainedStretchPoints;
        }

        return score;
    }

    private static bool IsMainCandidateSource(string source)
        => source.StartsWith("CONDUCT_MAIN_", StringComparison.Ordinal);

    private static bool IsPullbackCandidateSource(string source)
        => string.Equals(source, "CONDUCT_MAIN_PULLBACK", StringComparison.Ordinal);

    private static bool IsAlternateCandidateSource(string source)
        => source.StartsWith("CONDUCT_ALT_", StringComparison.Ordinal);

    private static bool TryGetVwapDistanceAtr(EnrichedBar row, double atrVal, out double distanceAtr)
    {
        distanceAtr = 0.0;
        if (atrVal <= 0 || double.IsNaN(row.Vwap) || row.Vwap <= 0)
            return false;

        distanceAtr = (row.Bar.Close - row.Vwap) / atrVal;
        return true;
    }

    private static bool TryGetSma20DistanceAtr(EnrichedBar row, double atrVal, out double distanceAtr)
    {
        distanceAtr = 0.0;
        if (atrVal <= 0 || double.IsNaN(row.Sma20))
            return false;

        distanceAtr = (row.Bar.Close - row.Sma20) / atrVal;
        return true;
    }

    private static ConductV3NoSignalDiagnostic CreateNoSignalDiagnostic(
        int signalIndex,
        DateTime timestampUtc,
        string rejectReason,
        string stage,
        string? candidateSet = null,
        string? candidateSource = null,
        TradeSide? candidateSide = null)
    {
        var reasonCodes = new List<string>
        {
            rejectReason,
            $"conduct-v3-stage:{stage}",
        };

        if (!string.IsNullOrWhiteSpace(candidateSet))
        {
            reasonCodes.Add($"conduct-v3-candidate-set:{candidateSet}");
        }

        if (!string.IsNullOrWhiteSpace(candidateSource))
        {
            reasonCodes.Add($"conduct-v3-source:{candidateSource}");
        }

        if (candidateSide is { } side)
        {
            reasonCodes.Add($"conduct-v3-side:{side.ToString().ToLowerInvariant()}");
        }

        return new ConductV3NoSignalDiagnostic(signalIndex, timestampUtc, rejectReason, reasonCodes.ToArray());
    }

    private bool IsMainBbLocationFavorable(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.BbPctB))
            return false;

        return side == TradeSide.Long
            ? row.BbPctB <= _cfg.MainLongMaxBbPctB
            : row.BbPctB >= _cfg.MainShortMinBbPctB;
    }

    private bool IsMainVwapDistanceFavorable(EnrichedBar row, double atrVal)
        => _cfg.MainEntryMaxVwapDeviationAtr > 0
            && TryGetVwapDistanceAtr(row, atrVal, out double distanceAtr)
            && Math.Abs(distanceAtr) <= _cfg.MainEntryMaxVwapDeviationAtr;

    private bool IsAlternateRsiExtreme(EnrichedBar row, TradeSide side)
    {
        if (double.IsNaN(row.Rsi14))
            return false;

        return side == TradeSide.Long
            ? row.Rsi14 <= _cfg.AlternateLongRsiMax
            : row.Rsi14 >= _cfg.AlternateShortRsiMin;
    }

    private static bool IsReversalCandle(EnrichedBar row, EnrichedBar prev, TradeSide side)
    {
        if (side == TradeSide.Long)
        {
            return row.IsHammer
                || row.IsBullishCandle
                || (row.Bar.Close > row.Bar.Open && row.Bar.Close >= prev.Bar.Close);
        }

        return row.IsStar
            || row.IsBearishCandle
            || (row.Bar.Close < row.Bar.Open && row.Bar.Close <= prev.Bar.Close);
    }

    private bool IsAlternateStretchContained(EnrichedBar row, TradeSide side, double atrVal)
    {
        if (_cfg.AlternateEntryMaxCountertrendMaDistAtr > 0
            && TryGetSma20DistanceAtr(row, atrVal, out double sma20DistAtr))
        {
            if (side == TradeSide.Long && sma20DistAtr < -_cfg.AlternateEntryMaxCountertrendMaDistAtr)
                return false;

            if (side == TradeSide.Short && sma20DistAtr > _cfg.AlternateEntryMaxCountertrendMaDistAtr)
                return false;
        }

        if (_cfg.AlternateEntryMaxVwapStretchAtr > 0
            && TryGetVwapDistanceAtr(row, atrVal, out double vwapDistAtr)
            && Math.Abs(vwapDistAtr) > _cfg.AlternateEntryMaxVwapStretchAtr)
        {
            return false;
        }

        return true;
    }

    private readonly record struct CandidateSide(TradeSide Side, int TriggerScore, string Source);
}

    internal sealed record ConductV3CandidateEligibilityEvaluation(
        bool Accepted,
        string MtfMomentum,
        string RejectReason)
    {
        public static ConductV3CandidateEligibilityEvaluation Accept(string mtfMomentum)
            => new(true, mtfMomentum, string.Empty);

        public static ConductV3CandidateEligibilityEvaluation Reject(string rejectReason)
            => new(false, string.Empty, rejectReason);
    }

    internal sealed record ConductV3IndicatorFilterEvaluation(
        bool Accepted,
        string RejectReason)
    {
        public static ConductV3IndicatorFilterEvaluation Accept()
            => new(true, string.Empty);

        public static ConductV3IndicatorFilterEvaluation Reject(string rejectReason)
            => new(false, rejectReason);
    }

    internal sealed record ConductV3EntryConstructionEvaluation(
        double EntryPrice,
        double StopPrice,
        double RiskPerShare,
        int PositionSize);

    internal sealed record ConductV3EntryValidityEvaluation(
        bool Accepted,
        string RejectReason)
    {
        public static ConductV3EntryValidityEvaluation Accept()
            => new(true, string.Empty);

        public static ConductV3EntryValidityEvaluation Reject(string rejectReason)
            => new(false, rejectReason);
    }

    internal sealed record ConductV3PreEntryGuardEvaluation(
        int SignalIndex,
        EnrichedBar Row,
        DateTime TimestampUtc,
        double Price,
        int EntryIndex,
        double AtrValue,
        string RejectReason);

    internal sealed record ConductV3NoSignalDiagnostic(
        int SignalIndex,
        DateTime TimestampUtc,
        string RejectReason,
        IReadOnlyList<string> ReasonCodes);

