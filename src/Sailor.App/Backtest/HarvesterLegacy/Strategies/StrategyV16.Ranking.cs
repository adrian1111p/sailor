using Sailor.App.Backtest.Engine;
using Harvester.App.Strategy;

namespace Sailor.App.Backtest.Strategies;

public sealed partial class StrategyV16
{
    private readonly record struct DirectionScorecard(
        int LongScore,
        int ShortScore,
        TradeSide? DominantSide);

    private readonly record struct ConfluenceScorecard(
        int TotalScore,
        bool RsiPass,
        bool StochPass,
        bool WillRPass,
        bool MfiPass,
        bool MacdTurnPass,
        bool AdxPass,
        bool VwapPass,
        bool DpoPass,
        bool DonchianPass,
        bool L2Pass);

    internal V16SqzBreakoutRankingSnapshot BuildRankingSnapshot(
        string symbol,
        V3LiveFeatureSnapshot features,
        LiveBacktestSignalContext context,
        int minimumEntryScore,
        OpportunityScoringSettings? opportunitySettings = null)
    {
        opportunitySettings ??= OpportunityScoringSettings.Current;
        var triggerBars = context.TriggerBars ?? [];
        var evaluationIndex = ResolveLatestRankingBarIndex(triggerBars.Length);
        var hasRow = evaluationIndex >= 0 && evaluationIndex < triggerBars.Length;
        var timestampUtc = hasRow
            ? DateTime.SpecifyKind(triggerBars[evaluationIndex].Bar.Timestamp, DateTimeKind.Utc)
            : DateTime.SpecifyKind(features.TimestampUtc, DateTimeKind.Utc);
        var evaluations = new List<V16SqzBreakoutRankingParameterEvaluation>(41);

        static bool IsPassed(IReadOnlyList<V16SqzBreakoutRankingParameterEvaluation> rows, string rankingId)
            => rows.Any(row => string.Equals(row.RankingId, rankingId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.Status, "pass", StringComparison.OrdinalIgnoreCase));

        void Add(string rankingId, bool? passed, string detail)
        {
            var status = passed switch
            {
                true => "pass",
                false => "fail",
                null => "not-applicable"
            };

            evaluations.Add(new V16SqzBreakoutRankingParameterEvaluation(
                rankingId,
                status,
                passed == true ? V16SqzBreakoutRanking.ResolvePointValue(rankingId) : 0,
                detail));
        }

        Add("A0001", triggerBars.Length > 0, $"triggerBars={triggerBars.Length}");
        Add("A0002", triggerBars.Length >= StrategyConfig.MinimumRequiredSignalHistoryBars, $"triggerBars={triggerBars.Length}/{StrategyConfig.MinimumRequiredSignalHistoryBars}");
        Add("A0003", features.IsReady, string.IsNullOrWhiteSpace(features.RejectReason) ? "ready" : features.RejectReason);
        Add("A0004", triggerBars.Length >= _cfg.SqueezeLookback + 5, $"triggerBars={triggerBars.Length}/{_cfg.SqueezeLookback + 5}");

        var currentRow = hasRow ? triggerBars[evaluationIndex] : null;
        var directionScorecard = hasRow ? BuildDirectionScorecard(triggerBars, evaluationIndex) : default;

        var entryWindowPass = false;
        var entryWindowDetail = "no-evaluable-bar";
        if (currentRow is not null)
        {
            var minuteEt = TradingTime.GetMinuteOfDayEt(currentRow.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
            {
                entryWindowDetail = "before-entry-start";
            }
            else if (minuteEt > 960 - _cfg.LastEntryMinuteBeforeClose)
            {
                entryWindowDetail = "too-close-to-close";
            }
            else if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
            {
                entryWindowDetail = "outside-entry-window";
            }
            else
            {
                entryWindowPass = true;
                entryWindowDetail = $"minuteEt={minuteEt}";
            }
        }
        Add("A0005", entryWindowPass, entryWindowDetail);

        var pricePass = currentRow is not null && currentRow.Bar.Close >= _cfg.MinPrice && currentRow.Bar.Close <= _cfg.MaxPrice;
        Add("A0006", pricePass, currentRow is null ? "no-evaluable-bar" : $"close={currentRow.Bar.Close:F2} range=[{_cfg.MinPrice:F2},{_cfg.MaxPrice:F2}]");

        var rvolPass = currentRow is not null && !double.IsNaN(currentRow.Rvol) && currentRow.Rvol >= _cfg.RvolMin;
        Add("A0007", rvolPass, currentRow is null ? "no-evaluable-bar" : $"rvol={currentRow.Rvol:F2} floor={_cfg.RvolMin:F2}");

        var liquidityPass = currentRow is not null && !double.IsNaN(currentRow.L2Liquidity) && currentRow.L2Liquidity >= _cfg.L2LiquidityMin;
        Add("A0008", liquidityPass, currentRow is null ? "no-evaluable-bar" : $"l2Liquidity={currentRow.L2Liquidity:F2} floor={_cfg.L2LiquidityMin:F2}");

        var spreadPass = currentRow is not null && !double.IsNaN(currentRow.SpreadZ) && currentRow.SpreadZ <= _cfg.SpreadZMax;
        Add("A0009", spreadPass, currentRow is null ? "no-evaluable-bar" : $"spreadZ={currentRow.SpreadZ:F2} cap={_cfg.SpreadZMax:F2}");

        var volAccelPass = currentRow is not null && !double.IsNaN(currentRow.VolAccel) && currentRow.VolAccel >= _cfg.MinVolAccel;
        Add("A0010", volAccelPass, currentRow is null ? "no-evaluable-bar" : $"volAccel={currentRow.VolAccel:F2} floor={_cfg.MinVolAccel:F2}");

        var indicatorsPass = currentRow is not null && !double.IsNaN(currentRow.Ema9) && !double.IsNaN(currentRow.Ema21) && !double.IsNaN(currentRow.Rsi14);
        Add("A0011", indicatorsPass, currentRow is null ? "no-evaluable-bar" : $"ema9={currentRow.Ema9:F2} ema21={currentRow.Ema21:F2} rsi14={currentRow.Rsi14:F2}");

        var marketRegimePass = currentRow is not null && !IsMarketRegimeBlocked(currentRow);
        Add("A0012", marketRegimePass, currentRow is null ? "no-evaluable-bar" : marketRegimePass ? "allowed" : "market-regime-blocked");

        var squeezeReleasePass = false;
        var squeezeDetail = currentRow is null ? "no-evaluable-bar" : "no-squeeze-release";
        var squeezeEndBar = -1;
        if (currentRow is not null && triggerBars.Length > 0)
        {
            var squeezeState = ComputeSqueezeStates(triggerBars);
            var bwPctile = ComputeBandwidthPercentiles(triggerBars, 50);
            squeezeReleasePass = DetectSqueezeRelease(squeezeState, bwPctile, evaluationIndex, out squeezeEndBar);
            squeezeDetail = squeezeReleasePass ? $"squeezeEndBar={squeezeEndBar}" : "no-squeeze-release";
        }
        Add("A0013", squeezeReleasePass, squeezeDetail);

        var resolvedSide = currentRow is null
            ? null
            : DetermineBreakoutDirection(triggerBars, evaluationIndex, squeezeEndBar);
        var effectiveSide = resolvedSide ?? directionScorecard.DominantSide;
        Add("A0014", resolvedSide is not null, currentRow is null
            ? "no-evaluable-bar"
            : $"longScore={directionScorecard.LongScore} shortScore={directionScorecard.ShortScore} resolved={resolvedSide?.ToString() ?? "none"} dominant={directionScorecard.DominantSide?.ToString() ?? "none"}");

        bool? weakBreakoutPass = null;
        var weakBreakoutDetail = effectiveSide is null ? "direction-unresolved" : "not-long";
        if (effectiveSide == TradeSide.Long && currentRow is not null)
        {
            weakBreakoutPass = !_cfg.RequireBullishBreakoutBarForLongs
                || IsBullishBreakoutCandle(currentRow, _cfg.LongBreakoutMinCloseLocationPct, _cfg.LongBreakoutMaxUpperWickPct);
            weakBreakoutDetail = _cfg.RequireBullishBreakoutBarForLongs
                ? $"closeLocation>={_cfg.LongBreakoutMinCloseLocationPct:F2} upperWick<={_cfg.LongBreakoutMaxUpperWickPct:F2}"
                : "disabled";
        }
        Add("A0015", weakBreakoutPass, weakBreakoutDetail);

        bool? overextendedPass = null;
        var overextendedDetail = effectiveSide is null ? "direction-unresolved" : "not-long";
        if (effectiveSide == TradeSide.Long && currentRow is not null)
        {
            overextendedPass = !_cfg.RejectOverextendedLongContinuationForLongs || !IsOverextendedLongContinuation(triggerBars, evaluationIndex);
            overextendedDetail = _cfg.RejectOverextendedLongContinuationForLongs
                ? "requires entry RSI, prior-bar RVOL, and prior-bar ATR range to stay below the overextension model"
                : "disabled";
        }
        Add("A0016", overextendedPass, overextendedDetail);

        var confluence = effectiveSide is not null && currentRow is not null
            ? BuildConfluenceScorecard(triggerBars, evaluationIndex, effectiveSide.Value)
            : default;
        Add("A0017", effectiveSide is null ? null : confluence.RsiPass, effectiveSide is null ? "direction-unresolved" : "RSI supportive range");
        Add("A0018", effectiveSide is null ? null : confluence.StochPass, effectiveSide is null ? "direction-unresolved" : "Stochastic supportive range");
        Add("A0019", effectiveSide is null ? null : confluence.WillRPass, effectiveSide is null ? "direction-unresolved" : "Williams %R supportive range");
        Add("A0020", effectiveSide is null ? null : confluence.MfiPass, effectiveSide is null ? "direction-unresolved" : "MFI supportive range");
        Add("A0021", effectiveSide is null ? null : confluence.MacdTurnPass, effectiveSide is null ? "direction-unresolved" : "MACD histogram turn");
        Add("A0022", effectiveSide is null ? null : confluence.AdxPass, effectiveSide is null ? "direction-unresolved" : "ADX and DI alignment");
        Add("A0023", effectiveSide is null ? null : confluence.VwapPass, effectiveSide is null ? "direction-unresolved" : "VWAP context");
        Add("A0024", effectiveSide is null ? null : confluence.DpoPass, effectiveSide is null ? "direction-unresolved" : "DPO cycle sign");
        Add("A0025", effectiveSide is null ? null : confluence.DonchianPass, effectiveSide is null ? "direction-unresolved" : "Donchian percentile");
        Add("A0026", effectiveSide is null ? null : confluence.L2Pass, effectiveSide is null ? "direction-unresolved" : "OFI and imbalance support");

        var adaptiveControls = effectiveSide is not null && currentRow is not null
            ? BuildAdaptiveEntryControls(triggerBars, evaluationIndex, effectiveSide.Value)
            : default;
        var baseRequiredScore = effectiveSide switch
        {
            TradeSide.Long => _cfg.LongMinConfluenceScore ?? _cfg.MinConfluenceScore,
            TradeSide.Short => _cfg.ShortMinConfluenceScore ?? _cfg.MinConfluenceScore,
            _ => (int?)null,
        };
        var requiredScore = baseRequiredScore.HasValue
            ? baseRequiredScore.Value + adaptiveControls.ScoreBoost
            : (int?)null;

        bool? htfBiasPass = null;
        var htfBiasDetail = effectiveSide is null || currentRow is null ? "direction-unresolved" : "not-evaluated";
        if (effectiveSide is not null && currentRow is not null)
        {
            var htfBias = ComputeHtfBias(currentRow.Bar.Timestamp, context.Bars15m, context.Bars1h, context.Bars1d);
            htfBiasPass = PassesHtfBias(effectiveSide.Value, htfBias, confluence.TotalScore, out var htfReason);
            htfBiasDetail = htfBiasPass == true
                ? $"htfBias={htfBias}"
                : $"htfBias={htfReason}";
        }
        Add("A0027", htfBiasPass, htfBiasDetail);

        bool? confluencePass = null;
        var confluenceDetail = effectiveSide is null || requiredScore is null
            ? "direction-unresolved"
            : $"confluence={confluence.TotalScore} required={requiredScore.Value} base={baseRequiredScore.GetValueOrDefault()} boost={adaptiveControls.ScoreBoost}";
        if (effectiveSide is not null && requiredScore is not null)
        {
            confluencePass = confluence.TotalScore >= requiredScore.Value;
        }
        Add("A0028", confluencePass, confluenceDetail);

        bool? l2EntryPass = null;
        var l2EntryDetail = effectiveSide is null || currentRow is null ? "direction-unresolved" : "not-evaluated";
        if (effectiveSide is not null && currentRow is not null)
        {
            var requireL2EntryFilter = _cfg.RequireL2EntryFilter
                || (_cfg.RequireL2EntryFilterForLongsOnly && effectiveSide == TradeSide.Long);
            l2EntryPass = !requireL2EntryFilter || PassesL2EntryFilter(currentRow, effectiveSide.Value);
            l2EntryDetail = requireL2EntryFilter
                ? $"ofi={currentRow.OfiSignal:F2} imbalance={currentRow.ImbalanceRatio:F2}"
                : "not-required";
        }
        Add("A0029", l2EntryPass, l2EntryDetail);

        bool? indecisionPass = currentRow is null ? false : true;
        var indecisionDetail = currentRow is null ? "no-evaluable-bar" : "disabled";
        if (currentRow is not null && _cfg.RejectIndecisionBar)
        {
            var body = Math.Abs(currentRow.Bar.Close - currentRow.Bar.Open);
            var range = currentRow.Bar.High - currentRow.Bar.Low;
            indecisionPass = range > 0 && body / range > _cfg.IndecisionBarMaxBodyPct;
            indecisionDetail = range <= 0
                ? "zero-range-bar"
                : $"bodyPct={body / range:F2} max={_cfg.IndecisionBarMaxBodyPct:F2}";
        }
        Add("A0030", indecisionPass, indecisionDetail);

        var selfLearningPass = !IsSelfLearningBlocked("V16_SQZ");
        Add("A0031", selfLearningPass, selfLearningPass ? "clear" : "self-learning-blocked");

        bool? riskPass = null;
        bool? positionSizePass = null;
        bool? signalBarPass = null;
        double? entryPrice = null;
        double? stopPrice = null;
        double? riskPerShare = null;
        int? positionSize = null;
        int? signalBarIndex = null;

        if (effectiveSide is not null && currentRow is not null && squeezeEndBar >= 0)
        {
            entryPrice = _cfg.UseNextBarOpenEntry && evaluationIndex + 1 < triggerBars.Length
                ? triggerBars[evaluationIndex + 1].Bar.Open
                : currentRow.Bar.Close;

            stopPrice = ComputeStopPrice(triggerBars, evaluationIndex, squeezeEndBar, effectiveSide.Value, currentRow.Atr14);
            var rawDistance = Math.Abs(entryPrice.Value - stopPrice.Value);
            rawDistance *= adaptiveControls.StopDistanceMultiplier;
            var adjustedDistance = ApplySelfLearningStopMultiplier(rawDistance);
            stopPrice = adjustedDistance != rawDistance
                ? effectiveSide == TradeSide.Long ? entryPrice.Value - adjustedDistance : entryPrice.Value + adjustedDistance
                : effectiveSide == TradeSide.Long ? entryPrice.Value - rawDistance : entryPrice.Value + rawDistance;
            riskPerShare = Math.Abs(entryPrice.Value - stopPrice.Value);
            riskPass = riskPerShare >= _cfg.MinRiskPerShare && riskPerShare >= 0.001;

            if (riskPass == true)
            {
                positionSize = BacktestHelpers.ComputePositionSize(
                    entryPrice.Value,
                    riskPerShare.Value,
                    _cfg.RiskPerTradeDollars * adaptiveControls.RiskScale,
                    _cfg.AccountSize,
                    _cfg.MaxPositionNotionalPctOfAccount,
                    _cfg.MaxShares);
                positionSize = (int)Math.Floor(positionSize.Value * adaptiveControls.PositionScale);
                positionSize = ApplySelfLearningPositionSize(positionSize.Value, "V16_SQZ");
                positionSizePass = positionSize > 0;
            }
            else
            {
                positionSizePass = false;
            }

            signalBarIndex = _cfg.UseNextBarOpenEntry ? evaluationIndex + 1 : evaluationIndex;
            signalBarPass = signalBarIndex < triggerBars.Length;
        }

        Add("A0032", riskPass, riskPerShare.HasValue ? $"riskPerShare={riskPerShare.Value:F4} floor={Math.Max(_cfg.MinRiskPerShare, 0.001):F4}" : "not-evaluated");
        Add("A0033", positionSizePass, positionSize.HasValue ? $"positionSize={positionSize.Value}" : "not-evaluated");
        Add("A0034", signalBarPass, signalBarIndex.HasValue ? $"signalBar={signalBarIndex.Value} triggerBars={triggerBars.Length}" : "not-evaluated");

        EntryGateEvaluation? entryGateEvaluation = null;
        if (currentRow is not null
            && effectiveSide is not null
            && IsPassed(evaluations, "A0004")
            && entryWindowPass
            && pricePass
            && rvolPass
            && liquidityPass
            && spreadPass
            && volAccelPass
            && indicatorsPass
            && marketRegimePass
            && squeezeReleasePass
            && resolvedSide is not null
            && weakBreakoutPass is not false
            && overextendedPass is not false
            && htfBiasPass is not false
            && confluencePass is not false
            && l2EntryPass is not false
            && indecisionPass is not false
            && selfLearningPass
            && riskPass is true
            && positionSizePass is true
            && signalBarPass is true
            && entryPrice.HasValue
            && stopPrice.HasValue
            && riskPerShare.HasValue
            && positionSize.HasValue
            && signalBarIndex.HasValue)
        {
            var signal = new BacktestSignal(
                signalBarIndex.Value,
                triggerBars[signalBarIndex.Value].Bar.Timestamp,
                effectiveSide.Value,
                entryPrice.Value,
                stopPrice.Value,
                riskPerShare.Value,
                positionSize.Value,
                currentRow.Atr14,
                HtfBias.Neutral,
                string.Empty,
                "V16_SQZ",
                confluence.TotalScore);
            entryGateEvaluation = BacktestStrategyBase.EvaluateEntryGates(signal, triggerBars, minimumEntryScore, opportunitySettings);
        }

        var softGateReasons = entryGateEvaluation?.SoftGates
            .Select(gate => gate.ReasonCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
        Add("B0001", entryGateEvaluation is null ? null : !softGateReasons.Contains("score-below-min"), entryGateEvaluation is null ? "raw-signal-not-built" : $"minimumEntryScore={minimumEntryScore}");
        Add("B0002", entryGateEvaluation is null ? null : !softGateReasons.Contains(effectiveSide == TradeSide.Long ? "entry-bar-not-bullish" : "entry-bar-not-bearish"), entryGateEvaluation is null ? "raw-signal-not-built" : "shared entry-candle gate");
        Add("B0003", entryGateEvaluation is null ? null : !softGateReasons.Contains("second-confirmation-bar-missing"), entryGateEvaluation is null ? "raw-signal-not-built" : "shared second-confirmation gate");
        Add("B0004", entryGateEvaluation is null ? null : !softGateReasons.Contains("l1-trend-not-confirmed"), entryGateEvaluation is null ? "raw-signal-not-built" : "shared L1 confirmation gate");
        Add("B0005", entryGateEvaluation is null ? null : !softGateReasons.Contains("l2-trend-not-confirmed"), entryGateEvaluation is null ? "raw-signal-not-built" : "shared L2 confirmation gate");
        Add("B0006", entryGateEvaluation is null ? null : !string.Equals(entryGateEvaluation.RejectReason, "below-quality-floor", StringComparison.OrdinalIgnoreCase), entryGateEvaluation is null ? "raw-signal-not-built" : $"rejected={entryGateEvaluation.Rejected} hard={entryGateEvaluation.HardRejected} opportunityScore={entryGateEvaluation.OpportunityScore:F2} reason={entryGateEvaluation.RejectReason}");
        Add("B0013", entryGateEvaluation is null ? null : !entryGateEvaluation.Rejected, entryGateEvaluation is null ? "raw-signal-not-built" : entryGateEvaluation.Rejected ? entryGateEvaluation.RejectReason : "execution-ready");

        return new V16SqzBreakoutRankingSnapshot(
            Symbol: symbol,
            TimestampUtc: timestampUtc,
            SuggestedSide: effectiveSide?.ToString() ?? "Unresolved",
            TerminalStage: string.Empty,
            TerminalReason: string.Empty,
            CurrentState: string.Empty,
            FinalDetail: string.Empty,
            Rank: 0,
            SourceTag: string.Empty,
            PhaseTag: string.Empty,
            LastRefreshUtc: null,
            TotalPoints: evaluations.Sum(evaluation => evaluation.EarnedPoints),
            PossiblePoints: V16SqzBreakoutRanking.MaximumPossiblePoints,
            PassedParameters: evaluations.Count(evaluation => string.Equals(evaluation.Status, "pass", StringComparison.OrdinalIgnoreCase)),
            FailedParameters: evaluations.Count(evaluation => string.Equals(evaluation.Status, "fail", StringComparison.OrdinalIgnoreCase)),
            NotApplicableParameters: evaluations.Count(evaluation => string.Equals(evaluation.Status, "not-applicable", StringComparison.OrdinalIgnoreCase)),
            DirectionLongScore: directionScorecard.LongScore,
            DirectionShortScore: directionScorecard.ShortScore,
            ConfluenceScore: effectiveSide is null ? null : confluence.TotalScore,
            RequiredConfluenceScore: requiredScore,
            CandidateScore: null,
            RiskPerShare: riskPerShare,
            PositionSize: positionSize,
            Parameters: evaluations.ToArray());
    }

    private int ResolveLatestRankingBarIndex(int triggerBarCount)
    {
        if (triggerBarCount <= 0)
        {
            return -1;
        }

        return _cfg.UseNextBarOpenEntry
            ? triggerBarCount - 2
            : triggerBarCount - 1;
    }

    private DirectionScorecard BuildDirectionScorecard(EnrichedBar[] bars, int currentBar)
    {
        var row = bars[currentBar];
        var aboveBbMid = row.Bar.Close > row.BbMid;
        var belowBbMid = row.Bar.Close < row.BbMid;
        var macdBullish = !double.IsNaN(row.MacdHist) && row.MacdHist > 0;
        var macdBearish = !double.IsNaN(row.MacdHist) && row.MacdHist < 0;

        var macdTurningBull = false;
        var macdTurningBear = false;
        if (currentBar > 0 && !double.IsNaN(row.MacdHist) && !double.IsNaN(bars[currentBar - 1].MacdHist))
        {
            var histDelta = row.MacdHist - bars[currentBar - 1].MacdHist;
            macdTurningBull = histDelta > 0;
            macdTurningBear = histDelta < 0;
        }

        var ema9AboveEma21 = !double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21) && row.Ema9 > row.Ema21;
        var stBullish = !double.IsNaN(row.StDirection) && row.StDirection > 0;
        var stBearish = !double.IsNaN(row.StDirection) && row.StDirection < 0;

        var longScore = 0;
        var shortScore = 0;

        if (aboveBbMid)
        {
            longScore += 2;
        }
        else if (belowBbMid)
        {
            shortScore += 2;
        }

        if (macdBullish)
        {
            longScore++;
        }
        else if (macdBearish)
        {
            shortScore++;
        }

        if (macdTurningBull)
        {
            longScore++;
        }
        else if (macdTurningBear)
        {
            shortScore++;
        }

        if (ema9AboveEma21)
        {
            longScore++;
        }
        else
        {
            shortScore++;
        }

        if (stBullish)
        {
            longScore++;
        }
        else if (stBearish)
        {
            shortScore++;
        }

        var dominantSide = longScore > shortScore
            ? TradeSide.Long
            : shortScore > longScore
                ? TradeSide.Short
                : (TradeSide?)null;
        return new DirectionScorecard(longScore, shortScore, dominantSide);
    }

    private ConfluenceScorecard BuildConfluenceScorecard(EnrichedBar[] bars, int idx, TradeSide side)
    {
        var row = bars[idx];

        var rsiPass = !double.IsNaN(row.Rsi14)
            && (side == TradeSide.Long
                ? row.Rsi14 >= _cfg.RsiLongMin && row.Rsi14 <= _cfg.RsiLongMax
                : row.Rsi14 >= _cfg.RsiShortMin && row.Rsi14 <= _cfg.RsiShortMax);

        var stochPass = !double.IsNaN(row.StochK)
            && !double.IsNaN(row.StochD)
            && (side == TradeSide.Long
                ? row.StochK >= _cfg.StochLongMin && row.StochK > row.StochD
                : row.StochK <= _cfg.StochShortMax && row.StochK < row.StochD);

        var willRPass = !double.IsNaN(row.WillR14)
            && (side == TradeSide.Long
                ? row.WillR14 >= _cfg.WillRLongMax
                : row.WillR14 <= _cfg.WillRShortMin);

        var mfiPass = !double.IsNaN(row.Mfi14)
            && (side == TradeSide.Long
                ? row.Mfi14 >= _cfg.MfiLongMin
                : row.Mfi14 <= _cfg.MfiShortMax);

        var macdTurnPass = idx > 0
            && !double.IsNaN(row.MacdHist)
            && !double.IsNaN(bars[idx - 1].MacdHist)
            && (side == TradeSide.Long
                ? row.MacdHist - bars[idx - 1].MacdHist > 0
                : row.MacdHist - bars[idx - 1].MacdHist < 0);

        var adxPass = !double.IsNaN(row.Adx)
            && row.Adx >= _cfg.AdxMin
            && row.Adx <= _cfg.AdxMax
            && !double.IsNaN(row.PlusDi)
            && !double.IsNaN(row.MinusDi)
            && (side == TradeSide.Long ? row.PlusDi > row.MinusDi : row.MinusDi > row.PlusDi);

        var vwapPass = false;
        if (!double.IsNaN(row.Vwap))
        {
            var vwapDistance = (row.Bar.Close - row.Vwap) / Math.Max(0.01, row.Atr14);
            vwapPass = side == TradeSide.Long ? vwapDistance <= 0.5 : vwapDistance >= -0.5;
        }

        var dpoPass = !double.IsNaN(row.Dpo20)
            && (side == TradeSide.Long ? row.Dpo20 > 0 : row.Dpo20 < 0);

        var donchianPass = !double.IsNaN(row.DcPct)
            && (side == TradeSide.Long ? row.DcPct >= 0.60 : row.DcPct <= 0.40);

        var l2Pass = !double.IsNaN(row.OfiSignal)
            && !double.IsNaN(row.ImbalanceRatio)
            && (side == TradeSide.Long
                ? row.OfiSignal > 0 && row.ImbalanceRatio >= 0.85
                : row.OfiSignal < 0 && row.ImbalanceRatio <= 1.15);

        var totalScore = 0;
        if (rsiPass) totalScore++;
        if (stochPass) totalScore++;
        if (willRPass) totalScore++;
        if (mfiPass) totalScore++;
        if (macdTurnPass) totalScore++;
        if (adxPass) totalScore++;
        if (vwapPass) totalScore++;
        if (dpoPass) totalScore++;
        if (donchianPass) totalScore++;
        if (l2Pass) totalScore++;

        return new ConfluenceScorecard(
            totalScore,
            rsiPass,
            stochPass,
            willRPass,
            mfiPass,
            macdTurnPass,
            adxPass,
            vwapPass,
            dpoPass,
            donchianPass,
            l2Pass);
    }
}
