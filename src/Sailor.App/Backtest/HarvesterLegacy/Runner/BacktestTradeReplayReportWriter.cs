using Sailor.App.Backtest;
using Sailor.App.Backtest.DataFetcher;
using Sailor.App.Backtest.Engine;
using Harvester.App.IBKR.Runtime;
using Harvester.App.Strategy;
using Harvester.Contracts;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sailor.App.Backtest.Runner;

internal static class BacktestTradeReplayReportWriter
{
    private const int ReplayContextBarsBeforeEntry = 7;
    private const int ReplayContextBarsAfterExit = 3;
    private static readonly V3LiveConfig ApexInsightConfig = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string WriteComparisonReport(
        IReadOnlyList<StrategyComparisonRow> summaryRows,
        IReadOnlyList<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> trades,
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        string reportsDir,
        SelfLearningSignalAdapter? selfLearning,
        RealityModelProfile? realityModelProfile = null,
        IReadOnlyDictionary<string, PairReplayTradeResult>? pairedTradesByIntentId = null,
        bool includeSyntheticPairedOppositePreview = false)
    {
        Directory.CreateDirectory(reportsDir);

        var generatedUtc = DateTime.UtcNow;
        var slug = generatedUtc.ToString("yyyyMMdd_HHmmss");
        var htmlPath = Path.Combine(reportsDir, $"backtest_trade_replay_{slug}.html");
        var jsonPath = Path.Combine(reportsDir, $"backtest_trade_replay_{slug}.json");

        var reportPayload = BuildPayload(
            summaryRows,
            trades,
            allData,
            selfLearning,
            generatedUtc,
            realityModelProfile,
            pairedTradesByIntentId,
            includeSyntheticPairedOppositePreview);
        var json = JsonSerializer.Serialize(reportPayload, JsonOptions);
        File.WriteAllText(jsonPath, json, Encoding.UTF8);
        File.WriteAllText(htmlPath, BuildHtml(reportPayload, json), Encoding.UTF8);

        return htmlPath;
    }

    private static object BuildPayload(
        IReadOnlyList<StrategyComparisonRow> summaryRows,
        IReadOnlyList<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> trades,
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        SelfLearningSignalAdapter? selfLearning,
        DateTime generatedUtc,
        RealityModelProfile? realityModelProfile,
        IReadOnlyDictionary<string, PairReplayTradeResult>? pairedTradesByIntentId,
        bool includeSyntheticPairedOppositePreview)
    {
        var selectedRealityModelProfile = realityModelProfile ?? RealityModelProfileCatalog.CreateBacktestComparisonDefault();
        var decompositions = StrategyComparisonRunner.BuildReplayDecomposition(trades, allData);
        var executedRunPassed = summaryRows.Any(row => row.Trades > 0);
        var comparisonProfiles = RealityModelProfileCatalog.BuildComparisonMatrix(selectedRealityModelProfile, executedRunPassed);
        var promotionRealityProfileSatisfied = comparisonProfiles.Any(profile => profile.PromotionQualified);
        var strongestEvidenceAnchor = summaryRows.FirstOrDefault(row => row.StrongestEvidenceAnchor) ?? summaryRows.FirstOrDefault();
        var recommendedHardEnvironmentProfileId = strongestEvidenceAnchor is null ? string.Empty : "hard-environment-learning";
        var adaptiveRecoveryActions = summaryRows
            .SelectMany(row => row.AdaptiveRecoveryTasks)
            .Select(task => task.Action)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(action => action, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var governorStoppedStrategyCount = summaryRows.Count(row => row.GovernorStops > 0);
        var negativePfOrPnlStrategyCount = summaryRows.Count(row => row.TotalPnl <= 0.0 || row.ProfitFactor < 1.0);
        var repairActionStrategyCount = summaryRows.Count(row => row.AdaptiveRecoveryTasks.Count > 0);
        var strategyVariantIds = trades
            .Select(trade =>
            {
                var variantId = ConductV3VariantManifestResolver.ResolveBacktestVariantEvidenceId(trade.Strategy, trade.Variant);
                return string.IsNullOrWhiteSpace(variantId)
                    ? V16VariantManifestResolver.ResolveBacktestVariantEvidenceId(trade.Strategy, trade.Variant)
                    : variantId;
            })
            .Where(variantId => !string.IsNullOrWhiteSpace(variantId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(variantId => variantId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var observedBranches = ConductV3VariantManifestResolver.ResolveObservedBranches(
            trades.SelectMany(EnumerateBranchHints));
        string[] manifestEnabledBranches = Array.Empty<string>();
        string[] manifestDisabledBranches = Array.Empty<string>();
        if (strategyVariantIds.Length == 1)
        {
            if (ConductV3VariantManifestResolver.TryResolveManifestByVariantId(strategyVariantIds[0], out var resolvedConductManifest))
            {
                manifestEnabledBranches = resolvedConductManifest.EnabledBranches.ToArray();
                manifestDisabledBranches = resolvedConductManifest.DisabledBranches.ToArray();
            }
            else if (V16VariantManifestResolver.TryResolveManifestByVariantId(strategyVariantIds[0], out var resolvedV16Manifest))
            {
                manifestEnabledBranches = resolvedV16Manifest.EnabledBranches.ToArray();
                manifestDisabledBranches = resolvedV16Manifest.DisabledBranches.ToArray();
            }
        }
        var replayTrades = new List<object>(trades.Count);
        var historicalFeatureSources = new Dictionary<string, HistoricalReplayFeatureSource>(StringComparer.OrdinalIgnoreCase);
        var strategyTradeCounts = trades
            .GroupBy(x => x.Strategy, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var strategyTradeOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var strategyVariantFactories = BuildStrategyVariantFactoryLookup();

        foreach (var (strategy, variant, symbol, trade) in trades)
        {
            if (!allData.TryGetValue(symbol, out var data))
                continue;

            var strategyTradeNumber = strategyTradeOrdinals.TryGetValue(strategy, out var ordinal)
                ? ordinal + 1
                : 1;
            strategyTradeOrdinals[strategy] = strategyTradeNumber;

            if (!historicalFeatureSources.TryGetValue(symbol, out var historicalFeatureSource))
            {
                historicalFeatureSource = new HistoricalReplayFeatureSource(symbol, data.Trigger);
                historicalFeatureSources[symbol] = historicalFeatureSource;
            }

            var bars = BuildBars(trade, data.Trigger);
            var supplementalChart = BuildSupplementalChartPayload(strategy, trade, data.Ctx5m, data.Ctx15m);
            var metrics = ComputeMetrics(trade, data.Trigger);
            var setupIdentity = ResolveBacktestSetupIdentity(strategy, variant, trade.SubStrategy);
            var resolvedSelectedSubStrategy = setupIdentity.SelectedSubStrategy ?? setupIdentity.CompositeSetup;
            var setup = resolvedSelectedSubStrategy;
            var apexBonus = BuildApexBonusPayload(
                trade,
                data.Trigger,
                historicalFeatureSource.GetEntryFeatureSnapshot(trade.EntryBar, ApexInsightConfig));
            var routeDecision = BuildRouteDecisionPayload(trade, symbol, setupIdentity.CompositeSetup, historicalFeatureSource.GetEntryFeatureSnapshot(trade.EntryBar, ApexInsightConfig));
            var pairedOppositePreview = includeSyntheticPairedOppositePreview
                ? BuildPairedOppositePreviewPayload(
                    strategy,
                    variant,
                    symbol,
                    trade,
                    strategyTradeNumber,
                    strategyTradeCounts[strategy],
                    data.Trigger,
                    selfLearning,
                    strategyVariantFactories)
                : null;
            var pairedSecondLegPreview = trade.SelectedEntryIntent is not null
                && pairedTradesByIntentId is not null
                && pairedTradesByIntentId.TryGetValue(trade.SelectedEntryIntent.IntentId, out var pairedTrade)
                    ? BuildPairedSecondLegPreviewPayload(
                        strategy,
                        variant,
                        symbol,
                        trade,
                        pairedTrade,
                        strategyTradeNumber,
                        strategyTradeCounts[strategy])
                    : null;

            string? setupAction = null;
            double appliedStopMultiplier = 1.0;
            double appliedPositionMultiplier = 1.0;
            bool hasExitOverrides = false;

            if (selfLearning is { IsLoaded: true })
            {
                appliedStopMultiplier = selfLearning.GetEffectiveStopMultiplier(setup);
                appliedPositionMultiplier = selfLearning.GetEffectivePositionSizeMultiplier(symbol, setup);
                hasExitOverrides = selfLearning.GetExitOverrides(setup) is not null;
                if (selfLearning.TryGetSetupRecommendation(setup, out var recommendation))
                    setupAction = recommendation.Action;
            }

            replayTrades.Add(new
            {
                strategy,
                variant,
                symbol,
                strategyTradeNumber,
                strategyTradeCount = strategyTradeCounts[strategy],
                tradeId = $"BT_{strategy}_{variant}_{symbol}_{trade.EffectiveEntryTime:yyyyMMdd_HHmmss}_{trade.EffectiveEntryBar}_{trade.ExitBar}",
                setup = setupIdentity.CompositeSetup,
                compositeSetup = setupIdentity.CompositeSetup,
                selectedSubStrategy = resolvedSelectedSubStrategy,
                normalizedPatternKey = setupIdentity.NormalizedPattern.PatternKey,
                normalizedPatternFamily = setupIdentity.NormalizedPattern.PatternFamily,
                subStrategy = trade.SubStrategy,
                side = trade.Side.ToString(),
                quantity = trade.PositionSize,
                entryTimeUtc = trade.EffectiveEntryTime.ToString("o"),
                originalEntryTimeUtc = trade.OriginalEntryTimeResolved.ToString("o"),
                originalEntryBar = trade.OriginalEntryBarResolved,
                shuffledEntryTimeUtc = trade.ShuffledEntryTime?.ToString("o"),
                shuffledEntryBar = trade.ShuffledEntryBar,
                shuffleReason = string.IsNullOrWhiteSpace(trade.ShuffleReason) ? null : trade.ShuffleReason,
                exitTimeUtc = trade.ExitTime.ToString("o"),
                entryPrice = Math.Round(trade.EntryPrice, 4),
                exitPrice = Math.Round(trade.ExitPrice, 4),
                stopPrice = Math.Round(trade.StopPrice, 4),
                pnlUsd = Math.Round(trade.Pnl, 4),
                pnlPerShareUsd = trade.PositionSize > 0 ? Math.Round(trade.Pnl / trade.PositionSize, 6) : 0.0,
                pnlR = Math.Round(trade.PnlR, 4),
                peakR = Math.Round(trade.PeakR, 4),
                barsHeld = trade.BarsHeld,
                exitReason = trade.ExitReason.ToString(),
                governorBucket = trade.GovernorBucket,
                governorTriggeredStop = trade.GovernorTriggeredStop,
                governorStopReason = trade.GovernorStopReason,
                apexBonus,
                routeDecision,
                pairedOppositePreview,
                pairedSecondLegPreview,
                mfeUsd = Math.Round(metrics.MfeUsd, 4),
                maeUsd = Math.Round(metrics.MaeUsd, 4),
                peakFavorablePrice = Math.Round(metrics.PeakFavorablePrice, 4),
                peakFavorableTimeUtc = metrics.PeakFavorableTimestampUtc?.ToString("o"),
                peakAdversePrice = Math.Round(metrics.PeakAdversePrice, 4),
                peakAdverseTimeUtc = metrics.PeakAdverseTimestampUtc?.ToString("o"),
                appliedSelfLearning = new
                {
                    loaded = selfLearning?.IsLoaded ?? false,
                    version = selfLearning?.LoadedVersion,
                    stopMultiplier = Math.Round(appliedStopMultiplier, 4),
                    positionMultiplier = Math.Round(appliedPositionMultiplier, 4),
                    setupAction,
                    hasExitOverrides,
                },
                selectedEntryIntent = trade.SelectedEntryIntent is null
                    ? null
                    : new
                    {
                        trade.SelectedEntryIntent.IntentId,
                        signal = new
                        {
                            trade.SelectedEntryIntent.Signal.BarIndex,
                            timestampUtc = trade.SelectedEntryIntent.Signal.Timestamp.ToString("o"),
                            side = trade.SelectedEntryIntent.Signal.Side.ToString(),
                            entryPrice = Math.Round(trade.SelectedEntryIntent.Signal.EntryPrice, 4),
                            stopPrice = Math.Round(trade.SelectedEntryIntent.Signal.StopPrice, 4),
                            riskPerShare = Math.Round(trade.SelectedEntryIntent.Signal.RiskPerShare, 4),
                            trade.SelectedEntryIntent.Signal.PositionSize,
                            trade.SelectedEntryIntent.Signal.SubStrategy,
                            trade.SelectedEntryIntent.Signal.EntryScore,
                        },
                        exitProfile = trade.SelectedEntryIntent.ExitProfile,
                        lifecycleMetadata = trade.SelectedEntryIntent.LifecycleMetadata,
                    },
                lifecycleFinalState = trade.LifecycleFinalState,
                lifecycleEvents = (trade.LifecycleEvents ?? Array.Empty<BacktestTradeLifecycleEvent>()).Select(evt => new
                {
                    eventType = evt.EventType.ToString(),
                    evt.BarIndex,
                    timestampUtc = evt.Timestamp.ToString("o"),
                    price = Math.Round(evt.Price, 4),
                    evt.Quantity,
                    evt.Reason,
                    evt.Detail,
                    referencePrice = evt.ReferencePrice.HasValue ? Math.Round(evt.ReferencePrice.Value, 4) : (double?)null,
                    rMultiple = evt.RMultiple.HasValue ? Math.Round(evt.RMultiple.Value, 4) : (double?)null,
                }).ToArray(),
                actionTypes = (trade.ReplayActions ?? Array.Empty<BacktestTradeAction>())
                    .Select(action => action.ActionType)
                    .Where(actionType => !string.IsNullOrWhiteSpace(actionType))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(actionType => actionType, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                annotations = BuildAnnotations(trade),
                actions = (trade.ReplayActions ?? Array.Empty<BacktestTradeAction>()).Select(action => new
                {
                    barIndex = action.BarIndex,
                    timestampUtc = action.Timestamp.ToString("o"),
                    price = Math.Round(action.Price, 4),
                    actionType = action.ActionType,
                    description = action.Description,
                    referencePrice = action.ReferencePrice.HasValue ? Math.Round(action.ReferencePrice.Value, 4) : (double?)null,
                    rMultiple = action.RMultiple.HasValue ? Math.Round(action.RMultiple.Value, 4) : (double?)null,
                }).ToArray(),
                bars,
                supplementalChart,
            });
        }

        return new
        {
            generatedUtc = generatedUtc.ToString("o"),
            acceptanceSharpeBaseline = StrategyComparisonRunner.AcceptanceSharpeBaseline,
            pessimisticAcceptancePolicy = "pass-or-research-only",
            strategyVariantIds,
            enabledBranches = manifestEnabledBranches,
            disabledBranches = manifestDisabledBranches,
            observedBranches,
            topReasonCodes = Array.Empty<string>(),
            realityModelProfileId = selectedRealityModelProfile.ProfileId,
            realityModelProfileGrade = selectedRealityModelProfile.GradeLabel,
            realityModelProfileHash = selectedRealityModelProfile.Hash,
            executionAssumptionsDeclared = true,
            executionAssumptionsSummary = selectedRealityModelProfile.AssumptionSummary,
            realityModelProfiles = comparisonProfiles,
            promotionRealityProfileSatisfied,
            strongestEvidenceStrategy = strongestEvidenceAnchor?.Strategy ?? string.Empty,
            strongestEvidenceVariant = strongestEvidenceAnchor?.Variant ?? string.Empty,
            strongestEvidencePromotionScore = strongestEvidenceAnchor?.PromotionScore ?? 0.0,
            hardEnvironmentLearningProfileId = recommendedHardEnvironmentProfileId,
            hardEnvironmentLearningProfileRecommended = strongestEvidenceAnchor is not null,
            hardEnvironmentLearningBaseStrategy = strongestEvidenceAnchor?.Strategy ?? string.Empty,
            hardEnvironmentLearningBaseVariant = strongestEvidenceAnchor?.Variant ?? string.Empty,
            hardEnvironmentLearningPreservesEligibility = true,
            governorStoppedStrategyCount,
            negativePfOrPnlStrategyCount,
            repairActionStrategyCount,
            adaptiveRecoveryTaskCount = adaptiveRecoveryActions.Length,
            adaptiveRecoveryActions,
            hardEnvironmentLearningProfile = strongestEvidenceAnchor is null
                ? null
                : new
                {
                    profileId = recommendedHardEnvironmentProfileId,
                    baseStrategy = strongestEvidenceAnchor.Strategy,
                    baseVariant = strongestEvidenceAnchor.Variant,
                    preservesSymbolEligibility = true,
                    preservesTimeBucketEligibility = true,
                    description = "Routes every configured symbol and trading-hour bucket through the safest currently validated behavior while repair tasks improve weak contexts instead of excluding them."
                },
            summary = summaryRows.Select(BuildSerializableSummaryRow).ToArray(),
            decompositions,
            selfLearning = BuildSelfLearningSummary(selfLearning),
            totals = new
            {
                strategyCount = summaryRows.Count,
                tradeCount = replayTrades.Count,
                symbolCount = trades.Select(t => t.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            },
            trades = replayTrades,
        };
    }

    private static object BuildSerializableSummaryRow(StrategyComparisonRow row)
    {
        return new
        {
            row.Strategy,
            row.Variant,
            row.Symbols,
            row.Trades,
            row.MeetsMinTrades,
            row.GovernorStops,
            row.GovernorStopReason,
            row.PromotionScore,
            PromotionScoreDisplay = FormatSummaryNumber(row.PromotionScore),
            row.StrictPromotionPass,
            row.PessimisticPromotionPass,
            row.HardEnvironmentEligible,
            row.StrongestEvidenceAnchor,
            AdaptiveRecoveryTasks = row.AdaptiveRecoveryTasks.Select(task => new
            {
                task.Category,
                task.Action,
                task.Reason,
                task.Scope,
                task.Evidence,
                task.Severity,
            }).ToArray(),
            WinRate = SanitizeNumber(row.WinRate),
            WinRateDisplay = FormatSummaryPercent(row.WinRate),
            ProfitFactor = SanitizeNumber(row.ProfitFactor),
            ProfitFactorDisplay = FormatSummaryNumber(row.ProfitFactor),
            Sharpe = SanitizeNumber(row.Sharpe),
            SharpeDisplay = FormatSummaryNumber(row.Sharpe),
            Sortino = SanitizeNumber(row.Sortino),
            SortinoDisplay = FormatSummaryNumber(row.Sortino),
            DownsideDeviation = SanitizeNumber(row.DownsideDeviation),
            DownsideDeviationDisplay = FormatSummaryPercent(row.DownsideDeviation, 2),
            EquityCurveSharpe = SanitizeNumber(row.EquityCurveSharpe),
            EquityCurveSharpeDisplay = FormatSummaryNumber(row.EquityCurveSharpe),
            EquityCurveSortino = SanitizeNumber(row.EquityCurveSortino),
            EquityCurveSortinoDisplay = FormatSummaryNumber(row.EquityCurveSortino),
            EquityCurveDownsideDeviation = SanitizeNumber(row.EquityCurveDownsideDeviation),
            EquityCurveDownsideDeviationDisplay = FormatSummaryPercent(row.EquityCurveDownsideDeviation, 2),
            TotalPnl = SanitizeNumber(row.TotalPnl),
            TotalPnlDisplay = FormatSummaryNumber(row.TotalPnl),
            MaxDrawdown = SanitizeNumber(row.MaxDrawdown),
            MaxDrawdownDisplay = FormatSummaryNumber(row.MaxDrawdown),
            AvgWin = SanitizeNumber(row.AvgWin),
            AvgWinDisplay = FormatSummaryNumber(row.AvgWin),
            AvgLoss = SanitizeNumber(row.AvgLoss),
            AvgLossDisplay = FormatSummaryNumber(row.AvgLoss),
            ExpectancyR = SanitizeNumber(row.ExpectancyR),
            ExpectancyRDisplay = FormatSummaryNumber(row.ExpectancyR),
        };
    }

    private static double? SanitizeNumber(double value)
    {
        return double.IsFinite(value) ? value : null;
    }

    private static string FormatSummaryNumber(double value, int digits = 2)
    {
        if (double.IsPositiveInfinity(value))
        {
            return "INF";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-INF";
        }

        if (double.IsNaN(value))
        {
            return "-";
        }

        return value.ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    private static string FormatSummaryPercent(double value, int digits = 1)
    {
        if (!double.IsFinite(value))
        {
            return "-";
        }

        return $"{(value * 100.0).ToString($"F{digits}", CultureInfo.InvariantCulture)}%";
    }

    private static object BuildSelfLearningSummary(SelfLearningSignalAdapter? selfLearning)
    {
        if (selfLearning is not { IsLoaded: true })
        {
            return new
            {
                loaded = false,
                version = (string?)null,
                stopMultiplier = 1.0,
                positionMultiplier = 1.0,
                setupRecommendations = Array.Empty<object>(),
            };
        }

        return new
        {
            loaded = true,
            version = selfLearning.LoadedVersion,
            stopMultiplier = Math.Round(selfLearning.StopDistanceMultiplier, 4),
            positionMultiplier = Math.Round(selfLearning.PositionSizeMultiplier, 4),
            setupRecommendations = selfLearning.SetupRecommendations.Select(r => new
            {
                r.PatternKey,
                r.PatternFamily,
                r.Action,
                confidence = Math.Round(r.Confidence, 4),
                r.SampleCount,
                averageReturnBps = Math.Round(r.AverageReturnBps, 4),
            }).ToArray(),
        };
    }

    private static (string CompositeSetup, string? SelectedSubStrategy, NormalizedPattern NormalizedPattern) ResolveBacktestSetupIdentity(
        string strategy,
        string variant,
        string? subStrategy)
    {
        var setup = string.IsNullOrWhiteSpace(subStrategy)
            ? $"{strategy}:{variant}"
            : subStrategy.Trim();
        var normalized = PatternNormalizer.Normalize(setup);
        return (setup, setup, normalized);
    }

    private static IEnumerable<string?> EnumerateBranchHints((string Strategy, string Variant, string Symbol, BacktestTradeResult Trade) trade)
    {
        yield return trade.Trade.SubStrategy;
        yield return trade.Trade.SelectedEntryIntent?.Signal.SubStrategy;
        yield return trade.Variant;
        yield return trade.Strategy;
    }

    private static object[] BuildBars(BacktestTradeResult trade, EnrichedBar[] triggerBars)
    {
        if (trade.EffectiveEntryBar < 0 || trade.EffectiveEntryBar >= triggerBars.Length)
            return Array.Empty<object>();

        var firstBar = Math.Max(0, trade.EffectiveEntryBar - ReplayContextBarsBeforeEntry);
        var exitBar = Math.Clamp(trade.ExitBar, trade.EffectiveEntryBar, triggerBars.Length - 1);
        var lastBar = Math.Min(triggerBars.Length - 1, exitBar + ReplayContextBarsAfterExit);
        var bars = new List<object>(lastBar - firstBar + 1);
        for (var i = firstBar; i <= lastBar; i++)
        {
            var row = triggerBars[i];
            var phase = i < trade.EffectiveEntryBar
                ? "pre-entry"
                : i == trade.EffectiveEntryBar
                    ? "entry"
                    : i == exitBar
                        ? "exit"
                        : i > exitBar
                            ? "post-exit"
                            : "monitor";

            bars.Add(new
            {
                barIndex = i,
                timestampUtc = row.Bar.Timestamp.ToString("o"),
                open = Math.Round(row.Bar.Open, 4),
                high = Math.Round(row.Bar.High, 4),
                low = Math.Round(row.Bar.Low, 4),
                close = Math.Round(row.Bar.Close, 4),
                volume = Math.Round(row.Bar.Volume, 0),
                ma20 = SafeNumber(row.Sma20),
                ma200 = SafeNumber(row.Sma200),
                ema9 = SafeNumber(row.Ema9),
                ema21 = SafeNumber(row.Ema21),
                vwap = SafeNumber(row.Vwap),
                atr14 = SafeNumber(row.Atr14),
                rvol = SafeNumber(row.Rvol),
                phase,
            });
        }

        return bars.ToArray();
    }

    private static object? BuildSupplementalChartPayload(
        string strategy,
        BacktestTradeResult trade,
        EnrichedBar[]? ctx5m,
        EnrichedBar[]? ctx15m)
    {
        if (UsesFifteenMinuteSupplementalChart(strategy))
        {
            var bars = BuildSupplementalBars(trade, ctx15m, TimeSpan.FromMinutes(15));
            return bars.Length == 0
                ? null
                : new
                {
                    timeframe = "15m",
                    bars,
                };
        }

        if (UsesFiveMinuteSupplementalChart(strategy))
        {
            var bars = BuildSupplementalBars(trade, ctx5m, TimeSpan.FromMinutes(5));
            return bars.Length == 0
                ? null
                : new
                {
                    timeframe = "5m",
                    bars,
                };
        }

        return null;
    }

    private static bool UsesFifteenMinuteSupplementalChart(string strategy)
        => strategy.StartsWith("V21", StringComparison.OrdinalIgnoreCase)
           || strategy.StartsWith("V22", StringComparison.OrdinalIgnoreCase);

    private static bool UsesFiveMinuteSupplementalChart(string strategy)
        => strategy.StartsWith("V23", StringComparison.OrdinalIgnoreCase)
           || strategy.StartsWith("V24", StringComparison.OrdinalIgnoreCase);

    private static object[] BuildSupplementalBars(
        BacktestTradeResult trade,
        EnrichedBar[]? sourceBars,
        TimeSpan timeframe)
    {
        if (sourceBars is null || sourceBars.Length == 0)
            return Array.Empty<object>();

        var entryBar = ResolveSupplementalBarIndex(sourceBars, trade.EffectiveEntryTime, timeframe);
        if (entryBar < 0)
            return Array.Empty<object>();

        var exitBar = ResolveSupplementalBarIndex(sourceBars, trade.ExitTime, timeframe);
        if (exitBar < entryBar)
            exitBar = entryBar;

        var firstBar = Math.Max(0, entryBar - ReplayContextBarsBeforeEntry);
        var lastBar = Math.Min(sourceBars.Length - 1, exitBar + ReplayContextBarsAfterExit);
        var bars = new List<object>(lastBar - firstBar + 1);
        for (var i = firstBar; i <= lastBar; i++)
        {
            var row = sourceBars[i];
            var phase = i < entryBar
                ? "pre-entry"
                : i == entryBar
                    ? "entry"
                    : i == exitBar
                        ? "exit"
                        : i > exitBar
                            ? "post-exit"
                            : "monitor";

            bars.Add(new
            {
                barIndex = i,
                timestampUtc = row.Bar.Timestamp.ToString("o"),
                open = Math.Round(row.Bar.Open, 4),
                high = Math.Round(row.Bar.High, 4),
                low = Math.Round(row.Bar.Low, 4),
                close = Math.Round(row.Bar.Close, 4),
                volume = Math.Round(row.Bar.Volume, 0),
                ma20 = SafeNumber(row.Sma20),
                ma200 = SafeNumber(row.Sma200),
                ema9 = SafeNumber(row.Ema9),
                ema21 = SafeNumber(row.Ema21),
                vwap = SafeNumber(row.Vwap),
                atr14 = SafeNumber(row.Atr14),
                rvol = SafeNumber(row.Rvol),
                phase,
            });
        }

        return bars.ToArray();
    }

    private static int ResolveSupplementalBarIndex(
        IReadOnlyList<EnrichedBar> sourceBars,
        DateTime targetTimeUtc,
        TimeSpan timeframe)
    {
        for (var i = 0; i < sourceBars.Count; i++)
        {
            var barTime = sourceBars[i].Bar.Timestamp;
            if (targetTimeUtc >= barTime && targetTimeUtc < barTime.Add(timeframe))
                return i;
        }

        for (var i = sourceBars.Count - 1; i >= 0; i--)
        {
            if (sourceBars[i].Bar.Timestamp <= targetTimeUtc)
                return i;
        }

        return -1;
    }

    private static ReplayMetrics ComputeMetrics(BacktestTradeResult trade, EnrichedBar[] triggerBars)
    {
        if (trade.EffectiveEntryBar < 0 || trade.EffectiveEntryBar >= triggerBars.Length)
            return new ReplayMetrics();

        var lastBar = Math.Clamp(trade.ExitBar, trade.EffectiveEntryBar, triggerBars.Length - 1);
        var peakFavorablePrice = trade.EntryPrice;
        var peakAdversePrice = trade.EntryPrice;
        DateTime? peakFavorableTs = trade.EffectiveEntryTime;
        DateTime? peakAdverseTs = trade.EffectiveEntryTime;

        for (var i = trade.EffectiveEntryBar; i <= lastBar; i++)
        {
            var bar = triggerBars[i].Bar;
            if (trade.Side == TradeSide.Long)
            {
                if (bar.High > peakFavorablePrice)
                {
                    peakFavorablePrice = bar.High;
                    peakFavorableTs = bar.Timestamp;
                }
                if (bar.Low < peakAdversePrice)
                {
                    peakAdversePrice = bar.Low;
                    peakAdverseTs = bar.Timestamp;
                }
            }
            else
            {
                if (bar.Low < peakFavorablePrice)
                {
                    peakFavorablePrice = bar.Low;
                    peakFavorableTs = bar.Timestamp;
                }
                if (bar.High > peakAdversePrice)
                {
                    peakAdversePrice = bar.High;
                    peakAdverseTs = bar.Timestamp;
                }
            }
        }

        var favorablePerShare = trade.Side == TradeSide.Long
            ? Math.Max(0.0, peakFavorablePrice - trade.EntryPrice)
            : Math.Max(0.0, trade.EntryPrice - peakFavorablePrice);
        var adversePerShare = trade.Side == TradeSide.Long
            ? Math.Max(0.0, trade.EntryPrice - peakAdversePrice)
            : Math.Max(0.0, peakAdversePrice - trade.EntryPrice);

        return new ReplayMetrics
        {
            MfeUsd = favorablePerShare * trade.PositionSize,
            MaeUsd = adversePerShare * trade.PositionSize,
            PeakFavorablePrice = peakFavorablePrice,
            PeakFavorableTimestampUtc = peakFavorableTs,
            PeakAdversePrice = peakAdversePrice,
            PeakAdverseTimestampUtc = peakAdverseTs,
        };
    }

    private static double? SafeNumber(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? null : Math.Round(value, 4);

    private static object BuildPairedOppositePreviewPayload(
        string strategy,
        string variant,
        string symbol,
        BacktestTradeResult trade,
        int strategyTradeNumber,
        int strategyTradeCount,
        EnrichedBar[] triggerBars,
        SelfLearningSignalAdapter? selfLearning,
        IReadOnlyDictionary<string, Func<IBacktestStrategy>> strategyVariantFactories)
    {
        var independentOppositeTrade = TrySimulateIndependentOppositeTrade(
            strategy,
            variant,
            symbol,
            trade,
            triggerBars,
            selfLearning,
            strategyVariantFactories);

        return independentOppositeTrade is null
            ? BuildMirroredPairedOppositePreviewPayload(strategy, variant, symbol, trade, strategyTradeNumber, strategyTradeCount, triggerBars)
            : BuildIndependentPairedOppositePreviewPayload(strategy, variant, symbol, trade, independentOppositeTrade, strategyTradeNumber, strategyTradeCount, triggerBars);
    }

    private static object BuildIndependentPairedOppositePreviewPayload(
        string strategy,
        string variant,
        string symbol,
        BacktestTradeResult primaryTrade,
        BacktestTradeResult oppositeTrade,
        int strategyTradeNumber,
        int strategyTradeCount,
        EnrichedBar[] triggerBars)
    {
        var quantity = Math.Max(0, oppositeTrade.PositionSize);
        var primarySide = primaryTrade.Side.ToString();
        var balance = PairWinLossBalanceEvaluator.Evaluate(
            primaryTrade.Pnl,
            Math.Max(0, primaryTrade.PositionSize),
            oppositeTrade.Pnl,
            quantity,
            PairWinLossBalancePolicy.Default);
        var flattenDecision = PairOppositeFlattenDecisionEvaluator.Evaluate(
            primarySide,
            primaryTrade.EntryPrice,
            primaryTrade.ExitPrice,
            Math.Max(0, primaryTrade.PositionSize),
            primaryTrade.Pnl,
            BuildPairOppositeFlattenDecisionBars(primaryTrade, triggerBars),
            BuildPairOppositeFlattenDecisionEvents(primaryTrade),
            PairWinLossBalancePolicy.Default);
        var reverseFlattenDecision = PairOppositeFlattenDecisionEvaluator.Evaluate(
            oppositeTrade.Side.ToString(),
            oppositeTrade.EntryPrice,
            oppositeTrade.ExitPrice,
            quantity,
            oppositeTrade.Pnl,
            BuildPairOppositeFlattenDecisionBars(oppositeTrade, triggerBars),
            BuildPairOppositeFlattenDecisionEvents(oppositeTrade),
            PairWinLossBalancePolicy.Default);

        return new
        {
            strategy = $"{strategy}(second)",
            variant,
            symbol,
            strategyTradeNumber,
            strategyTradeCount,
            side = oppositeTrade.Side.ToString(),
            quantity,
            entryTimeUtc = primaryTrade.EffectiveEntryTime.ToString("o", CultureInfo.InvariantCulture),
            exitTimeUtc = oppositeTrade.ExitTime.ToString("o", CultureInfo.InvariantCulture),
            entryPrice = Math.Round(primaryTrade.EntryPrice, 4),
            exitPrice = Math.Round(oppositeTrade.ExitPrice, 4),
            stopPrice = Math.Round(oppositeTrade.StopPrice, 4),
            exitReason = oppositeTrade.ExitReason.ToString(),
            pnlUsd = Math.Round(oppositeTrade.Pnl, 4),
            pnlPerShareUsd = quantity > 0 ? Math.Round(oppositeTrade.Pnl / quantity, 6) : 0.0,
            primaryPnlUsd = Math.Round(primaryTrade.Pnl, 4),
            primaryPnlPerShareUsd = primaryTrade.PositionSize > 0 ? Math.Round(primaryTrade.Pnl / primaryTrade.PositionSize, 6) : 0.0,
            balanceStatus = balance.Status,
            balanced = balance.Balanced,
            winnerRole = balance.WinnerRole,
            winnerPnlPerShareUsd = balance.WinnerPnlPerShareUsd,
            loserLossPerShareUsd = balance.LoserLossPerShareUsd,
            netPnlPerShareUsd = balance.NetPnlPerShareUsd,
            winLossPerShareRatio = balance.WinLossPerShareRatio,
            failureReasons = balance.FailureReasons,
            thresholds = new
            {
                goodWinPerShareUsd = balance.GoodWinPerShareThresholdUsd,
                maxSmallLossPerShareUsd = balance.MaxSmallLossPerShareThresholdUsd,
                minNetEdgePerShareUsd = balance.MinNetEdgePerShareThresholdUsd,
                minWinLossPerShareRatio = balance.MinWinLossPerShareRatioThreshold,
            },
            flattenDecision = BuildPairOppositeFlattenDecisionPayload(flattenDecision, winnerRole: "primary", loserRole: "opposite"),
            reverseFlattenDecision = BuildPairOppositeFlattenDecisionPayload(reverseFlattenDecision, winnerRole: "opposite", loserRole: "primary"),
            actions = (oppositeTrade.ReplayActions ?? Array.Empty<BacktestTradeAction>()).Select(action => new
            {
                barIndex = action.BarIndex,
                timestampUtc = action.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                price = Math.Round(action.Price, 4),
                actionType = action.ActionType,
                description = action.Description,
                referencePrice = action.ReferencePrice.HasValue ? Math.Round(action.ReferencePrice.Value, 4) : (double?)null,
                rMultiple = action.RMultiple.HasValue ? Math.Round(action.RMultiple.Value, 4) : (double?)null,
            }).ToArray(),
            bars = BuildBars(oppositeTrade, triggerBars),
            note = "Independent opposite-side lifecycle simulated from the same strategy/variant at the same entry bar; report totals remain based on actual executed primary backtest trades only.",
        };
    }

    private static object BuildMirroredPairedOppositePreviewPayload(
        string strategy,
        string variant,
        string symbol,
        BacktestTradeResult trade,
        int strategyTradeNumber,
        int strategyTradeCount,
        EnrichedBar[] triggerBars)
    {
        var quantity = Math.Max(0, trade.PositionSize);
        var primarySide = trade.Side.ToString();
        var oppositeSide = PairWinLossBalanceEvaluator.InvertSide(primarySide);
        var primaryGrossPnlUsd = PairWinLossBalanceEvaluator.CalculateGrossPnl(
            trade.EntryPrice,
            trade.ExitPrice,
            quantity,
            primarySide);
        var oppositePnlUsd = PairWinLossBalanceEvaluator.EstimateOppositeMirrorPnl(primaryGrossPnlUsd, trade.Pnl);
        var balance = PairWinLossBalanceEvaluator.Evaluate(
            trade.Pnl,
            quantity,
            oppositePnlUsd,
            quantity,
            PairWinLossBalancePolicy.Default);
        var flattenDecision = PairOppositeFlattenDecisionEvaluator.Evaluate(
            primarySide,
            trade.EntryPrice,
            trade.ExitPrice,
            quantity,
            trade.Pnl,
            BuildPairOppositeFlattenDecisionBars(trade, triggerBars),
            BuildPairOppositeFlattenDecisionEvents(trade),
            PairWinLossBalancePolicy.Default);

        return new
        {
            strategy = $"{strategy}(second)",
            variant,
            symbol,
            strategyTradeNumber,
            strategyTradeCount,
            side = oppositeSide,
            quantity,
            entryTimeUtc = trade.EffectiveEntryTime.ToString("o", CultureInfo.InvariantCulture),
            exitTimeUtc = trade.ExitTime.ToString("o", CultureInfo.InvariantCulture),
            entryPrice = Math.Round(trade.EntryPrice, 4),
            exitPrice = Math.Round(trade.ExitPrice, 4),
            pnlUsd = Math.Round(oppositePnlUsd, 4),
            pnlPerShareUsd = quantity > 0 ? Math.Round(oppositePnlUsd / quantity, 6) : 0.0,
            primaryPnlUsd = Math.Round(trade.Pnl, 4),
            primaryPnlPerShareUsd = quantity > 0 ? Math.Round(trade.Pnl / quantity, 6) : 0.0,
            balanceStatus = balance.Status,
            balanced = balance.Balanced,
            winnerRole = balance.WinnerRole,
            winnerPnlPerShareUsd = balance.WinnerPnlPerShareUsd,
            loserLossPerShareUsd = balance.LoserLossPerShareUsd,
            netPnlPerShareUsd = balance.NetPnlPerShareUsd,
            winLossPerShareRatio = balance.WinLossPerShareRatio,
            failureReasons = balance.FailureReasons,
            thresholds = new
            {
                goodWinPerShareUsd = balance.GoodWinPerShareThresholdUsd,
                maxSmallLossPerShareUsd = balance.MaxSmallLossPerShareThresholdUsd,
                minNetEdgePerShareUsd = balance.MinNetEdgePerShareThresholdUsd,
                minWinLossPerShareRatio = balance.MinWinLossPerShareRatioThreshold,
            },
            flattenDecision = BuildPairOppositeFlattenDecisionPayload(flattenDecision, winnerRole: "primary", loserRole: "opposite"),
            reverseFlattenDecision = (object?)null,
            note = "Synthetic mirrored opposite-leg preview; strategy-specific opposite replay was unavailable, so report totals remain based on actual executed backtest trades only.",
        };
    }

    private static IReadOnlyDictionary<string, Func<IBacktestStrategy>> BuildStrategyVariantFactoryLookup()
        => StrategyComparisonRunner.GetAllPlans()
            .SelectMany(plan => plan.Variants.Select(variant => new
            {
                Key = BuildStrategyVariantFactoryKey(plan.Name, variant.Variant),
                variant.Factory,
            }))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Factory, StringComparer.OrdinalIgnoreCase);

    private static string BuildStrategyVariantFactoryKey(string strategy, string variant)
        => $"{strategy.Trim()}\u001f{variant.Trim()}";

    private static BacktestTradeResult? TrySimulateIndependentOppositeTrade(
        string strategy,
        string variant,
        string symbol,
        BacktestTradeResult trade,
        EnrichedBar[] triggerBars,
        SelfLearningSignalAdapter? selfLearning,
        IReadOnlyDictionary<string, Func<IBacktestStrategy>> strategyVariantFactories)
    {
        if (!strategyVariantFactories.TryGetValue(BuildStrategyVariantFactoryKey(strategy, variant), out var strategyFactory))
        {
            return null;
        }

        var sourceSignal = trade.SelectedEntryIntent?.Signal ?? BuildSyntheticSourceSignal(trade, triggerBars);
        if (sourceSignal is null)
        {
            return null;
        }

        var oppositeSignal = BuildOppositeSignal(sourceSignal);
        if (oppositeSignal is null)
        {
            return null;
        }

        var strategyInstance = strategyFactory();
        if (strategyInstance is BacktestStrategyBase strategyBase)
        {
            strategyBase.Symbol = symbol;
            strategyBase.SelfLearning = selfLearning;
        }

        if (strategyInstance is IBacktestLifecycleStrategy lifecycleStrategy)
        {
            var triggerTimeframe = trade.SelectedEntryIntent?.LifecycleMetadata.TriggerTimeframe ?? "1m";
            var oppositeIntent = lifecycleStrategy.CreateSelectedEntryIntent(oppositeSignal, triggerTimeframe);
            return lifecycleStrategy.SimulateAcceptedEntryIntent(oppositeIntent, triggerBars).Trade;
        }

        return strategyInstance.SimulateTrade(oppositeSignal, triggerBars);
    }

    private static BacktestSignal? BuildSyntheticSourceSignal(BacktestTradeResult trade, EnrichedBar[] triggerBars)
    {
        if (trade.EffectiveEntryBar < 0 || trade.EffectiveEntryBar >= triggerBars.Length)
        {
            return null;
        }

        var riskPerShare = Math.Abs(trade.EntryPrice - trade.StopPrice);
        if (riskPerShare <= 0)
        {
            return null;
        }

        var atrValue = triggerBars[trade.EffectiveEntryBar].Atr14;
        return new BacktestSignal(
            trade.EffectiveEntryBar,
            trade.EffectiveEntryTime,
            trade.Side,
            trade.EntryPrice,
            trade.StopPrice,
            riskPerShare,
            Math.Max(1, trade.PositionSize),
            double.IsFinite(atrValue) ? atrValue : 0.0,
            HtfBias.Neutral,
            string.Empty,
            trade.SubStrategy,
            0);
    }

    private static BacktestSignal? BuildOppositeSignal(BacktestSignal sourceSignal)
    {
        var riskPerShare = sourceSignal.RiskPerShare > 0
            ? sourceSignal.RiskPerShare
            : Math.Abs(sourceSignal.EntryPrice - sourceSignal.StopPrice);
        if (riskPerShare <= 0)
        {
            return null;
        }

        var oppositeSide = sourceSignal.Side == TradeSide.Long ? TradeSide.Short : TradeSide.Long;
        var oppositeStopPrice = oppositeSide == TradeSide.Long
            ? sourceSignal.EntryPrice - riskPerShare
            : sourceSignal.EntryPrice + riskPerShare;

        return sourceSignal with
        {
            Side = oppositeSide,
            StopPrice = oppositeStopPrice,
            RiskPerShare = riskPerShare,
        };
    }

    private static object BuildPairedSecondLegPreviewPayload(
        string strategy,
        string variant,
        string symbol,
        BacktestTradeResult trade,
        PairReplayTradeResult pairedTrade,
        int strategyTradeNumber,
        int strategyTradeCount)
    {
        var oppositeQuantity = pairedTrade.OppositeLeg.FilledQuantity > 0
            ? pairedTrade.OppositeLeg.FilledQuantity
            : pairedTrade.OppositeLeg.IntendedQuantity;
        var primaryQuantity = pairedTrade.PrimaryLeg.FilledQuantity > 0
            ? pairedTrade.PrimaryLeg.FilledQuantity
            : pairedTrade.PrimaryLeg.IntendedQuantity;
        var balance = PairWinLossBalanceEvaluator.Evaluate(
            pairedTrade.PrimaryLeg.NetAfterPlaceholdersUsd,
            primaryQuantity,
            pairedTrade.OppositeLeg.NetAfterPlaceholdersUsd,
            oppositeQuantity,
            PairWinLossBalancePolicy.Default);
        var actualFlattenTriggered = pairedTrade.OppositeLeg.ExitReason.Contains("pair-opposite-flatten", StringComparison.OrdinalIgnoreCase)
            || pairedTrade.OppositeLeg.ExitReason.Contains("pair-kill-switch", StringComparison.OrdinalIgnoreCase);
        var oppositeLossPerShare = Math.Max(0.0, -pairedTrade.OppositeLeg.NetAfterPlaceholdersPerShareUsd);
        var primaryRiskPerShare = Math.Abs(pairedTrade.PairIntent.PrimaryLeg.IntendedEntryPrice - pairedTrade.PairIntent.PrimaryLeg.IntendedStopPrice);
        var primaryPnlPerShare = primaryQuantity > 0
            ? pairedTrade.PrimaryLeg.NetAfterPlaceholdersUsd / primaryQuantity
            : 0.0;
        var primaryRMultiple = primaryRiskPerShare > 0
            ? primaryPnlPerShare / primaryRiskPerShare
            : 0.0;
        var pairTransitionReason = pairedTrade.FinalState.LastTransitionReason ?? string.Empty;
        var pairManagedReason = pairTransitionReason.Contains("pair-kill-switch", StringComparison.OrdinalIgnoreCase)
            ? "pair-kill-switch"
            : pairTransitionReason.Contains("pair-opposite-flatten", StringComparison.OrdinalIgnoreCase)
                ? "pair-opposite-flatten"
                : null;
        var actualFlattenStatus = pairManagedReason
            ?? (actualFlattenTriggered ? pairedTrade.OppositeLeg.ExitReason : "not-triggered");
        actualFlattenTriggered |= !string.IsNullOrWhiteSpace(pairManagedReason);

        return new
        {
            strategy = $"{strategy}(paired)",
            variant,
            symbol,
            strategyTradeNumber,
            strategyTradeCount,
            side = pairedTrade.OppositeLeg.Side.ToString(),
            quantity = (int)Math.Round(oppositeQuantity, MidpointRounding.AwayFromZero),
            entryTimeUtc = (pairedTrade.OppositeLeg.EntryFilledUtc ?? pairedTrade.OppositeLeg.EntrySubmittedUtc ?? trade.EffectiveEntryTime).ToString("o", CultureInfo.InvariantCulture),
            exitTimeUtc = (pairedTrade.OppositeLeg.ExitFilledUtc ?? pairedTrade.PairClosedUtc ?? trade.ExitTime).ToString("o", CultureInfo.InvariantCulture),
            entryPrice = Math.Round(pairedTrade.OppositeLeg.AverageEntryPrice > 0 ? pairedTrade.OppositeLeg.AverageEntryPrice : pairedTrade.PairIntent.OppositeLeg.IntendedEntryPrice, 4),
            exitPrice = Math.Round(pairedTrade.OppositeLeg.AverageExitPrice > 0 ? pairedTrade.OppositeLeg.AverageExitPrice : trade.ExitPrice, 4),
            stopPrice = Math.Round(pairedTrade.PairIntent.OppositeLeg.IntendedStopPrice, 4),
            pnlUsd = Math.Round(pairedTrade.OppositeLeg.NetAfterPlaceholdersUsd, 4),
            pnlPerShareUsd = Math.Round(pairedTrade.OppositeLeg.NetAfterPlaceholdersPerShareUsd, 6),
            primaryPnlUsd = Math.Round(pairedTrade.PrimaryLeg.NetAfterPlaceholdersUsd, 4),
            primaryPnlPerShareUsd = primaryQuantity > 0 ? Math.Round(primaryPnlPerShare, 6) : 0.0,
            balanceStatus = balance.Status,
            balanced = balance.Balanced,
            winnerRole = balance.WinnerRole,
            winnerPnlPerShareUsd = balance.WinnerPnlPerShareUsd,
            loserLossPerShareUsd = balance.LoserLossPerShareUsd,
            netPnlPerShareUsd = balance.NetPnlPerShareUsd,
            winLossPerShareRatio = balance.WinLossPerShareRatio,
            failureReasons = balance.FailureReasons,
            thresholds = new
            {
                goodWinPerShareUsd = balance.GoodWinPerShareThresholdUsd,
                maxSmallLossPerShareUsd = balance.MaxSmallLossPerShareThresholdUsd,
                minNetEdgePerShareUsd = balance.MinNetEdgePerShareThresholdUsd,
                minWinLossPerShareRatio = balance.MinWinLossPerShareRatioThreshold,
            },
            flattenDecision = new
            {
                triggered = actualFlattenTriggered,
                status = actualFlattenStatus,
                winnerRole = "primary",
                loserRole = "opposite",
                barIndex = (int?)null,
                timestampUtc = (pairedTrade.OppositeLeg.ExitFilledUtc ?? pairedTrade.PairClosedUtc)?.ToString("o", CultureInfo.InvariantCulture),
                primaryPrice = Math.Round(pairedTrade.OppositeLeg.AverageExitPrice > 0 ? pairedTrade.OppositeLeg.AverageExitPrice : trade.ExitPrice, 4),
                oppositeExitPrice = Math.Round(pairedTrade.OppositeLeg.AverageExitPrice > 0 ? pairedTrade.OppositeLeg.AverageExitPrice : trade.ExitPrice, 4),
                primaryPnlUsdAtDecision = Math.Round(pairedTrade.PrimaryLeg.NetAfterPlaceholdersUsd, 4),
                primaryPnlPerShareUsdAtDecision = primaryQuantity > 0 ? Math.Round(primaryPnlPerShare, 6) : 0.0,
                oppositePnlUsdAtDecision = Math.Round(pairedTrade.OppositeLeg.NetAfterPlaceholdersUsd, 4),
                oppositeLossPerShareUsdAtDecision = Math.Round(oppositeLossPerShare, 6),
                primaryRMultipleAtDecision = Math.Round(primaryRMultiple, 4),
                sourceEventType = pairManagedReason ?? pairedTrade.OppositeLeg.ExitReason,
                sourceReason = pairTransitionReason,
                reason = actualFlattenTriggered
                    ? $"Actual paired replay flattened the losing opposite leg via {actualFlattenStatus}."
                    : $"Actual paired replay opposite-leg exit reason: {pairedTrade.OppositeLeg.ExitReason}.",
                evidence = pairTransitionReason,
                lossWithinSmallLossGate = oppositeLossPerShare <= PairWinLossBalancePolicy.Default.MaxSmallLossPerShareUsd,
                chartLabel = actualFlattenTriggered ? "Flatten losing opposite" : "Opposite exit",
                note = actualFlattenTriggered
                    ? "Actual paired replay close-only exit for the losing opposite leg."
                    : "Actual paired replay opposite-leg exit was not triggered by the pair close-only supervisor.",
                thresholds = new
                {
                    primaryWinPerShareTriggerUsd = PairWinLossBalancePolicy.Default.MaxSmallLossPerShareUsd,
                    maxOppositeLossPerShareUsd = PairWinLossBalancePolicy.Default.MaxSmallLossPerShareUsd,
                    minPrimaryRMultiple = PairOppositeFlattenDecisionEvaluator.DefaultMinPrimaryRMultiple,
                }
            },
            reverseFlattenDecision = (object?)null,
            exitReason = pairedTrade.OppositeLeg.ExitReason,
            accountId = pairedTrade.OppositeLeg.AccountId,
            role = pairedTrade.OppositeLeg.Role.ToString(),
            note = "Actual paired replay opposite leg; totals reflect executed pair results rather than a mirrored preview.",
        };
    }

    private static PairOppositeFlattenDecisionBar[] BuildPairOppositeFlattenDecisionBars(
        BacktestTradeResult trade,
        EnrichedBar[] triggerBars)
    {
        if (trade.EffectiveEntryBar < 0 || trade.EffectiveEntryBar >= triggerBars.Length)
        {
            return [];
        }

        var firstBar = Math.Max(0, trade.EffectiveEntryBar - ReplayContextBarsBeforeEntry);
        var exitBar = Math.Clamp(trade.ExitBar, trade.EffectiveEntryBar, triggerBars.Length - 1);
        var lastBar = Math.Min(triggerBars.Length - 1, exitBar + ReplayContextBarsAfterExit);
        var bars = new List<PairOppositeFlattenDecisionBar>(lastBar - firstBar + 1);
        for (var i = firstBar; i <= lastBar; i++)
        {
            var row = triggerBars[i].Bar;
            var phase = i < trade.EffectiveEntryBar
                ? "pre-entry"
                : i == trade.EffectiveEntryBar
                    ? "entry"
                    : i == exitBar
                        ? "exit"
                        : i > exitBar
                            ? "post-exit"
                            : "monitor";

            bars.Add(new PairOppositeFlattenDecisionBar(
                BarIndex: i,
                TimestampUtc: row.Timestamp,
                Open: row.Open,
                High: row.High,
                Low: row.Low,
                Close: row.Close,
                Phase: phase));
        }

        return bars.ToArray();
    }

    private static PairOppositeFlattenDecisionEvent[] BuildPairOppositeFlattenDecisionEvents(BacktestTradeResult trade)
    {
        var lifecycleEvents = (trade.LifecycleEvents ?? Array.Empty<BacktestTradeLifecycleEvent>())
            .Select(evt => new PairOppositeFlattenDecisionEvent(
                EventType: evt.EventType.ToString(),
                BarIndex: evt.BarIndex,
                TimestampUtc: evt.Timestamp,
                Price: evt.Price,
                Reason: evt.Reason,
                Detail: evt.Detail,
                RMultiple: evt.RMultiple));
        var actionEvents = (trade.ReplayActions ?? Array.Empty<BacktestTradeAction>())
            .Select(action => new PairOppositeFlattenDecisionEvent(
                EventType: "ReplayAction",
                BarIndex: action.BarIndex,
                TimestampUtc: action.Timestamp,
                Price: action.Price,
                Reason: action.ActionType,
                Detail: action.Description,
                RMultiple: action.RMultiple));

        return lifecycleEvents.Concat(actionEvents)
            .OrderBy(evt => evt.TimestampUtc)
            .ThenBy(evt => evt.BarIndex ?? int.MaxValue)
            .ToArray();
    }

    private static object BuildPairOppositeFlattenDecisionPayload(
        PairOppositeFlattenDecision decision,
        string winnerRole,
        string loserRole)
        => new
        {
            enabled = decision.Enabled,
            triggered = decision.Triggered,
            status = decision.Status,
            winnerRole,
            loserRole,
            barIndex = decision.BarIndex,
            timestampUtc = decision.TimestampUtc,
            primaryPrice = decision.PrimaryPrice,
            oppositeExitPrice = decision.OppositeExitPrice,
            primaryPnlUsdAtDecision = decision.PrimaryPnlUsdAtDecision,
            primaryPnlPerShareUsdAtDecision = decision.PrimaryPnlPerShareUsdAtDecision,
            oppositePnlUsdAtDecision = decision.OppositePnlUsdAtDecision,
            oppositeLossPerShareUsdAtDecision = decision.OppositeLossPerShareUsdAtDecision,
            primaryRMultipleAtDecision = decision.PrimaryRMultipleAtDecision,
            sourceEventType = decision.SourceEventType,
            sourceReason = decision.SourceReason,
            reason = decision.Reason,
            evidence = decision.Evidence,
            lossWithinSmallLossGate = decision.LossWithinSmallLossGate,
            chartLabel = decision.ChartLabel,
            note = decision.Note,
            thresholds = new
            {
                primaryWinPerShareTriggerUsd = decision.Thresholds.PrimaryWinPerShareTriggerUsd,
                maxOppositeLossPerShareUsd = decision.Thresholds.MaxOppositeLossPerShareUsd,
                minPrimaryRMultiple = decision.Thresholds.MinPrimaryRMultiple,
            },
        };

    private static object[] BuildAnnotations(BacktestTradeResult trade)
    {
        var annotations = new List<object>();
        var replayActions = trade.ReplayActions ?? Array.Empty<BacktestTradeAction>();
        var profitExtensionArmedCount = replayActions.Count(action =>
            string.Equals(action.ActionType, "profit-extension-armed", StringComparison.OrdinalIgnoreCase));

        if (profitExtensionArmedCount > 0)
        {
            annotations.Add(new
            {
                key = "profit-extension-armed",
                label = profitExtensionArmedCount == 1
                    ? "Profit Extension Armed"
                    : $"Profit Extension Armed x{profitExtensionArmedCount}",
                tone = "info",
            });
        }

        if (trade.ExitReason == ExitReason.TrendChangeFlatten)
        {
            annotations.Add(new
            {
                key = "trend-change-flatten",
                label = "Trend Change Flatten",
                tone = "warning",
            });
        }

        return annotations.ToArray();
    }

    private static object BuildApexBonusPayload(BacktestTradeResult trade, EnrichedBar[] triggerBars, V3LiveFeatureSnapshot? featureSnapshot)
    {
        var insight = ApexBonusInsightBuilder.BuildForBacktestTrade(trade, triggerBars, ApexInsightConfig, featureSnapshot);
        return new
        {
            patternKey = insight.PatternKey,
            patternFamily = insight.PatternFamily,
            displayLabel = insight.DisplayLabel,
            side = insight.Side,
            patternMatched = insight.PatternMatched,
            smaTrendStatus = insight.SmaTrendStatus,
            smaTrendAligned = insight.SmaTrendAligned,
            l1Confirmed = insight.L1Confirmed,
            l2Confirmed = insight.L2Confirmed,
            totalBonusPct = Math.Round(insight.TotalBonusPct, 4),
            components = insight.Components.Select(component => new
            {
                key = component.Key,
                label = component.Label,
                bonusPct = Math.Round(component.BonusPct, 4),
                active = component.Active,
                detail = component.Detail,
            }).ToArray(),
        };
    }

    private static object? BuildRouteDecisionPayload(
        BacktestTradeResult trade,
        string symbol,
        string setup,
        V3LiveFeatureSnapshot? featureSnapshot)
    {
        if (featureSnapshot is null)
        {
            return null;
        }

        var proposedOrder = new V3LiveProposedOrder(
            IntentId: trade.SelectedEntryIntent?.IntentId ?? $"BT_ROUTE_{symbol}_{trade.EffectiveEntryTime:yyyyMMddHHmmss}",
            TimestampUtc: DateTime.SpecifyKind(trade.EffectiveEntryTime, DateTimeKind.Utc),
            Symbol: symbol,
            Side: trade.Side == TradeSide.Long ? OrderSide.Buy : OrderSide.Sell,
            OrderType: OrderType.Limit,
            TimeInForce: OrderTimeInForce.Day,
            Quantity: trade.PositionSize,
            EntryPrice: trade.EntryPrice,
            StopPrice: trade.StopPrice,
            TakeProfitPrice: 0.0,
            EstimatedRiskDollars: Math.Abs(trade.EntryPrice - trade.StopPrice) * trade.PositionSize,
            Atr14: featureSnapshot.Atr14,
            Setup: setup,
            Source: "backtest-trade-replay",
            SelectedSignal: trade.SelectedEntryIntent?.Signal,
            GovernorBucket: trade.GovernorBucket,
            Regime: string.Empty);
        var route = ExecutionRouteOptimizer.BuildForProposal(proposedOrder, featureSnapshot);

        return new
        {
            route.SelectedRouteId,
            route.SelectedVenue,
            route.Exchange,
            route.OrderType,
            route.TimeInForce,
            route.Urgency,
            expectedSlippageBps = Math.Round(route.ExpectedSlippageBps, 4),
            expectedExplicitCostUsd = Math.Round(route.ExpectedExplicitCostUsd, 4),
            expectedTotalCostUsd = Math.Round(route.ExpectedTotalCostUsd, 4),
            route.FallbackRouteId,
            route.DecisionSummary,
            route.FairnessPolicy,
            route.AdaptiveRepairPolicy,
            route.DecisionTags,
            route.BookSource,
            route.BookVenue,
            venueQualityScore = Math.Round(route.VenueQualityScore, 4),
            route.FallbackMode,
            route.StructuralRiskSummary,
            candidates = route.Candidates.Select(candidate => new
            {
                candidate.RouteId,
                candidate.Venue,
                candidate.Exchange,
                candidate.OrderType,
                candidate.TimeInForce,
                candidate.Urgency,
                estimatedExplicitCostUsd = Math.Round(candidate.EstimatedExplicitCostUsd, 4),
                estimatedSlippageCostUsd = Math.Round(candidate.EstimatedSlippageCostUsd, 4),
                estimatedFillProbability = Math.Round(candidate.EstimatedFillProbability, 4),
                qualityScore = Math.Round(candidate.QualityScore, 4),
                candidate.Reason,
            }).ToArray(),
        };
    }

    private sealed class HistoricalReplayFeatureSource
    {
        private readonly string _symbol;
        private readonly HistoricalBarRow[] _historicalBars;
        private readonly BidAskTick[] _bidAskTicks;
        private readonly TradeTick[] _tradeTicks;
        private readonly V3LiveFeatureBuilder _featureBuilder = new();
        private readonly Dictionary<int, V3LiveFeatureSnapshot?> _featureCache = new();

        public HistoricalReplayFeatureSource(string symbol, IReadOnlyList<EnrichedBar> triggerBars)
        {
            _symbol = symbol;
            _historicalBars = triggerBars
                .Select(row => BacktestDataFetcher.ToHistoricalBarRow(row.Bar, requestId: 0))
                .ToArray();
            _bidAskTicks = CsvTickStorage.BidAskExists(symbol) ? CsvTickStorage.LoadBidAskTicks(symbol) : [];
            _tradeTicks = CsvTickStorage.TradesExist(symbol) ? CsvTickStorage.LoadTradeTicks(symbol) : [];
        }

        public V3LiveFeatureSnapshot? GetEntryFeatureSnapshot(int barIndex, V3LiveConfig config)
        {
            if (_featureCache.TryGetValue(barIndex, out var cached))
            {
                return cached;
            }

            if (barIndex < 0 || barIndex >= _historicalBars.Length)
            {
                _featureCache[barIndex] = null;
                return null;
            }

            var barStart = _historicalBars[barIndex].TimestampUtc;
            var barEnd = barIndex + 1 < _historicalBars.Length
                ? _historicalBars[barIndex + 1].TimestampUtc
                : barStart.AddMinutes(1);

            var latestBidAsk = _bidAskTicks
                .Where(tick => tick.TimestampUtc <= barEnd)
                .OrderByDescending(tick => tick.TimestampUtc)
                .FirstOrDefault();
            var latestTrade = _tradeTicks
                .Where(tick => tick.TimestampUtc <= barEnd)
                .OrderByDescending(tick => tick.TimestampUtc)
                .FirstOrDefault();

            if (latestBidAsk is null && latestTrade is null)
            {
                _featureCache[barIndex] = null;
                return null;
            }

            var topTicks = BuildCarryForwardTopTicks(latestBidAsk, latestTrade);
            var depthRows = BuildCarryForwardDepthRows(latestBidAsk, config.DepthLevels);
            var slice = new StrategyDataSlice(
                TimestampUtc: barEnd,
                Mode: "backtest-replay-entry-snapshot",
                TopTicks: topTicks,
                DepthRows: depthRows,
                HistoricalBars: new ArraySegment<HistoricalBarRow>(_historicalBars, 0, barIndex + 1).ToArray(),
                Positions: [],
                AccountSummary: [],
                CanonicalOrderEvents: [],
                Symbol: _symbol);

            var snapshot = _featureBuilder.Build(slice, config.DepthLevels, _symbol, minReadyBars: config.MinReadyBars);
            _featureCache[barIndex] = snapshot;
            return snapshot;
        }

        private static TopTickRow[] BuildCarryForwardTopTicks(BidAskTick? latestBidAsk, TradeTick? latestTrade)
        {
            var rows = new List<TopTickRow>();

            if (latestBidAsk is not null)
            {
                rows.Add(new TopTickRow(latestBidAsk.TimestampUtc, 0, "tickPrice", 1, latestBidAsk.Bid, 0, string.Empty));
                rows.Add(new TopTickRow(latestBidAsk.TimestampUtc, 0, "tickPrice", 2, latestBidAsk.Ask, 0, string.Empty));
                rows.Add(new TopTickRow(latestBidAsk.TimestampUtc, 0, "tickSize", 0, 0, (int)Math.Round(latestBidAsk.BidSize), string.Empty));
                rows.Add(new TopTickRow(latestBidAsk.TimestampUtc, 0, "tickSize", 3, 0, (int)Math.Round(latestBidAsk.AskSize), string.Empty));
            }

            if (latestTrade is not null)
            {
                rows.Add(new TopTickRow(latestTrade.TimestampUtc, 0, "tickPrice", 4, latestTrade.Price, 0, string.Empty));
            }

            return rows.ToArray();
        }

        private static DepthRow[] BuildCarryForwardDepthRows(BidAskTick? latestBidAsk, int depthLevels)
        {
            if (latestBidAsk is null)
            {
                return [];
            }

            return L1L2Synthesizer.SynthesizeDepthRows(
                [latestBidAsk],
                latestBidAsk.TimestampUtc,
                latestBidAsk.TimestampUtc.AddMilliseconds(1),
                depthLevels);
        }
    }

    private static string BuildHtml(object payload, string payloadJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine("<title>Backtest Trade Replay Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;background:#f3efe6;color:#1e2430;margin:0;padding:24px;}header{margin-bottom:20px;}h1,h2,h3{margin:0 0 10px;}table{border-collapse:collapse;width:100%;background:#fff;}th,td{border:1px solid #d8d2c3;padding:8px 10px;font-size:13px;text-align:left;}th{background:#e8ddc5;}section{margin:20px 0;} .card{background:#fff;border:1px solid #d8d2c3;border-radius:12px;padding:16px;margin:14px 0;box-shadow:0 2px 8px rgba(0,0,0,.04);} .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;} .pill{display:inline-block;padding:4px 8px;border-radius:999px;background:#d7eadf;color:#0e5135;font-size:12px;margin-right:6px;margin-top:4px;} .warn{background:#f5d9d3;color:#8c271e;} .info{background:#d9ecff;color:#114a7a;} .muted{color:#666;} .positive{color:#157347;font-weight:700;} .negative{color:#b42318;font-weight:700;} .side-long{color:#157347;font-weight:700;} .side-short{color:#b42318;font-weight:700;} details summary{cursor:pointer;font-weight:600;} .apex-title-badge{display:inline-block;margin-left:18px;padding:4px 10px;border-radius:999px;background:#ffd2f5;color:#a10f78;font-size:12px;font-weight:700;vertical-align:middle;} .variant-badge{display:inline-block;padding:3px 8px;border-radius:999px;font-size:11px;font-weight:700;margin-left:4px;vertical-align:middle;} .variant-strict{background:#d1ecf1;color:#0c5460;border:1px solid #17a2b8;} canvas{width:100%;max-width:1000px;height:280px;border:1px solid #d8d2c3;background:#fffdf8;border-radius:8px;} ol{margin:8px 0 0 20px;padding-left:18px;} .mono{font-family:Consolas,monospace;} .actions{max-height:220px;overflow:auto;} .toolbar{display:flex;gap:10px;flex-wrap:wrap;margin:12px 0;} .summary-toolbar{align-items:center;} .toolbar label{display:inline-flex;align-items:center;gap:8px;font-size:12px;color:#5b4636;} input,select{padding:8px;border:1px solid #c9c1b0;border-radius:8px;background:#fff;} .sort-btn{display:inline-flex;align-items:center;gap:6px;padding:0;border:0;background:transparent;color:inherit;font:inherit;cursor:pointer;} .sort-btn:hover{color:#114a7a;} .sort-icon{font-size:11px;color:#7a6751;} .sort-btn:hover .sort-icon{color:#114a7a;} .chart-legend{display:flex;gap:16px;flex-wrap:wrap;align-items:center;margin:10px 0 8px;font-size:12px;color:#5b4636;} .chart-legend span{display:inline-flex;align-items:center;gap:6px;} .legend-swatch{display:inline-block;width:18px;height:3px;border-radius:999px;} .legend-ma20{background:#17b7d9;} .legend-ma200{background:#7b2cbf;} .legend-window{background:#f2d6a2;} .legend-setup{background:#1d3557;} .legend-flatten{background:#ffb703;} .audit-flags{display:flex;gap:6px;flex-wrap:wrap;margin:8px 0 0;} .action-profit-extension{color:#114a7a;} .action-trend-change{color:#8c271e;font-weight:700;} .action-opposite-flatten{color:#9a6700;font-weight:700;} .annotation-summary-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px;} .annotation-summary-card{border:1px solid #d8d2c3;border-radius:10px;padding:12px;background:#fffdf8;} .annotation-summary-card h3{font-size:14px;margin-bottom:6px;} .annotation-summary-meta{display:flex;gap:10px;flex-wrap:wrap;margin-bottom:8px;font-size:12px;color:#5b4636;} .annotation-symbols{display:flex;gap:6px;flex-wrap:wrap;} .decomp-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:14px;} .table-wrap{overflow:auto;} .metric-table th,.metric-table td{font-size:12px;} .metric-table .key-cell{font-weight:600;} .decomp-subcard{border:1px solid #d8d2c3;border-radius:10px;padding:12px;background:#fffdf8;} .decomp-subcard h3{font-size:14px;margin-bottom:8px;} .stat-strip{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px;margin:10px 0 14px;} .stat-strip > div{background:#fffdf8;border:1px solid #d8d2c3;border-radius:10px;padding:10px;} .trade-group-stack{display:grid;gap:14px;} .trade-subcard{border:1px solid #d8d2c3;border-radius:10px;padding:14px;background:#fffdf8;} .trade-subcard h3{margin-bottom:12px;} .paired-trade-card{background:#fff8ef;border-color:#e7d7bc;} </style>");
        sb.AppendLine("<style>.chart-compare-row{display:flex;gap:14px;align-items:flex-start;flex-wrap:nowrap;margin:0 0 10px;} .chart-primary-panel{flex:1 1 auto;min-width:0;} .chart-secondary-panel{flex:0 0 320px;max-width:320px;min-width:280px;} .chart-secondary-panel canvas{height:220px;max-width:320px;} .mini-chart-title{margin:0 0 6px;font-size:12px;font-weight:700;color:#5b4636;} .mini-chart-subtitle{margin:0 0 8px;font-size:11px;color:#7a6751;} @media (max-width:1280px){.chart-compare-row{flex-wrap:wrap;} .chart-secondary-panel{flex:1 1 320px;max-width:100%;min-width:0;} .chart-secondary-panel canvas{max-width:100%;}}</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<header><h1>Backtest Trade Replay Report</h1><div class=\"muted\">Auto-generated after compare-mode backtest export.</div></header>");
        sb.AppendLine("<section class=\"card\"><h2>Summary</h2><div id=\"totals\"></div></section>");
        sb.AppendLine("<section class=\"card\"><h2>Execution Assumptions</h2><div id=\"execution-assumptions\"></div></section>");
        sb.AppendLine("<section class=\"card\"><h2>Self-Learning</h2><div id=\"self-learning\"></div></section>");
        sb.AppendLine("<section class=\"card\"><h2>Strategy Table</h2><div class=\"toolbar summary-toolbar\"><label for=\"summary-min-trades-filter\"><span>&gt;=50 filter</span><select id=\"summary-min-trades-filter\"><option value=\"all\">All</option><option value=\"yes\">YES</option><option value=\"no\">NO</option></select></label></div><div id=\"summary-table\"></div></section>");
        sb.AppendLine("<section class=\"card\"><h2>Combined Strategy Table</h2><div id=\"combined-summary-table\"></div></section>");
        sb.AppendLine("<section class=\"card\"><h2>Promotion Ranking &amp; Recovery</h2><div id=\"promotion-ranking\"></div></section>");
        sb.AppendLine("<section class=\"card\"><h2>Metric Decomposition</h2><div class=\"toolbar\"><select id=\"decomposition-filter\"><option value=\"\">Select strategy / variant</option></select></div><div id=\"metric-decomposition\"></div></section>");
        sb.AppendLine("<section class=\"card\"><h2>Trades</h2><div class=\"toolbar\"><input id=\"trade-filter\" placeholder=\"Filter strategy / symbol / side / reason\" /><select id=\"strategy-filter\"><option value=\"\">All strategies</option></select></div><div id=\"annotation-summary\"></div><div id=\"trades\"></div></section>");
        sb.AppendLine("<script>");
        sb.Append("const reportData = ");
        sb.Append(payloadJson);
        sb.AppendLine(";");
        sb.AppendLine("""
function fmt(v, digits=2){ return v === null || v === undefined || Number.isNaN(v) ? '-' : Number(v).toFixed(digits); }
function fmtSigned(v, digits=2){ if(v === null || v === undefined || Number.isNaN(v)) return '-'; const n = Number(v); return `${n > 0 ? '+' : ''}${n.toFixed(digits)}`; }
function pct(v){ return v === null || v === undefined ? '-' : `${(Number(v)*100).toFixed(1)}%`; }
function pnlClass(v){ const n = Number(v); if(Number.isNaN(n) || n === 0) return ''; return n > 0 ? 'positive' : 'negative'; }
function sideClass(side){ return String(side).toLowerCase() === 'long' ? 'side-long' : 'side-short'; }
function variantBadge(variant){ const v = String(variant || '').toLowerCase(); if(v.includes('strict')) return '<span class="variant-badge variant-strict">Strict</span>'; return ''; }
function toFiniteNumber(v){ if(v === null || v === undefined) return null; const n = Number(v); return Number.isFinite(n) ? n : null; }
const summaryState = { minTradesFilter: 'all', sortKey: null, sortDir: 'desc' };
function renderTotals(){
  const t = reportData.totals;
  document.getElementById('totals').innerHTML = `<div class="grid"><div><strong>Generated</strong><div class="mono">${reportData.generatedUtc}</div></div><div><strong>Strategies</strong><div>${t.strategyCount}</div></div><div><strong>Trades</strong><div>${t.tradeCount}</div></div><div><strong>Symbols</strong><div>${t.symbolCount}</div></div></div>`;
}
function renderSelfLearning(){
  const s = reportData.selfLearning;
  if(!s.loaded){ document.getElementById('self-learning').innerHTML = '<div>No self-learning artifact loaded for this run.</div>'; return; }
  const recs = (s.setupRecommendations || []).slice(0, 12).map(r => `<tr><td>${r.PatternKey}</td><td>${r.Action}</td><td>${fmt(r.confidence,4)}</td><td>${r.SampleCount}</td><td>${fmt(r.averageReturnBps,2)}</td></tr>`).join('');
  document.getElementById('self-learning').innerHTML = `<div class="grid"><div><strong>Version</strong><div>${s.version || '-'}</div></div><div><strong>Stop Mult</strong><div>${fmt(s.stopMultiplier,4)}</div></div><div><strong>Position Mult</strong><div>${fmt(s.positionMultiplier,4)}</div></div><div><strong>Setup Recs</strong><div>${(s.setupRecommendations || []).length}</div></div></div><table><thead><tr><th>Pattern</th><th>Action</th><th>Confidence</th><th>Samples</th><th>AvgReturnBps</th></tr></thead><tbody>${recs}</tbody></table>`;
}
function renderExecutionAssumptions(){
    const selectedId = reportData.realityModelProfileId || '-';
    const selectedGrade = reportData.realityModelProfileGrade || '-';
    const selectedHash = reportData.realityModelProfileHash || '-';
    const assumptions = reportData.executionAssumptionsSummary || 'No execution assumptions exported.';
    const rows = (reportData.realityModelProfiles || []).map(profile => `<tr><td>${profile.ProfileId}</td><td>${profile.Grade}</td><td>${profile.Selected ? 'YES' : 'NO'}</td><td>${profile.Executed ? 'YES' : 'NO'}</td><td>${profile.PromotionEligible ? 'YES' : 'NO'}</td><td>${profile.PromotionQualified ? 'YES' : 'NO'}</td><td class="mono">${profile.ProfileHash}</td><td>${profile.AssumptionSummary}</td></tr>`).join('');
    document.getElementById('execution-assumptions').innerHTML = `<div class="grid"><div><strong>Selected Profile</strong><div>${selectedId}</div></div><div><strong>Grade</strong><div>${selectedGrade}</div></div><div><strong>Profile Hash</strong><div class="mono">${selectedHash}</div></div><div><strong>Promotion Ready</strong><div>${reportData.promotionRealityProfileSatisfied ? 'YES' : 'NO'}</div></div></div><div style="margin:12px 0"><strong>Summary</strong><div>${assumptions}</div></div><table><thead><tr><th>Profile</th><th>Grade</th><th>Selected</th><th>Executed</th><th>Promotion Eligible</th><th>Promotion Qualified</th><th>Hash</th><th>Assumptions</th></tr></thead><tbody>${rows}</tbody></table>`;
}
function summarySortIcon(key){
    if(summaryState.sortKey !== key) return 'â‡…';
    return summaryState.sortDir === 'asc' ? 'â–²' : 'â–¼';
}
function toggleSummarySort(key){
    if(summaryState.sortKey === key){
        summaryState.sortDir = summaryState.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
        summaryState.sortKey = key;
        summaryState.sortDir = 'desc';
    }
    renderSummaryTable();
}
function compareSummaryRows(left, right){
    if(!summaryState.sortKey) return left.index - right.index;
    let cmp = 0;
    if(summaryState.sortKey === 'MeetsMinTrades'){
        cmp = Number(Boolean(left.row.MeetsMinTrades)) - Number(Boolean(right.row.MeetsMinTrades));
    } else if(summaryState.sortKey === 'TotalPnl'){
        const leftValue = toFiniteNumber(left.row.TotalPnl);
        const rightValue = toFiniteNumber(right.row.TotalPnl);
        cmp = (leftValue ?? Number.NEGATIVE_INFINITY) - (rightValue ?? Number.NEGATIVE_INFINITY);
    }
    if(cmp === 0) return left.index - right.index;
    return summaryState.sortDir === 'asc' ? cmp : -cmp;
}
function renderSummaryTable(){
    const filter = document.getElementById('summary-min-trades-filter');
    summaryState.minTradesFilter = filter ? filter.value : summaryState.minTradesFilter;
    const rows = (reportData.summary || [])
        .map((row, index) => ({ row, index }))
        .filter(item => {
            if(summaryState.minTradesFilter === 'yes') return Boolean(item.row.MeetsMinTrades);
            if(summaryState.minTradesFilter === 'no') return !item.row.MeetsMinTrades;
            return true;
        })
        .sort(compareSummaryRows)
        .map(item => item.row)
                    .map(r => `<tr><td>${r.Strategy}</td><td>${r.Variant}${variantBadge(r.Variant)}</td><td>${r.Symbols}</td><td>${r.Trades}</td><td>${r.MeetsMinTrades ? 'YES' : 'NO'}</td><td>${r.WinRateDisplay ?? pct(r.WinRate)}</td><td>${r.ProfitFactorDisplay ?? fmt(r.ProfitFactor)}</td><td>${r.SharpeDisplay ?? fmt(r.Sharpe)}</td><td>${r.EquityCurveSharpeDisplay ?? fmt(r.EquityCurveSharpe)}</td><td>${r.EquityCurveSortinoDisplay ?? fmt(r.EquityCurveSortino)}</td><td>${r.EquityCurveDownsideDeviationDisplay ?? '-'}</td><td class="${pnlClass(r.TotalPnl)}">${r.TotalPnlDisplay ?? fmt(r.TotalPnl)}</td><td>${r.MaxDrawdownDisplay ?? fmt(r.MaxDrawdown)}</td><td>${r.AvgWinDisplay ?? fmt(r.AvgWin)}</td><td>${r.AvgLossDisplay ?? fmt(r.AvgLoss)}</td><td>${r.ExpectancyRDisplay ?? fmt(r.ExpectancyR)}</td><td>${r.GovernorStops}</td><td>${r.GovernorStopReason || '-'}</td></tr>`)
        .join('');
            document.getElementById('summary-table').innerHTML = `<table><thead><tr><th>Strategy</th><th>Variant</th><th>Symbols</th><th>Trades</th><th><button class="sort-btn" type="button" onclick="toggleSummarySort('MeetsMinTrades')">&gt;=50 <span class="sort-icon">${summarySortIcon('MeetsMinTrades')}</span></button></th><th>WinRate</th><th>PF</th><th>Sharpe</th><th>EqSharpe</th><th>EqSortino</th><th>EqDownDev</th><th><button class="sort-btn" type="button" onclick="toggleSummarySort('TotalPnl')">TotalPnL$ <span class="sort-icon">${summarySortIcon('TotalPnl')}</span></button></th><th>MaxDD$</th><th>AvgWin$</th><th>AvgLoss$</th><th>Expectancy</th><th>GovStops</th><th>GovReason</th></tr></thead><tbody>${rows}</tbody></table>`;
    renderCombinedSummaryTable();
}
function parseTimestampUtc(value){
    if(!value) return Number.POSITIVE_INFINITY;
    const parsed = Date.parse(String(value));
    return Number.isFinite(parsed) ? parsed : Number.POSITIVE_INFINITY;
}
function computeMaxDrawdownUsd(trades){
    const ordered = [...trades].sort((left, right) => {
        const exitCmp = parseTimestampUtc(left.exitTimeUtc) - parseTimestampUtc(right.exitTimeUtc);
        if(exitCmp !== 0) return exitCmp;
        const entryCmp = parseTimestampUtc(left.entryTimeUtc) - parseTimestampUtc(right.entryTimeUtc);
        if(entryCmp !== 0) return entryCmp;
        return String(left.symbol || '').localeCompare(String(right.symbol || ''));
    });
    let runningPnl = 0;
    let peak = 0;
    let maxDrawdown = 0;
    ordered.forEach(trade => {
        runningPnl += Number(trade.pnlUsd || 0);
        peak = Math.max(peak, runningPnl);
        maxDrawdown = Math.max(maxDrawdown, peak - runningPnl);
    });
    return maxDrawdown;
}
function buildCombinedStrategySummaryRows(){
    const scopes = [
        { label: 'Winning primary trades', includeForcedOpposite: false },
        { label: 'Winning primary trades & Forced-opposite losing-leg trades', includeForcedOpposite: true }
    ];
    const rows = [];
    scopes.forEach((scope, scopeIndex) => {
        const groups = new Map();
        (reportData.trades || []).forEach(trade => {
            const key = `${trade.strategy}\u001f${trade.variant}`;
            if(!groups.has(key)){
                groups.set(key, {
                    Scope: scope.label,
                    scopeIndex,
                    Strategy: trade.strategy,
                    Variant: trade.variant,
                    trades: []
                });
            }
            const group = groups.get(key);
            group.trades.push({
                symbol: trade.symbol,
                entryTimeUtc: trade.entryTimeUtc,
                exitTimeUtc: trade.exitTimeUtc,
                pnlUsd: Number(trade.pnlUsd || 0)
            });
            if(scope.includeForcedOpposite && trade.pairedOppositePreview && !trade.pairedSecondLegPreview){
                group.trades.push({
                    symbol: trade.symbol,
                    entryTimeUtc: trade.pairedOppositePreview.entryTimeUtc,
                    exitTimeUtc: trade.pairedOppositePreview.exitTimeUtc,
                    pnlUsd: Number(trade.pairedOppositePreview.pnlUsd || 0)
                });
            }
        });

        [...groups.values()].forEach(group => {
            const tradeCount = group.trades.length;
            const winners = group.trades.filter(trade => trade.pnlUsd > 0);
            const losers = group.trades.filter(trade => trade.pnlUsd < 0);
            const totalPnl = group.trades.reduce((sum, trade) => sum + trade.pnlUsd, 0);
            const grossWin = winners.reduce((sum, trade) => sum + trade.pnlUsd, 0);
            const grossLoss = losers.reduce((sum, trade) => sum + Math.abs(trade.pnlUsd), 0);
            rows.push({
                Scope: group.Scope,
                scopeIndex: group.scopeIndex,
                Strategy: group.Strategy,
                Variant: group.Variant,
                Symbols: new Set(group.trades.map(trade => trade.symbol)).size,
                Trades: tradeCount,
                MeetsMinTrades: tradeCount >= 50,
                WinRate: tradeCount > 0 ? winners.length / tradeCount : 0,
                ProfitFactor: grossLoss > 0 ? grossWin / grossLoss : (grossWin > 0 ? Number.POSITIVE_INFINITY : 0),
                TotalPnl: totalPnl,
                MaxDrawdown: computeMaxDrawdownUsd(group.trades),
                AvgWin: winners.length > 0 ? grossWin / winners.length : 0,
                AvgLoss: losers.length > 0 ? -grossLoss / losers.length : 0,
                AvgPnl: tradeCount > 0 ? totalPnl / tradeCount : 0,
            });
        });
    });
    return rows
        .filter(row => {
            if(summaryState.minTradesFilter === 'yes') return Boolean(row.MeetsMinTrades);
            if(summaryState.minTradesFilter === 'no') return !row.MeetsMinTrades;
            return true;
        })
        .sort((left, right) => {
            const pnlCmp = Number(right.TotalPnl || 0) - Number(left.TotalPnl || 0);
            if(pnlCmp !== 0) return pnlCmp;
            const scopeCmp = left.scopeIndex - right.scopeIndex;
            if(scopeCmp !== 0) return scopeCmp;
            const strategyCmp = String(left.Strategy || '').localeCompare(String(right.Strategy || ''));
            if(strategyCmp !== 0) return strategyCmp;
            return String(left.Variant || '').localeCompare(String(right.Variant || ''));
        });
}
function renderCombinedSummaryTable(){
    const host = document.getElementById('combined-summary-table');
    if(!host) return;
    const rows = buildCombinedStrategySummaryRows();
    if(!rows.length){
        host.innerHTML = '<div class="muted">No combined forced-opposite strategy summary is available for this report.</div>';
        return;
    }
    const body = rows.map(row => `<tr><td>${row.Scope}</td><td>${row.Strategy}</td><td>${row.Variant}${variantBadge(row.Variant)}</td><td>${row.Symbols}</td><td>${row.Trades}</td><td>${row.MeetsMinTrades ? 'YES' : 'NO'}</td><td>${pct(row.WinRate)}</td><td>${Number.isFinite(row.ProfitFactor) ? fmt(row.ProfitFactor) : 'INF'}</td><td class="${pnlClass(row.TotalPnl)}">${fmt(row.TotalPnl)}</td><td>${fmt(row.MaxDrawdown)}</td><td>${fmt(row.AvgWin)}</td><td>${fmt(row.AvgLoss)}</td><td>${fmt(row.AvgPnl)}</td></tr>`).join('');
    host.innerHTML = `<div class="muted" style="margin:0 0 10px">Compares the executed primary trade set against the primary-plus-forced-opposite synthetic pair set derived from the same replay report.</div><table><thead><tr><th>Scope</th><th>Strategy</th><th>Variant</th><th>Symbols</th><th>Trades</th><th>&gt;=50</th><th>WinRate</th><th>PF</th><th>TotalPnL$</th><th>MaxDD$</th><th>AvgWin$</th><th>AvgLoss$</th><th>AvgPnL$</th></tr></thead><tbody>${body}</tbody></table>`;
}
function renderPromotionRanking(){
    const strongest = reportData.summary?.find(row => row.StrongestEvidenceAnchor) || reportData.summary?.[0];
    const recommended = reportData.hardEnvironmentLearningProfile;
    const rows = [...(reportData.summary || [])]
        .sort((left, right) => (Number(right.PromotionScore || 0) - Number(left.PromotionScore || 0)) || String(left.Strategy).localeCompare(String(right.Strategy)));
    const tableRows = rows.map(row => {
        const taskList = (row.AdaptiveRecoveryTasks || []).map(task => `<li><strong>${task.Category}</strong> (${task.Severity}) â€” ${task.Action}<div class="muted">${task.Reason} | ${task.Scope} | ${task.Evidence}</div></li>`).join('');
        const anchorBadge = row.StrongestEvidenceAnchor ? '<span class="pill info">strongest evidence anchor</span>' : '';
        const strictBadge = row.StrictPromotionPass ? '<span class="pill">strict pass</span>' : '<span class="pill warn">strict repair</span>';
        const pessimisticBadge = row.PessimisticPromotionPass ? '<span class="pill">pessimistic pass</span>' : '<span class="pill warn">research-only until pessimistic pass</span>';
        const hardEnvBadge = row.HardEnvironmentEligible ? '<span class="pill info">hard-environment eligible</span>' : '<span class="pill warn">blocked</span>';
        return `<tr><td><div><strong>${row.Strategy}</strong> / ${row.Variant}</div><div class="audit-flags">${anchorBadge}${strictBadge}${pessimisticBadge}${hardEnvBadge}</div></td><td>${fmt(row.PromotionScore)}</td><td>${row.Trades}</td><td>${row.SharpeDisplay ?? fmt(row.Sharpe)}</td><td>${row.EquityCurveSharpeDisplay ?? fmt(row.EquityCurveSharpe)}</td><td>${row.EquityCurveSortinoDisplay ?? fmt(row.EquityCurveSortino)}</td><td>${row.EquityCurveDownsideDeviationDisplay ?? '-'}</td><td class="${pnlClass(row.TotalPnl)}">${row.TotalPnlDisplay ?? fmt(row.TotalPnl)}</td><td>${row.ProfitFactorDisplay ?? fmt(row.ProfitFactor)}</td><td>${row.GovernorStops}</td><td>${(row.AdaptiveRecoveryTasks || []).length}</td><td>${taskList ? `<ol>${taskList}</ol>` : '<span class="muted">No repair tasks required.</span>'}</td></tr>`;
    }).join('');
    const header = recommended
        ? `<div class="stat-strip"><div><strong>Recommended profile</strong><div>${recommended.profileId}</div></div><div><strong>Base strategy</strong><div>${recommended.baseStrategy}</div></div><div><strong>Base variant</strong><div>${recommended.baseVariant}</div></div><div><strong>Eligibility preserved</strong><div>${recommended.preservesSymbolEligibility && recommended.preservesTimeBucketEligibility ? 'YES' : 'NO'}</div></div></div><div class="muted">${recommended.description}</div>`
        : '<div class="muted">No hard-environment-learning recommendation exported.</div>';
    const strongestLine = strongest
        ? `<div style="margin:12px 0"><strong>Current evidence anchor:</strong> ${strongest.Strategy} / ${strongest.Variant} with promotionScore ${fmt(strongest.PromotionScore)}, Sharpe ${strongest.SharpeDisplay ?? fmt(strongest.Sharpe)}, EqSharpe ${strongest.EquityCurveSharpeDisplay ?? fmt(strongest.EquityCurveSharpe)}, EqSortino ${strongest.EquityCurveSortinoDisplay ?? fmt(strongest.EquityCurveSortino)}, EqDownDev ${strongest.EquityCurveDownsideDeviationDisplay ?? '-'}.</div>`
        : '';
    document.getElementById('promotion-ranking').innerHTML = `${header}${strongestLine}<div class="muted" style="margin:8px 0 14px">Acceptance alignment: pessimistic profile must pass for a promotion-ready candidate; otherwise the row is explicitly marked research-only. Sharpe and equity-curve Sharpe must both be better than ${fmt(reportData.acceptanceSharpeBaseline)}. Every symbol and every trading-hour bucket remains eligible, with weak contexts repaired rather than excluded.</div><table><thead><tr><th>Strategy / Variant</th><th>PromotionScore</th><th>Trades</th><th>Sharpe</th><th>EqSharpe</th><th>EqSortino</th><th>EqDownDev</th><th>TotalPnL$</th><th>PF</th><th>GovStops</th><th>Tasks</th><th>Adaptive recovery actions</th></tr></thead><tbody>${tableRows}</tbody></table>`;
}
function decompositionOptions(){
    const select = document.getElementById('decomposition-filter');
    const groups = reportData.decompositions || [];
    groups.forEach((group, index) => {
        const opt = document.createElement('option');
        opt.value = `${index}`;
        opt.textContent = `${group.Strategy} / ${group.Variant} (${group.TradeCount} trades)`;
        select.appendChild(opt);
    });
    if(groups.length > 0){
        select.value = '0';
    }
}
function formatBucketRows(rows, includeExpectancy=true){
    return (rows || []).map(row => `<tr><td class="key-cell">${row.Label}</td><td>${row.Trades}</td><td>${pct(row.WinRate)}</td><td class="${pnlClass(row.TotalPnl)}">${fmt(row.TotalPnl)}</td><td>${fmt(row.AvgPnl)}</td><td>${fmt(row.AvgWin)}</td><td>${fmt(row.AvgLoss)}</td><td>${fmt(row.ProfitFactor)}</td>${includeExpectancy ? `<td>${fmt(row.ExpectancyR,4)}</td>` : ''}<td>${fmt(row.AvgMfeUsd)}</td><td>${fmt(row.AvgMaeUsd)}</td><td>${fmt(row.AvgGivebackUsd)}</td></tr>`).join('');
}
function renderBucketTable(title, rows, includeExpectancy=true){
    return `<div class="decomp-subcard"><h3>${title}</h3><div class="table-wrap"><table class="metric-table"><thead><tr><th>Bucket</th><th>Trades</th><th>WinRate</th><th>TotalPnL$</th><th>AvgPnL$</th><th>AvgWin$</th><th>AvgLoss$</th><th>PF</th>${includeExpectancy ? '<th>ExpectancyR</th>' : ''}<th>AvgMFE$</th><th>AvgMAE$</th><th>AvgGiveback$</th></tr></thead><tbody>${formatBucketRows(rows, includeExpectancy)}</tbody></table></div></div>`;
}
function renderMfeMaeBucketTable(title, rows){
    return `<div class="decomp-subcard"><h3>${title}</h3><div class="table-wrap"><table class="metric-table"><thead><tr><th>Bucket</th><th>Trades</th><th>TotalPnL$</th><th>AvgPnL$</th><th>AvgPnLR</th><th>AvgMFE$</th><th>AvgMAE$</th><th>AvgGiveback$</th><th>Capture%</th><th>MAE/Risk</th><th>AvgBars</th></tr></thead><tbody>${(rows || []).map(row => `<tr><td class="key-cell">${row.Label}</td><td>${row.Trades}</td><td class="${pnlClass(row.TotalPnl)}">${fmt(row.TotalPnl)}</td><td>${fmt(row.AvgPnl)}</td><td>${fmt(row.AvgPnlR,4)}</td><td>${fmt(row.AvgMfeUsd)}</td><td>${fmt(row.AvgMaeUsd)}</td><td>${fmt(row.AvgGivebackUsd)}</td><td>${pct(row.ProfitCapturePct)}</td><td>${pct(row.AdverseExcursionPctOfRisk)}</td><td>${fmt(row.AvgBarsHeld,1)}</td></tr>`).join('')}</tbody></table></div></div>`;
}
function renderDrawdownTable(rows){
    return `<div class="decomp-subcard"><h3>drawdownContributors</h3><div class="table-wrap"><table class="metric-table"><thead><tr><th>Symbol</th><th>Side</th><th>Exit</th><th>Setup</th><th>Hour</th><th>PnL$</th><th>DD Contribution$</th><th>MFE$</th><th>MAE$</th><th>Giveback$</th></tr></thead><tbody>${(rows || []).map(row => `<tr><td class="key-cell">${row.Symbol}</td><td>${row.Side}</td><td>${row.ExitReason}</td><td>${row.Setup}</td><td>${row.EntryHourUtc}:00</td><td class="${pnlClass(row.PnlUsd)}">${fmt(row.PnlUsd)}</td><td>${fmt(row.DrawdownContributionUsd)}</td><td>${fmt(row.MfeUsd)}</td><td>${fmt(row.MaeUsd)}</td><td>${fmt(row.GivebackUsd)}</td></tr>`).join('')}</tbody></table></div></div>`;
}
function renderHardStopDiagnostics(diag){
    return `<div class="decomp-subcard"><h3>hardStopDiagnostics</h3><div class="stat-strip"><div><strong>Trades</strong><div>${diag.TradeCount}</div></div><div><strong>TotalPnL$</strong><div class="${pnlClass(diag.TotalPnl)}">${fmt(diag.TotalPnl)}</div></div><div><strong>AvgPnL$</strong><div>${fmt(diag.AvgPnl)}</div></div><div><strong>AvgPnLR</strong><div>${fmt(diag.AvgPnlR,4)}</div></div><div><strong>AvgMFE$</strong><div>${fmt(diag.AvgMfeUsd)}</div></div><div><strong>AvgMAE$</strong><div>${fmt(diag.AvgMaeUsd)}</div></div><div><strong>AvgStopUtil%</strong><div>${pct(diag.AvgStopUtilizationPct)}</div></div><div><strong>AvgMinutes</strong><div>${fmt(diag.AvgTimeToExitMinutes,1)}</div></div></div><div class="decomp-grid">${renderBucketTable('HardStop bySymbol', diag.BySymbol)}${renderBucketTable('HardStop byEntryHourUtc', diag.ByEntryHourUtc)}${renderBucketTable('HardStop bySetup', diag.BySetup)}</div><div class="table-wrap"><table class="metric-table"><thead><tr><th>Symbol</th><th>Side</th><th>Setup</th><th>Hour</th><th>PnL$</th><th>PnLR</th><th>MFE$</th><th>MAE$</th><th>Giveback$</th><th>StopDist$</th><th>StopUtil%</th><th>Minutes</th><th>Bars</th></tr></thead><tbody>${(diag.Trades || []).map(row => `<tr><td class="key-cell">${row.Symbol}</td><td>${row.Side}</td><td>${row.Setup}</td><td>${row.EntryHourUtc}:00</td><td class="${pnlClass(row.PnlUsd)}">${fmt(row.PnlUsd)}</td><td>${fmt(row.PnlR,4)}</td><td>${fmt(row.MfeUsd)}</td><td>${fmt(row.MaeUsd)}</td><td>${fmt(row.GivebackUsd)}</td><td>${fmt(row.StopDistanceUsd)}</td><td>${pct(row.StopUtilizationPct)}</td><td>${fmt(row.TimeToExitMinutes,1)}</td><td>${row.BarsHeld}</td></tr>`).join('')}</tbody></table></div></div>`;
}
function renderMfeMaeDiagnostics(diag){
    return `<div class="decomp-subcard"><h3>mfeMaeDiagnostics</h3><div class="decomp-grid">${renderMfeMaeBucketTable('MFE/MAE bySetup', diag.BySetup)}${renderMfeMaeBucketTable('MFE/MAE byExitReason', diag.ByExitReason)}</div><div class="decomp-grid"><div class="decomp-subcard"><h3>Top Giveback Trades</h3><div class="table-wrap"><table class="metric-table"><thead><tr><th>Symbol</th><th>Side</th><th>Exit</th><th>Setup</th><th>Hour</th><th>PnL$</th><th>PnLR</th><th>MFE$</th><th>MAE$</th><th>Giveback$</th><th>Capture%</th><th>MAE/Risk</th></tr></thead><tbody>${(diag.TopGivebackTrades || []).map(row => `<tr><td class="key-cell">${row.Symbol}</td><td>${row.Side}</td><td>${row.ExitReason}</td><td>${row.Setup}</td><td>${row.EntryHourUtc}:00</td><td class="${pnlClass(row.PnlUsd)}">${fmt(row.PnlUsd)}</td><td>${fmt(row.PnlR,4)}</td><td>${fmt(row.MfeUsd)}</td><td>${fmt(row.MaeUsd)}</td><td>${fmt(row.GivebackUsd)}</td><td>${pct(row.ProfitCapturePct)}</td><td>${pct(row.AdverseExcursionPctOfRisk)}</td></tr>`).join('')}</tbody></table></div></div><div class="decomp-subcard"><h3>Top MAE Trades</h3><div class="table-wrap"><table class="metric-table"><thead><tr><th>Symbol</th><th>Side</th><th>Exit</th><th>Setup</th><th>Hour</th><th>PnL$</th><th>PnLR</th><th>MFE$</th><th>MAE$</th><th>Giveback$</th><th>Capture%</th><th>MAE/Risk</th></tr></thead><tbody>${(diag.TopMaeTrades || []).map(row => `<tr><td class="key-cell">${row.Symbol}</td><td>${row.Side}</td><td>${row.ExitReason}</td><td>${row.Setup}</td><td>${row.EntryHourUtc}:00</td><td class="${pnlClass(row.PnlUsd)}">${fmt(row.PnlUsd)}</td><td>${fmt(row.PnlR,4)}</td><td>${fmt(row.MfeUsd)}</td><td>${fmt(row.MaeUsd)}</td><td>${fmt(row.GivebackUsd)}</td><td>${pct(row.ProfitCapturePct)}</td><td>${pct(row.AdverseExcursionPctOfRisk)}</td></tr>`).join('')}</tbody></table></div></div></div></div>`;
}
function renderMetricDecomposition(){
    const host = document.getElementById('metric-decomposition');
    const groups = reportData.decompositions || [];
    if(!groups.length){
        host.innerHTML = '<div class="muted">No decomposition data available for this report.</div>';
        return;
    }
    const selectedIndex = Number(document.getElementById('decomposition-filter').value || 0);
    const group = groups[Math.max(0, Math.min(groups.length - 1, selectedIndex))];
    host.innerHTML = `<div class="stat-strip"><div><strong>Strategy</strong><div>${group.Strategy}</div></div><div><strong>Variant</strong><div>${group.Variant}</div></div><div><strong>Trades</strong><div>${group.TradeCount}</div></div></div><div class="decomp-grid">${renderBucketTable('bySymbol', group.BySymbol)}${renderBucketTable('bySide', group.BySide)}${renderBucketTable('byExitReason', group.ByExitReason)}${renderBucketTable('byEntryHourUtc', group.ByEntryHourUtc)}${renderBucketTable('bySetup', group.BySetup)}</div>${renderDrawdownTable(group.DrawdownContributors)}${renderHardStopDiagnostics(group.HardStopDiagnostics)}${renderMfeMaeDiagnostics(group.MfeMaeDiagnostics)}`;
}
function strategyOptions(){
  const select = document.getElementById('strategy-filter');
  const strategies = [...new Set(reportData.trades.map(t => t.strategy))].sort();
  strategies.forEach(strategy => {
    const opt = document.createElement('option');
    opt.value = strategy; opt.textContent = strategy; select.appendChild(opt);
  });
}
function filteredTrades(){
  const q = (document.getElementById('trade-filter').value || '').toLowerCase();
  const strategy = document.getElementById('strategy-filter').value;
  return reportData.trades.filter(t => {
    if(strategy && t.strategy !== strategy) return false;
    if(!q) return true;
        const paired = t.pairedSecondLegPreview || t.pairedOppositePreview;
                const hay = [t.strategy,t.variant,t.symbol,t.side,paired?.side,paired?.balanceStatus,paired?.flattenDecision?.status,paired?.flattenDecision?.reason,paired?.reverseFlattenDecision?.status,paired?.reverseFlattenDecision?.reason,paired?.exitReason,t.exitReason,t.subStrategy,t.setup,t.compositeSetup,t.selectedSubStrategy,t.normalizedPatternKey,t.normalizedPatternFamily,(t.actionTypes || []).join(' '),(t.annotations || []).map(a => a.label).join(' ')].join(' ').toLowerCase();
    return hay.includes(q);
  });
}
function buildAnnotationSummary(trades){
    const grouped = new Map();
    trades.forEach(trade => {
        (trade.annotations || []).forEach(annotation => {
            const key = String(annotation.key || annotation.label || 'annotation').toLowerCase();
            if(!grouped.has(key)){
                grouped.set(key, {
                    key,
                    label: annotation.label || annotation.key || 'Annotation',
                    tone: annotation.tone || 'info',
                    count: 0,
                    symbols: new Map()
                });
            }
            const bucket = grouped.get(key);
            bucket.count += 1;
            const symbol = trade.symbol || '-';
            bucket.symbols.set(symbol, (bucket.symbols.get(symbol) || 0) + 1);
        });
    });

    return [...grouped.values()]
        .map(item => ({
            ...item,
            symbols: [...item.symbols.entries()]
                .sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0]))
                .map(([symbol, count]) => ({ symbol, count }))
        }))
        .sort((left, right) => right.count - left.count || left.label.localeCompare(right.label));
}
function renderAnnotationSummary(){
    const host = document.getElementById('annotation-summary');
    const trades = filteredTrades();
    const summary = buildAnnotationSummary(trades);
    if(!summary.length){
        host.innerHTML = '<div class="card"><h3>Annotation Summary</h3><div class="muted">No annotation pills match the current filter.</div></div>';
        return;
    }
    host.innerHTML = `<div class="card"><h3>Annotation Summary</h3><div class="muted">Grouped by annotation type, count, and impacted symbols for the current trade filter.</div><div class="annotation-summary-grid">${summary.map(item => `<div class="annotation-summary-card"><div><span class="pill ${annotationToneClass(item.tone)}">${item.label}</span></div><div class="annotation-summary-meta"><span><strong>Trades</strong> ${item.count}</span><span><strong>Symbols</strong> ${item.symbols.length}</span></div><div class="annotation-symbols">${item.symbols.map(symbol => `<span class="pill">${symbol.symbol} x${symbol.count}</span>`).join('')}</div></div>`).join('')}</div></div>`;
}
function annotationToneClass(tone){ return tone === 'warning' ? 'warn' : 'info'; }
function actionClass(actionType){
    const normalized = String(actionType || '').toLowerCase();
    if(normalized === 'profit-extension-armed') return 'action-profit-extension';
    if(normalized === 'trend-change-flatten') return 'action-trend-change';
    if(normalized === 'opposite-flatten') return 'action-opposite-flatten';
    return '';
}
function renderApexPills(apex){
    if(!apex || !apex.components) return '<span class="muted">No Apex contribution recorded.</span>';
    return apex.components.map(component => {
        const cls = component.active ? '' : 'warn';
        const state = component.active ? `+${pct(component.bonusPct)}` : 'inactive';
        return `<span class="pill ${cls}" title="${component.detail}">${component.label}: ${state}</span>`;
    }).join('');
}
function extractSetupToken(trade){
    const raw = String(trade.normalizedPatternKey || trade.apexBonus?.patternKey || trade.selectedSubStrategy || trade.subStrategy || trade.setup || '').toUpperCase();
    if(!raw) return '';
    let token = raw.includes('::') ? raw.split('::')[1] : raw;
    token = token.includes('|') ? token.split('|')[0] : token;
    token = token.includes('+') ? token.split('+')[0] : token;
    return token.trim();
}
function resolveSetupLabel(trade){
    if(trade.apexBonus?.displayLabel && trade.apexBonus.displayLabel !== 'No Apex Pattern') return trade.apexBonus.displayLabel;
    const token = extractSetupToken(trade);
    const map = {
        'BUY_SETUP':'Buy Setup',
        'SELL_SETUP':'Sell Setup',
        'BREAKOUT':'Breakout',
        'BREAKDOWN':'Breakdown',
        '123_LONG':'123 Long',
        '123_SHORT':'123 Short',
        'EXHAUSTION_LONG':'Exhaustion Long',
        'EXHAUSTION_SHORT':'Exhaustion Short',
        'VWAP_REVERSION':'VWAP Reversion',
        'BB_BOUNCE':'BB Bounce',
        'PARABOLIC_EXHAUSTION':'Parabolic Exhaustion',
        'V3_COMPOSITE':'Conduct V3 Composite'
    };
    return map[token] || (token ? token.replaceAll('_',' ') : 'Unlabeled Setup');
}
function resolveApexTotalBonusPct(trade){
    const value = Number(trade.apexBonus?.totalBonusPct || 0);
    return Number.isFinite(value) ? value : 0;
}
function renderApexTitleBadge(trade){
    const value = resolveApexTotalBonusPct(trade);
    if(!(value > 0)) return '';
    return `<span class="apex-title-badge">Apex ${pct(value)}</span>`;
}
function computeMa20TrendInfo(trade){
    const explicitStatus = String(trade.apexBonus?.smaTrendStatus || '').toLowerCase();
    if(explicitStatus === 'unavailable') return { label:'unavailable', aligned:false, entryValue:null };
    if(explicitStatus === 'aligned') return { label:'aligned', aligned:true, entryValue:toFiniteNumber(trade.ma20) };
    if(explicitStatus === 'countertrend') return { label:'countertrend', aligned:false, entryValue:toFiniteNumber(trade.ma20) };
    const bars = trade.bars || [];
    const entryIndex = bars.findIndex(b => b.phase === 'entry');
    if(entryIndex <= 0) return { label:'n/a', aligned:false, entryValue:null };
    const startIndex = Math.max(0, entryIndex - 7);
    const first = toFiniteNumber(bars[startIndex]?.ma20);
    const current = toFiniteNumber(bars[entryIndex]?.ma20);
    if(first === null || current === null) return { label:'n/a', aligned:false, entryValue:current };
    const delta = current - first;
    const close = toFiniteNumber(bars[entryIndex]?.close);
    const side = String(trade.side || '').toLowerCase();
    const label = Math.abs(delta) < 0.0001 ? 'flat' : delta > 0 ? 'rising' : 'falling';
    const aligned = side === 'long'
        ? delta > 0 && close !== null && close >= current
        : side === 'short'
            ? delta < 0 && close !== null && close <= current
            : false;
    return { label, aligned, entryValue:current };
}
function resolveMicrostructureStatus(trade, key){
    const value = trade.apexBonus?.[key];
    if(value === true) return 'yes';
    if(value === false) return 'no';
    return 'n/a';
}
function drawPreEntryWindow(ctx, bars, x, pad, width, height, entryBarIndex){
    if(entryBarIndex <= 0) return;
    const startIndex = Math.max(0, entryBarIndex - 7);
    const endIndex = entryBarIndex - 1;
    const step = (width - pad.l - pad.r) / Math.max(bars.length - 1, 1);
    const left = x(startIndex) - Math.max(6, step / 2);
    const right = x(endIndex) + Math.max(6, step / 2);
    ctx.save();
    ctx.fillStyle = 'rgba(242, 214, 162, 0.28)';
    ctx.fillRect(left, pad.t, right - left, height - pad.t - pad.b);
    ctx.strokeStyle = 'rgba(180, 130, 60, 0.65)';
    ctx.setLineDash([3, 3]);
    ctx.strokeRect(left, pad.t, right - left, height - pad.t - pad.b);
    ctx.setLineDash([]);
    ctx.fillStyle = '#8a5a14';
    ctx.font = '11px Segoe UI';
    ctx.fillText('Pre-Entry 7-Bar Window', left + 6, pad.t + 14);
    ctx.restore();
}
function drawSetupCallout(ctx, trade, entryX, entryY){
    const setupLabel = resolveSetupLabel(trade);
    const bonusText = resolveApexTotalBonusPct(trade) > 0 ? ` | Bonus ${pct(resolveApexTotalBonusPct(trade))}` : '';
    const text = `${setupLabel}${bonusText}`;
    ctx.save();
    ctx.font = '12px Segoe UI';
    const width = ctx.measureText(text).width + 16;
    const boxX = Math.max(10, entryX - width / 2);
    const boxY = Math.max(18, entryY - 34);
    ctx.fillStyle = 'rgba(29, 53, 87, 0.92)';
    ctx.fillRect(boxX, boxY, width, 20);
    ctx.fillStyle = '#fff';
    ctx.fillText(text, boxX + 8, boxY + 14);
    ctx.restore();
}
function drawTrendInfoBox(ctx, trade, pad){
    const ma20 = computeMa20TrendInfo(trade);
    const lines = [
        `Setup: ${resolveSetupLabel(trade)}`,
        `MA20 trend: ${ma20.label}${ma20.aligned ? ' aligned' : ''}`,
        `L1: ${resolveMicrostructureStatus(trade, 'l1Confirmed')} | L2: ${resolveMicrostructureStatus(trade, 'l2Confirmed')}`,
        `Apex bonus: ${pct(resolveApexTotalBonusPct(trade))}`
    ];
    ctx.save();
    ctx.font = '11px Segoe UI';
    const width = Math.max(...lines.map(line => ctx.measureText(line).width)) + 14;
    const height = 16 + (lines.length * 14);
    ctx.fillStyle = 'rgba(255, 253, 248, 0.92)';
    ctx.fillRect(pad.l + 8, pad.t + 8, width, height);
    ctx.strokeStyle = '#d8d2c3';
    ctx.strokeRect(pad.l + 8, pad.t + 8, width, height);
    ctx.fillStyle = '#433325';
    lines.forEach((line, index) => ctx.fillText(line, pad.l + 15, pad.t + 24 + (index * 14)));
    ctx.restore();
}
function resolveFlattenDecision(trade){
    if(trade?.chartMode === 'opposite' && trade?.pairedOppositePreview && !trade?.pairedSecondLegPreview) return null;
    const paired = trade?.pairedOppositePreview || trade?.pairedSecondLegPreview;
    if(!paired) return null;
    const forward = paired.flattenDecision || null;
    const reverse = paired.reverseFlattenDecision || null;
    if(trade?.chartMode === 'opposite') return forward;
    if(forward?.triggered) return forward;
    if(reverse?.triggered) return reverse;
    return forward || reverse;
}
function resolveDecisionLocalIndex(bars, decision){
    if(!decision) return -1;
    if(decision.barIndex !== null && decision.barIndex !== undefined){
        const byBarIndex = bars.findIndex(bar => Number(bar.barIndex ?? bar.BarIndex ?? -1) === Number(decision.barIndex));
        if(byBarIndex >= 0) return byBarIndex;
        const firstBarIndex = Number(bars[0]?.barIndex ?? bars[0]?.BarIndex ?? 0);
        const offset = Number(decision.barIndex) - firstBarIndex;
        if(Number.isFinite(offset) && offset >= 0 && offset < bars.length) return offset;
    }
    if(decision.timestampUtc){
        const target = String(decision.timestampUtc).slice(0,16);
        const byTime = bars.findIndex(bar => String(bar.timestampUtc || '').slice(0,16) === target);
        if(byTime >= 0) return byTime;
    }
    return -1;
}
function drawOppositeFlattenMarker(ctx, trade, bars, x, y, pad, height){
    const decision = resolveFlattenDecision(trade);
    if(!decision || !decision.triggered) return;
    const idx = resolveDecisionLocalIndex(bars, decision);
    if(idx < 0) return;
    const price = toFiniteNumber(trade.chartMode === 'opposite' ? decision.oppositeExitPrice : decision.primaryPrice);
    if(price === null) return;
    const xi = x(idx);
    const yi = y(price);
    const label = trade.chartMode === 'opposite'
        ? (decision.loserRole === 'primary' ? 'Flatten losing primary' : 'Flatten losing opposite')
        : (decision.loserRole === 'primary' ? 'Primary flatten' : 'Opposite flatten');
    ctx.save();
    ctx.strokeStyle = '#ffb703';
    ctx.fillStyle = '#ffb703';
    ctx.lineWidth = 2;
    ctx.setLineDash([3, 3]);
    ctx.beginPath(); ctx.moveTo(xi, pad.t); ctx.lineTo(xi, height - pad.b); ctx.stroke();
    ctx.setLineDash([]);
    ctx.beginPath(); ctx.moveTo(xi, yi - 7); ctx.lineTo(xi + 7, yi); ctx.lineTo(xi, yi + 7); ctx.lineTo(xi - 7, yi); ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#6b4e00';
    ctx.font = '11px Segoe UI';
    ctx.fillText(label, Math.min(xi + 9, Math.max(pad.l + 4, (canvasWidthSafe(ctx) - pad.r) - 132)), Math.max(pad.t + 14, yi - 10));
    ctx.restore();
}
function canvasWidthSafe(ctx){ return ctx.canvas ? ctx.canvas.width / (window.devicePixelRatio || 1) : 1000; }
function renderFlattenDecision(decision){
    if(!decision) return '<div class="muted">Paired opposite flatten decision unavailable.</div>';
    if(!decision.triggered) return `<div class="muted">Paired opposite flatten decision: ${decision.status} â€” ${decision.reason}</div>`;
    const statusClass = decision.lossWithinSmallLossGate ? '' : 'warn';
    return `<div><strong>Paired opposite flatten decision</strong><div><span class="pill ${statusClass}">${decision.status}</span> <span class="mono">${decision.timestampUtc || '-'}</span> @ ${fmt(decision.primaryPrice,4)}</div><div class="muted">${decision.reason}</div><div class="muted">primary/share ${fmtSigned(decision.primaryPnlPerShareUsdAtDecision,4)} | opposite loss/share ${fmt(decision.oppositeLossPerShareUsdAtDecision,4)} | R ${fmt(decision.primaryRMultipleAtDecision,2)} | ${decision.sourceEventType || '-'}</div></div>`;
}
function renderDirectedFlattenDecision(title, decision){
    if(!decision) return '';
    if(!decision.triggered) return `<div><strong>${title}</strong><div class="muted">${decision.status} â€” ${decision.reason}</div></div>`;
    const statusClass = decision.lossWithinSmallLossGate ? '' : 'warn';
    return `<div><strong>${title}</strong><div><span class="pill ${statusClass}">${decision.status}</span> <span class="mono">${decision.timestampUtc || '-'}</span> @ ${fmt(decision.primaryPrice,4)}</div><div class="muted">${decision.reason}</div><div class="muted">${decision.winnerRole || 'winner'} winner/share ${fmtSigned(decision.primaryPnlPerShareUsdAtDecision,4)} | ${decision.loserRole || 'loser'} loss/share ${fmt(decision.oppositeLossPerShareUsdAtDecision,4)} | R ${fmt(decision.primaryRMultipleAtDecision,2)} | ${decision.sourceEventType || '-'}</div></div>`;
}
function resolveFlattenLegendLabel(trade){
    const decision = resolveFlattenDecision(trade);
    if(!decision || !decision.triggered) return 'Pair flatten marker';
    if(trade?.chartMode === 'opposite') return decision.loserRole === 'primary' ? 'Flatten losing primary' : 'Flatten losing opposite';
    return decision.loserRole === 'primary' ? 'Primary flatten' : 'Opposite flatten';
}
function resolveFlattenPillLabel(paired){
    if(!paired) return 'Pair flatten';
    const forward = paired.flattenDecision || null;
    const reverse = paired.reverseFlattenDecision || null;
    const active = forward?.triggered ? forward : (reverse?.triggered ? reverse : forward || reverse);
    if(!active || !active.triggered) return 'Pair flatten';
    return active.loserRole === 'primary' ? 'Primary flatten' : 'Opposite flatten';
}
function renderDiagramTitle(prefix, symbol, side, quantity, pnlUsd){
    return `${prefix} - ${symbol || '-'} / <span class="${sideClass(side)}">${side || '-'}</span> / qty ${quantity ?? 0} / pnl <span class="${pnlClass(pnlUsd)}">${fmtSigned(pnlUsd)}</span>`;
}
function buildOppositeChartTrade(trade){
    const paired = trade.pairedSecondLegPreview || trade.pairedOppositePreview;
    if(!paired) return null;
    const decision = paired.flattenDecision;
    const decisionTriggered = Boolean(decision && decision.triggered);
    const isActualPairedLeg = Boolean(trade.pairedSecondLegPreview);
    const baseBars = Array.isArray(paired.bars) && paired.bars.length ? paired.bars : (trade.bars || []);
    const baseActions = Array.isArray(paired.actions) && paired.actions.length ? paired.actions : [];
    return {
        ...trade,
        chartMode: 'opposite',
        side: paired.side,
        entryTimeUtc: paired.entryTimeUtc,
        exitTimeUtc: isActualPairedLeg && decisionTriggered ? decision.timestampUtc : paired.exitTimeUtc,
        entryPrice: paired.entryPrice,
        exitPrice: isActualPairedLeg && decisionTriggered ? decision.oppositeExitPrice : paired.exitPrice,
        stopPrice: paired.stopPrice || paired.entryPrice,
        pnlUsd: isActualPairedLeg && decisionTriggered ? decision.oppositePnlUsdAtDecision : paired.pnlUsd,
        exitReason: isActualPairedLeg && decisionTriggered ? (decision.status || paired.exitReason) : (paired.exitReason || trade.exitReason),
        bars: baseBars,
        pairedOppositePreview: paired,
        pairedSecondLegPreview: trade.pairedSecondLegPreview,
        actions: baseActions
    };
}
function renderActionTimeline(title, items, emptyMessage){
    return `<div class="actions"><h3>${title}</h3><ol>${items || `<li>${emptyMessage}</li>`}</ol></div>`;
}
function renderPairedTradeBlock(trade, idx){
    const paired = trade.pairedSecondLegPreview;
    if(!paired) return '';
    const pairedChartTrade = buildOppositeChartTrade(trade);
    if(!pairedChartTrade) return '';
    const flattenDecision = paired.flattenDecision;
    const flattenPill = flattenDecision?.triggered ? `<span class="pill ${flattenDecision.lossWithinSmallLossGate ? '' : 'warn'}">Opposite flatten</span>` : '';
    const pairedActionItems = (pairedChartTrade.actions || []).map((action, actionIndex) => `<li class="${actionClass(action.actionType)}"><strong>${actionIndex + 1}.</strong> <span class="mono">${action.timestampUtc}</span> | <strong>${action.actionType}</strong> @ ${fmt(action.price,4)} | ${action.description}</li>`).join('');
    const pairedTimeline = [`<li><strong>Entry.</strong> <span class="mono">${paired.entryTimeUtc}</span> | <strong>${paired.side}</strong> @ ${fmt(paired.entryPrice,4)} | qty ${paired.quantity}</li>`, `<li><strong>Exit.</strong> <span class="mono">${pairedChartTrade.exitTimeUtc || paired.exitTimeUtc || '-'}</span> | <strong>${paired.exitReason || flattenDecision?.status || 'exit'}</strong> @ ${fmt(pairedChartTrade.exitPrice,4)} | pnl <span class="${pnlClass(pairedChartTrade.pnlUsd)}">${fmtSigned(pairedChartTrade.pnlUsd)}</span></li>`, pairedActionItems].filter(Boolean).join('');
    return `<div class="trade-subcard paired-trade-card"><h3>Paired Account Trade ${flattenPill}</h3><div class="grid"><div><strong>Entry</strong><div>${paired.entryTimeUtc}</div><div>${fmt(paired.entryPrice,4)}</div></div><div><strong>Exit</strong><div>${pairedChartTrade.exitTimeUtc || paired.exitTimeUtc || '-'}</div><div>${fmt(pairedChartTrade.exitPrice,4)}</div></div><div><strong>Reason</strong><div>${paired.exitReason || flattenDecision?.status || '-'}</div><div class="muted">role ${paired.role || '-'}${paired.accountId ? ` | acct ${paired.accountId}` : ''}</div></div><div><strong>PnL</strong><div><span class="${pnlClass(paired.pnlUsd)}">${fmtSigned(paired.pnlUsd)}</span></div><div class="muted">${fmtSigned(paired.pnlPerShareUsd,4)}/share</div></div><div><strong>Pair Balance</strong><div><span class="pill ${paired.balanced ? '' : 'warn'}">${paired.balanceStatus}</span></div><div class="muted">winner/share ${fmt(paired.winnerPnlPerShareUsd,4)} | loss/share ${fmt(paired.loserLossPerShareUsd,4)}</div></div><div><strong>Flatten Decision</strong>${renderFlattenDecision(flattenDecision)}</div></div><div class="muted" style="margin:10px 0 12px">${paired.note}</div><h3>${renderDiagramTitle('Paired account diagram', paired.symbol, paired.side, paired.quantity, paired.pnlUsd)}</h3><div class="chart-legend"><span><i class="legend-swatch legend-ma20"></i>MA20</span><span><i class="legend-swatch legend-ma200"></i>MA200</span><span><i class="legend-swatch legend-window"></i>Pre-Entry 7-Bar Window</span><span><i class="legend-swatch legend-flatten"></i>${resolveFlattenLegendLabel(pairedChartTrade)}</span></div><canvas id="chart-${idx}-paired"></canvas>${renderActionTimeline('Paired Action Timeline', pairedTimeline, 'No paired actions recorded for this trade.')}</div>`;
}
function drawChart(canvas, trade){
  const bars = trade.bars || [];
  if(!bars.length) return;
  const ctx = canvas.getContext('2d');
  const ratio = window.devicePixelRatio || 1;
  const width = canvas.clientWidth || 960;
  const height = canvas.clientHeight || 280;
  canvas.width = width * ratio;
  canvas.height = height * ratio;
  ctx.scale(ratio, ratio);
  ctx.clearRect(0,0,width,height);
  const pad = {l:48,r:14,t:14,b:24};
  const lows = bars.map(b => Number(b.low));
  const highs = bars.map(b => Number(b.high));
    const indicatorValues = [];
    bars.forEach(bar => {
                const ema9 = toFiniteNumber(bar.ema9);
        const ma20 = toFiniteNumber(bar.ma20);
        const ma200 = toFiniteNumber(bar.ma200);
                if(ema9 !== null) indicatorValues.push(ema9);
        if(ma20 !== null) indicatorValues.push(ma20);
        if(ma200 !== null) indicatorValues.push(ma200);
    });
    const flattenDecision = resolveFlattenDecision(trade);
    const flattenPrice = flattenDecision?.triggered ? toFiniteNumber(trade.chartMode === 'opposite' ? flattenDecision.oppositeExitPrice : flattenDecision.primaryPrice) : null;
    const flattenBounds = flattenPrice === null ? [] : [flattenPrice];
    const min = Math.min(...lows, trade.stopPrice, trade.exitPrice, trade.entryPrice, ...(indicatorValues.length ? indicatorValues : [trade.entryPrice]), ...flattenBounds);
    const max = Math.max(...highs, trade.stopPrice, trade.exitPrice, trade.entryPrice, ...(indicatorValues.length ? indicatorValues : [trade.entryPrice]), ...flattenBounds);
  const y = p => pad.t + (max - p) / Math.max(max-min, 0.0001) * (height - pad.t - pad.b);
  const x = i => pad.l + i * ((width - pad.l - pad.r) / Math.max(bars.length - 1, 1));
    const drawLineSeries = (values, color, lineWidth) => {
        ctx.save();
        ctx.strokeStyle = color;
        ctx.lineWidth = lineWidth;
        ctx.beginPath();
        let started = false;
        values.forEach((value, index) => {
            const numericValue = toFiniteNumber(value);
            if(numericValue === null){ started = false; return; }
            const xi = x(index);
            const yi = y(numericValue);
            if(!started){ ctx.moveTo(xi, yi); started = true; }
            else { ctx.lineTo(xi, yi); }
        });
        ctx.stroke();
        ctx.restore();
    };
  ctx.strokeStyle = '#d7d1c0';
  ctx.lineWidth = 1;
  for(let i=0;i<4;i++){ const py = pad.t + i * ((height-pad.t-pad.b)/3); ctx.beginPath(); ctx.moveTo(pad.l,py); ctx.lineTo(width-pad.r,py); ctx.stroke(); }
    const entryBarIndex = bars.findIndex(b => b.phase === 'entry');
    drawPreEntryWindow(ctx, bars, x, pad, width, height, entryBarIndex);
  ctx.strokeStyle = '#2b6f77';
  ctx.lineWidth = 1.2;
  bars.forEach((bar, i) => {
    const xi = x(i);
    ctx.strokeStyle = '#555';
    ctx.beginPath(); ctx.moveTo(xi, y(bar.high)); ctx.lineTo(xi, y(bar.low)); ctx.stroke();
    ctx.fillStyle = Number(bar.close) >= Number(bar.open) ? '#2a9d8f' : '#c44536';
    const top = y(Math.max(bar.open, bar.close));
    const bottom = y(Math.min(bar.open, bar.close));
    ctx.fillRect(xi - 3, top, 6, Math.max(1, bottom - top));
  });
        drawLineSeries(bars.map(bar => bar.ema9), '#f4d03f', 1.7);
    drawLineSeries(bars.map(bar => bar.ma20), '#17b7d9', 1.8);
    drawLineSeries(bars.map(bar => bar.ma200), '#7b2cbf', 2.0);
    const entryX = x(entryBarIndex >= 0 ? entryBarIndex : 0);
    const entryY = y(trade.entryPrice);
    ctx.strokeStyle = '#1d3557'; ctx.setLineDash([4,3]);
  ctx.beginPath(); ctx.moveTo(pad.l,y(trade.entryPrice)); ctx.lineTo(width-pad.r,y(trade.entryPrice)); ctx.stroke();
  ctx.strokeStyle = '#9b2226';
  ctx.beginPath(); ctx.moveTo(pad.l,y(trade.stopPrice)); ctx.lineTo(width-pad.r,y(trade.stopPrice)); ctx.stroke();
  ctx.setLineDash([]);
    (trade.actions || []).forEach((action, actionIndex) => {
    const idx = Math.max(0, Math.min(bars.length - 1, Number(action.barIndex) - Number(bars[0].barIndex || bars[0].BarIndex || 0)));
    const xi = x(idx);
    const yi = y(action.price);
    ctx.fillStyle = String(action.actionType || '').toLowerCase() === 'profit-extension-armed' ? '#1d6fd8' : '#e76f51';
    ctx.beginPath(); ctx.arc(xi, yi, 4, 0, Math.PI * 2); ctx.fill();
        ctx.fillStyle = '#5b4636';
        ctx.fillText(String(actionIndex + 1), xi + 6, yi - 6);
  });
    drawOppositeFlattenMarker(ctx, trade, bars, x, y, pad, height);
  const exitBarIndex = bars.findIndex(b => b.phase === 'exit');
  const exitX = x(exitBarIndex >= 0 ? exitBarIndex : bars.length - 1);
  const exitY = y(trade.exitPrice);
    ctx.fillStyle = '#1d3557'; ctx.beginPath(); ctx.arc(entryX, entryY, 5, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = '#111'; ctx.beginPath(); ctx.arc(exitX, exitY, 5, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = '#333'; ctx.font = '11px Segoe UI';
    ctx.fillText(`Entry ${fmt(trade.entryPrice,4)}`, Math.max(pad.l + 4, entryX - 42), entryY - 8);
  ctx.fillText(`Stop ${fmt(trade.stopPrice,4)}`, pad.l + 4, y(trade.stopPrice) - 6);
  ctx.fillText(`Exit ${fmt(trade.exitPrice,4)}`, Math.max(pad.l + 4, exitX - 42), exitY - 8);
    drawSetupCallout(ctx, trade, entryX, entryY);
    drawTrendInfoBox(ctx, trade, pad);
}
function appendEma9LegendItem(legend){
    if(!legend || legend.querySelector('.legend-ema9')) return;
    if(!legend.querySelector('.legend-ma20')) return;
    const item = document.createElement('span');
    item.innerHTML = '<i class="legend-swatch legend-ema9" style="background:#f4d03f"></i>EMA9';
    const anchor = legend.querySelector('.legend-ma20')?.closest('span');
    if(anchor?.nextSibling) legend.insertBefore(item, anchor.nextSibling);
    else legend.appendChild(item);
}
function resolveSupplementalChart(trade){
    const chart = trade?.supplementalChart || null;
    if(!chart || !Array.isArray(chart.bars) || !chart.bars.length) return null;
    const strategy = String(trade?.strategy || '').toUpperCase();
    const timeframe = String(chart.timeframe || '').toLowerCase();
    if((strategy.startsWith('V21') || strategy.startsWith('V22')) && timeframe === '15m') return chart;
    if((strategy.startsWith('V23') || strategy.startsWith('V24')) && timeframe === '5m') return chart;
    return null;
}
function ensureSupplementalChartLayout(primaryCanvas, trade, idx){
    const supplemental = resolveSupplementalChart(trade);
    if(!primaryCanvas || !supplemental || !primaryCanvas.parentElement) return null;
    const existing = document.getElementById(`chart-${idx}-supplemental`);
    if(existing) return existing;
    const row = document.createElement('div');
    row.className = 'chart-compare-row';
    const primaryPanel = document.createElement('div');
    primaryPanel.className = 'chart-primary-panel';
    const secondaryPanel = document.createElement('div');
    secondaryPanel.className = 'chart-secondary-panel';
    const title = document.createElement('div');
    title.className = 'mini-chart-title';
    title.textContent = `${String(supplemental.timeframe || '').toUpperCase()} candle bars`;
    const subtitle = document.createElement('div');
    subtitle.className = 'mini-chart-subtitle';
    subtitle.textContent = 'Higher-timeframe context aligned to the same trade.';
    const secondaryCanvas = document.createElement('canvas');
    secondaryCanvas.id = `chart-${idx}-supplemental`;
    const parent = primaryCanvas.parentElement;
    parent.insertBefore(row, primaryCanvas);
    row.appendChild(primaryPanel);
    primaryPanel.appendChild(primaryCanvas);
    row.appendChild(secondaryPanel);
    secondaryPanel.appendChild(title);
    secondaryPanel.appendChild(subtitle);
    secondaryPanel.appendChild(secondaryCanvas);
    return secondaryCanvas;
}
function drawSupplementalChart(canvas, trade, supplemental){
  const bars = supplemental?.bars || [];
  if(!canvas || !bars.length) return;
  const ctx = canvas.getContext('2d');
  const ratio = window.devicePixelRatio || 1;
  const width = canvas.clientWidth || 320;
  const height = canvas.clientHeight || 220;
  canvas.width = width * ratio;
  canvas.height = height * ratio;
  ctx.scale(ratio, ratio);
  ctx.clearRect(0,0,width,height);
  const pad = {l:40,r:10,t:22,b:22};
  const lows = bars.map(b => Number(b.low));
  const highs = bars.map(b => Number(b.high));
  const indicatorValues = [];
  bars.forEach(bar => {
      const ema9 = toFiniteNumber(bar.ema9);
      const ma20 = toFiniteNumber(bar.ma20);
      const ma200 = toFiniteNumber(bar.ma200);
      if(ema9 !== null) indicatorValues.push(ema9);
      if(ma20 !== null) indicatorValues.push(ma20);
      if(ma200 !== null) indicatorValues.push(ma200);
  });
  const bounds = [trade.entryPrice, trade.exitPrice].map(toFiniteNumber).filter(value => value !== null);
  const min = Math.min(...lows, ...(indicatorValues.length ? indicatorValues : []), ...(bounds.length ? bounds : [Math.min(...lows)]));
  const max = Math.max(...highs, ...(indicatorValues.length ? indicatorValues : []), ...(bounds.length ? bounds : [Math.max(...highs)]));
  const y = p => pad.t + (max - p) / Math.max(max-min, 0.0001) * (height - pad.t - pad.b);
  const x = i => pad.l + i * ((width - pad.l - pad.r) / Math.max(bars.length - 1, 1));
  const drawLineSeries = (values, color, lineWidth) => {
      ctx.save();
      ctx.strokeStyle = color;
      ctx.lineWidth = lineWidth;
      ctx.beginPath();
      let started = false;
      values.forEach((value, index) => {
          const numericValue = toFiniteNumber(value);
          if(numericValue === null){ started = false; return; }
          const xi = x(index);
          const yi = y(numericValue);
          if(!started){ ctx.moveTo(xi, yi); started = true; }
          else { ctx.lineTo(xi, yi); }
      });
      ctx.stroke();
      ctx.restore();
  };
  ctx.strokeStyle = '#e2dac9';
  ctx.lineWidth = 1;
  for(let i=0;i<3;i++){
      const py = pad.t + i * ((height - pad.t - pad.b) / 2);
      ctx.beginPath();
      ctx.moveTo(pad.l, py);
      ctx.lineTo(width - pad.r, py);
      ctx.stroke();
  }
  bars.forEach((bar, i) => {
      const xi = x(i);
      ctx.strokeStyle = '#666';
      ctx.beginPath();
      ctx.moveTo(xi, y(bar.high));
      ctx.lineTo(xi, y(bar.low));
      ctx.stroke();
      ctx.fillStyle = Number(bar.close) >= Number(bar.open) ? '#2a9d8f' : '#c44536';
      const top = y(Math.max(bar.open, bar.close));
      const bottom = y(Math.min(bar.open, bar.close));
      ctx.fillRect(xi - 4, top, 8, Math.max(1, bottom - top));
  });
  drawLineSeries(bars.map(bar => bar.ema9), '#f4d03f', 1.4);
  drawLineSeries(bars.map(bar => bar.ma20), '#17b7d9', 1.5);
  drawLineSeries(bars.map(bar => bar.ma200), '#7b2cbf', 1.6);
  const entryIndex = bars.findIndex(bar => bar.phase === 'entry');
  const exitIndex = bars.findIndex(bar => bar.phase === 'exit');
  if(entryIndex >= 0){
      const entryX = x(entryIndex);
      const entryY = y(trade.entryPrice);
      ctx.save();
      ctx.strokeStyle = '#1d3557';
      ctx.setLineDash([4,3]);
      ctx.beginPath();
      ctx.moveTo(entryX, pad.t);
      ctx.lineTo(entryX, height - pad.b);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = '#1d3557';
      ctx.beginPath();
      ctx.arc(entryX, entryY, 4, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();
  }
  if(exitIndex >= 0){
      const exitX = x(exitIndex);
      const exitY = y(trade.exitPrice);
      ctx.save();
      ctx.strokeStyle = '#111';
      ctx.setLineDash([4,3]);
      ctx.beginPath();
      ctx.moveTo(exitX, pad.t);
      ctx.lineTo(exitX, height - pad.b);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = '#111';
      ctx.beginPath();
      ctx.arc(exitX, exitY, 4, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();
  }
  ctx.fillStyle = '#433325';
  ctx.font = '11px Segoe UI';
  ctx.fillText(`${String(supplemental.timeframe || '').toUpperCase()} context`, pad.l, 14);
}
function renderTrades(){
  const host = document.getElementById('trades');
  const trades = filteredTrades();
    renderAnnotationSummary();
  host.innerHTML = trades.map((t, idx) => {
        const actionItems = (t.actions || []).map((a, actionIndex) => `<li class="${actionClass(a.actionType)}"><strong>${actionIndex + 1}.</strong> <span class="mono">${a.timestampUtc}</span> | <strong>${a.actionType}</strong> @ ${fmt(a.price,4)} | ${a.description}</li>`).join('');
    const annotationPills = (t.annotations || []).map(a => `<span class="pill ${annotationToneClass(a.tone)}">${a.label}</span>`).join('');
        const summaryPills = (t.annotations || []).slice(0, 2).map(a => `<span class="pill ${annotationToneClass(a.tone)}">${a.label}</span>`).join('');
        const apexTitleBadge = renderApexTitleBadge(t);
        const paired = t.pairedSecondLegPreview || t.pairedOppositePreview;
                const flattenDecision = paired?.flattenDecision;
                const reverseFlattenDecision = paired?.reverseFlattenDecision;
                const flattenTriggered = Boolean(flattenDecision?.triggered || reverseFlattenDecision?.triggered);
                const flattenWarn = Boolean((flattenDecision?.triggered && !flattenDecision.lossWithinSmallLossGate) || (reverseFlattenDecision?.triggered && !reverseFlattenDecision.lossWithinSmallLossGate));
                const flattenPill = flattenTriggered ? `<span class="pill ${flattenWarn ? 'warn' : ''}">${resolveFlattenPillLabel(paired)}</span>` : '';
            const pairedTitle = t.pairedSecondLegPreview ? 'Actual Paired Leg' : 'Paired Preview';
            const pairedSecondLine = paired ? `<span style="display:block;margin-top:6px;color:#5b4636;font-weight:500">${paired.strategy} #${paired.strategyTradeNumber}/${paired.strategyTradeCount} / ${paired.variant} / ${paired.symbol} / <span class="${sideClass(paired.side)}">${paired.side}</span> / qty ${paired.quantity} / pnl <span class="${pnlClass(paired.pnlUsd)}">${fmtSigned(paired.pnlUsd)}</span> <span class="pill ${paired.balanced ? '' : 'warn'}">${paired.balanceStatus}</span> ${flattenPill}</span>` : '';
            const pairedExitLine = paired?.exitReason ? `<div class="muted">exit ${paired.exitReason}${paired.accountId ? ` | acct ${paired.accountId}` : ''}</div>` : '';
            const pairedBalanceBlock = paired && !t.pairedSecondLegPreview ? `<div><strong>${pairedTitle}</strong><div>${paired.strategy} / <span class="${sideClass(paired.side)}">${paired.side}</span> / qty ${paired.quantity}</div><div>pnl <span class="${pnlClass(paired.pnlUsd)}">${fmtSigned(paired.pnlUsd)}</span> (${fmtSigned(paired.pnlPerShareUsd,4)}/share)</div><div class="muted"><strong>Primary Exit</strong> ${t.exitReason} @ ${fmt(t.exitPrice,4)} | <strong>Opposite Exit</strong> ${paired.exitReason || '-'} @ ${fmt(paired.exitPrice,4)}</div><div class="muted">winner/share ${fmt(paired.winnerPnlPerShareUsd,4)} | loss/share ${fmt(paired.loserLossPerShareUsd,4)} | net/share <span class="${pnlClass(paired.netPnlPerShareUsd)}">${fmtSigned(paired.netPnlPerShareUsd,4)}</span> | ratio ${fmt(paired.winLossPerShareRatio,2)}x</div>${pairedExitLine}${renderDirectedFlattenDecision('Winning primary -> Forced-opposite flatten', flattenDecision)}${reverseFlattenDecision ? renderDirectedFlattenDecision('Forced-opposite -> Primary flatten', reverseFlattenDecision) : ''}<div class="muted">${paired.note}</div></div>` : '';
            const forcedOppositeTrade = paired && !t.pairedSecondLegPreview ? buildOppositeChartTrade(t) : null;
            const oppositeActionItems = forcedOppositeTrade ? (forcedOppositeTrade.actions || []).map((a, actionIndex) => `<li class="${actionClass(a.actionType)}"><strong>${actionIndex + 1}.</strong> <span class="mono">${a.timestampUtc}</span> | <strong>${a.actionType}</strong> @ ${fmt(a.price,4)} | ${a.description}</li>`).join('') : '';
            const primaryTimeline = renderActionTimeline('Primary Action Timeline', actionItems, 'No intermediate action markers recorded for this trade.');
                const oppositeChart = paired && !t.pairedSecondLegPreview ? `<h3>${renderDiagramTitle('Forced-opposite losing-leg diagram', paired.symbol, paired.side, paired.quantity, paired.pnlUsd)}</h3><div class="chart-legend"><span class="muted">Opposite side simulated independently from the same strategy/variant; report totals remain actual executed primary backtest trades only.</span></div><canvas id="chart-${idx}-opposite"></canvas>${renderActionTimeline('Forced-opposite Action Timeline', oppositeActionItems, 'No opposite-side actions recorded before exit.')}` : '';
                const pairedTradeBlock = renderPairedTradeBlock(t, idx);
                const primaryHeading = t.pairedSecondLegPreview ? '<h3>Primary Trade</h3>' : '';
                return `<details class="card"><summary>${t.strategy} #${t.strategyTradeNumber}/${t.strategyTradeCount} / ${t.variant}${variantBadge(t.variant)} / ${t.symbol} / <span class="${sideClass(t.side)}">${t.side}</span> / qty ${t.quantity} / pnl <span class="${pnlClass(t.pnlUsd)}">${fmtSigned(t.pnlUsd)}</span> ${summaryPills}${apexTitleBadge}${pairedSecondLine}</summary><div class="trade-group-stack"><div class="trade-subcard">${primaryHeading}<div class="grid"><div><strong>Entry</strong><div>${t.entryTimeUtc}</div><div>${fmt(t.entryPrice,4)}</div></div><div><strong>Exit</strong><div>${t.exitTimeUtc}</div><div>${fmt(t.exitPrice,4)}</div></div><div><strong>Reason</strong><div>${t.exitReason}</div><div class="muted">Bars held: ${t.barsHeld}</div><div class="audit-flags">${annotationPills || '<span class="muted">No special audit annotations.</span>'}</div></div><div><strong>MFE / MAE</strong><div><span class="${pnlClass(t.mfeUsd)}">${fmtSigned(t.mfeUsd)}</span> / <span class="${pnlClass(-Math.abs(Number(t.maeUsd || 0)))}">${fmtSigned(-Math.abs(Number(t.maeUsd || 0)))}</span></div><div class="muted">PeakR: <span class="${pnlClass(t.peakR)}">${fmtSigned(t.peakR,4)}</span></div></div>${pairedBalanceBlock}<div><strong>Self-Learning</strong><div>Stop x ${fmt(t.appliedSelfLearning.stopMultiplier,4)}</div><div>Pos x ${fmt(t.appliedSelfLearning.positionMultiplier,4)}</div><div class="muted">Action: ${t.appliedSelfLearning.setupAction || '-'} | V3 exit: ${t.appliedSelfLearning.hasExitOverrides ? 'yes' : 'no'}</div></div><div><strong>Apex Bonus</strong><div>${resolveSetupLabel(t)}</div><div class="muted">Total ${pct(resolveApexTotalBonusPct(t))} | MA20 ${computeMa20TrendInfo(t).label}</div><div class="audit-flags">${renderApexPills(t.apexBonus)}</div></div></div><h3>${renderDiagramTitle('Winning primary diagram', t.symbol, t.side, t.quantity, t.pnlUsd)}</h3><div class="chart-legend"><span><i class="legend-swatch legend-ma20"></i>MA20</span><span><i class="legend-swatch legend-ma200"></i>MA200</span><span><i class="legend-swatch legend-window"></i>Pre-Entry 7-Bar Window</span><span><i class="legend-swatch legend-setup"></i>Setup Marker</span><span><i class="legend-swatch legend-flatten"></i>${resolveFlattenLegendLabel(t)}</span></div><canvas id="chart-${idx}"></canvas>${primaryTimeline}${oppositeChart}</div>${pairedTradeBlock}</div></details>`;
    }).join('');
        host.querySelectorAll('.chart-legend').forEach(appendEma9LegendItem);
    trades.forEach((trade, idx) => {
        const canvas = document.getElementById(`chart-${idx}`);
        const supplementalCanvas = canvas ? ensureSupplementalChartLayout(canvas, trade, idx) : null;
        if(canvas) drawChart(canvas, trade);
        if(supplementalCanvas) drawSupplementalChart(supplementalCanvas, trade, resolveSupplementalChart(trade));
        const pairedCanvas = document.getElementById(`chart-${idx}-paired`);
        const pairedTrade = trade.pairedSecondLegPreview ? buildOppositeChartTrade(trade) : null;
        if(pairedCanvas && pairedTrade) drawChart(pairedCanvas, pairedTrade);
        const oppositeCanvas = document.getElementById(`chart-${idx}-opposite`);
        const forcedOppositeTrade = trade.pairedSecondLegPreview ? null : buildOppositeChartTrade(trade);
        if(oppositeCanvas && forcedOppositeTrade) drawChart(oppositeCanvas, forcedOppositeTrade);
    });
}
document.getElementById('trade-filter').addEventListener('input', renderTrades);
document.getElementById('strategy-filter').addEventListener('change', renderTrades);
document.getElementById('summary-min-trades-filter').addEventListener('change', renderSummaryTable);
document.getElementById('decomposition-filter').addEventListener('change', renderMetricDecomposition);
renderTotals(); renderExecutionAssumptions(); renderSelfLearning(); renderSummaryTable(); renderPromotionRanking(); decompositionOptions(); renderMetricDecomposition(); strategyOptions(); renderTrades();
""");
        sb.AppendLine("</script>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private sealed class ReplayMetrics
    {
        public double MfeUsd { get; init; }
        public double MaeUsd { get; init; }
        public double PeakFavorablePrice { get; init; }
        public DateTime? PeakFavorableTimestampUtc { get; init; }
        public double PeakAdversePrice { get; init; }
        public DateTime? PeakAdverseTimestampUtc { get; init; }
    }
}
