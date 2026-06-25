using Sailor.App.Backtest.DataFetcher;
using Sailor.App.Backtest.Engine;
using Sailor.App.Backtest.Strategies;
using Harvester.App.Strategy;
using Harvester.Contracts.Risk;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sailor.App.Backtest.Runner;

public sealed record StrategyComparisonRow(
    string Strategy,
    string Variant,
    int Symbols,
    int Trades,
    double WinRate,
    double ProfitFactor,
    double Sharpe,
    double TotalPnl,
    double MaxDrawdown,
    double AvgWin,
    double AvgLoss,
    double ExpectancyR,
    bool MeetsMinTrades,
    int GovernorStops,
    string GovernorStopReason,
    double PromotionScore,
    bool StrictPromotionPass,
    bool PessimisticPromotionPass,
    bool HardEnvironmentEligible,
    bool StrongestEvidenceAnchor,
    IReadOnlyList<StrategyAdaptiveRecoveryTask> AdaptiveRecoveryTasks,
    double EquityCurveSharpe = 0.0,
    double DownsideDeviation = 0.0,
    double EquityCurveDownsideDeviation = 0.0,
    double Sortino = 0.0,
    double EquityCurveSortino = 0.0)
{
    public StrategyComparisonRow(
        string Strategy,
        string Variant,
        int Symbols,
        int Trades,
        double WinRate,
        double ProfitFactor,
        double Sharpe,
        double TotalPnl,
        double MaxDrawdown,
        double AvgWin,
        double AvgLoss,
        double ExpectancyR,
        bool MeetsMinTrades,
        int GovernorStops,
        string GovernorStopReason)
        : this(
            Strategy,
            Variant,
            Symbols,
            Trades,
            WinRate,
            ProfitFactor,
            Sharpe,
            TotalPnl,
            MaxDrawdown,
            AvgWin,
            AvgLoss,
            ExpectancyR,
            MeetsMinTrades,
            GovernorStops,
            GovernorStopReason,
            PromotionScore: 0.0,
            StrictPromotionPass: false,
            PessimisticPromotionPass: false,
            HardEnvironmentEligible: true,
            StrongestEvidenceAnchor: false,
            AdaptiveRecoveryTasks: Array.Empty<StrategyAdaptiveRecoveryTask>(),
            EquityCurveSharpe: 0.0)
    {
    }
}

public sealed record StrategyAdaptiveRecoveryTask(
    string Category,
    string Action,
    string Reason,
    string Scope,
    string Evidence,
    string Severity);

public sealed record ReplayBucketSummary(
    string Key,
    string Label,
    int Trades,
    int Winners,
    int Losers,
    double WinRate,
    double TotalPnl,
    double AvgPnl,
    double AvgWin,
    double AvgLoss,
    double? ProfitFactor,
    double ExpectancyR,
    double AvgMfeUsd,
    double AvgMaeUsd,
    double AvgGivebackUsd,
    double AvgBarsHeld,
    double MaxWin,
    double MaxLoss);

public sealed record ReplayDrawdownContributor(
    string Symbol,
    string Side,
    string ExitReason,
    string Setup,
    int EntryHourUtc,
    string EntryTimeUtc,
    string ExitTimeUtc,
    double PnlUsd,
    double DrawdownContributionUsd,
    double MfeUsd,
    double MaeUsd,
    double GivebackUsd);

public sealed record ReplayHardStopTradeDiagnostic(
    string Symbol,
    string Side,
    string Setup,
    int EntryHourUtc,
    string EntryTimeUtc,
    string ExitTimeUtc,
    double PnlUsd,
    double PnlR,
    double MfeUsd,
    double MaeUsd,
    double GivebackUsd,
    double StopDistanceUsd,
    double StopUtilizationPct,
    double TimeToExitMinutes,
    int BarsHeld);

public sealed record ReplayHardStopDiagnostics(
    int TradeCount,
    double TotalPnl,
    double AvgPnl,
    double AvgPnlR,
    double AvgMfeUsd,
    double AvgMaeUsd,
    double AvgGivebackUsd,
    double AvgStopDistanceUsd,
    double AvgStopUtilizationPct,
    double AvgTimeToExitMinutes,
    IReadOnlyList<ReplayBucketSummary> BySymbol,
    IReadOnlyList<ReplayBucketSummary> ByEntryHourUtc,
    IReadOnlyList<ReplayBucketSummary> BySetup,
    IReadOnlyList<ReplayHardStopTradeDiagnostic> Trades);

public sealed record ReplayMfeMaeBucketSummary(
    string Key,
    string Label,
    int Trades,
    double TotalPnl,
    double AvgPnl,
    double AvgPnlR,
    double AvgMfeUsd,
    double AvgMaeUsd,
    double AvgGivebackUsd,
    double ProfitCapturePct,
    double AdverseExcursionPctOfRisk,
    double AvgBarsHeld);

public sealed record ReplayMfeMaeTradeDiagnostic(
    string Symbol,
    string Side,
    string ExitReason,
    string Setup,
    int EntryHourUtc,
    string EntryTimeUtc,
    string ExitTimeUtc,
    double PnlUsd,
    double PnlR,
    double MfeUsd,
    double MaeUsd,
    double GivebackUsd,
    double ProfitCapturePct,
    double AdverseExcursionPctOfRisk);

public sealed record ReplayMfeMaeDiagnostics(
    IReadOnlyList<ReplayMfeMaeBucketSummary> BySetup,
    IReadOnlyList<ReplayMfeMaeBucketSummary> ByExitReason,
    IReadOnlyList<ReplayMfeMaeTradeDiagnostic> TopGivebackTrades,
    IReadOnlyList<ReplayMfeMaeTradeDiagnostic> TopMaeTrades);

public sealed record ReplayStrategyDecomposition(
    string Strategy,
    string Variant,
    int TradeCount,
    IReadOnlyList<ReplayBucketSummary> BySymbol,
    IReadOnlyList<ReplayBucketSummary> BySide,
    IReadOnlyList<ReplayBucketSummary> ByExitReason,
    IReadOnlyList<ReplayBucketSummary> ByEntryHourUtc,
    IReadOnlyList<ReplayBucketSummary> BySetup,
    IReadOnlyList<ReplayDrawdownContributor> DrawdownContributors,
    ReplayHardStopDiagnostics HardStopDiagnostics,
    ReplayMfeMaeDiagnostics MfeMaeDiagnostics);

public static class StrategyComparisonRunner
{
    private const double SparseFallbackMinTradeCoverageRatio = 0.70;
    private const double SparseLossFallbackMinTradeCoverageRatio = 0.60;
    private const double SparseFallbackMinPnlRetentionRatio = 0.85;
    private const double SparseLossFallbackMaxAdditionalLossDollars = 30.0;
    internal const double AcceptanceSharpeBaseline = -2.33;
    private const double StrictPromotionMinProfitFactor = 1.15;
    private const double PessimisticPromotionMinProfitFactor = 1.75;
    private const double StrictPromotionMaxDrawdownUsd = 40.0;
    private const double PessimisticPromotionMaxDrawdownUsd = 30.0;
    private const double PessimisticPromotionMinExpectancyR = 0.03;
    private const double PessimisticPromotionMinSharpe = AcceptanceSharpeBaseline;
    private const double PessimisticPromotionMinEquityCurveSharpe = AcceptanceSharpeBaseline;

    // Plans excluded from the DEFAULT/active comparison set. They remain fully runnable when explicitly
    // requested (e.g. "--strategy V11" or "folder-all") for historical/regression comparison.
    // Phase 6.15: the frozen V-line strategies (V10, V11, V12) are superseded by Conduct-V3 and the
    // later V-line strategies and are therefore archived out of the default comparison plans. Their trade
    // conduct is unchanged when selected explicitly (constraints 1 & 2 preserved).
    private static readonly HashSet<string> ArchivedStrategyPlans =
    [
        "V1-First",
        "V2-Conduct",
        "V10",
        "V11",
        "V12",
    ];

    // Self-learning injection is now handled by BacktestStrategyBase.InjectSelfLearning()

    internal static IReadOnlyList<ReplayStrategyDecomposition> BuildReplayDecomposition(
        IReadOnlyList<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> trades,
        IReadOnlyDictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData)
    {
        var envelopes = trades
            .Select(trade => BuildReplayTradeEnvelope(trade, allData))
            .ToArray();

        return envelopes
            .GroupBy(trade => new { trade.Strategy, trade.Variant })
            .Select(group =>
            {
                var groupedTrades = group
                    .OrderBy(trade => trade.Trade.ExitTime)
                    .ThenBy(trade => trade.Trade.EffectiveEntryTime)
                    .ThenBy(trade => trade.Symbol, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new ReplayStrategyDecomposition(
                    Strategy: group.Key.Strategy,
                    Variant: group.Key.Variant,
                    TradeCount: groupedTrades.Length,
                    BySymbol: BuildBucketSummaries(groupedTrades, trade => trade.Symbol, symbol => symbol, top: 25),
                    BySide: BuildBucketSummaries(groupedTrades, trade => trade.Trade.Side.ToString(), side => side),
                    ByExitReason: BuildBucketSummaries(groupedTrades, trade => trade.Trade.ExitReason.ToString(), reason => reason),
                    ByEntryHourUtc: BuildBucketSummaries(groupedTrades, trade => trade.EntryHourUtc.ToString("00"), hour => $"{hour}:00 UTC"),
                    BySetup: BuildBucketSummaries(groupedTrades, trade => trade.Setup, setup => setup, top: 25),
                    DrawdownContributors: BuildDrawdownContributors(groupedTrades),
                    HardStopDiagnostics: BuildHardStopDiagnostics(groupedTrades),
                    MfeMaeDiagnostics: BuildMfeMaeDiagnostics(groupedTrades));
            })
            .OrderByDescending(group => group.TradeCount)
            .ThenBy(group => group.Strategy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Variant, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static List<StrategyComparisonRow> MarkStrongestEvidenceAnchor(IReadOnlyList<StrategyComparisonRow> rows)
    {
        var ranked = rows
            .Select(row => row with { StrongestEvidenceAnchor = false })
            .ToList();

        if (ranked.Count > 0)
        {
            ranked[0] = ranked[0] with { StrongestEvidenceAnchor = true };
        }

        return ranked;
    }

    public static List<StrategyComparisonRow> RunAll(
        string[]? symbols = null,
        int minTrades = 50,
        string? episodeExportDir = null,
        int maxTradesPerSymbol = 0,
        Action<string>? log = null,
        bool mainVariantsOnly = false,
        bool includeSyntheticPairedOppositePreview = false,
        bool shuffleRepeatSymbolEntryTimes = false)
    {
        log ??= Console.WriteLine;

        var symbolUniverse = symbols?.Length > 0
            ? symbols
            : CsvBarStorage.ListSymbols();

        symbolUniverse = symbolUniverse
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .OrderBy(s => s)
            .ToArray();

        if (symbolUniverse.Length < 5)
        {
            throw new InvalidOperationException(
                $"Need at least 5 symbols for comparison; found {symbolUniverse.Length}.");
        }

        log($"Using {symbolUniverse.Length} scanner symbols: {string.Join(", ", symbolUniverse)}");
        var allData = ParameterSweep.LoadAllData(symbolUniverse, _ => { });

        var rows = new List<StrategyComparisonRow>();
        var allEpisodeTrades = new List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)>();

        var plans = BuildPlans()
            .Where(p => !ArchivedStrategyPlans.Contains(p.Name))
            .Select(p => mainVariantsOnly ? SelectMainVariantPlan(p) : p)
            .ToList();
        foreach (var plan in plans)
        {
            var (best, trades) = EvaluatePlanWithTrades(plan, allData, minTrades, maxTradesPerSymbol, shuffleRepeatSymbolEntryTimes);
            rows.Add(best);
            allEpisodeTrades.AddRange(trades);
            log($"{best.Strategy,-10} [{best.Variant}] -> {best.Trades} trades | WR {best.WinRate:P1} | PnL ${best.TotalPnl:F2} | PF {best.ProfitFactor:F2} | Sharpe {best.Sharpe:F2} | EqSharpe {best.EquityCurveSharpe:F2} | EqSortino {best.EquityCurveSortino:F2}");
        }

        var rankedRows = MarkStrongestEvidenceAnchor(RankRows(rows));
        PrintTable(rankedRows, minTrades, log);

        if (!string.IsNullOrEmpty(episodeExportDir) && allEpisodeTrades.Count > 0)
        {
            var count = ExportEpisodesFromTrades(allEpisodeTrades, episodeExportDir, allData);
            log($"\nExported {count} trade episodes to {episodeExportDir}");

            var replayReportPath = BacktestTradeReplayReportWriter.WriteComparisonReport(
                rankedRows,
                allEpisodeTrades,
                allData,
                Path.Combine("exports", "reports"),
                BacktestStrategyBase.LoadSharedSelfLearning(),
                includeSyntheticPairedOppositePreview: includeSyntheticPairedOppositePreview);
            log($"Trade replay report: {replayReportPath}");
        }

        return rankedRows;
    }

    /// <summary>
    /// Run comparison for a filtered set of strategy plans and optionally export trades as episode JSONs.
    /// </summary>
    public static List<StrategyComparisonRow> RunFiltered(
        string[] strategyFilter,
        string[]? symbols = null,
        int minTrades = 50,
        string? episodeExportDir = null,
        int maxTradesPerSymbol = 0,
        Action<string>? log = null,
        bool allVariants = false,
        bool mainVariantsOnly = false,
        bool includeSyntheticPairedOppositePreview = false,
        bool shuffleRepeatSymbolEntryTimes = false)
    {
        log ??= Console.WriteLine;

        var symbolUniverse = symbols?.Length > 0
            ? symbols
            : CsvBarStorage.ListSymbols();

        symbolUniverse = symbolUniverse
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .OrderBy(s => s)
            .ToArray();

        if (symbolUniverse.Length < 5)
            throw new InvalidOperationException($"Need at least 5 symbols; found {symbolUniverse.Length}.");

        log($"Using {symbolUniverse.Length} symbols: {string.Join(", ", symbolUniverse)}");
        log($"Strategy filter: {string.Join(", ", strategyFilter)}");
        var allData = ParameterSweep.LoadAllData(symbolUniverse, _ => { });
        var rows = new List<StrategyComparisonRow>();
        var allEpisodeTrades = new List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)>();

        var plans = ResolveFilteredPlans(strategyFilter, mainVariantsOnly);

        if (plans.Count == 0)
        {
            log("WARNING: No matching strategy plans found. Available:");
            foreach (var p in BuildPlans().Where(p => !ArchivedStrategyPlans.Contains(p.Name)))
                log($"  - {p.Name}: {string.Join(", ", p.Variants.Select(v => v.Name))}");
            return rows;
        }

        foreach (var plan in plans)
        {
            if (allVariants)
            {
                var allResults = EvaluateAllVariants(plan, allData, minTrades, maxTradesPerSymbol, shuffleRepeatSymbolEntryTimes);
                foreach (var (row, trades) in allResults)
                {
                    rows.Add(row);
                    allEpisodeTrades.AddRange(trades);
                    log($"{row.Strategy,-18} [{row.Variant}] -> {row.Trades} trades | WR {row.WinRate:P1} | PnL ${row.TotalPnl:F2} | PF {row.ProfitFactor:F2} | Sharpe {row.Sharpe:F2} | EqSharpe {row.EquityCurveSharpe:F2} | EqSortino {row.EquityCurveSortino:F2}");
                }
            }
            else
            {
                var (best, trades) = EvaluatePlanWithTrades(plan, allData, minTrades, maxTradesPerSymbol, shuffleRepeatSymbolEntryTimes);
                rows.Add(best);
                allEpisodeTrades.AddRange(trades);
                log($"{best.Strategy,-18} [{best.Variant}] -> {best.Trades} trades | WR {best.WinRate:P1} | PnL ${best.TotalPnl:F2} | PF {best.ProfitFactor:F2} | Sharpe {best.Sharpe:F2} | EqSharpe {best.EquityCurveSharpe:F2} | EqSortino {best.EquityCurveSortino:F2}");
            }
        }

        var rankedRows = MarkStrongestEvidenceAnchor(RankRows(rows));
        PrintTable(rankedRows, minTrades, log);

        if (!string.IsNullOrEmpty(episodeExportDir) && allEpisodeTrades.Count > 0)
        {
            var count = ExportEpisodesFromTrades(allEpisodeTrades, episodeExportDir, allData);
            log($"\nExported {count} trade episodes to {episodeExportDir}");

            var replayReportPath = BacktestTradeReplayReportWriter.WriteComparisonReport(
                rankedRows,
                allEpisodeTrades,
                allData,
                Path.Combine("exports", "reports"),
                BacktestStrategyBase.LoadSharedSelfLearning(),
                includeSyntheticPairedOppositePreview: includeSyntheticPairedOppositePreview);
            log($"Trade replay report: {replayReportPath}");
        }

        return rankedRows;
    }

    private static List<(StrategyComparisonRow Row, List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> Trades)> EvaluateAllVariants(
        StrategyPlan plan,
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        int minTrades,
        int maxTradesPerSymbol,
        bool shuffleRepeatSymbolEntryTimes)
    {
        var candidates = new List<(StrategyComparisonRow Row, List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> Trades)>();

        foreach (var variant in plan.Variants)
        {
            var strategy = variant.Factory();
            BacktestStrategyBase.InjectSelfLearning(strategy);
            if (strategy is IBacktestDiagnosticsProvider diagnosticsProvider)
                diagnosticsProvider.ResetDiagnostics();

            var backtests = new List<BacktestResult>();
            var variantTrades = new List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)>();

            foreach (var (sym, data) in allData)
            {
                var bt = BacktestEngine.RunBacktest(
                    symbol: sym, strategy: strategy,
                    bars1m: data.Trigger, triggerTf: "1m",
                    bars5m: data.Ctx5m, bars15m: data.Ctx15m,
                    bars1h: data.Ctx1h, bars1d: data.Ctx1d,
                    initialCapital: plan.InitialCapital);

                backtests.Add(bt);
                foreach (var t in bt.Trades)
                    variantTrades.Add((plan.Name, variant.Name, sym, t));
            }

            var preparedVariantTrades = shuffleRepeatSymbolEntryTimes
                ? BacktestRepeatedEntryTimeShuffler.Apply(variantTrades, allData)
                : variantTrades;

            var (filteredVariantTrades, comparisonGovernorReport) = PrepareComparisonTradesCore(
                preparedVariantTrades,
                trade => trade.Trade,
                plan.InitialCapital,
                maxTradesPerSymbol,
                trade => trade.Symbol);
            var comparisonTrades = filteredVariantTrades.Select(trade => trade.Trade).ToList();
            var governorReport = MergeGovernorReports(AggregateGovernorReport(backtests), comparisonGovernorReport);
            var stats = BacktestEngine.ComputeStatistics(comparisonTrades, plan.InitialCapital, governorReport);
            Console.WriteLine($"  >> {plan.Name} [{variant.Name}] {stats.TotalTrades} trades | WR {stats.WinRate:P1} | PnL ${stats.TotalPnl:F2} | PF {stats.ProfitFactor:F2} | Sharpe {stats.Sharpe:F2} | EqSharpe {stats.EquityCurveSharpe:F2} | EqSortino {stats.EquityCurveSortino:F2}");
            if (strategy is IBacktestDiagnosticsProvider diagnosticsProviderSummary)
            {
                foreach (var line in diagnosticsProviderSummary.GetDiagnosticsSummaryLines())
                {
                    Console.WriteLine($"     {line}");
                }
            }

            var baseRow = new StrategyComparisonRow(
                Strategy: plan.Name, Variant: variant.Name,
                Symbols: allData.Count, Trades: stats.TotalTrades,
                WinRate: stats.WinRate, ProfitFactor: stats.ProfitFactor,
                Sharpe: stats.Sharpe, TotalPnl: stats.TotalPnl,
                MaxDrawdown: stats.MaxDrawdown, AvgWin: stats.AvgWin,
                AvgLoss: stats.AvgLoss, ExpectancyR: stats.ExpectancyR,
                MeetsMinTrades: stats.TotalTrades >= minTrades,
                GovernorStops: CountGovernorStops(governorReport),
                GovernorStopReason: SummarizeGovernorReasons(governorReport),
                PromotionScore: 0.0,
                StrictPromotionPass: false,
                PessimisticPromotionPass: false,
                HardEnvironmentEligible: true,
                StrongestEvidenceAnchor: false,
                AdaptiveRecoveryTasks: Array.Empty<StrategyAdaptiveRecoveryTask>(),
                EquityCurveSharpe: stats.EquityCurveSharpe,
                DownsideDeviation: stats.DownsideDeviation,
                EquityCurveDownsideDeviation: stats.EquityCurveDownsideDeviation,
                Sortino: stats.Sortino,
                EquityCurveSortino: stats.EquityCurveSortino);

            var decomposition = BuildReplayDecomposition(filteredVariantTrades, allData).SingleOrDefault();
            var row = AttachPromotionAssessment(baseRow, decomposition, minTrades);

            candidates.Add((row, filteredVariantTrades));
        }

        return candidates;
    }

    private static (StrategyComparisonRow Best, List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> Trades) EvaluatePlanWithTrades(
        StrategyPlan plan,
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        int minTrades,
        int maxTradesPerSymbol,
        bool shuffleRepeatSymbolEntryTimes)
    {
        return SelectBestResult(EvaluateAllVariants(plan, allData, minTrades, maxTradesPerSymbol, shuffleRepeatSymbolEntryTimes), minTrades);
    }

    internal static (StrategyComparisonRow Best, T Payload) SelectBestResult<T>(
        IReadOnlyList<(StrategyComparisonRow Row, T Payload)> candidates,
        int minTrades)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("At least one strategy comparison candidate is required.");
        }

        StrategyComparisonRow? bestEligible = null;
        T? bestEligiblePayload = default;
        StrategyComparisonRow? bestAny = null;
        T? bestAnyPayload = default;

        foreach (var candidate in candidates)
        {
            if (bestAny == null || IsBetter(candidate.Row, bestAny, preferTradeFloor: false))
            {
                bestAny = candidate.Row;
                bestAnyPayload = candidate.Payload;
            }

            if (candidate.Row.MeetsMinTrades && (bestEligible == null || IsBetter(candidate.Row, bestEligible, preferTradeFloor: false)))
            {
                bestEligible = candidate.Row;
                bestEligiblePayload = candidate.Payload;
            }
        }

        if (bestEligible is not null)
        {
            return (bestEligible, bestEligiblePayload!);
        }

        var guardedFallback = SelectSparseFallback(candidates, bestAny!, minTrades);
        return guardedFallback ?? (bestAny!, bestAnyPayload!);
    }

    // â”€â”€ Episode Export â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly JsonSerializerOptions EpisodeJsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static int ExportEpisodesFromTrades(
        List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> trades,
        string outputDir,
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData)
    {
        Directory.CreateDirectory(outputDir);
        var count = 0;

        foreach (var (strategy, variant, symbol, trade) in trades)
        {
            var tradeId = $"BT_{strategy}_{variant}_{symbol}_{trade.EffectiveEntryTime:yyyyMMdd_HHmmss}_{count:D4}";
            var side = trade.Side == TradeSide.Long ? "LONG" : "SHORT";
            var exitReasonStr = trade.ExitReason.ToString();
            var pnl = trade.Pnl;
            var win = pnl > 0 ? "WIN" : "LOSS";
            var setupIdentity = ResolveBacktestSetupIdentity(strategy, variant, trade.SubStrategy);
            var resolvedSelectedSubStrategy = setupIdentity.SelectedSubStrategy ?? setupIdentity.CompositeSetup;

            var replaySeries = Array.Empty<object>();
            var replayMetrics = new TradeReplayMetrics();
            if (allData.TryGetValue(symbol, out var replayData))
            {
                replaySeries = BuildReplaySeries(trade, replayData.Trigger);
                replayMetrics = ComputeReplayMetrics(trade, replayData.Trigger);
            }

            // Look up entry bar indicator snapshot
            object[] featuresPre = Array.Empty<object>();
            if (allData.TryGetValue(symbol, out var data)
                && trade.EntryBar >= 0
                && trade.EntryBar < data.Trigger.Length)
            {
                var bar = data.Trigger[trade.EntryBar];
                featuresPre = new object[]
                {
                    BuildIndicatorSnapshot(bar)
                };
            }

            var episode = new Dictionary<string, object>
            {
                ["TradeId"] = tradeId,
                ["Symbol"] = symbol,
                ["Side"] = side,
                ["Quantity"] = trade.PositionSize,
                ["Setup"] = setupIdentity.CompositeSetup,
                ["CompositeSetup"] = setupIdentity.CompositeSetup,
                ["SelectedSubStrategy"] = resolvedSelectedSubStrategy,
                ["NormalizedPatternKey"] = setupIdentity.NormalizedPattern.PatternKey,
                ["NormalizedPatternFamily"] = setupIdentity.NormalizedPattern.PatternFamily,
                ["Entry"] = new
                {
                    TimestampUtc = trade.EffectiveEntryTime.ToString("o"),
                    OriginalTimestampUtc = trade.OriginalEntryTimeResolved.ToString("o"),
                    ShuffledTimestampUtc = trade.ShuffledEntryTime?.ToString("o"),
                    BarIndex = trade.EffectiveEntryBar,
                    OriginalBarIndex = trade.OriginalEntryBarResolved,
                    ShuffledBarIndex = trade.ShuffledEntryBar,
                    trade.ShuffleReason,
                    Price = trade.EntryPrice,
                },
                ["Exit"] = new { TimestampUtc = trade.ExitTime.ToString("o"), Price = trade.ExitPrice },
                ["Labels"] = new
                {
                    PnlUsd = Math.Round(pnl, 4),
                    RMultiple = Math.Round(trade.PnlR, 4),
                    MfeUsd = Math.Round(replayMetrics.MfeUsd, 4),
                    MaeUsd = Math.Round(replayMetrics.MaeUsd, 4),
                    ExitReason = exitReasonStr,
                    WinLoss = win,
                    PeakR = Math.Round(trade.PeakR, 4),
                    BarsHeld = trade.BarsHeld,
                },
                ["DecisionTrace"] = new
                {
                    EntryReason = setupIdentity.CompositeSetup,
                    CompositeSetup = setupIdentity.CompositeSetup,
                    SelectedSubStrategy = resolvedSelectedSubStrategy,
                    NormalizedPatternKey = setupIdentity.NormalizedPattern.PatternKey,
                    NormalizedPatternFamily = setupIdentity.NormalizedPattern.PatternFamily,
                    Strategy = strategy,
                    Variant = variant,
                },
                ["ReplaySummary"] = new
                {
                    PeakFavorablePrice = Math.Round(replayMetrics.PeakFavorablePrice, 4),
                    PeakFavorableTimestampUtc = replayMetrics.PeakFavorableTimestampUtc?.ToString("o"),
                    PeakAdversePrice = Math.Round(replayMetrics.PeakAdversePrice, 4),
                    PeakAdverseTimestampUtc = replayMetrics.PeakAdverseTimestampUtc?.ToString("o"),
                    SeriesBarCount = replaySeries.Length,
                },
                ["Fills"] = new[]
                {
                    new { TimestampUtc = trade.EffectiveEntryTime.ToString("o"), OriginalTimestampUtc = (string?)trade.OriginalEntryTimeResolved.ToString("o"), Price = trade.EntryPrice, Quantity = trade.PositionSize, Side = side == "LONG" ? "BUY" : "SELL", Source = $"entry:{setupIdentity.SelectedSubStrategy ?? setupIdentity.CompositeSetup}" },
                    new { TimestampUtc = trade.ExitTime.ToString("o"), OriginalTimestampUtc = (string?)null, Price = trade.ExitPrice, Quantity = trade.PositionSize, Side = side == "LONG" ? "SELL" : "BUY", Source = $"exit:{exitReasonStr}" },
                },
                ["FeaturesPre"] = featuresPre,
                ["Series"] = replaySeries,
                ["Actions"] = (trade.ReplayActions ?? Array.Empty<BacktestTradeAction>()).Select(action => new
                {
                    action.BarIndex,
                    TimestampUtc = action.Timestamp.ToString("o"),
                    action.Price,
                    action.ActionType,
                    action.Description,
                    action.ReferencePrice,
                    action.RMultiple,
                }).ToArray(),
            };

            var day = trade.EffectiveEntryTime.ToString("yyyy-MM-dd");
            var dir = Path.Combine(outputDir, day, symbol);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{tradeId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(episode, EpisodeJsonOpts));
            count++;
        }

        return count;
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

    private static object[] BuildReplaySeries(BacktestTradeResult trade, EnrichedBar[] triggerBars)
    {
        if (trade.EffectiveEntryBar < 0 || trade.EffectiveEntryBar >= triggerBars.Length)
            return Array.Empty<object>();

        var lastBar = Math.Clamp(trade.ExitBar, trade.EffectiveEntryBar, triggerBars.Length - 1);
        var rows = new List<object>(lastBar - trade.EffectiveEntryBar + 1);
        for (var i = trade.EffectiveEntryBar; i <= lastBar; i++)
        {
            var row = triggerBars[i];
            rows.Add(new
            {
                BarIndex = i,
                TimestampUtc = row.Bar.Timestamp.ToString("o"),
                Open = row.Bar.Open,
                High = row.Bar.High,
                Low = row.Bar.Low,
                Close = row.Bar.Close,
                Volume = row.Bar.Volume,
                MarkPrice = row.Bar.Close,
                Ema9 = SafeNumber(row.Ema9),
                Ema21 = SafeNumber(row.Ema21),
                Vwap = SafeNumber(row.Vwap),
                Atr14 = SafeNumber(row.Atr14),
                Rvol = SafeNumber(row.Rvol),
                Phase = i == trade.EffectiveEntryBar ? "entry" : i == lastBar ? "exit" : "monitor",
            });
        }

        return rows.ToArray();
    }

    private static TradeReplayMetrics ComputeReplayMetrics(BacktestTradeResult trade, EnrichedBar[] triggerBars)
    {
        if (trade.EffectiveEntryBar < 0 || trade.EffectiveEntryBar >= triggerBars.Length)
            return new TradeReplayMetrics();

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

        return new TradeReplayMetrics
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
        => double.IsNaN(value) || double.IsInfinity(value) ? null : Math.Round(value, 6);

    private sealed class TradeReplayMetrics
    {
        public double MfeUsd { get; init; }
        public double MaeUsd { get; init; }
        public double PeakFavorablePrice { get; init; }
        public DateTime? PeakFavorableTimestampUtc { get; init; }
        public double PeakAdversePrice { get; init; }
        public DateTime? PeakAdverseTimestampUtc { get; init; }
    }

    /// <summary>Build a flat indicator snapshot dict from an EnrichedBar for episode export.</summary>
    private static Dictionary<string, object?> BuildIndicatorSnapshot(EnrichedBar bar)
    {
        static object? N(double v) => double.IsNaN(v) ? null : Math.Round(v, 6);

        var snap = new Dictionary<string, object?>
        {
            // OHLCV
            ["TimestampUtc"] = bar.Bar.Timestamp.ToString("o"),
            ["MarkPrice"] = bar.Bar.Close,
            ["Open"] = bar.Bar.Open,
            ["High"] = bar.Bar.High,
            ["Low"] = bar.Bar.Low,
            ["Close"] = bar.Bar.Close,
            ["Volume"] = bar.Bar.Volume,
            // Moving Averages
            ["Ema9"] = N(bar.Ema9),
            ["Ema21"] = N(bar.Ema21),
            ["Ema50"] = N(bar.Ema50),
            ["Sma20"] = N(bar.Sma20),
            ["Sma200"] = N(bar.Sma200),
            // ATR
            ["Atr14"] = N(bar.Atr14),
            // RSI
            ["Rsi14"] = N(bar.Rsi14),
            // MACD
            ["Macd"] = N(bar.Macd),
            ["MacdSignal"] = N(bar.MacdSignal),
            ["MacdHist"] = N(bar.MacdHist),
            // Bollinger Bands
            ["BbMid"] = N(bar.BbMid),
            ["BbUpper"] = N(bar.BbUpper),
            ["BbLower"] = N(bar.BbLower),
            ["BbPctB"] = N(bar.BbPctB),
            ["BbBandwidth"] = N(bar.BbBandwidth),
            // ADX
            ["Adx"] = N(bar.Adx),
            ["PlusDi"] = N(bar.PlusDi),
            ["MinusDi"] = N(bar.MinusDi),
            // Supertrend
            ["Supertrend"] = N(bar.Supertrend),
            ["StDirection"] = bar.StDirection,
            // RVOL
            ["Rvol"] = N(bar.Rvol),
            // VWAP
            ["Vwap"] = N(bar.Vwap),
            // Stochastic
            ["StochK"] = N(bar.StochK),
            ["StochD"] = N(bar.StochD),
            // Keltner Channels
            ["KcMid"] = N(bar.KcMid),
            ["KcUpper"] = N(bar.KcUpper),
            ["KcLower"] = N(bar.KcLower),
            // MFI
            ["Mfi14"] = N(bar.Mfi14),
            // OFI
            ["OfiSignal"] = N(bar.OfiSignal),
            // Spread
            ["SpreadRatio"] = N(bar.SpreadRatio),
            ["SpreadPct"] = N(bar.SpreadPct),
            // Williams %R
            ["WillR14"] = N(bar.WillR14),
            // Donchian Channels
            ["DcUpper"] = N(bar.DcUpper),
            ["DcLower"] = N(bar.DcLower),
            ["DcMid"] = N(bar.DcMid),
            ["DcPct"] = N(bar.DcPct),
            // DPO
            ["Dpo20"] = N(bar.Dpo20),
            // L2 Book
            ["ImbalanceRatio"] = N(bar.ImbalanceRatio),
            ["L2ImbalanceTopN"] = N(bar.ImbalanceRatio),
            // Volume Acceleration
            ["VolAccel"] = N(bar.VolAccel),
            // Candle Patterns
            ["IsBullishCandle"] = bar.IsBullishCandle,
            ["IsBearishCandle"] = bar.IsBearishCandle,
            ["IsHammer"] = bar.IsHammer,
            ["IsStar"] = bar.IsStar,
            // Previous bar
            ["PrevClose"] = N(bar.PrevClose),
            ["PrevHigh"] = N(bar.PrevHigh),
            ["PrevLow"] = N(bar.PrevLow),
        };
        return snap;
    }

    private static StrategyComparisonRow EvaluatePlan(
        StrategyPlan plan,
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
        int minTrades)
    {
        var candidates = new List<StrategyComparisonRow>();

        foreach (var variant in plan.Variants)
        {
            var strategy = variant.Factory();
            BacktestStrategyBase.InjectSelfLearning(strategy);
            var allTrades = new List<BacktestTradeResult>();
            var backtests = new List<BacktestResult>();

            foreach (var (sym, data) in allData)
            {
                var bt = BacktestEngine.RunBacktest(
                    symbol: sym,
                    strategy: strategy,
                    bars1m: data.Trigger,
                    triggerTf: "1m",
                    bars5m: data.Ctx5m,
                    bars15m: data.Ctx15m,
                    bars1h: data.Ctx1h,
                    bars1d: data.Ctx1d,
                    initialCapital: plan.InitialCapital);

                backtests.Add(bt);
                allTrades.AddRange(bt.Trades);
            }

            var (comparisonTrades, comparisonGovernorReport) = PrepareComparisonTrades(allTrades, plan.InitialCapital);
            var governorReport = MergeGovernorReports(AggregateGovernorReport(backtests), comparisonGovernorReport);
            var stats = BacktestEngine.ComputeStatistics(comparisonTrades, plan.InitialCapital, governorReport);
            var governorSuffix = governorReport.SessionStopped || governorReport.HaltedBucketCount > 0
                ? $" | Gov {governorReport.HaltedBucketCount}/{backtests.Count} stop={governorReport.StopReason}"
                : string.Empty;
            Console.WriteLine($"  >> {plan.Name} [{variant.Name}] {stats.TotalTrades} trades | WR {stats.WinRate:P1} | PnL ${stats.TotalPnl:F2} | PF {stats.ProfitFactor:F2} | Sharpe {stats.Sharpe:F2} | EqSharpe {stats.EquityCurveSharpe:F2} | EqSortino {stats.EquityCurveSortino:F2}{governorSuffix}");

            // Per-sub-strategy breakdown
            var subGroups = comparisonTrades
                .Where(t => !string.IsNullOrEmpty(t.SubStrategy))
                .GroupBy(t => t.SubStrategy)
                .OrderByDescending(g => g.Count());
            foreach (var sg in subGroups)
            {
                int wins = sg.Count(t => t.Pnl > 0);
                double wr = sg.Count() > 0 ? (double)wins / sg.Count() : 0;
                double pnl = sg.Sum(t => t.Pnl);
                Console.WriteLine($"     [{sg.Key}] {sg.Count()} trades, WR {wr:P1}, PnL ${pnl:F2}");
            }
            var row = AttachPromotionAssessment(new StrategyComparisonRow(
                Strategy: plan.Name,
                Variant: variant.Name,
                Symbols: allData.Count,
                Trades: stats.TotalTrades,
                WinRate: stats.WinRate,
                ProfitFactor: stats.ProfitFactor,
                Sharpe: stats.Sharpe,
                TotalPnl: stats.TotalPnl,
                MaxDrawdown: stats.MaxDrawdown,
                AvgWin: stats.AvgWin,
                AvgLoss: stats.AvgLoss,
                ExpectancyR: stats.ExpectancyR,
                MeetsMinTrades: stats.TotalTrades >= minTrades,
                GovernorStops: CountGovernorStops(governorReport),
                GovernorStopReason: SummarizeGovernorReasons(governorReport),
                PromotionScore: 0.0,
                StrictPromotionPass: false,
                PessimisticPromotionPass: false,
                HardEnvironmentEligible: true,
                StrongestEvidenceAnchor: false,
                AdaptiveRecoveryTasks: Array.Empty<StrategyAdaptiveRecoveryTask>(),
                EquityCurveSharpe: stats.EquityCurveSharpe,
                DownsideDeviation: stats.DownsideDeviation,
                EquityCurveDownsideDeviation: stats.EquityCurveDownsideDeviation,
                Sortino: stats.Sortino,
                EquityCurveSortino: stats.EquityCurveSortino),
                decomposition: null,
                minTrades: minTrades);

            candidates.Add(row);
        }

        return SelectBestResult(candidates.Select(row => (row, row)).ToArray(), minTrades).Best;
    }

    private static StrategyComparisonRow AttachPromotionAssessment(
        StrategyComparisonRow row,
        ReplayStrategyDecomposition? decomposition,
        int minTrades)
    {
        var promotionSharpe = Math.Min(row.Sharpe, row.EquityCurveSharpe);
        var strictPromotionPass = row.MeetsMinTrades
            && row.TotalPnl > 0.0
            && NormalizeProfitFactor(row.ProfitFactor) >= StrictPromotionMinProfitFactor
            && row.ExpectancyR > 0.0
            && row.MaxDrawdown <= StrictPromotionMaxDrawdownUsd
            && row.GovernorStops == 0;

        var pessimisticPromotionPass = strictPromotionPass
            && NormalizeProfitFactor(row.ProfitFactor) >= PessimisticPromotionMinProfitFactor
            && row.ExpectancyR >= PessimisticPromotionMinExpectancyR
            && row.MaxDrawdown <= PessimisticPromotionMaxDrawdownUsd
            && promotionSharpe >= PessimisticPromotionMinSharpe
            && row.EquityCurveSharpe >= PessimisticPromotionMinEquityCurveSharpe;

        var adaptiveRecoveryTasks = BuildAdaptiveRecoveryTasks(row, decomposition);
        var promotionScore = ComputePromotionScore(row, minTrades, strictPromotionPass, pessimisticPromotionPass, adaptiveRecoveryTasks.Count);

        return row with
        {
            PromotionScore = Math.Round(promotionScore, 2),
            StrictPromotionPass = strictPromotionPass,
            PessimisticPromotionPass = pessimisticPromotionPass,
            HardEnvironmentEligible = true,
            AdaptiveRecoveryTasks = adaptiveRecoveryTasks,
        };
    }

    private static IReadOnlyList<StrategyAdaptiveRecoveryTask> BuildAdaptiveRecoveryTasks(
        StrategyComparisonRow row,
        ReplayStrategyDecomposition? decomposition)
    {
        var tasks = new List<StrategyAdaptiveRecoveryTask>();

        void AddTask(string category, string action, string reason, string scope, string evidence, string severity)
        {
            if (tasks.Any(existing => string.Equals(existing.Category, category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Scope, scope, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            tasks.Add(new StrategyAdaptiveRecoveryTask(category, action, reason, scope, evidence, severity));
        }

        if (row.GovernorStops > 0)
        {
            AddTask(
                category: "size",
                action: "Reduce per-trade risk and loss-cluster sizing in governor-hit contexts while preserving every symbol/hour bucket.",
                reason: "Governor-stopped contexts need safer continuation, not silent removal.",
                scope: "portfolio-governor",
                evidence: string.IsNullOrWhiteSpace(row.GovernorStopReason) ? $"governorStops={row.GovernorStops}" : row.GovernorStopReason,
                severity: "critical");

            AddTask(
                category: "conduct changes",
                action: "Emit near-breach conduct recovery rules before the governor halts the bucket.",
                reason: "Repeated governor breaches should trigger repair actions and supervised recovery steps.",
                scope: "governor-prewarning",
                evidence: string.IsNullOrWhiteSpace(row.GovernorStopReason) ? $"governorStops={row.GovernorStops}" : row.GovernorStopReason,
                severity: "critical");
        }

        if (row.TotalPnl <= 0.0)
        {
            AddTask(
                category: "entry confirmation",
                action: "Require stronger context confirmation before entry while keeping every symbol and time bucket tradable.",
                reason: "Negative portfolio PnL signals weak entry quality or context selection.",
                scope: "strategy-entry",
                evidence: $"totalPnl={row.TotalPnl:F2}",
                severity: "high");
        }

        if (NormalizeProfitFactor(row.ProfitFactor) < 1.0)
        {
            AddTask(
                category: "exit timing",
                action: "Rebalance loss containment versus profit extension so gross losses shrink without banning hard buckets.",
                reason: "Profit factor below 1 means losses currently overwhelm wins.",
                scope: "portfolio-exits",
                evidence: $"profitFactor={FormatFiniteMetric(row.ProfitFactor)}",
                severity: "high");
        }

        if (decomposition is not null
            && decomposition.HardStopDiagnostics.TradeCount > 0
            && decomposition.HardStopDiagnostics.TotalPnl < 0.0)
        {
            AddTask(
                category: "stop geometry",
                action: "Adapt stop distance floors/ceilings and pre-stop intervention using the hard-stop diagnostics, while preserving full bucket eligibility.",
                reason: "Hard stops are a direct loss bucket that should be repaired before promotion.",
                scope: "hard-stop",
                evidence: $"hardStopTrades={decomposition.HardStopDiagnostics.TradeCount} totalPnl={decomposition.HardStopDiagnostics.TotalPnl:F2}",
                severity: "high");

            if (decomposition.HardStopDiagnostics.AvgStopUtilizationPct >= 0.85
                || decomposition.HardStopDiagnostics.AvgTimeToExitMinutes <= 15.0)
            {
                AddTask(
                    category: "execution style",
                    action: "Use more conservative order urgency and entry mechanics when early adverse-selection risk spikes.",
                    reason: "Fast full-stop utilization points to bad fills or fragile entry timing.",
                    scope: "microstructure",
                    evidence: $"avgStopUtil={decomposition.HardStopDiagnostics.AvgStopUtilizationPct:P1} avgMinutes={decomposition.HardStopDiagnostics.AvgTimeToExitMinutes:F1}",
                    severity: "high");
            }
        }

        var weakestSide = decomposition?.BySide
            .Where(bucket => bucket.Trades > 0 && bucket.TotalPnl < 0.0)
            .OrderBy(bucket => bucket.TotalPnl)
            .FirstOrDefault();
        if (weakestSide is not null)
        {
            AddTask(
                category: "size",
                action: $"Downshift sizing and confirmation aggressiveness for the {weakestSide.Label} side until it recovers, without removing that side from eligibility.",
                reason: "A side-specific loss cluster is degrading risk-adjusted performance.",
                scope: $"side:{weakestSide.Key}",
                evidence: $"trades={weakestSide.Trades} totalPnl={weakestSide.TotalPnl:F2} winRate={weakestSide.WinRate:P1}",
                severity: "moderate");
        }

        var weakestHour = decomposition?.ByEntryHourUtc
            .Where(bucket => bucket.Trades > 0 && bucket.TotalPnl < 0.0)
            .OrderBy(bucket => bucket.TotalPnl)
            .FirstOrDefault();
        if (weakestHour is not null)
        {
            AddTask(
                category: "exit timing",
                action: $"Add hour-aware recovery behavior for {weakestHour.Label} using safer timing, exits, and sizeâ€”never a trading ban.",
                reason: "The hour bucket is currently loss-heavy and needs adaptive handling inside the learning set.",
                scope: $"hour:{weakestHour.Key}",
                evidence: $"trades={weakestHour.Trades} totalPnl={weakestHour.TotalPnl:F2}",
                severity: "moderate");
        }

        var weakestSymbol = decomposition?.BySymbol
            .Where(bucket => bucket.Trades > 0 && bucket.TotalPnl < 0.0)
            .OrderBy(bucket => bucket.TotalPnl)
            .FirstOrDefault();
        if (weakestSymbol is not null)
        {
            AddTask(
                category: "entry confirmation",
                action: $"Use symbol-context adaptive confirmation and sizing for {weakestSymbol.Label} instead of excluding it from future opportunities.",
                reason: "Weak symbol buckets should remain eligible but require better context handling.",
                scope: $"symbol:{weakestSymbol.Key}",
                evidence: $"trades={weakestSymbol.Trades} totalPnl={weakestSymbol.TotalPnl:F2}",
                severity: "moderate");
        }

        var topDrawdownContributor = decomposition?.DrawdownContributors.FirstOrDefault();
        if (topDrawdownContributor is not null
            && topDrawdownContributor.DrawdownContributionUsd >= Math.Max(8.0, row.MaxDrawdown * 0.30))
        {
            AddTask(
                category: "conduct changes",
                action: "Add drawdown-aware conduct throttles that soften clustered losses before they turn into portfolio damage.",
                reason: "Drawdown is being driven by a small number of outsized adverse sequences.",
                scope: "drawdown-cluster",
                evidence: $"symbol={topDrawdownContributor.Symbol} contribution={topDrawdownContributor.DrawdownContributionUsd:F2} exit={topDrawdownContributor.ExitReason}",
                severity: "moderate");
        }

        return tasks.Take(6).ToArray();
    }

    private static double ComputePromotionScore(
        StrategyComparisonRow row,
        int minTrades,
        bool strictPromotionPass,
        bool pessimisticPromotionPass,
        int recoveryTaskCount)
    {
        var normalizedProfitFactor = NormalizeProfitFactor(row.ProfitFactor);
        var promotionSharpe = Math.Min(row.Sharpe, row.EquityCurveSharpe);
        var tradeCoverageScore = Math.Clamp(row.Trades / Math.Max(1.0, minTrades), 0.0, 1.50) * 20.0;
        var profitFactorScore = Math.Clamp((normalizedProfitFactor - 1.0) / 1.50, -1.0, 1.50) * 20.0;
        var pnlScore = Math.Clamp(row.TotalPnl / 200.0, -1.0, 1.50) * 15.0;
        var drawdownScore = (1.0 - Math.Clamp(row.MaxDrawdown / StrictPromotionMaxDrawdownUsd, 0.0, 1.50)) * 15.0;
        var sharpeScore = Math.Clamp((promotionSharpe + 3.0) / 4.0, 0.0, 1.0) * 10.0;
        var expectancyScore = Math.Clamp(row.ExpectancyR / 0.08, -0.5, 1.25) * 10.0;
        var governorPenalty = Math.Min(20.0, row.GovernorStops * 8.0);
        var recoveryPenalty = Math.Min(8.0, recoveryTaskCount * 1.25);
        var failingPnlPenalty = row.TotalPnl <= 0.0 ? 8.0 : 0.0;
        var failingProfitFactorPenalty = normalizedProfitFactor < 1.0 ? 8.0 : 0.0;
        var strictBonus = strictPromotionPass ? 12.0 : 0.0;
        var pessimisticBonus = pessimisticPromotionPass ? 8.0 : 0.0;

        return Math.Clamp(
            tradeCoverageScore
            + profitFactorScore
            + pnlScore
            + drawdownScore
            + sharpeScore
            + expectancyScore
            + strictBonus
            + pessimisticBonus
            - governorPenalty
            - recoveryPenalty
            - failingPnlPenalty
            - failingProfitFactorPenalty,
            0.0,
            100.0);
    }

    private static double NormalizeProfitFactor(double profitFactor)
    {
        if (double.IsPositiveInfinity(profitFactor))
        {
            return 3.0;
        }

        if (double.IsNaN(profitFactor) || double.IsNegativeInfinity(profitFactor))
        {
            return 0.0;
        }

        return Math.Max(0.0, profitFactor);
    }

    private static string FormatFiniteMetric(double value)
    {
        if (double.IsPositiveInfinity(value))
        {
            return "INF";
        }

        if (!double.IsFinite(value))
        {
            return "-";
        }

        return value.ToString("F2");
    }

    private static ReplayTradeEnvelope BuildReplayTradeEnvelope(
        (string Strategy, string Variant, string Symbol, BacktestTradeResult Trade) tradeEnvelope,
        IReadOnlyDictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData)
    {
        var setupIdentity = ResolveBacktestSetupIdentity(tradeEnvelope.Strategy, tradeEnvelope.Variant, tradeEnvelope.Trade.SubStrategy);
        var replayMetrics = allData.TryGetValue(tradeEnvelope.Symbol, out var data)
            ? ComputeReplayMetrics(tradeEnvelope.Trade, data.Trigger)
            : new TradeReplayMetrics();

        var givebackUsd = tradeEnvelope.Trade.Side == TradeSide.Long
            ? Math.Max(0.0, replayMetrics.PeakFavorablePrice - tradeEnvelope.Trade.ExitPrice) * tradeEnvelope.Trade.PositionSize
            : Math.Max(0.0, tradeEnvelope.Trade.ExitPrice - replayMetrics.PeakFavorablePrice) * tradeEnvelope.Trade.PositionSize;
        var stopDistanceUsd = Math.Abs(tradeEnvelope.Trade.EntryPrice - tradeEnvelope.Trade.StopPrice) * tradeEnvelope.Trade.PositionSize;
        var stopUtilizationPct = stopDistanceUsd > 0.0
            ? Math.Min(5.0, replayMetrics.MaeUsd / stopDistanceUsd)
            : 0.0;
        var profitCapturePct = replayMetrics.MfeUsd > 0.0
            ? tradeEnvelope.Trade.Pnl / replayMetrics.MfeUsd
            : 0.0;
        var adverseExcursionPctOfRisk = stopDistanceUsd > 0.0
            ? replayMetrics.MaeUsd / stopDistanceUsd
            : 0.0;

        return new ReplayTradeEnvelope(
            tradeEnvelope.Strategy,
            tradeEnvelope.Variant,
            tradeEnvelope.Symbol,
            setupIdentity.CompositeSetup,
            tradeEnvelope.Trade,
            tradeEnvelope.Trade.EffectiveEntryTime.Hour,
            replayMetrics.MfeUsd,
            replayMetrics.MaeUsd,
            givebackUsd,
            stopDistanceUsd,
            stopUtilizationPct,
            profitCapturePct,
            adverseExcursionPctOfRisk,
            Math.Max(0.0, (tradeEnvelope.Trade.ExitTime - tradeEnvelope.Trade.EffectiveEntryTime).TotalMinutes),
            0.0);
    }

    private static IReadOnlyList<ReplayBucketSummary> BuildBucketSummaries(
        IReadOnlyList<ReplayTradeEnvelope> trades,
        Func<ReplayTradeEnvelope, string> keySelector,
        Func<string, string> labelSelector,
        int top = int.MaxValue)
    {
        return trades
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildBucketSummary(group.Key, labelSelector(group.Key), group.ToArray()))
            .OrderByDescending(summary => summary.TotalPnl)
            .ThenByDescending(summary => summary.Trades)
            .ThenBy(summary => summary.Key, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToArray();
    }

    private static ReplayBucketSummary BuildBucketSummary(string key, string label, IReadOnlyList<ReplayTradeEnvelope> trades)
    {
        var winners = trades.Where(trade => trade.Trade.Pnl > 0.0).Select(trade => trade.Trade.Pnl).ToArray();
        var losers = trades.Where(trade => trade.Trade.Pnl < 0.0).Select(trade => trade.Trade.Pnl).ToArray();
        var grossProfit = winners.Sum();
        var grossLoss = Math.Abs(losers.Sum());
        var profitFactor = grossLoss > 0.0
            ? grossProfit / grossLoss
            : grossProfit > 0.0
                ? double.PositiveInfinity
                : 0.0;

        return new ReplayBucketSummary(
            Key: key,
            Label: label,
            Trades: trades.Count,
            Winners: winners.Length,
            Losers: losers.Length,
            WinRate: trades.Count == 0 ? 0.0 : winners.Length / (double)trades.Count,
            TotalPnl: trades.Sum(trade => trade.Trade.Pnl),
            AvgPnl: trades.Count == 0 ? 0.0 : trades.Average(trade => trade.Trade.Pnl),
            AvgWin: winners.Length == 0 ? 0.0 : winners.Average(),
            AvgLoss: losers.Length == 0 ? 0.0 : losers.Average(),
            ProfitFactor: double.IsFinite(profitFactor) ? profitFactor : null,
            ExpectancyR: trades.Count == 0 ? 0.0 : trades.Average(trade => trade.Trade.PnlR),
            AvgMfeUsd: trades.Count == 0 ? 0.0 : trades.Average(trade => trade.MfeUsd),
            AvgMaeUsd: trades.Count == 0 ? 0.0 : trades.Average(trade => trade.MaeUsd),
            AvgGivebackUsd: trades.Count == 0 ? 0.0 : trades.Average(trade => trade.GivebackUsd),
            AvgBarsHeld: trades.Count == 0 ? 0.0 : trades.Average(trade => trade.Trade.BarsHeld),
            MaxWin: winners.Length == 0 ? 0.0 : winners.Max(),
            MaxLoss: losers.Length == 0 ? 0.0 : losers.Min());
    }

    private static IReadOnlyList<ReplayDrawdownContributor> BuildDrawdownContributors(IReadOnlyList<ReplayTradeEnvelope> trades)
    {
        var runningPnl = 0.0;
        var peakPnl = 0.0;
        var previousDrawdown = 0.0;
        var enrichedTrades = new List<ReplayTradeEnvelope>(trades.Count);

        foreach (var trade in trades)
        {
            var afterPnl = runningPnl + trade.Trade.Pnl;
            var updatedPeak = Math.Max(peakPnl, afterPnl);
            var afterDrawdown = Math.Max(0.0, updatedPeak - afterPnl);
            var contribution = Math.Max(0.0, afterDrawdown - previousDrawdown);

            enrichedTrades.Add(trade with { DrawdownContributionUsd = contribution });
            runningPnl = afterPnl;
            peakPnl = updatedPeak;
            previousDrawdown = afterDrawdown;
        }

        return enrichedTrades
            .Where(trade => trade.DrawdownContributionUsd > 0.0)
            .OrderByDescending(trade => trade.DrawdownContributionUsd)
            .ThenBy(trade => trade.Trade.ExitTime)
            .Take(20)
            .Select(trade => new ReplayDrawdownContributor(
                Symbol: trade.Symbol,
                Side: trade.Trade.Side.ToString(),
                ExitReason: trade.Trade.ExitReason.ToString(),
                Setup: trade.Setup,
                EntryHourUtc: trade.EntryHourUtc,
                EntryTimeUtc: trade.Trade.EffectiveEntryTime.ToString("o"),
                ExitTimeUtc: trade.Trade.ExitTime.ToString("o"),
                PnlUsd: trade.Trade.Pnl,
                DrawdownContributionUsd: trade.DrawdownContributionUsd,
                MfeUsd: trade.MfeUsd,
                MaeUsd: trade.MaeUsd,
                GivebackUsd: trade.GivebackUsd))
            .ToArray();
    }

    private static ReplayHardStopDiagnostics BuildHardStopDiagnostics(IReadOnlyList<ReplayTradeEnvelope> trades)
    {
        var hardStops = trades
            .Where(trade => trade.Trade.ExitReason == ExitReason.HardStop)
            .OrderBy(trade => trade.Trade.ExitTime)
            .ToArray();

        return new ReplayHardStopDiagnostics(
            TradeCount: hardStops.Length,
            TotalPnl: hardStops.Sum(trade => trade.Trade.Pnl),
            AvgPnl: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.Trade.Pnl),
            AvgPnlR: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.Trade.PnlR),
            AvgMfeUsd: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.MfeUsd),
            AvgMaeUsd: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.MaeUsd),
            AvgGivebackUsd: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.GivebackUsd),
            AvgStopDistanceUsd: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.StopDistanceUsd),
            AvgStopUtilizationPct: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.StopUtilizationPct),
            AvgTimeToExitMinutes: hardStops.Length == 0 ? 0.0 : hardStops.Average(trade => trade.TimeToExitMinutes),
            BySymbol: BuildBucketSummaries(hardStops, trade => trade.Symbol, symbol => symbol, top: 20),
            ByEntryHourUtc: BuildBucketSummaries(hardStops, trade => trade.EntryHourUtc.ToString("00"), hour => $"{hour}:00 UTC"),
            BySetup: BuildBucketSummaries(hardStops, trade => trade.Setup, setup => setup, top: 20),
            Trades: hardStops
                .OrderBy(trade => trade.Trade.Pnl)
                .ThenBy(trade => trade.Trade.ExitTime)
                .Take(20)
                .Select(trade => new ReplayHardStopTradeDiagnostic(
                    Symbol: trade.Symbol,
                    Side: trade.Trade.Side.ToString(),
                    Setup: trade.Setup,
                    EntryHourUtc: trade.EntryHourUtc,
                    EntryTimeUtc: trade.Trade.EffectiveEntryTime.ToString("o"),
                    ExitTimeUtc: trade.Trade.ExitTime.ToString("o"),
                    PnlUsd: trade.Trade.Pnl,
                    PnlR: trade.Trade.PnlR,
                    MfeUsd: trade.MfeUsd,
                    MaeUsd: trade.MaeUsd,
                    GivebackUsd: trade.GivebackUsd,
                    StopDistanceUsd: trade.StopDistanceUsd,
                    StopUtilizationPct: trade.StopUtilizationPct,
                    TimeToExitMinutes: trade.TimeToExitMinutes,
                    BarsHeld: trade.Trade.BarsHeld))
                .ToArray());
    }

    private static ReplayMfeMaeDiagnostics BuildMfeMaeDiagnostics(IReadOnlyList<ReplayTradeEnvelope> trades)
    {
        static ReplayMfeMaeBucketSummary BuildBucket(string key, string label, IReadOnlyList<ReplayTradeEnvelope> items)
        {
            var totalMfe = items.Sum(item => Math.Max(0.0, item.MfeUsd));
            var totalRisk = items.Sum(item => Math.Max(0.0, item.StopDistanceUsd));
            return new ReplayMfeMaeBucketSummary(
                Key: key,
                Label: label,
                Trades: items.Count,
                TotalPnl: items.Sum(item => item.Trade.Pnl),
                AvgPnl: items.Count == 0 ? 0.0 : items.Average(item => item.Trade.Pnl),
                AvgPnlR: items.Count == 0 ? 0.0 : items.Average(item => item.Trade.PnlR),
                AvgMfeUsd: items.Count == 0 ? 0.0 : items.Average(item => item.MfeUsd),
                AvgMaeUsd: items.Count == 0 ? 0.0 : items.Average(item => item.MaeUsd),
                AvgGivebackUsd: items.Count == 0 ? 0.0 : items.Average(item => item.GivebackUsd),
                ProfitCapturePct: totalMfe > 0.0 ? items.Sum(item => item.Trade.Pnl) / totalMfe : 0.0,
                AdverseExcursionPctOfRisk: totalRisk > 0.0 ? items.Sum(item => item.MaeUsd) / totalRisk : 0.0,
                AvgBarsHeld: items.Count == 0 ? 0.0 : items.Average(item => item.Trade.BarsHeld));
        }

        return new ReplayMfeMaeDiagnostics(
            BySetup: trades
                .GroupBy(trade => trade.Setup, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildBucket(group.Key, group.Key, group.ToArray()))
                .OrderByDescending(bucket => bucket.TotalPnl)
                .ThenBy(bucket => bucket.Key, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToArray(),
            ByExitReason: trades
                .GroupBy(trade => trade.Trade.ExitReason.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildBucket(group.Key, group.Key, group.ToArray()))
                .OrderByDescending(bucket => bucket.TotalPnl)
                .ThenBy(bucket => bucket.Key, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToArray(),
            TopGivebackTrades: trades
                .OrderByDescending(trade => trade.GivebackUsd)
                .ThenBy(trade => trade.Trade.ExitTime)
                .Take(20)
                .Select(BuildMfeMaeTradeDiagnostic)
                .ToArray(),
            TopMaeTrades: trades
                .OrderByDescending(trade => trade.MaeUsd)
                .ThenBy(trade => trade.Trade.ExitTime)
                .Take(20)
                .Select(BuildMfeMaeTradeDiagnostic)
                .ToArray());
    }

    private static ReplayMfeMaeTradeDiagnostic BuildMfeMaeTradeDiagnostic(ReplayTradeEnvelope trade)
        => new(
            Symbol: trade.Symbol,
            Side: trade.Trade.Side.ToString(),
            ExitReason: trade.Trade.ExitReason.ToString(),
            Setup: trade.Setup,
            EntryHourUtc: trade.EntryHourUtc,
            EntryTimeUtc: trade.Trade.EffectiveEntryTime.ToString("o"),
            ExitTimeUtc: trade.Trade.ExitTime.ToString("o"),
            PnlUsd: trade.Trade.Pnl,
            PnlR: trade.Trade.PnlR,
            MfeUsd: trade.MfeUsd,
            MaeUsd: trade.MaeUsd,
            GivebackUsd: trade.GivebackUsd,
            ProfitCapturePct: trade.ProfitCapturePct,
            AdverseExcursionPctOfRisk: trade.AdverseExcursionPctOfRisk);

    private sealed record ReplayTradeEnvelope(
        string Strategy,
        string Variant,
        string Symbol,
        string Setup,
        BacktestTradeResult Trade,
        int EntryHourUtc,
        double MfeUsd,
        double MaeUsd,
        double GivebackUsd,
        double StopDistanceUsd,
        double StopUtilizationPct,
        double ProfitCapturePct,
        double AdverseExcursionPctOfRisk,
        double TimeToExitMinutes,
        double DrawdownContributionUsd);

    internal static (IReadOnlyList<BacktestTradeResult> Trades, BacktestGovernorReport GovernorReport) PrepareComparisonTrades(
        IReadOnlyList<BacktestTradeResult> trades,
        double initialCapital)
    {
        var (filteredTrades, governorReport) = PrepareComparisonTradesCore(
            trades,
            trade => trade,
            initialCapital,
            envelopeUpdater: static (_, trade) => trade);

        return (filteredTrades, governorReport);
    }

    internal static (IReadOnlyList<(string Symbol, BacktestTradeResult Trade)> Trades, BacktestGovernorReport GovernorReport) PrepareComparisonTrades(
        IReadOnlyList<(string Symbol, BacktestTradeResult Trade)> trades,
        double initialCapital,
        int maxTradesPerSymbol)
    {
        var (filteredTrades, governorReport) = PrepareComparisonTradesCore(
            trades,
            trade => trade.Trade,
            initialCapital,
            maxTradesPerSymbol,
            trade => trade.Symbol,
            static (envelope, trade) => (envelope.Symbol, trade));

        return (filteredTrades, governorReport);
    }

    private static bool IsBetter(StrategyComparisonRow left, StrategyComparisonRow right, bool preferTradeFloor)
    {
        if (preferTradeFloor && left.MeetsMinTrades != right.MeetsMinTrades)
            return left.MeetsMinTrades;

        if ((left.Trades > 0) != (right.Trades > 0))
            return left.Trades > 0;

        int promotionCmp = left.PromotionScore.CompareTo(right.PromotionScore);
        if (promotionCmp != 0) return promotionCmp > 0;

        int pnlCmp = left.TotalPnl.CompareTo(right.TotalPnl);
        if (pnlCmp != 0) return pnlCmp > 0;

        int ddCmp = right.MaxDrawdown.CompareTo(left.MaxDrawdown);
        if (ddCmp != 0) return ddCmp > 0;

        int sharpeCmp = left.Sharpe.CompareTo(right.Sharpe);
        if (sharpeCmp != 0) return sharpeCmp > 0;

        int equitySharpeCmp = left.EquityCurveSharpe.CompareTo(right.EquityCurveSharpe);
        if (equitySharpeCmp != 0) return equitySharpeCmp > 0;

        int pfCmp = left.ProfitFactor.CompareTo(right.ProfitFactor);
        if (pfCmp != 0) return pfCmp > 0;

        return left.Trades > right.Trades;
    }

    private static (StrategyComparisonRow Best, T Payload)? SelectSparseFallback<T>(
        IReadOnlyList<(StrategyComparisonRow Row, T Payload)> candidates,
        StrategyComparisonRow bestAny,
        int minTrades)
    {
        var minimumGuardedTrades = (int)Math.Ceiling(minTrades * GetSparseFallbackTradeCoverageRatio(bestAny));

        StrategyComparisonRow? fallback = null;
        T? fallbackPayload = default;

        foreach (var candidate in candidates)
        {
            if (candidate.Row.Trades <= bestAny.Trades)
            {
                continue;
            }

            if (candidate.Row.Trades < minimumGuardedTrades)
            {
                continue;
            }

            if (!IsSparseFallbackPnlAcceptable(candidate.Row, bestAny))
            {
                continue;
            }

            if (fallback == null || IsSparseFallbackBetter(candidate.Row, fallback))
            {
                fallback = candidate.Row;
                fallbackPayload = candidate.Payload;
            }
        }

        return fallback is null
            ? null
            : (fallback, fallbackPayload!);
    }

    private static double GetSparseFallbackTradeCoverageRatio(StrategyComparisonRow bestAny)
        => bestAny.TotalPnl > 0
            ? SparseFallbackMinTradeCoverageRatio
            : SparseLossFallbackMinTradeCoverageRatio;

    private static bool IsSparseFallbackPnlAcceptable(StrategyComparisonRow candidate, StrategyComparisonRow bestAny)
    {
        if (bestAny.TotalPnl > 0)
        {
            return candidate.TotalPnl >= bestAny.TotalPnl * SparseFallbackMinPnlRetentionRatio;
        }

        return candidate.TotalPnl >= bestAny.TotalPnl - SparseLossFallbackMaxAdditionalLossDollars;
    }

    private static bool IsSparseFallbackBetter(StrategyComparisonRow left, StrategyComparisonRow right)
    {
        int tradesCmp = left.Trades.CompareTo(right.Trades);
        if (tradesCmp != 0) return tradesCmp > 0;

        int pnlCmp = left.TotalPnl.CompareTo(right.TotalPnl);
        if (pnlCmp != 0) return pnlCmp > 0;

        int ddCmp = right.MaxDrawdown.CompareTo(left.MaxDrawdown);
        if (ddCmp != 0) return ddCmp > 0;

        int sharpeCmp = left.Sharpe.CompareTo(right.Sharpe);
        if (sharpeCmp != 0) return sharpeCmp > 0;

        int equitySharpeCmp = left.EquityCurveSharpe.CompareTo(right.EquityCurveSharpe);
        if (equitySharpeCmp != 0) return equitySharpeCmp > 0;

        return left.ProfitFactor > right.ProfitFactor;
    }

    private static List<StrategyComparisonRow> RankRows(List<StrategyComparisonRow> rows)
    {
        var ranked = rows.ToList();
        ranked.Sort(CompareRowsForTable);
        return ranked;
    }

    private static int CompareRowsForTable(StrategyComparisonRow left, StrategyComparisonRow right)
    {
        if (IsBetter(left, right, preferTradeFloor: true)) return -1;
        if (IsBetter(right, left, preferTradeFloor: true)) return 1;

        int strategyCmp = string.Compare(left.Strategy, right.Strategy, StringComparison.OrdinalIgnoreCase);
        if (strategyCmp != 0) return strategyCmp;

        return string.Compare(left.Variant, right.Variant, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintTable(List<StrategyComparisonRow> rows, int minTrades, Action<string> log)
    {
        log("\n=== STRATEGY COMPARISON TABLE ===");
        log($"Min trades target per strategy: {minTrades}");
        log("| Rank | Strategy | Variant | Symbols | Trades | >=50 | WinRate | PF | Sharpe | EqSharpe | EqSortino | EqDownDev | TotalPnL$ | MaxDD$ | AvgWin$ | AvgLoss$ | Expectancy | GovStops | GovReason |");
        log("|---:|---|---|---:|---:|:---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var meets = r.MeetsMinTrades ? "YES" : "NO";
            var pf = double.IsInfinity(r.ProfitFactor) ? "INF" : r.ProfitFactor.ToString("F2");
            var governorReason = string.IsNullOrWhiteSpace(r.GovernorStopReason) ? "-" : r.GovernorStopReason;
            log($"| {i + 1} | {r.Strategy} | {r.Variant} | {r.Symbols} | {r.Trades} | {meets} | {r.WinRate:P1} | {pf} | {r.Sharpe:F2} | {r.EquityCurveSharpe:F2} | {r.EquityCurveSortino:F2} | {r.EquityCurveDownsideDeviation:P2} | {r.TotalPnl:F2} | {r.MaxDrawdown:F2} | {r.AvgWin:F2} | {r.AvgLoss:F2} | {r.ExpectancyR:F2} | {r.GovernorStops} | {governorReason} |");
            log($"    promotionScore={r.PromotionScore:F2} strictPass={r.StrictPromotionPass} pessimisticPass={r.PessimisticPromotionPass} hardEnvironmentEligible={r.HardEnvironmentEligible} recoveryTasks={r.AdaptiveRecoveryTasks.Count}");
        }
    }

    private static (List<TEnvelope> Trades, BacktestGovernorReport GovernorReport) PrepareComparisonTradesCore<TEnvelope>(
        IReadOnlyList<TEnvelope> trades,
        Func<TEnvelope, BacktestTradeResult> tradeSelector,
        double initialCapital,
        int maxTradesPerSymbol = 0,
        Func<TEnvelope, string?>? symbolSelector = null,
        Func<TEnvelope, BacktestTradeResult, TEnvelope>? envelopeUpdater = null)
    {
        var orderedTrades = trades
            .Select((envelope, sequence) => new ComparisonTradeEnvelope<TEnvelope>(sequence, envelope, tradeSelector(envelope)))
            .OrderBy(x => x.Trade.ExitTime)
            .ThenBy(x => x.Trade.EffectiveEntryTime)
            .ThenBy(x => ResolveGovernorBucket(x.Trade), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Sequence)
            .ToList();

        var config = RiskGovernorConfigFactory.CreateComparisonBacktest(initialCapital);
        if (orderedTrades.Count == 0)
        {
            return (orderedTrades.Select(x => x.Envelope).ToList(), BacktestGovernorReport.None);
        }

        var governor = new RiskGovernor(config);
        var sessionStart = orderedTrades.Min(x => x.Trade.EffectiveEntryTime <= x.Trade.ExitTime ? x.Trade.EffectiveEntryTime : x.Trade.ExitTime);
        governor.ResetSession(sessionStart, initialCapital);

        var allowedTrades = new List<TEnvelope>();
        var activeTrades = new Dictionary<int, ComparisonTradeEnvelope<TEnvelope>>();
        var realizedPnlByBucket = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var stopReasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var haltedBuckets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acceptedTradesBySymbol = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var recentClosedTrades = new List<BacktestTradeResult>();
        var realizedPnl = 0.0;
        var sessionStopped = false;
        var stopReason = string.Empty;

        foreach (var tradeEvent in BuildTradeEvents(orderedTrades))
        {
            var bucket = ResolveGovernorBucket(tradeEvent.Trade);
            if (tradeEvent.IsEntry)
            {
                if (maxTradesPerSymbol > 0 && symbolSelector is not null)
                {
                    var symbol = symbolSelector(tradeEvent.Envelope)?.Trim();
                    if (!string.IsNullOrWhiteSpace(symbol)
                        && acceptedTradesBySymbol.TryGetValue(symbol, out var acceptedTradeCount)
                        && acceptedTradeCount >= maxTradesPerSymbol)
                    {
                        continue;
                    }
                }

                var entryCheck = governor.EvaluateEntry(tradeEvent.Time, bucket);
                if (!entryCheck.EntryAllowed)
                {
                    if (entryCheck.PortfolioBlocked)
                    {
                        sessionStopped = true;
                        if (string.IsNullOrWhiteSpace(stopReason))
                        {
                            stopReason = entryCheck.Reason;
                        }
                    }

                    if (entryCheck.BucketBlocked && !string.IsNullOrWhiteSpace(bucket))
                    {
                        haltedBuckets.Add(bucket);
                    }

                    IncrementReasonCount(stopReasonCounts, entryCheck.Reason);
                    continue;
                }

                var sizedTradeEvent = ApplyAdaptiveComparisonSizing(
                    tradeEvent,
                    entryCheck,
                    activeTrades.Values,
                    recentClosedTrades,
                    symbolSelector,
                    envelopeUpdater);

                activeTrades[tradeEvent.Sequence] = sizedTradeEvent;
                if (maxTradesPerSymbol > 0 && symbolSelector is not null)
                {
                    var symbol = symbolSelector(sizedTradeEvent.Envelope)?.Trim();
                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        acceptedTradesBySymbol[symbol] = acceptedTradesBySymbol.GetValueOrDefault(symbol) + 1;
                    }
                }

                var entryUpdate = governor.UpdatePortfolio(
                    tradeEvent.Time,
                    realizedPnl,
                    unrealizedPnlUsd: 0.0,
                    openPositionCount: activeTrades.Count,
                    bucketStates: BuildBucketStates(activeTrades.Values, realizedPnlByBucket));
                CaptureGovernorTransitions(entryUpdate, haltedBuckets, stopReasonCounts, ref sessionStopped, ref stopReason);
                continue;
            }

            if (!activeTrades.Remove(tradeEvent.Sequence, out var activeTrade))
            {
                continue;
            }

            allowedTrades.Add(activeTrade.Envelope);
            realizedPnl += activeTrade.Trade.Pnl;
            recentClosedTrades.Add(activeTrade.Trade);
            if (recentClosedTrades.Count > 8)
            {
                recentClosedTrades.RemoveAt(0);
            }
            if (!realizedPnlByBucket.TryAdd(bucket, activeTrade.Trade.Pnl))
            {
                realizedPnlByBucket[bucket] += activeTrade.Trade.Pnl;
            }

            var exitUpdate = governor.UpdatePortfolio(
                tradeEvent.Time,
                realizedPnl,
                unrealizedPnlUsd: 0.0,
                openPositionCount: activeTrades.Count,
                bucketStates: BuildBucketStates(activeTrades.Values, realizedPnlByBucket));
            CaptureGovernorTransitions(exitUpdate, haltedBuckets, stopReasonCounts, ref sessionStopped, ref stopReason);
        }

        return (allowedTrades, new BacktestGovernorReport(
            SessionStopped: sessionStopped,
            StopReason: stopReason,
            HaltedBucketCount: haltedBuckets.Count,
            HaltedBucketSummary: haltedBuckets.Count == 0
                ? string.Empty
                : string.Join(", ", haltedBuckets.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            StopReasonCounts: stopReasonCounts));
    }

    private static ComparisonTradeEnvelope<TEnvelope> ApplyAdaptiveComparisonSizing<TEnvelope>(
        ComparisonTradeEnvelope<TEnvelope> tradeEvent,
        RiskGovernorEntryCheckResult entryCheck,
        IEnumerable<ComparisonTradeEnvelope<TEnvelope>> activeTrades,
        IReadOnlyList<BacktestTradeResult> recentClosedTrades,
        Func<TEnvelope, string?>? symbolSelector,
        Func<TEnvelope, BacktestTradeResult, TEnvelope>? envelopeUpdater)
    {
        if (envelopeUpdater is null)
        {
            return tradeEvent;
        }

        var multiplier = entryCheck.SuggestedEntrySizeMultiplier;
        multiplier *= ResolveLossClusterSizingMultiplier(recentClosedTrades);
        multiplier *= ResolveExposureClusterSizingMultiplier(tradeEvent, activeTrades, symbolSelector);
        multiplier = Math.Clamp(multiplier, 0.30, 1.0);

        if (multiplier >= 0.999)
        {
            return tradeEvent;
        }

        var scaledTrade = ScaleTradeForComparison(tradeEvent.Trade, multiplier);
        return tradeEvent with
        {
            Trade = scaledTrade,
            Envelope = envelopeUpdater(tradeEvent.Envelope, scaledTrade)
        };
    }

    private static double ResolveLossClusterSizingMultiplier(IReadOnlyList<BacktestTradeResult> recentClosedTrades)
    {
        if (recentClosedTrades.Count == 0)
        {
            return 1.0;
        }

        var consecutiveLosses = 0;
        var recentLossCount = 0;
        var recentLossMagnitude = 0.0;
        for (var i = recentClosedTrades.Count - 1; i >= 0; i--)
        {
            var trade = recentClosedTrades[i];
            if (trade.Pnl < 0)
            {
                recentLossCount++;
                recentLossMagnitude += Math.Abs(trade.Pnl);
                consecutiveLosses++;
            }
            else if (consecutiveLosses > 0)
            {
                break;
            }
        }

        var multiplier = 1.0;
        if (consecutiveLosses >= 2)
        {
            multiplier *= consecutiveLosses >= 4
                ? 0.50
                : consecutiveLosses == 3
                    ? 0.62
                    : 0.78;
        }

        if (recentLossCount >= 3)
        {
            multiplier *= 0.90;
        }

        if (recentLossMagnitude >= 60.0)
        {
            multiplier *= recentLossMagnitude >= 120.0 ? 0.80 : 0.90;
        }

        return Math.Clamp(multiplier, 0.35, 1.0);
    }

    private static double ResolveExposureClusterSizingMultiplier<TEnvelope>(
        ComparisonTradeEnvelope<TEnvelope> tradeEvent,
        IEnumerable<ComparisonTradeEnvelope<TEnvelope>> activeTrades,
        Func<TEnvelope, string?>? symbolSelector)
    {
        var trade = tradeEvent.Trade;
        var multiplier = 1.0;
        var isSmallCap = trade.EntryPrice > 0 && trade.EntryPrice <= 5.0;
        if (!isSmallCap)
        {
            return multiplier;
        }

        var activeSmallCaps = activeTrades.Count(x => x.Trade.EntryPrice > 0 && x.Trade.EntryPrice <= 5.0);
        var activeSameSideSmallCaps = activeTrades.Count(x => x.Trade.EntryPrice > 0 && x.Trade.EntryPrice <= 5.0 && x.Trade.Side == trade.Side);
        if (activeSmallCaps > 0)
        {
            multiplier *= 1.0 / (1.0 + activeSmallCaps * 0.18);
        }

        if (activeSameSideSmallCaps > 0)
        {
            multiplier *= 1.0 / (1.0 + activeSameSideSmallCaps * 0.12);
        }

        if (symbolSelector is not null)
        {
            var currentSymbol = symbolSelector(tradeEvent.Envelope)?.Trim();
            if (!string.IsNullOrWhiteSpace(currentSymbol))
            {
                var sameSymbolActive = activeTrades.Count(x => string.Equals(symbolSelector(x.Envelope)?.Trim(), currentSymbol, StringComparison.OrdinalIgnoreCase));
                if (sameSymbolActive > 0)
                {
                    multiplier *= 0.85;
                }
            }
        }

        if (trade.EntryPrice <= 2.0)
        {
            multiplier *= 0.82;
        }

        return Math.Clamp(multiplier, 0.35, 1.0);
    }

    private static BacktestTradeResult ScaleTradeForComparison(BacktestTradeResult trade, double multiplier)
    {
        if (trade.PositionSize <= 0)
        {
            return trade;
        }

        var scaledPositionSize = Math.Max(1, (int)Math.Floor(trade.PositionSize * multiplier));
        var effectiveMultiplier = (double)scaledPositionSize / trade.PositionSize;
        if (effectiveMultiplier >= 0.999)
        {
            return trade;
        }

        BacktestSelectedEntryIntent? scaledSelectedEntryIntent = null;
        if (trade.SelectedEntryIntent is not null)
        {
            var scaledSignal = trade.SelectedEntryIntent.Signal with
            {
                PositionSize = scaledPositionSize
            };

            scaledSelectedEntryIntent = trade.SelectedEntryIntent with
            {
                Signal = scaledSignal
            };
        }

        var scaledLifecycleState = trade.LifecycleFinalState is null
            ? null
            : trade.LifecycleFinalState with
            {
                OriginalQuantity = Math.Max(1, (int)Math.Round(trade.LifecycleFinalState.OriginalQuantity * effectiveMultiplier)),
                OpenQuantity = Math.Max(0, (int)Math.Round(trade.LifecycleFinalState.OpenQuantity * effectiveMultiplier))
            };

        var scaledLifecycleEvents = trade.LifecycleEvents?.Select(evt => evt with
        {
            Quantity = evt.Quantity is int quantity
                ? Math.Max(1, (int)Math.Round(quantity * effectiveMultiplier))
                : null
        }).ToArray();

        return trade with
        {
            PositionSize = scaledPositionSize,
            Pnl = Math.Round(trade.Pnl * effectiveMultiplier, 4),
            SelectedEntryIntent = scaledSelectedEntryIntent,
            LifecycleFinalState = scaledLifecycleState,
            LifecycleEvents = scaledLifecycleEvents
        };
    }

    private static IEnumerable<ComparisonTradeEnvelope<TEnvelope>> BuildTradeEvents<TEnvelope>(
        IReadOnlyList<ComparisonTradeEnvelope<TEnvelope>> orderedTrades)
    {
        return orderedTrades
            .SelectMany(trade => new[]
            {
                trade with { Time = trade.Trade.ExitTime, SortOrder = 0, IsEntry = false },
                trade with { Time = trade.Trade.EffectiveEntryTime, SortOrder = 1, IsEntry = true },
            })
            .OrderBy(x => x.Time)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Sequence);
    }

    private static IReadOnlyList<RiskGovernorBucketStateInput> BuildBucketStates<TEnvelope>(
        IEnumerable<ComparisonTradeEnvelope<TEnvelope>> activeTrades,
        IReadOnlyDictionary<string, double> realizedPnlByBucket)
    {
        var activeCounts = activeTrades
            .GroupBy(x => ResolveGovernorBucket(x.Trade), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var bucketKeys = new HashSet<string>(realizedPnlByBucket.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in activeCounts.Keys)
        {
            bucketKeys.Add(bucket);
        }

        return bucketKeys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(bucket => new RiskGovernorBucketStateInput(
                Bucket: bucket,
                RealizedPnlUsd: realizedPnlByBucket.TryGetValue(bucket, out var realizedPnl) ? realizedPnl : 0.0,
                UnrealizedPnlUsd: 0.0,
                OpenPositionCount: activeCounts.TryGetValue(bucket, out var openPositions) ? openPositions : 0))
            .ToArray();
    }

    private static void CaptureGovernorTransitions(
        RiskGovernorUpdateResult update,
        HashSet<string> haltedBuckets,
        Dictionary<string, int> stopReasonCounts,
        ref bool sessionStopped,
        ref string stopReason)
    {
        if (update.Snapshot.EntriesBlocked)
        {
            sessionStopped = true;
            if (string.IsNullOrWhiteSpace(stopReason))
            {
                stopReason = update.Snapshot.ActiveReason;
            }
        }

        foreach (var bucket in update.NewlyStoppedBuckets)
        {
            haltedBuckets.Add(bucket.Bucket);
            IncrementReasonCount(stopReasonCounts, bucket.ActiveReason);
        }
    }

    private static void IncrementReasonCount(Dictionary<string, int> stopReasonCounts, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        if (!stopReasonCounts.TryAdd(reason, 1))
        {
            stopReasonCounts[reason]++;
        }
    }

    private static string ResolveGovernorBucket(BacktestTradeResult trade)
    {
        if (!string.IsNullOrWhiteSpace(trade.GovernorBucket))
        {
            return trade.GovernorBucket;
        }

        if (!string.IsNullOrWhiteSpace(trade.SubStrategy))
        {
            return trade.SubStrategy;
        }

        return "default";
    }

    private static BacktestGovernorReport MergeGovernorReports(params BacktestGovernorReport[] reports)
    {
        var reasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stoppedBuckets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessionStopped = false;
        var stopReason = string.Empty;

        foreach (var report in reports)
        {
            if (report.SessionStopped)
            {
                sessionStopped = true;
                if (string.IsNullOrWhiteSpace(stopReason) && !string.IsNullOrWhiteSpace(report.StopReason))
                {
                    stopReason = report.StopReason;
                }
            }

            if (!string.IsNullOrWhiteSpace(report.HaltedBucketSummary))
            {
                foreach (var bucket in report.HaltedBucketSummary.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    stoppedBuckets.Add(bucket);
                }
            }

            foreach (var kvp in report.StopReasonCounts)
            {
                if (!reasonCounts.TryAdd(kvp.Key, kvp.Value))
                {
                    reasonCounts[kvp.Key] += kvp.Value;
                }
            }
        }

        return new BacktestGovernorReport(
            SessionStopped: sessionStopped,
            StopReason: stopReason,
            HaltedBucketCount: stoppedBuckets.Count,
            HaltedBucketSummary: stoppedBuckets.Count == 0 ? string.Empty : string.Join("; ", stoppedBuckets.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            StopReasonCounts: reasonCounts);
    }

    private static int CountGovernorStops(BacktestGovernorReport report)
    {
        return (report.SessionStopped ? 1 : 0) + report.HaltedBucketCount;
    }

    private static BacktestGovernorReport AggregateGovernorReport(IReadOnlyList<BacktestResult> backtests)
    {
        var reasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var haltedBuckets = new List<string>();
        var sessionStopped = false;
        var sessionReason = string.Empty;

        foreach (var backtest in backtests)
        {
            if (backtest.Stats.Governor.SessionStopped)
            {
                sessionStopped = true;
                if (string.IsNullOrWhiteSpace(sessionReason) && !string.IsNullOrWhiteSpace(backtest.Stats.Governor.StopReason))
                {
                    sessionReason = backtest.Stats.Governor.StopReason;
                }
            }

            foreach (var kvp in backtest.Stats.Governor.StopReasonCounts)
            {
                if (!reasonCounts.TryAdd(kvp.Key, kvp.Value))
                {
                    reasonCounts[kvp.Key] += kvp.Value;
                }
            }

            if (!string.IsNullOrWhiteSpace(backtest.Stats.Governor.HaltedBucketSummary))
            {
                haltedBuckets.Add(backtest.Stats.Governor.HaltedBucketSummary);
            }
        }

        return new BacktestGovernorReport(
            SessionStopped: sessionStopped,
            StopReason: sessionReason,
            HaltedBucketCount: haltedBuckets.Count,
            HaltedBucketSummary: haltedBuckets.Count == 0 ? string.Empty : string.Join("; ", haltedBuckets),
            StopReasonCounts: reasonCounts);
    }

    private static string SummarizeGovernorReasons(BacktestGovernorReport report)
    {
        if (report.StopReasonCounts.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", report.StopReasonCounts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(x => $"{x.Key}x{x.Value}"));
    }

    private sealed record ComparisonTradeEnvelope<TEnvelope>(
        int Sequence,
        TEnvelope Envelope,
        BacktestTradeResult Trade)
    {
        public DateTime Time { get; init; }
        public int SortOrder { get; init; }
        public bool IsEntry { get; init; }
    }

    private sealed record StrategyPlan(
        string Name,
        double InitialCapital,
        IReadOnlyList<StrategyVariant> Variants);

    private sealed record StrategyVariant(string Name, Func<IBacktestStrategy> Factory);

    /// <summary>
    /// Returns non-archived strategy plans for external consumers (e.g. self-learning bridge).
    /// </summary>
    public static List<(string Name, double Capital, List<(string Variant, Func<IBacktestStrategy> Factory)> Variants)> GetActivePlans()
    {
        return BuildPlans()
            .Where(p => !ArchivedStrategyPlans.Contains(p.Name))
            .Select(p => (
                p.Name,
                p.InitialCapital,
                p.Variants.Select(v => (v.Name, v.Factory)).ToList()))
            .ToList();
    }

    internal static List<(string Name, double Capital, List<(string Variant, Func<IBacktestStrategy> Factory)> Variants)> GetAllPlans()
    {
        return BuildPlans()
            .Select(p => (
                p.Name,
                p.InitialCapital,
                p.Variants.Select(v => (v.Name, v.Factory)).ToList()))
            .ToList();
    }

    private static IBacktestStrategy CreateV11SilverPearl()
        => new StrategyV11(new V11Config
        {
            RiskPerTradeDollars = 20.0,
            AccountSize = 25_000.0,
            MaxPositionNotionalPctOfAccount = 0.14,
            MaxShares = 5000,
            CooldownBars = 1,
            RvolMin = 0.78,
            L2LiquidityMin = 18.0,
            SpreadZMax = 2.2,
            VolAccelMin = -0.25,
            BbLongThreshold = 0.14,
            BbShortThreshold = 0.86,
            VwapDeviationAtr = 0.48,
            RsiLongMax = 40.0,
            RsiShortMin = 60.0,
            AdxMin = 12.0,
            AdxMax = 34.0,
            MinScore = 6,
            HardStopR = 0.88,
            BreakevenR = 0.40,
            TrailR = 0.24,
            GivebackPct = 0.22,
            Tp1R = 0.55,
            Tp2R = 1.05,
            MaxHoldBars = 24,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = 24.0,
            UseUnifiedEntryScore = true,
            EnableDiagnostics = true,
            DiagnosticsLabel = "silver-pearl",
            UseEmaTrail = true,
            EmaTrailBufferAtr = 0.15,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = 0.50,
            PeakGivebackActivateR = 0.30,
            FlattenOnStagnation = true,
            StagnationBars = 8,
            StagnationMinPeakR = 0.15,
            StagnationMaxAdverseR = -0.08,
            UseL1L2DecisionOnOppositeBarsFlatten = true,
        });

    private static IBacktestStrategy CreateV11PinkPearl()
        => new StrategyV11(new V11Config
        {
            RiskPerTradeDollars = 30.0,
            AccountSize = 25_000.0,
            MaxPositionNotionalPctOfAccount = 0.18,
            MaxShares = 6500,
            CooldownBars = 1,
            RvolMin = 0.85,
            L2LiquidityMin = 18.0,
            SpreadZMax = 2.2,
            VolAccelMin = -0.25,
            BbLongThreshold = 0.14,
            BbShortThreshold = 0.86,
            VwapDeviationAtr = 0.48,
            RsiLongMax = 40.0,
            RsiShortMin = 60.0,
            AdxMin = 12.0,
            AdxMax = 36.0,
            MinScore = 6,
            HardStopR = 0.88,
            BreakevenR = 0.40,
            TrailR = 0.24,
            GivebackPct = 0.22,
            Tp1R = 0.55,
            Tp2R = 1.05,
            MaxHoldBars = 24,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = 30.0,
            UseUnifiedEntryScore = true,
            EnableDiagnostics = true,
            DiagnosticsLabel = "pink-pearl",
            UseEmaTrail = true,
            EmaTrailBufferAtr = 0.15,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = 0.50,
            PeakGivebackActivateR = 0.30,
            FlattenOnStagnation = true,
            StagnationBars = 8,
            StagnationMinPeakR = 0.15,
            StagnationMaxAdverseR = -0.08,
        });

    internal static IReadOnlyList<string> ResolveFilteredPlanNames(string[] strategyFilter)
    {
        return ResolveFilteredPlans(strategyFilter)
            .Select(plan => plan.Name)
            .ToList();
    }

    internal static IReadOnlyList<(string Strategy, IReadOnlyList<string> Variants)> ResolveFilteredPlanVariants(
        string[] strategyFilter,
        bool mainVariantsOnly = false)
    {
        return ResolveFilteredPlans(strategyFilter, mainVariantsOnly)
            .Select(plan => (
                plan.Name,
                (IReadOnlyList<string>)plan.Variants.Select(variant => variant.Name).ToList()))
            .ToList();
    }

    private static List<StrategyPlan> ResolveFilteredPlans(string[] strategyFilter, bool mainVariantsOnly = false)
    {
        var allPlans = BuildPlans();
        var normalizedFilter = strategyFilter
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Select(filter => filter.Trim())
            .ToArray();

        if (normalizedFilter.Any(filter => string.Equals(filter, "folder-all", StringComparison.OrdinalIgnoreCase)))
            return ApplyMainVariantFilter(allPlans, mainVariantsOnly);

        var fullPlanFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variantFilter = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawFilter in normalizedFilter)
        {
            var parts = rawFilter.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                if (!variantFilter.TryGetValue(parts[0], out var variants))
                {
                    variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    variantFilter[parts[0]] = variants;
                }

                variants.Add(parts[1]);
                continue;
            }

            fullPlanFilter.Add(parts[0]);
        }

        var filteredPlans = allPlans
            .Select(plan =>
            {
                if (fullPlanFilter.Contains(plan.Name))
                    return plan;

                if (!variantFilter.TryGetValue(plan.Name, out var requestedVariants))
                    return null;

                var filteredVariants = plan.Variants
                    .Where(v => requestedVariants.Contains(v.Name))
                    .ToList();

                return filteredVariants.Count == 0
                    ? null
                    : plan with { Variants = filteredVariants };
            })
            .Where(plan => plan is not null)
            .Select(plan => plan!)
            .ToList();

        return ApplyMainVariantFilter(filteredPlans, mainVariantsOnly);
    }

    private static List<StrategyPlan> ApplyMainVariantFilter(IEnumerable<StrategyPlan> plans, bool mainVariantsOnly)
        => mainVariantsOnly
            ? plans.Select(SelectMainVariantPlan).ToList()
            : plans.ToList();

    private static StrategyPlan SelectMainVariantPlan(StrategyPlan plan)
    {
        var mainVariant = ResolveMainVariant(plan);
        return plan with { Variants = [mainVariant] };
    }

    private static StrategyVariant ResolveMainVariant(StrategyPlan plan)
    {
        var defaultVariant = plan.Variants.FirstOrDefault(static variant => string.Equals(variant.Name, "default", StringComparison.OrdinalIgnoreCase))
            ?? plan.Variants[0];

        var promotedAlias = plan.Variants.FirstOrDefault(variant =>
            !string.Equals(variant.Name, "default", StringComparison.OrdinalIgnoreCase) &&
            variant.Factory == defaultVariant.Factory);

        return promotedAlias is null
            ? defaultVariant
            : defaultVariant with { Name = promotedAlias.Name };
    }

    private static IBacktestStrategy CreateConductVwapBb()
        => new ConductStrategyV3(new StrategyConfig
        {
            RiskPerTradeDollars = 10.0,
            CooldownBars = 2,
            RequireSupertrend = false,
            RequireMtfAlignment = true,
            RequireL2EntryConfirmation = true,
            StrictMissingDataChecks = true,
            IgnoreSelfLearningSetupBlockSources = ["CONDUCT_MAIN_PULLBACK"],
            MinPrice = 0.3,
            MaxPrice = 700.0,
            EntryWindows = [(570, 950)],
            MaxShares = 6_500,
            AdxThreshold = 13.0,
            RvolMin = 0.8,
            MaxMaDistAtr = 1.2,
            MainLongMaxBbPctB = 0.80,
            MainShortMinBbPctB = 0.20,
            MainEntryMaxVwapDeviationAtr = 0.0,
            VwapReversionEnabled = true,
            VwapStretchAtr = 1.2,
            BbBounceEnabled = true,
            BbEntryPctbLow = 0.10,
            BbEntryPctbHigh = 0.90,
            HardStopR = 1.0,
            BreakevenR = 0.8,
            TrailR = 0.35,
            GivebackPct = 0.30,
            Tp1R = 1.0,
            Tp1BreakevenBufferAtr = 0.05,
            Tp2R = 2.0,
            MaxHoldBars = 100,
        });

    private static IBacktestStrategy CreateConductCatamaran()
        => new ConductStrategyV3(new StrategyConfig
        {
            RiskPerTradeDollars = 10.0,
            MaxPositionNotionalPctOfAccount = 0.25,
            MaxShares = 8_000,
            CooldownBars = 1,
            RequireSupertrend = false,
            RequireMtfAlignment = true,
            StrictMissingDataChecks = true,
            IgnoreSelfLearningSetupBlockSources = ["CONDUCT_MAIN_PULLBACK"],
            MinPrice = 0.3,
            MaxPrice = 700.0,
            EntryWindows = [(570, 950)],
            AdxThreshold = 13.0,
            RvolMin = 1.0,
            MaxMaDistAtr = 1.0,
            MainLongMaxBbPctB = 0.80,
            MainShortMinBbPctB = 0.20,
            MainEntryMaxVwapDeviationAtr = 0.50,
            PullbackRvolMin = 1.20,
            PullbackReentryCooldownBars = 0,
            VwapReversionEnabled = true,
            AllowAlternateEntriesAfterRejectedMainCandidates = true,
            VwapStretchAtr = 1.1,
            AlternateVwapLongMinRiskPerShare = 0.03,
            BbBounceEnabled = true,
            BbEntryPctbLow = 0.05,
            BbEntryPctbHigh = 0.95,
            HardStopR = 1.0,
            BreakevenR = 0.8,
            TrailR = 0.35,
            GivebackPct = 0.30,
            Tp1R = 1.0,
            Tp1BreakevenBufferAtr = 0.05,
            Tp2R = 2.0,
            MaxHoldBars = 100,
        });

    private static List<StrategyPlan> BuildPlans()
    {
        return
        [
            new StrategyPlan(
                "V1-First",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV1()),
                    new StrategyVariant("mid-trades", () => new StrategyV1(new StrategyConfig
                    {
                        TrailR = 1.3,
                        GivebackPct = 0.60,
                        Tp1R = 1.4,
                        Tp2R = 2.6,
                        HardStopR = 1.1,
                        BreakevenR = 0.8,
                        RvolMin = 0.85,
                        AdxThreshold = 14.0,
                        RsiLongRange = (30.0, 75.0),
                        RsiShortRange = (25.0, 70.0),
                        RequireSupertrend = true,
                        MaxMaDistAtr = 0.7,
                        RiskPerTradeDollars = 45.0,
                        AccountSize = 25_000.0,
                        UseNotionalGivebackCap = true,
                        GivebackPctOfNotional = 0.01,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("balanced-trades", () => new StrategyV1(new StrategyConfig
                    {
                        TrailR = 1.2,
                        GivebackPct = 0.55,
                        Tp1R = 1.2,
                        Tp2R = 2.2,
                        HardStopR = 1.0,
                        BreakevenR = 0.7,
                        RvolMin = 0.5,
                        AdxThreshold = 10.0,
                        RsiLongRange = (25.0, 80.0),
                        RsiShortRange = (20.0, 75.0),
                        RequireSupertrend = false,
                        MaxMaDistAtr = 1.0,
                        RiskPerTradeDollars = 40.0,
                        AccountSize = 25_000.0,
                        UseNotionalGivebackCap = true,
                        GivebackPctOfNotional = 0.01,
                        GivebackUsdCap = 30.0,
                    }))
                ]),

            // V2 "ApexFlow": trend-pullback continuation with order-flow confirmation.
            // Complete refactor of the legacy worst-performer; designed to beat V16.
            new StrategyPlan(
                "V2-Conduct",
                25_000.0,
                [
                    // Default throughput profile ("flow") â€” THROUGHPUT REFACTOR (2026-06-05).
                    // The five single-factor veto gates (EMA50 / Supertrend / DI / HTF / order-flow) are
                    // folded into the confluence score instead of hard-blocking, the per-day cap is lifted
                    // (6â†’14), cooldown shortened (2â†’1) and the session window widened. Goal: clear the
                    // 50-trade floor on the basket while the 4/5 confluence floor + elite exit cascade keep
                    // quality. "Defend the yard, but don't get lost outside it."
                    new StrategyVariant("flow", () => new StrategyV2(new V2Config())),

                    // Quality reference: the prior strict gate stack (confluence 6/7, all single-factor
                    // requirements ON, per-day cap 6). Retained for transparency / A-B comparison.
                    new StrategyVariant("quality", () => new StrategyV2(new V2Config
                    {
                        MinConfluenceScore = 6,
                        ShortMinConfluenceScore = 7,
                        MaxSignalsPerDay = 6,
                        CooldownBars = 2,
                        RvolMin = 0.95,
                        L2LiquidityMin = 15.0,
                        SpreadZMax = 2.5,
                        AdxMin = 14.0,
                        AdxMax = 55.0,
                        RequireEma50Alignment = true,
                        RequireSupertrendAlignment = true,
                        RequireDiAlignment = true,
                        RequireHtfBias = true,
                        RequireOrderFlowConfirmation = true,
                        PullbackMaxDistAtr = 0.90,
                        PullbackMinDepthAtr = 0.38,
                        MaxExtensionAtr = 1.6,
                        SkipFirstNMinutes = 8,
                        LastEntryMinuteBeforeClose = 30,
                        EntryWindows = [(576, 945)],
                    })),

                    // Selective: the ultra-high-PF gem (rare, very high per-trade edge). Reference profile.
                    new StrategyVariant("selective", () => new StrategyV2(new V2Config
                    {
                        MinConfluenceScore = 6,
                        ShortMinConfluenceScore = 7,
                        MaxSignalsPerDay = 2,
                        CooldownBars = 10,
                        RvolMin = 1.05,
                        RequireEma50Alignment = true,
                        RequireSupertrendAlignment = true,
                        RequireDiAlignment = true,
                        RequireHtfBias = true,
                        RequireOrderFlowConfirmation = true,
                        PullbackMaxDistAtr = 0.75,
                        PullbackMinDepthAtr = 0.45,
                        AdxMin = 16.0,
                        SkipFirstNMinutes = 20,
                        LastEntryMinuteBeforeClose = 90,
                        EntryWindows = [(590, 900)],
                        RiskPerTradeDollars = 26.0,
                    })),

                    // Flow + premium tight-base breakouts (reference; breakouts are roughly break-even on
                    // this basket so they are OFF by default, retained here for transparency).
                    new StrategyVariant("flow-breakout", () => new StrategyV2(new V2Config
                    {
                        UseMomentumBreakout = true,
                        BreakoutMinRvol = 1.30,
                        BreakoutMaxBaseAtr = 2.4,
                        BreakoutMaxExtensionAtr = 0.70,
                        BreakoutMinConfluenceScore = 7,
                    })),
                ]),

            new StrategyPlan(
                "Conduct-V3",
                25_000.0,
                [
                    new StrategyVariant("default", CreateConductCatamaran),
                    new StrategyVariant("vwap-bb", CreateConductVwapBb),
                    new StrategyVariant("catamaran", CreateConductCatamaran),
                ]),

            new StrategyPlan(
                "V10-Hybrid",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV10()),
                    new StrategyVariant("balanced", () => new StrategyV10(new V10Config
                    {
                        RiskPerTradeDollars = 30.0,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        L2LiquidityMin = 14.0,
                        SpreadZMax = 2.8,
                        RvolMin = 0.5,
                        VolAccelMin = -0.40,
                        VwapStretchAtr = 1.0,
                        BbEntryPctbLow = 0.15,
                        BbEntryPctbHigh = 0.85,
                        MinEntryScore = 3,
                        MaxEntriesPerDirectionPerDay = 3,
                        HardStopR = 0.75,
                        BreakevenR = 0.55,
                        TrailR = 0.35,
                        GivebackPct = 0.30,
                        Tp1R = 1.60,
                        Tp2R = 3.00,
                        MaxHoldBars = 45,
                        EntryWindows = [(575, 705), (780, 955)],
                        AccountSize = 25_000.0,
                    })),
                    new StrategyVariant("active", () => new StrategyV10(new V10Config
                    {
                        RiskPerTradeDollars = 28.0,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        L2LiquidityMin = 12.0,
                        SpreadZMax = 2.8,
                        RvolMin = 0.45,
                        VolAccelMin = -0.50,
                        VwapStretchAtr = 0.85,
                        BbEntryPctbLow = 0.20,
                        BbEntryPctbHigh = 0.80,
                        MinOrBreakDistanceAtr = 0.0,
                        MinOfiForOrb = 0.02,
                        MinEntryScore = 2,
                        MaxEntriesPerDirectionPerDay = 3,
                        HardStopR = 0.90,
                        BreakevenR = 0.40,
                        TrailR = 0.35,
                        GivebackPct = 0.30,
                        Tp1R = 0.85,
                        Tp2R = 1.50,
                        MaxHoldBars = 35,
                        EntryWindows = [(570, 960)],
                        RequireHtfBias = false,
                        AccountSize = 25_000.0,
                    })),
                    new StrategyVariant("defensive", () => new StrategyV10(new V10Config
                    {
                        RiskPerTradeDollars = 25.0,
                        L2LiquidityMin = 20.0,
                        SpreadZMax = 2.2,
                        RvolMin = 0.8,
                        VolAccelMin = -0.10,
                        MinEntryScore = 5,
                        MaxEntriesPerDirectionPerDay = 1,
                        HardStopR = 0.85,
                        BreakevenR = 0.35,
                        TrailR = 0.30,
                        GivebackPct = 0.25,
                        Tp1R = 0.80,
                        Tp2R = 1.40,
                        MaxHoldBars = 35,
                        EntryWindows = [(585, 690), (810, 930)],
                        AccountSize = 25_000.0,
                    })),
                    new StrategyVariant("small-cap", () => new StrategyV10(new V10Config
                    {
                        RiskPerTradeDollars = 25.0,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        L2LiquidityMin = 0.0,
                        SpreadZMax = 10.0,
                        RvolMin = 0.0,
                        VolAccelMin = -1.0,
                        VwapStretchAtr = 2.0,
                        BbEntryPctbLow = 0.10,
                        BbEntryPctbHigh = 0.90,
                        MinOrBreakDistanceAtr = 0.0,
                        MinOfiForOrb = 0.0,
                        MinEntryScore = 1,
                        MaxEntriesPerDirectionPerDay = 5,
                        RequireHtfBias = false,
                        HardStopR = 0.80,
                        BreakevenR = 0.60,
                        TrailR = 0.35,
                        GivebackPct = 0.35,
                        Tp1R = 1.60,
                        Tp2R = 3.00,
                        MaxHoldBars = 50,
                        EntryWindows = [(570, 960)],
                        AccountSize = 25_000.0,
                    }))
                ]),
            new StrategyPlan(
                "V11",
                25_000.0,
                [
                    new StrategyVariant("silver-pearl", CreateV11SilverPearl),
                    new StrategyVariant("pink-pearl", CreateV11PinkPearl),
                    new StrategyVariant("default", CreateV11SilverPearl),
                    new StrategyVariant("balanced", () => new StrategyV11(new V11Config
                    {
                        RiskPerTradeDollars = 30.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6500,
                        CooldownBars = 5,
                        RvolMin = 0.95,
                        L2LiquidityMin = 24.0,
                        SpreadZMax = 1.9,
                        VolAccelMin = -0.10,
                        BbLongThreshold = 0.10,
                        BbShortThreshold = 0.90,
                        VwapDeviationAtr = 0.70,
                        RsiLongMax = 37.0,
                        RsiShortMin = 63.0,
                        AdxMin = 12.0,
                        AdxMax = 34.0,
                        MinScore = 5,
                        HardStopR = 0.82,
                        BreakevenR = 0.40,
                        TrailR = 0.25,
                        GivebackPct = 0.20,
                        Tp1R = 0.60,
                        Tp2R = 1.10,
                        MaxHoldBars = 22,
                        UseFixedGivebackUsdCap = true,
                        UseVariableGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    })),
                    new StrategyVariant("defensive", () => new StrategyV11(new V11Config
                    {
                        RiskPerTradeDollars = 20.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6500,
                        RvolMin = 0.90,
                        L2LiquidityMin = 24.0,
                        SpreadZMax = 2.0,
                        VolAccelMin = -0.15,
                        BbLongThreshold = 0.14,
                        BbShortThreshold = 0.86,
                        VwapDeviationAtr = 0.50,
                        RsiLongMax = 38.0,
                        RsiShortMin = 62.0,
                        AdxMin = 10.0,
                        AdxMax = 38.0,
                        MinScore = 5,
                        HardStopR = 0.80,
                        BreakevenR = 0.45,
                        TrailR = 0.28,
                        GivebackPct = 0.22,
                        Tp1R = 0.70,
                        Tp2R = 1.20,
                        MaxHoldBars = 26,
                        UseFixedGivebackUsdCap = true,
                        UseVariableGivebackUsdCap = true,
                        GivebackUsdCap = 30.0,
                    }))
                ]),

            new StrategyPlan(
                "V13",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV13(new V13Config
                    {
                        DiagnosticsLabel = "default",
                        RiskPerTradeDollars = 24.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6_500,
                        MaxSignalsPerDay = 4,
                        CooldownBars = 8,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.85,
                        BreakoutRvolMin = 0.95,
                        L2LiquidityMin = 18.0,
                        SpreadZMax = 2.0,
                        MinVolAccel = -0.10,
                        OpeningRangeMinutes = 15,
                        AllowOpeningRangeFallback = true,
                        OpeningRangeFallbackLatestMinute = 645,
                        OpeningRangeFallbackMinBars = 5,
                        SkipFirstNMinutes = 12,
                        EntryWindows =
                        [
                            (582, 940),
                        ],
                        BreakoutConfirmAtr = 0.08,
                        PullbackMinBars = 2,
                        PullbackMaxBars = 12,
                        PullbackTouchAtr = 0.16,
                        VwapTouchAtr = 0.20,
                        MaxExtensionFromEma9Atr = 0.70,
                        SwingLookback = 6,
                        AdxMin = 12.0,
                        AdxMax = 52.0,
                        AllowSoftAdxPass = true,
                        AdxSoftTolerance = 4.0,
                        RequireHtfBias = true,
                        UseDirectionalStrengthHtfOverride = true,
                        AllowWeakCounterTrendHtf = true,
                        AllowStrongCounterTrendHtf = true,
                        WeakCounterTrendHtfScoreMin = 3,
                        StrongCounterTrendHtfScoreMin = 4,
                        RequireMtfAlign = false,
                        RsiLongMin = 39.0,
                        RsiLongMax = 72.0,
                        RsiShortMin = 28.0,
                        RsiShortMax = 60.0,
                        AllowAdaptiveRsi = true,
                        AdaptiveRsiTolerance = 6.0,
                        HardStopR = 0.65,
                        BreakevenR = 0.50,
                        TrailR = 0.30,
                        GivebackPct = 0.25,
                        GivebackUsdCap = 20.0,
                        Tp1R = 1.40,
                        Tp2R = 2.60,
                        MaxHoldBars = 30,
                    })),
                    new StrategyVariant("high-winrate", () => new StrategyV13(new V13Config
                    {
                        DiagnosticsLabel = "high-winrate",
                        RiskPerTradeDollars = 22.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6_500,
                        MaxSignalsPerDay = 2,
                        CooldownBars = 12,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.95,
                        BreakoutRvolMin = 1.05,
                        L2LiquidityMin = 20.0,
                        SpreadZMax = 1.9,
                        MinVolAccel = 0.00,
                        OpeningRangeMinutes = 15,
                        AllowOpeningRangeFallback = true,
                        OpeningRangeFallbackLatestMinute = 635,
                        OpeningRangeFallbackMinBars = 6,
                        SkipFirstNMinutes = 12,
                        EntryWindows =
                        [
                            (582, 935),
                        ],
                        BreakoutConfirmAtr = 0.10,
                        PullbackMinBars = 2,
                        PullbackMaxBars = 9,
                        PullbackTouchAtr = 0.12,
                        VwapTouchAtr = 0.15,
                        MaxExtensionFromEma9Atr = 0.55,
                        SwingLookback = 5,
                        AdxMin = 12.0,
                        AdxMax = 52.0,
                        AllowSoftAdxPass = true,
                        AdxSoftTolerance = 3.0,
                        RequireHtfBias = true,
                        UseDirectionalStrengthHtfOverride = true,
                        AllowWeakCounterTrendHtf = true,
                        AllowStrongCounterTrendHtf = true,
                        WeakCounterTrendHtfScoreMin = 4,
                        StrongCounterTrendHtfScoreMin = 5,
                        RequireMtfAlign = false,
                        RsiLongMin = 40.0,
                        RsiLongMax = 68.0,
                        RsiShortMin = 31.0,
                        RsiShortMax = 58.0,
                        AllowAdaptiveRsi = true,
                        AdaptiveRsiTolerance = 5.0,
                        HardStopR = 0.72,
                        BreakevenR = 0.34,
                        TrailR = 0.20,
                        GivebackPct = 0.16,
                        GivebackUsdCap = 20.0,
                        Tp1R = 0.45,
                        Tp2R = 0.90,
                        MaxHoldBars = 16,
                    })),
                    new StrategyVariant("balanced", () => new StrategyV13(new V13Config
                    {
                        DiagnosticsLabel = "balanced",
                        RiskPerTradeDollars = 26.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6_500,
                        MaxSignalsPerDay = 2,
                        CooldownBars = 10,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.85,
                        BreakoutRvolMin = 1.00,
                        L2LiquidityMin = 18.0,
                        SpreadZMax = 2.1,
                        MinVolAccel = -0.10,
                        OpeningRangeMinutes = 15,
                        AllowOpeningRangeFallback = true,
                        OpeningRangeFallbackLatestMinute = 645,
                        OpeningRangeFallbackMinBars = 5,
                        SkipFirstNMinutes = 12,
                        EntryWindows =
                        [
                            (582, 945),
                        ],
                        BreakoutConfirmAtr = 0.10,
                        PullbackMinBars = 2,
                        PullbackMaxBars = 10,
                        PullbackTouchAtr = 0.14,
                        VwapTouchAtr = 0.18,
                        MaxExtensionFromEma9Atr = 0.60,
                        SwingLookback = 7,
                        AdxMin = 12.0,
                        AdxMax = 52.0,
                        AllowSoftAdxPass = true,
                        AdxSoftTolerance = 4.0,
                        RequireHtfBias = true,
                        UseDirectionalStrengthHtfOverride = true,
                        AllowWeakCounterTrendHtf = true,
                        AllowStrongCounterTrendHtf = true,
                        WeakCounterTrendHtfScoreMin = 3,
                        StrongCounterTrendHtfScoreMin = 4,
                        RequireMtfAlign = false,
                        RsiLongMin = 39.0,
                        RsiLongMax = 72.0,
                        RsiShortMin = 28.0,
                        RsiShortMax = 60.0,
                        AllowAdaptiveRsi = true,
                        AdaptiveRsiTolerance = 6.0,
                        HardStopR = 0.85,
                        BreakevenR = 0.42,
                        TrailR = 0.26,
                        GivebackPct = 0.20,
                        GivebackUsdCap = 26.0,
                        Tp1R = 0.60,
                        Tp2R = 1.15,
                        MaxHoldBars = 22,
                    })),
                    new StrategyVariant("small-cap", () => new StrategyV13(new V13Config
                    {
                        DiagnosticsLabel = "small-cap",
                        RiskPerTradeDollars = 20.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6_500,
                        MaxSignalsPerDay = 6,
                        CooldownBars = 5,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.0,
                        BreakoutRvolMin = 0.0,
                        L2LiquidityMin = 0.0,
                        SpreadZMax = 10.0,
                        MinVolAccel = -1.0,
                        OpeningRangeMinutes = 10,
                        AllowOpeningRangeFallback = true,
                        OpeningRangeFallbackLatestMinute = 660,
                        OpeningRangeFallbackMinBars = 3,
                        SkipFirstNMinutes = 5,
                        EntryWindows =
                        [
                            (570, 960),
                        ],
                        BreakoutConfirmAtr = 0.04,
                        PullbackMinBars = 1,
                        PullbackMaxBars = 20,
                        PullbackTouchAtr = 0.30,
                        VwapTouchAtr = 0.40,
                        MaxExtensionFromEma9Atr = 1.50,
                        SwingLookback = 10,
                        AdxMin = 8.0,
                        AdxMax = 80.0,
                        AllowSoftAdxPass = true,
                        AdxSoftTolerance = 8.0,
                        RequireHtfBias = false,
                        UseDirectionalStrengthHtfOverride = false,
                        AllowWeakCounterTrendHtf = true,
                        AllowStrongCounterTrendHtf = true,
                        WeakCounterTrendHtfScoreMin = 1,
                        StrongCounterTrendHtfScoreMin = 2,
                        RequireMtfAlign = false,
                        RsiLongMin = 20.0,
                        RsiLongMax = 80.0,
                        RsiShortMin = 20.0,
                        RsiShortMax = 80.0,
                        AllowAdaptiveRsi = true,
                        AdaptiveRsiTolerance = 12.0,
                        HardStopR = 0.75,
                        BreakevenR = 0.55,
                        TrailR = 0.30,
                        GivebackPct = 0.30,
                        GivebackUsdCap = 25.0,
                        Tp1R = 1.50,
                        Tp2R = 2.80,
                        MaxHoldBars = 35,
                    })),
                    new StrategyVariant("tight-r", () => new StrategyV13(new V13Config
                    {
                        DiagnosticsLabel = "tight-r",
                        RiskPerTradeDollars = 24.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6_500,
                        MaxSignalsPerDay = 3,
                        CooldownBars = 10,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.80,
                        BreakoutRvolMin = 0.90,
                        L2LiquidityMin = 15.0,
                        SpreadZMax = 2.5,
                        MinVolAccel = -0.15,
                        OpeningRangeMinutes = 15,
                        AllowOpeningRangeFallback = true,
                        OpeningRangeFallbackLatestMinute = 645,
                        OpeningRangeFallbackMinBars = 5,
                        SkipFirstNMinutes = 12,
                        EntryWindows = [(582, 940)],
                        BreakoutConfirmAtr = 0.08,
                        PullbackMinBars = 2,
                        PullbackMaxBars = 12,
                        PullbackTouchAtr = 0.16,
                        VwapTouchAtr = 0.20,
                        MaxExtensionFromEma9Atr = 0.70,
                        SwingLookback = 6,
                        AdxMin = 12.0,
                        AdxMax = 52.0,
                        AllowSoftAdxPass = true,
                        AdxSoftTolerance = 4.0,
                        RequireHtfBias = true,
                        UseDirectionalStrengthHtfOverride = true,
                        AllowWeakCounterTrendHtf = true,
                        AllowStrongCounterTrendHtf = true,
                        WeakCounterTrendHtfScoreMin = 3,
                        StrongCounterTrendHtfScoreMin = 4,
                        RequireMtfAlign = false,
                        RsiLongMin = 39.0,
                        RsiLongMax = 72.0,
                        RsiShortMin = 28.0,
                        RsiShortMax = 60.0,
                        AllowAdaptiveRsi = true,
                        AdaptiveRsiTolerance = 6.0,
                        HardStopR = 0.55,
                        BreakevenR = 0.50,
                        TrailR = 0.28,
                        GivebackPct = 0.25,
                        GivebackUsdCap = 28.0,
                        Tp1R = 2.00,
                        Tp2R = 4.00,
                        MaxHoldBars = 40,
                        Tp1TightenToBe = true,
                        PeakGivebackKeepFraction = 0.45,
                        PeakGivebackActivateR = 0.70,
                        StagnationBars = 8,
                        StagnationMinPeakR = 0.30,
                        StagnationMaxAdverseR = -0.15,
                        RequireL2EntryFilter = true,
                        L2OfiMinLong = -0.10,
                        L2OfiMaxShort = 0.10,
                        L2ImbalanceMinLong = 0.70,
                        L2ImbalanceMaxShort = 1.40,
                        UsePriceTierMicroTrail = true,
                        UsePriceTierStopFloor = true,
                        UseMaExtensionL2Flip = true,
                        MaExtensionMinR = 0.30,
                        MaExtensionAtrThreshold = 1.50,
                    })),
                    new StrategyVariant("runner", () => new StrategyV13(new V13Config
                    {
                        DiagnosticsLabel = "runner",
                        RiskPerTradeDollars = 24.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MaxShares = 6_500,
                        MaxSignalsPerDay = 3,
                        CooldownBars = 10,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.80,
                        BreakoutRvolMin = 0.90,
                        L2LiquidityMin = 15.0,
                        SpreadZMax = 2.5,
                        MinVolAccel = -0.15,
                        OpeningRangeMinutes = 15,
                        AllowOpeningRangeFallback = true,
                        OpeningRangeFallbackLatestMinute = 645,
                        OpeningRangeFallbackMinBars = 5,
                        SkipFirstNMinutes = 12,
                        EntryWindows = [(582, 940)],
                        BreakoutConfirmAtr = 0.08,
                        PullbackMinBars = 2,
                        PullbackMaxBars = 12,
                        PullbackTouchAtr = 0.16,
                        VwapTouchAtr = 0.20,
                        MaxExtensionFromEma9Atr = 0.70,
                        SwingLookback = 6,
                        AdxMin = 12.0,
                        AdxMax = 52.0,
                        AllowSoftAdxPass = true,
                        AdxSoftTolerance = 4.0,
                        RequireHtfBias = true,
                        UseDirectionalStrengthHtfOverride = true,
                        AllowWeakCounterTrendHtf = true,
                        AllowStrongCounterTrendHtf = true,
                        WeakCounterTrendHtfScoreMin = 3,
                        StrongCounterTrendHtfScoreMin = 4,
                        RequireMtfAlign = false,
                        RsiLongMin = 39.0,
                        RsiLongMax = 72.0,
                        RsiShortMin = 28.0,
                        RsiShortMax = 60.0,
                        AllowAdaptiveRsi = true,
                        AdaptiveRsiTolerance = 6.0,
                        HardStopR = 0.60,
                        BreakevenR = 0.55,
                        TrailR = 0.25,
                        GivebackPct = 0.30,
                        GivebackUsdCap = 35.0,
                        Tp1R = 2.50,
                        Tp2R = 5.00,
                        MaxHoldBars = 60,
                        Tp1TightenToBe = false,
                        PeakGivebackKeepFraction = 0.35,
                        PeakGivebackActivateR = 1.00,
                        StagnationBars = 12,
                        StagnationMinPeakR = 0.40,
                        StagnationMaxAdverseR = -0.20,
                        RequireL2EntryFilter = true,
                        L2OfiMinLong = -0.10,
                        L2OfiMaxShort = 0.10,
                        L2ImbalanceMinLong = 0.70,
                        L2ImbalanceMaxShort = 1.40,
                        UsePriceTierMicroTrail = true,
                        UsePriceTierStopFloor = true,
                        UseMaExtensionL2Flip = true,
                        MaExtensionMinR = 0.40,
                        MaExtensionAtrThreshold = 1.80,
                    }))
                ]),

            new StrategyPlan(
                "V12",
                25_000.0,
                [
                    new StrategyVariant("default", () => new StrategyV12()),
                    new StrategyVariant("selective", () => new StrategyV12(new V12Config
                    {
                        MinConfluenceMomentum = 7,
                        MinConfluenceReversion = 8,
                        MinConfluenceContinuation = 7,
                        RvolMin = 0.90,
                        MaxSignalsPerDay = 2,
                        CooldownBars = 10,
                        AllowCounterTrendWithHighConfluence = false,
                        MomentumHardStopR = 0.75,
                        MomentumTp1R = 1.40,
                        MomentumTp2R = 2.60,
                        MomentumMaxHoldBars = 35,
                        ReversionHardStopR = 0.55,
                        ReversionTp1R = 1.20,
                        ReversionTp2R = 2.20,
                        ReversionMaxHoldBars = 25,
                        ContinuationHardStopR = 0.65,
                        ContinuationTp1R = 1.30,
                        ContinuationTp2R = 2.40,
                        ContinuationMaxHoldBars = 30,
                    })),
                    new StrategyVariant("wide", () => new StrategyV12(new V12Config
                    {
                        MinConfluenceMomentum = 5,
                        MinConfluenceReversion = 5,
                        MinConfluenceContinuation = 5,
                        RvolMin = 0.65,
                        MaxSignalsPerDay = 5,
                        CooldownBars = 5,
                        AdxMin = 12.0,
                        AdxMax = 55.0,
                        RsiLongMin = 35.0,
                        RsiLongMax = 75.0,
                        RsiShortMin = 25.0,
                        RsiShortMax = 65.0,
                    })),
                    new StrategyVariant("small-cap", () => new StrategyV12(new V12Config
                    {
                        MinConfluenceMomentum = 3,
                        MinConfluenceReversion = 3,
                        MinConfluenceContinuation = 3,
                        RvolMin = 0.0,
                        L2LiquidityMin = 0.0,
                        SpreadZMax = 10.0,
                        MinVolAccel = -1.0,
                        MaxSignalsPerDay = 8,
                        CooldownBars = 3,
                        AdxMin = 8.0,
                        AdxMax = 80.0,
                        RsiLongMin = 25.0,
                        RsiLongMax = 80.0,
                        RsiShortMin = 20.0,
                        RsiShortMax = 75.0,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        EntryWindows = [(570, 960)],
                        RequireHtfBias = false,
                        AllowCounterTrendWithHighConfluence = true,
                        CounterTrendMinConfluence = 4,
                        // Tuned SL/TP for small-cap R:R
                        MomentumHardStopR = 0.75,
                        MomentumTp1R = 1.40,
                        MomentumTp2R = 2.60,
                        MomentumMaxHoldBars = 35,
                        ReversionHardStopR = 0.55,
                        ReversionTp1R = 1.20,
                        ReversionTp2R = 2.20,
                        ReversionMaxHoldBars = 25,
                        ContinuationHardStopR = 0.65,
                        ContinuationTp1R = 1.30,
                        ContinuationTp2R = 2.40,
                        ContinuationMaxHoldBars = 30,
                    })),
                ]),

            // =========================================================
            //  V14 â€” Small-Cap / Micro-Cap Mean-Reversion Specialist
            // =========================================================
            new StrategyPlan(
                "V14-SmallCap",
                25_000.0,
                [
                    new StrategyVariant("baseline", () => new StrategyV14()),
                ]),

            // =========================================================
            //  V15 â€” Retained small-cap short profile
            //  Keeps only breakdown continuation by default and leaves the
            //  long VWAP branch opt-in only.
            // =========================================================
            new StrategyPlan(
                "V15-ShortCap",
                25_000.0,
                [
                    new StrategyVariant("retained-breakdown", () => new StrategyV15(new V15Config
                    {
                        AllowLong = true,
                        AllowShort = true,
                        MaxSignalsPerDay = 3,
                        CooldownBars = 6,
                        ShortEarliestMinuteEt = 610,
                        VwapRevLongEnabled = true,
                        VwapRevLongMinConfluence = 5,
                        LongVolumeSpikeRvol = 1.8,
                        BreakdownMinConfirm = 3,
                    })),
                    new StrategyVariant("red-pearl-v2", () => new StrategyV15(new V15Config
                    {
                        AllowLong = true,
                        AllowShort = true,
                        MaxSignalsPerDay = 4,
                        CooldownBars = 4,
                        ShortEarliestMinuteEt = 610,
                        VwapRevLongEnabled = true,
                        VwapRevLongMinConfluence = 5,
                        LongVolumeSpikeRvol = 1.6,
                        BreakdownMinConfirm = 3,
                        UseCandidateScoring = true,
                        SqueezeRecoveryLongEnabled = true,
                        SqueezeMinBars = 2,
                        SqueezeRecoveryMinConfirm = 2,
                        UseL2DirectionalGate = false,
                        UseL2DwmpGate = false,
                        UseL1LastVsMidGate = false,
                        L1SizeRatioMinForLong = 1.02,
                        L2ImbalanceMinForLong = 1.00,
                        L2DeepImbalanceMinForLong = 1.05,
                    })),
                ]),

                // â”€â”€ V16 "Squeeze Breakout" â€” BB/KC squeeze-release + multi-oscillator confluence â”€â”€
                new StrategyPlan("V16-SqzBreakout", 25_000.0,
                [
                    new StrategyVariant("default", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus-time-context-adaptive"))),
                    new StrategyVariant("aggressive", () => new Strategies.StrategyV16(new Strategies.V16Config
                    {
                        DiagnosticsLabel = "aggressive",
                        MinConfluenceScore = 3,
                        SqueezeMinBars = 3,
                        SqueezeReleaseMaxBars = 4,
                        RequireHtfBias = false,
                        RequireL2EntryFilter = false,
                        MaxSignalsPerDay = 5,
                        CooldownBars = 6,
                        AdxMin = 10.0,
                        RsiLongMin = 35.0,
                        RsiLongMax = 78.0,
                        RsiShortMin = 22.0,
                        RsiShortMax = 65.0,
                        StochLongMin = 15.0,
                        StochShortMax = 85.0,
                        SqueezeBandwidthMaxPctile = 0.50,
                    })),
                    new StrategyVariant("floor-balanced", () => new Strategies.StrategyV16(new Strategies.V16Config
                    {
                        DiagnosticsLabel = "floor-balanced",
                        MinConfluenceScore = 3,
                        SqueezeMinBars = 2,
                        SqueezeLookback = 18,
                        SqueezeReleaseMaxBars = 5,
                        SqueezeBandwidthMaxPctile = 0.55,
                        RequireHtfBias = false,
                        AllowWeakCounterTrendHtf = true,
                        RequireL2EntryFilter = false,
                        MaxSignalsPerDay = 6,
                        CooldownBars = 4,
                        RvolMin = 0.55,
                        L2LiquidityMin = 0.0,
                        SpreadZMax = 4.0,
                        MinVolAccel = -0.30,
                        AdxMin = 8.0,
                        HardStopR = 0.65,
                        BreakevenR = 0.35,
                        TrailR = 0.18,
                        Tp1R = 1.20,
                        Tp2R = 2.00,
                        MaxHoldBars = 30,
                    })),
                    new StrategyVariant("floor-balanced-plus", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus"))),
                    new StrategyVariant("floor-balanced-plus-time-context-adaptive", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus-time-context-adaptive"))),
                    new StrategyVariant("floor-balanced-plus-depth-adaptive", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus-depth-adaptive"))),
                    new StrategyVariant("floor-balanced-plus-vol-normalized", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus-vol-normalized"))),
                    new StrategyVariant("floor-balanced-plus-symbol-context-adaptive", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus-symbol-context-adaptive"))),
                    new StrategyVariant("floor-balanced-plus-hardstop-preempt", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus-hardstop-preempt"))),
                    new StrategyVariant("floor-balanced-plus-loss20", () => new Strategies.StrategyV16(Strategies.StrategyV16VariantFactory.CreateVariant("floor-balanced-plus-loss20"))),
                    new StrategyVariant("conservative", () => new Strategies.StrategyV16(new Strategies.V16Config
                    {
                        DiagnosticsLabel = "conservative",
                        MinConfluenceScore = 4,
                        SqueezeMinBars = 3,
                        SqueezeReleaseMaxBars = 4,
                        RequireHtfBias = false,
                        AllowWeakCounterTrendHtf = true,
                        RequireL2EntryFilter = false,
                        MaxSignalsPerDay = 4,
                        CooldownBars = 6,
                        RvolMin = 0.70,
                        L2LiquidityMin = 8.0,
                        SpreadZMax = 3.0,
                        HardStopR = 0.60,
                        BreakevenR = 0.40,
                        TrailR = 0.22,
                        Tp1R = 1.30,
                        SqueezeBandwidthMaxPctile = 0.45,
                    })),
                    new StrategyVariant("small-cap", () => new Strategies.StrategyV16(new Strategies.V16Config
                    {
                        DiagnosticsLabel = "small-cap",
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        MinConfluenceScore = 3,
                        SqueezeMinBars = 3,
                        SqueezeLookback = 18,
                        SqueezeReleaseMaxBars = 4,
                        SqueezeBandwidthMaxPctile = 0.50,
                        RvolMin = 0.70,
                        L2LiquidityMin = 10.0,
                        SpreadZMax = 3.0,
                        MinVolAccel = -0.20,
                        RequireHtfBias = false,
                        RequireL2EntryFilter = false,
                        MaxSignalsPerDay = 4,
                        CooldownBars = 6,
                        AdxMin = 10.0,
                        HardStopR = 0.65,
                        GivebackUsdCap = 18.0,
                        MaxHoldBars = 35,
                    })),
                    // â”€â”€ Evolved strategies from Trade Evolution Analytics â”€â”€
                    new StrategyVariant("sqzbreak-fortress", () => new Strategies.StrategyV16(new Strategies.V16Config
                    {
                        DiagnosticsLabel = "sqzbreak-fortress",
                        HardStopR = 0.36,
                        BreakevenR = 0.35,
                        TrailR = 0.17,
                        GivebackPct = 0.12,
                        Tp1R = 1.5,
                        Tp2R = 2.4,
                        MaxHoldBars = 18,
                    })),
                    new StrategyVariant("sqzbreak-evolution", () => new Strategies.StrategyV16(new Strategies.V16Config
                    {
                        DiagnosticsLabel = "sqzbreak-evolution",
                        BreakevenR = 0.5,
                        TrailR = 0.15,
                        GivebackPct = 0.6,
                        Tp1R = 1.15,
                        Tp2R = 2.18,
                        MaxHoldBars = 45,
                    })),
                ]),
                new StrategyPlan("V17-HybridFlow", 25_000.0,
                [
                    new StrategyVariant("legacy-default", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "legacy-default",
                        RiskPerTradeDollars = 15.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 4.0,
                        L2LiquidityMin = 0.0,
                        RvolMin = 0.40,
                        StrongRvolMin = 0.60,
                        MinNormalizedBarRangeAtr = 0.12,
                        MaxNormalizedBarRangeAtr = 3.00,
                        MinVolAccel = -0.30,
                        TrendScoreMin = 1,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.15,
                        NeutralDcDistancePct = 0.30,
                        RangeBreakoutConfirmAtr = -0.08,
                        PullbackToTrendAtr = 0.40,
                        LongVwapReclaimToleranceAtr = 0.22,
                        LongMaxBreakoutExtensionAtr = 0.95,
                        LongMaxVwapExtensionAtr = 1.20,
                        TrendEmaToleranceAtr = 0.22,
                        TrendDcMidToleranceAtr = 0.18,
                        L1L2ConfirmMinScore = 0,
                        ShortL1L2ConfirmMinScore = 2,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        SkipFirstNMinutes = 5,
                        LastEntryMinuteBeforeClose = 110,
                        EntryWindows = [(575, 905)],
                        CooldownBars = 3,
                        MaxSignalsPerDay = 10,
                        ShortEarliestMinuteEt = 585,
                        ShortMaxSignalsPerDay = 3,
                        ShortRiskPerTradeMultiplier = 0.22,
                        HardStopR = 1.00,
                        StopAtrMultiplierTrend = 1.00,
                        StopAtrMultiplierNeutral = 0.65,
                        Tp1R = 0.95,
                        Tp2R = 1.90,
                        MaxHoldBars = 22,
                        ShortMaxChaseBelowLowerBandAtr = 0.75,
                        EnableLongBreakoutSetup = true,
                        EnableLongOneTwoThreeSetup = true,
                        EnableLongBuySetup = true,
                    })),
                    new StrategyVariant("floor-active", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "floor-active",
                        RiskPerTradeDollars = 12.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 5.0,
                        L2LiquidityMin = 0.0,
                        RvolMin = 0.25,
                        StrongRvolMin = 0.45,
                        MinNormalizedBarRangeAtr = 0.08,
                        MaxNormalizedBarRangeAtr = 4.00,
                        MinVolAccel = -0.40,
                        TrendScoreMin = 1,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.18,
                        NeutralDcDistancePct = 0.35,
                        RangeBreakoutConfirmAtr = -0.12,
                        PullbackToTrendAtr = 0.50,
                        LongVwapReclaimToleranceAtr = 0.30,
                        LongMaxBreakoutExtensionAtr = 1.20,
                        LongMaxVwapExtensionAtr = 1.40,
                        TrendEmaToleranceAtr = 0.28,
                        TrendDcMidToleranceAtr = 0.24,
                        L1L2ConfirmMinScore = 0,
                        ShortL1L2ConfirmMinScore = 1,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        SkipFirstNMinutes = 0,
                        LastEntryMinuteBeforeClose = 120,
                        EntryWindows = [(570, 915)],
                        CooldownBars = 1,
                        MaxSignalsPerDay = 16,
                        ShortEarliestMinuteEt = 575,
                        ShortMaxSignalsPerDay = 5,
                        ShortRiskPerTradeMultiplier = 0.20,
                        HardStopR = 1.00,
                        StopAtrMultiplierTrend = 1.00,
                        StopAtrMultiplierNeutral = 0.65,
                        Tp1R = 0.90,
                        Tp2R = 1.80,
                        MaxHoldBars = 20,
                        ShortMaxChaseBelowLowerBandAtr = 0.85,
                        EnableLongBreakoutSetup = true,
                        EnableLongOneTwoThreeSetup = true,
                        EnableLongBuySetup = true,
                    })),
                    new StrategyVariant("floor-active-plus", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "floor-active-plus",
                        IgnoreSelfLearningSetupBlock = true,
                        RiskPerTradeDollars = 12.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 5.0,
                        L2LiquidityMin = 0.0,
                        RvolMin = 0.18,
                        StrongRvolMin = 0.35,
                        MinNormalizedBarRangeAtr = 0.05,
                        MaxNormalizedBarRangeAtr = 4.50,
                        MinVolAccel = -0.45,
                        TrendScoreMin = 1,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.20,
                        NeutralDcDistancePct = 0.38,
                        RangeBreakoutConfirmAtr = -0.18,
                        PullbackToTrendAtr = 0.60,
                        LongVwapReclaimToleranceAtr = 0.36,
                        LongMaxBreakoutExtensionAtr = 1.35,
                        LongMaxVwapExtensionAtr = 1.60,
                        TrendEmaToleranceAtr = 0.34,
                        TrendDcMidToleranceAtr = 0.28,
                        L1L2ConfirmMinScore = 0,
                        ShortL1L2ConfirmMinScore = 0,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        SkipFirstNMinutes = 0,
                        LastEntryMinuteBeforeClose = 45,
                        EntryWindows = [(570, 930)],
                        CooldownBars = 1,
                        MaxSignalsPerDay = 20,
                        ShortEarliestMinuteEt = 570,
                        ShortMaxSignalsPerDay = 6,
                        ShortRiskPerTradeMultiplier = 0.12,
                        HardStopR = 0.95,
                        StopAtrMultiplierTrend = 0.95,
                        StopAtrMultiplierNeutral = 0.60,
                        Tp1R = 0.85,
                        Tp2R = 1.70,
                        MaxHoldBars = 18,
                        ShortMaxChaseBelowLowerBandAtr = 1.00,
                        EnableLongBreakoutSetup = true,
                        EnableLongOneTwoThreeSetup = true,
                        EnableLongBuySetup = true,
                    })),
                    new StrategyVariant("floor-active-mid", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "floor-active-mid",
                        RiskPerTradeDollars = 12.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 4.8,
                        L2LiquidityMin = 0.0,
                        RvolMin = 0.20,
                        StrongRvolMin = 0.38,
                        MinNormalizedBarRangeAtr = 0.06,
                        MaxNormalizedBarRangeAtr = 4.20,
                        MinVolAccel = -0.35,
                        TrendScoreMin = 1,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.18,
                        NeutralDcDistancePct = 0.36,
                        RangeBreakoutConfirmAtr = -0.14,
                        PullbackToTrendAtr = 0.55,
                        LongVwapReclaimToleranceAtr = 0.32,
                        LongMaxBreakoutExtensionAtr = 1.30,
                        LongMaxVwapExtensionAtr = 1.50,
                        TrendEmaToleranceAtr = 0.30,
                        TrendDcMidToleranceAtr = 0.26,
                        L1L2ConfirmMinScore = 0,
                        ShortL1L2ConfirmMinScore = 1,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        SkipFirstNMinutes = 0,
                        LastEntryMinuteBeforeClose = 60,
                        EntryWindows = [(570, 915)],
                        CooldownBars = 1,
                        MaxSignalsPerDay = 18,
                        ShortEarliestMinuteEt = 570,
                        ShortMaxSignalsPerDay = 5,
                        ShortRiskPerTradeMultiplier = 0.14,
                        HardStopR = 0.98,
                        StopAtrMultiplierTrend = 0.98,
                        StopAtrMultiplierNeutral = 0.62,
                        Tp1R = 0.88,
                        Tp2R = 1.75,
                        MaxHoldBars = 18,
                        ShortMaxChaseBelowLowerBandAtr = 0.95,
                        EnableLongBreakoutSetup = true,
                        EnableLongOneTwoThreeSetup = true,
                        EnableLongBuySetup = true,
                    })),
                    new StrategyVariant("broad-balanced", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "broad-balanced",
                        RiskPerTradeDollars = 15.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 3.5,
                        L2LiquidityMin = 2.0,
                        RvolMin = 0.55,
                        StrongRvolMin = 0.80,
                        MinNormalizedBarRangeAtr = 0.16,
                        MaxNormalizedBarRangeAtr = 2.80,
                        MinVolAccel = -0.20,
                        TrendScoreMin = 2,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.12,
                        NeutralDcDistancePct = 0.25,
                        RangeBreakoutConfirmAtr = 0.00,
                        PullbackToTrendAtr = 0.35,
                        LongVwapReclaimToleranceAtr = 0.18,
                        LongMaxBreakoutExtensionAtr = 0.75,
                        LongMaxVwapExtensionAtr = 1.00,
                        TrendEmaToleranceAtr = 0.18,
                        TrendDcMidToleranceAtr = 0.15,
                        L1L2ConfirmMinScore = 1,
                        ShortL1L2ConfirmMinScore = 3,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        ShortRiskPerTradeMultiplier = 0.30,
                        SkipFirstNMinutes = 10,
                        LastEntryMinuteBeforeClose = 100,
                        EntryWindows = [(580, 900)],
                        CooldownBars = 5,
                        MaxSignalsPerDay = 8,
                        ShortEarliestMinuteEt = 590,
                        ShortMaxSignalsPerDay = 2,
                        HardStopR = 1.10,
                        StopAtrMultiplierTrend = 1.10,
                        StopAtrMultiplierNeutral = 0.70,
                        Tp1R = 1.05,
                        Tp2R = 2.10,
                        MaxHoldBars = 26,
                        ShortMaxChaseBelowLowerBandAtr = 0.60,
                        RequireLongAdxStrength = true,
                        LongMinAdx = 11.0,
                        LongMinPlusDiEdge = 0.75,
                        UseLongAdxCeiling = true,
                        LongMaxAdx = 40.0,
                        RequireLongRsiBand = true,
                        LongMinRsi14 = 44.0,
                        LongMaxRsi14 = 68.0,
                        RequireBullishBreakoutCandle = true,
                        LongBreakoutMinCloseLocationPct = 0.60,
                        LongBreakoutMaxUpperWickPct = 0.34,
                        RequireBullishReversalPullbackCandle = true,
                        EnableLongOneTwoThreeSetup = false,
                        EnableLongBuySetup = false,
                        UseBreakoutLongExitProfile = true,
                        LongLowAtrRiskMultiplier = 0.55,
                        LongLowAtrMaxPositionNotionalPctOfAccount = 0.09,
                        LongHighBandwidthThreshold = 0.052,
                        LongHighBandwidthRiskMultiplier = 0.65,
                    })),
                    new StrategyVariant("broad-balanced-short-lite", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "broad-balanced-short-lite",
                        RiskPerTradeDollars = 15.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 3.5,
                        L2LiquidityMin = 2.0,
                        RvolMin = 0.55,
                        StrongRvolMin = 0.80,
                        MinNormalizedBarRangeAtr = 0.16,
                        MaxNormalizedBarRangeAtr = 2.80,
                        MinVolAccel = -0.20,
                        TrendScoreMin = 2,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.12,
                        NeutralDcDistancePct = 0.25,
                        RangeBreakoutConfirmAtr = 0.00,
                        PullbackToTrendAtr = 0.35,
                        LongVwapReclaimToleranceAtr = 0.18,
                        LongMaxBreakoutExtensionAtr = 0.75,
                        LongMaxVwapExtensionAtr = 1.00,
                        TrendEmaToleranceAtr = 0.18,
                        TrendDcMidToleranceAtr = 0.15,
                        L1L2ConfirmMinScore = 1,
                        ShortL1L2ConfirmMinScore = 3,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        ShortRiskPerTradeMultiplier = 0.15,
                        SkipFirstNMinutes = 10,
                        LastEntryMinuteBeforeClose = 100,
                        EntryWindows = [(580, 900)],
                        CooldownBars = 5,
                        MaxSignalsPerDay = 8,
                        ShortEarliestMinuteEt = 590,
                        ShortMaxSignalsPerDay = 1,
                        HardStopR = 1.10,
                        StopAtrMultiplierTrend = 1.10,
                        StopAtrMultiplierNeutral = 0.70,
                        Tp1R = 1.05,
                        Tp2R = 2.10,
                        MaxHoldBars = 26,
                        ShortMaxChaseBelowLowerBandAtr = 0.60,
                        RequireLongAdxStrength = true,
                        LongMinAdx = 11.0,
                        LongMinPlusDiEdge = 0.75,
                        UseLongAdxCeiling = true,
                        LongMaxAdx = 40.0,
                        RequireLongRsiBand = true,
                        LongMinRsi14 = 44.0,
                        LongMaxRsi14 = 68.0,
                        RequireBullishBreakoutCandle = true,
                        LongBreakoutMinCloseLocationPct = 0.60,
                        LongBreakoutMaxUpperWickPct = 0.34,
                        RequireBullishReversalPullbackCandle = true,
                        EnableLongOneTwoThreeSetup = false,
                        EnableLongBuySetup = false,
                        UseBreakoutLongExitProfile = true,
                        LongLowAtrRiskMultiplier = 0.55,
                        LongLowAtrMaxPositionNotionalPctOfAccount = 0.09,
                        LongHighBandwidthThreshold = 0.052,
                        LongHighBandwidthRiskMultiplier = 0.65,
                    })),
                    new StrategyVariant("broad-balanced-runner", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "broad-balanced-runner",
                        RiskPerTradeDollars = 15.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 3.5,
                        L2LiquidityMin = 2.0,
                        RvolMin = 0.55,
                        StrongRvolMin = 0.80,
                        MinNormalizedBarRangeAtr = 0.16,
                        MaxNormalizedBarRangeAtr = 2.80,
                        MinVolAccel = -0.20,
                        TrendScoreMin = 2,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.12,
                        NeutralDcDistancePct = 0.25,
                        RangeBreakoutConfirmAtr = 0.00,
                        PullbackToTrendAtr = 0.35,
                        LongVwapReclaimToleranceAtr = 0.18,
                        LongMaxBreakoutExtensionAtr = 0.75,
                        LongMaxVwapExtensionAtr = 1.00,
                        TrendEmaToleranceAtr = 0.18,
                        TrendDcMidToleranceAtr = 0.15,
                        L1L2ConfirmMinScore = 1,
                        ShortL1L2ConfirmMinScore = 3,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        ShortRiskPerTradeMultiplier = 0.15,
                        SkipFirstNMinutes = 10,
                        LastEntryMinuteBeforeClose = 100,
                        EntryWindows = [(580, 900)],
                        CooldownBars = 5,
                        MaxSignalsPerDay = 8,
                        ShortEarliestMinuteEt = 590,
                        ShortMaxSignalsPerDay = 1,
                        HardStopR = 1.10,
                        StopAtrMultiplierTrend = 1.10,
                        StopAtrMultiplierNeutral = 0.70,
                        Tp1R = 1.05,
                        Tp2R = 2.10,
                        MaxHoldBars = 26,
                        ShortMaxChaseBelowLowerBandAtr = 0.60,
                        RequireLongAdxStrength = true,
                        LongMinAdx = 11.0,
                        LongMinPlusDiEdge = 0.75,
                        UseLongAdxCeiling = true,
                        LongMaxAdx = 40.0,
                        RequireLongRsiBand = true,
                        LongMinRsi14 = 44.0,
                        LongMaxRsi14 = 68.0,
                        RequireBullishBreakoutCandle = true,
                        LongBreakoutMinCloseLocationPct = 0.60,
                        LongBreakoutMaxUpperWickPct = 0.34,
                        RequireBullishReversalPullbackCandle = true,
                        EnableLongOneTwoThreeSetup = false,
                        EnableLongBuySetup = false,
                        UseBreakoutLongExitProfile = true,
                        BreakoutLongReversalFlatten = false,
                        BreakoutLongEmaTrail = false,
                        BreakoutLongGivebackMinPeakR = 0.50,
                        BreakoutLongBreakevenR = 0.80,
                        BreakoutLongTp1R = 1.50,
                        BreakoutLongTp2R = 3.00,
                        BreakoutLongMaxHoldBars = 30,
                        BreakoutLongMicroTrailCents = 5.0,
                        BreakoutLongMicroTrailActivateCents = 100.0,
                        BreakoutLongPeakGivebackKeepFraction = 0.50,
                        BreakoutLongPeakGivebackActivateR = 0.60,
                        BreakoutLongStagnationBars = 10,
                        BreakoutLongStagnationMinPeakR = 0.40,
                        BreakoutLongStagnationMaxAdverseR = -0.15,
                        LongLowAtrRiskMultiplier = 0.55,
                        LongLowAtrMaxPositionNotionalPctOfAccount = 0.09,
                        LongHighBandwidthThreshold = 0.052,
                        LongHighBandwidthRiskMultiplier = 0.65,
                    })),
                    new StrategyVariant("broad-active", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        DiagnosticsLabel = "broad-active",
                        RiskPerTradeDollars = 15.0,
                        MaxPositionNotionalPctOfAccount = 0.18,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        ShortMinPrice = 0.30,
                        SpreadZMax = 4.0,
                        L2LiquidityMin = 0.0,
                        RvolMin = 0.45,
                        StrongRvolMin = 0.70,
                        MinNormalizedBarRangeAtr = 0.12,
                        MaxNormalizedBarRangeAtr = 3.00,
                        MinVolAccel = -0.30,
                        TrendScoreMin = 1,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.15,
                        NeutralDcDistancePct = 0.30,
                        RangeBreakoutConfirmAtr = -0.05,
                        PullbackToTrendAtr = 0.40,
                        LongVwapReclaimToleranceAtr = 0.22,
                        LongMaxBreakoutExtensionAtr = 0.95,
                        LongMaxVwapExtensionAtr = 1.20,
                        TrendEmaToleranceAtr = 0.22,
                        TrendDcMidToleranceAtr = 0.18,
                        L1L2ConfirmMinScore = 0,
                        ShortL1L2ConfirmMinScore = 2,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        SkipFirstNMinutes = 5,
                        LastEntryMinuteBeforeClose = 110,
                        EntryWindows = [(575, 905)],
                        CooldownBars = 4,
                        MaxSignalsPerDay = 10,
                        ShortEarliestMinuteEt = 585,
                        ShortMaxSignalsPerDay = 3,
                        ShortRiskPerTradeMultiplier = 0.28,
                        HardStopR = 1.00,
                        StopAtrMultiplierTrend = 1.00,
                        StopAtrMultiplierNeutral = 0.65,
                        Tp1R = 0.95,
                        Tp2R = 1.90,
                        MaxHoldBars = 22,
                        ShortMaxChaseBelowLowerBandAtr = 0.75,
                        LongLowAtrRiskMultiplier = 0.52,
                        LongLowAtrMaxPositionNotionalPctOfAccount = 0.09,
                        LongHighBandwidthThreshold = 0.055,
                        LongHighBandwidthRiskMultiplier = 0.62,
                    })),
                    new StrategyVariant("small-cap-trend", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.65,
                        StrongRvolMin = 0.90,
                        MinNormalizedBarRangeAtr = 0.20,
                        MaxNormalizedBarRangeAtr = 2.40,
                        TrendScoreMin = 2,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.06,
                        RangeBreakoutConfirmAtr = 0.00,
                        LongVwapReclaimToleranceAtr = 0.12,
                        LongMaxBreakoutExtensionAtr = 0.45,
                        LongMaxVwapExtensionAtr = 0.70,
                        TrendEmaToleranceAtr = 0.12,
                        TrendDcMidToleranceAtr = 0.10,
                        L2LiquidityMin = 6.0,
                        L1L2ConfirmMinScore = 1,
                        ShortL1L2ConfirmMinScore = 4,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        LastEntryMinuteBeforeClose = 90,
                        EntryWindows = [(585, 885)],
                        HardStopR = 1.20,
                        StopAtrMultiplierTrend = 1.20,
                        StopAtrMultiplierNeutral = 0.75,
                        LongStopAnchorBufferAtr = 0.35,
                        LongStopEma21BufferAtr = 0.25,
                        LongStopVwapBufferAtr = 0.30,
                        Tp1R = 1.20,
                        Tp2R = 2.40,
                        MaxHoldBars = 28,
                        CooldownBars = 8,
                        MaxSignalsPerDay = 6,
                        ShortEarliestMinuteEt = 600,
                        ShortMaxChaseBelowLowerBandAtr = 0.45,
                    })),
                    new StrategyVariant("trend-loose", () => new Strategies.StrategyV17(new Strategies.V17Config
                    {
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.60,
                        StrongRvolMin = 0.85,
                        MinNormalizedBarRangeAtr = 0.18,
                        MaxNormalizedBarRangeAtr = 2.60,
                        TrendScoreMin = 2,
                        TrendLeadMin = 1,
                        RequireNonNeutralStructureForTrend = false,
                        NeutralBandwidthMax = 0.10,
                        NeutralDcDistancePct = 0.22,
                        RangeBreakoutConfirmAtr = -0.02,
                        LongVwapReclaimToleranceAtr = 0.15,
                        LongMaxBreakoutExtensionAtr = 0.55,
                        LongMaxVwapExtensionAtr = 0.85,
                        TrendEmaToleranceAtr = 0.15,
                        TrendDcMidToleranceAtr = 0.12,
                        L1L2ConfirmMinScore = 1,
                        ShortL1L2ConfirmMinScore = 3,
                        BuySetupLongExtraConfirmScore = 0,
                        BuySetupShortExtraConfirmScore = 0,
                        L2LiquidityMin = 4.0,
                        LastEntryMinuteBeforeClose = 105,
                        EntryWindows = [(590, 855)],
                        CooldownBars = 6,
                        StopAtrMultiplierTrend = 1.05,
                        StopAtrMultiplierNeutral = 0.65,
                        Tp1R = 1.00,
                        Tp2R = 2.00,
                        MaxHoldBars = 24,
                        MaxSignalsPerDay = 8,
                        ShortEarliestMinuteEt = 595,
                        ShortMaxChaseBelowLowerBandAtr = 0.55,
                    })),
                ]),

                // â”€â”€ V18 "Silver" â€” first-pass silver-style intraday strategy â”€â”€
                new StrategyPlan("V18-Silver", 25_000.0,
                [
                    new StrategyVariant("selective-short", () => new Strategies.StrategyV18(new Strategies.V18Config
                    {
                        AllowLong = true,
                        AllowShort = true,
                        UseFastReEntryMode = true,
                        UseFastReEntryShortMode = true,
                        ShortMaxSignalsPerDay = 1,
                        ShortEarliestMinuteEt = 630,
                        ShortStrongRvolMin = 1.20,
                        ShortRsiMin = 36.0,
                        ShortRsiMax = 54.0,
                        FastReEntryMinMinusDiEdge = 4.0,
                        FastReEntryShortMaxMfi14 = 54.0,
                        FastReEntryShortMaxKeltnerExtensionAtr = 0.28,
                        FastReEntryShortMinBaselineDistanceAtr = 0.08,
                        FastReEntryShortMinBreakoutAtr = 0.07,
                        FastReEntryShortBreakevenR = 0.35,
                        FastReEntryShortTrailR = 0.22,
                        FastReEntryShortGivebackPct = 0.20,
                        FastReEntryShortTp1R = 0.90,
                        FastReEntryShortTp2R = 1.80,
                        FastReEntryShortMaxHoldBars = 18,
                        FastReEntryShortPeakGivebackKeepFraction = 0.35,
                        FastReEntryShortPeakGivebackActivateR = 0.20,
                        FastReEntryShortStagnationBars = 5,
                        FastReEntryShortStagnationMinPeakR = 0.12,
                        FastReEntryShortStagnationMaxAdverseR = -0.03,
                    })),
                    new StrategyVariant("legacy-mixed", () => new Strategies.StrategyV18(new Strategies.V18Config
                    {
                        AllowLong = true,
                        AllowShort = true,
                        UseFastReEntryMode = false,
                        MinPrice = 0.30,
                        MaxPrice = 700.0,
                        RvolMin = 0.80,
                        StrongRvolMin = 1.05,
                        SkipFirstNMinutes = 20,
                        LastEntryMinuteBeforeClose = 60,
                        EntryWindows = [(600, 930)],
                        CooldownBars = 8,
                        MaxSignalsPerDay = 3,
                        AdxMin = 14.0,
                        AdxMax = 55.0,
                        LongRsiMin = 44.0,
                        LongRsiMax = 68.0,
                        StopAtrMultiplier = 1.15,
                    })),
                ]),

                // â”€â”€ V19 retained Purple Cloud breakdown profile â”€â”€
                new StrategyPlan("V19-PurpleCloud", 25_000.0,
                [
                    new StrategyVariant("retained-breakout", () => new Strategies.StrategyV19(new Strategies.V19Config
                    {
                        CooldownBars = 3,
                        MaxSignalsPerDay = 5,
                        ShortMaxSignalsPerDay = 3,
                        RvolMin = 0.60,
                        StrongRvolMin = 0.90,
                        SkipFirstNMinutes = 5,
                        LastEntryMinuteBeforeClose = 30,
                        ShortEarliestMinuteEt = 590,
                        EntryWindows = [(575, 940)],
                        BreakoutLookbackBars = 12,
                        BreakoutBufferAtr = 0.00,
                        MaxBreakoutExtensionAtr = 0.90,
                    })),
                ]),

                // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
                // V20 retained variant â€” keep only the strongest defensive path
                // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
                new StrategyPlan("V20-GEN001-ChoppyShield", 25_000.0,
                [
                    new StrategyVariant("default", () => new Strategies.StrategyV20(new Strategies.V20Config
                    {
                        AllowLong = true,
                        AllowShort = true,
                        CooldownBars = 5,
                        MaxSignalsPerDay = 5,
                        SkipFirstNMinutes = 10,
                        LastEntryMinuteBeforeClose = 60,
                        EntryWindows = [(580, 900)],
                        AdxMin = 10.0,
                        LongRsiMin = 42.0,
                        ShortRsiMax = 56.0,
                        MaxVwapExtensionAtr = 2.40,
                        PullbackTouchToleranceAtr = 0.80,
                        MinimumBodyFraction = 0.10,
                        MinimumCloseLocation = 0.50,
                        RequireEmaStack = false,
                        RequireVwapAlignment = false,
                        RequireMacdMomentum = false,
                        EnableChoppyFilter = false,
                    })),
                    new StrategyVariant("floor-composite", () => new Strategies.StrategyV20(new Strategies.V20Config
                    {
                        AllowLong = true,
                        AllowShort = true,
                        CooldownBars = 2,
                        MaxSignalsPerDay = 8,
                        SkipFirstNMinutes = 0,
                        LastEntryMinuteBeforeClose = 30,
                        EntryWindows = [(570, 930)],
                        RvolMin = 0.0,
                        RvolMax = 20.0,
                        AdxMin = 8.0,
                        AdxMax = 70.0,
                        LongRsiMin = 38.0,
                        LongRsiMax = 72.0,
                        ShortRsiMin = 28.0,
                        ShortRsiMax = 60.0,
                        MaxVwapExtensionAtr = 3.00,
                        PullbackTouchToleranceAtr = 1.10,
                        MinimumBodyFraction = 0.05,
                        MinimumCloseLocation = 0.45,
                        RequireEmaStack = false,
                        RequireHigherTimeframeTrend = false,
                        RequireVwapAlignment = false,
                        RequireMacdMomentum = false,
                        EnableChoppyFilter = false,
                        MinTradeableBbBandwidth = 0.0,
                        VolatileBbBandwidthMin = 10.0,
                    })),
                    new StrategyVariant("floor-composite-unblocked", () => new Strategies.StrategyV20(new Strategies.V20Config
                    {
                        IgnoreSelfLearningSetupBlock = true,
                        AllowLong = true,
                        AllowShort = true,
                        CooldownBars = 2,
                        MaxSignalsPerDay = 8,
                        SkipFirstNMinutes = 0,
                        LastEntryMinuteBeforeClose = 30,
                        EntryWindows = [(570, 930)],
                        RvolMin = 0.0,
                        RvolMax = 20.0,
                        AdxMin = 8.0,
                        AdxMax = 70.0,
                        LongRsiMin = 38.0,
                        LongRsiMax = 72.0,
                        ShortRsiMin = 28.0,
                        ShortRsiMax = 60.0,
                        MaxVwapExtensionAtr = 3.00,
                        PullbackTouchToleranceAtr = 1.10,
                        MinimumBodyFraction = 0.05,
                        MinimumCloseLocation = 0.45,
                        RequireEmaStack = false,
                        RequireHigherTimeframeTrend = false,
                        RequireVwapAlignment = false,
                        RequireMacdMomentum = false,
                        EnableChoppyFilter = false,
                        MinTradeableBbBandwidth = 0.0,
                        VolatileBbBandwidthMin = 10.0,
                    })),
                    new StrategyVariant("default-unblocked", () => new Strategies.StrategyV20(new Strategies.V20Config
                    {
                        IgnoreSelfLearningSetupBlock = true,
                        AllowLong = true,
                        AllowShort = true,
                        CooldownBars = 5,
                        MaxSignalsPerDay = 5,
                        SkipFirstNMinutes = 10,
                        LastEntryMinuteBeforeClose = 60,
                        EntryWindows = [(580, 900)],
                        AdxMin = 10.0,
                        LongRsiMin = 42.0,
                        ShortRsiMax = 56.0,
                        MaxVwapExtensionAtr = 2.40,
                        PullbackTouchToleranceAtr = 0.80,
                        MinimumBodyFraction = 0.10,
                        MinimumCloseLocation = 0.50,
                        RequireEmaStack = false,
                        RequireVwapAlignment = false,
                        RequireMacdMomentum = false,
                        EnableChoppyFilter = false,
                    })),
                    new StrategyVariant("balanced-unblocked", () => new Strategies.StrategyV20(new Strategies.V20Config
                    {
                        IgnoreSelfLearningSetupBlock = true,
                        AllowLong = true,
                        AllowShort = true,
                        CooldownBars = 3,
                        MaxSignalsPerDay = 7,
                        SkipFirstNMinutes = 5,
                        LastEntryMinuteBeforeClose = 45,
                        EntryWindows = [(575, 930)],
                        RvolMin = 0.0,
                        RvolMax = 20.0,
                        AdxMin = 8.0,
                        AdxMax = 70.0,
                        LongRsiMin = 40.0,
                        LongRsiMax = 72.0,
                        ShortRsiMin = 30.0,
                        ShortRsiMax = 58.0,
                        MaxVwapExtensionAtr = 2.80,
                        PullbackTouchToleranceAtr = 0.95,
                        MinimumBodyFraction = 0.06,
                        MinimumCloseLocation = 0.48,
                        RequireEmaStack = false,
                        RequireHigherTimeframeTrend = false,
                        RequireVwapAlignment = false,
                        RequireMacdMomentum = false,
                        EnableChoppyFilter = false,
                        MinTradeableBbBandwidth = 0.0,
                        VolatileBbBandwidthMin = 10.0,
                        HardStopR = 1.00,
                        BreakevenR = 0.35,
                        TrailR = 0.20,
                        GivebackPct = 0.32,
                        GivebackUsdCap = 10.0,
                        Tp1R = 1.00,
                        Tp2R = 1.70,
                        MaxHoldBars = 16,
                        PeakGivebackKeepFraction = 0.40,
                        PeakGivebackActivateR = 0.18,
                        StagnationBars = 3,
                        StagnationMinPeakR = 0.05,
                        StagnationMaxAdverseR = -0.02,
                    })),
                    new StrategyVariant("active-composite-unblocked", () => new Strategies.StrategyV20(new Strategies.V20Config
                    {
                        IgnoreSelfLearningSetupBlock = true,
                        AllowLong = true,
                        AllowShort = true,
                        CooldownBars = 1,
                        MaxSignalsPerDay = 12,
                        SkipFirstNMinutes = 0,
                        LastEntryMinuteBeforeClose = 15,
                        EntryWindows = [(570, 945)],
                        RvolMin = 0.0,
                        RvolMax = 30.0,
                        AdxMin = 6.0,
                        AdxMax = 80.0,
                        LongRsiMin = 35.0,
                        LongRsiMax = 75.0,
                        ShortRsiMin = 25.0,
                        ShortRsiMax = 65.0,
                        MaxVwapExtensionAtr = 3.50,
                        PullbackTouchToleranceAtr = 1.30,
                        MinimumBodyFraction = 0.02,
                        MinimumCloseLocation = 0.40,
                        RequireEmaStack = false,
                        RequireHigherTimeframeTrend = false,
                        RequireVwapAlignment = false,
                        RequireMacdMomentum = false,
                        EnableChoppyFilter = false,
                        MinTradeableBbBandwidth = 0.0,
                        VolatileBbBandwidthMin = 10.0,
                        HardStopR = 1.00,
                        BreakevenR = 0.35,
                        TrailR = 0.20,
                        GivebackPct = 0.30,
                        GivebackUsdCap = 10.0,
                        Tp1R = 1.00,
                        Tp2R = 1.70,
                        MaxHoldBars = 16,
                        PeakGivebackKeepFraction = 0.40,
                        PeakGivebackActivateR = 0.18,
                        StagnationBars = 3,
                        StagnationMinPeakR = 0.05,
                        StagnationMaxAdverseR = -0.02,
                    })),
                    new StrategyVariant("retained-unblocked", () => new Strategies.StrategyV20(new Strategies.V20Config
                    {
                        IgnoreSelfLearningSetupBlock = true,
                    })),
                ]),

                new StrategyPlan("V21-15Minutes", 25_000.0,
                [
                    new StrategyVariant("default", () => new Strategies.StrategyV21(new Strategies.V21Config
                    {
                        RiskPerTradeDollars = 25.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.20,
                        MaxShares = 10_000,
                        CommissionPerShare = 0.005,
                        AngleThresholdDegrees = 12.0,
                        StopAtrMultiplier = 1.0,
                        MinimumRiskPerShare = 0.05,
                        MarketOpenMinuteEt = 570,
                        LastEntryMinuteEt = 945,
                        EodFlattenMinuteEt = 955,
                        MinimumBars15mForSignal = 9,
                        DiagnosticsLabel = "default",
                    })),
                ]),

                new StrategyPlan("V23-5Minutes", 25_000.0,
                [
                    new StrategyVariant("default", () => new Strategies.StrategyV23(new Strategies.V23Config
                    {
                        RiskPerTradeDollars = 25.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.20,
                        MaxShares = 10_000,
                        CommissionPerShare = 0.005,
                        AngleThresholdDegrees = 12.0,
                        StopAtrMultiplier = 1.0,
                        MinimumRiskPerShare = 0.05,
                        MarketOpenMinuteEt = 570,
                        LastEntryMinuteEt = 945,
                        EodFlattenMinuteEt = 955,
                        MinimumBars5mForSignal = 9,
                        DiagnosticsLabel = "default",
                    })),
                ]),

                new StrategyPlan("V22-15Minutes", 25_000.0,
                [
                    new StrategyVariant("default", () => new Strategies.StrategyV22(new Strategies.V22Config
                    {
                        RiskPerTradeDollars = 25.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.20,
                        MaxShares = 10_000,
                        CommissionPerShare = 0.005,
                        AngleThresholdDegrees = 12.0,
                        ExitReversalAngleDegrees = 6.0,
                        StopAtrMultiplier = 1.00,
                        TrailAtrMultiplier = 0.85,
                        MinimumRiskPerShare = 0.05,
                        MaxStructuralRiskAtr = 1.20,
                        PullbackMaxAtr = 0.80,
                        VwapStretchMaxAtr = 1.45,
                        BreakoutExtensionMaxAtr = 1.80,
                        TrendAdxMin = 14.0,
                        RvolMin = 0.80,
                        LongRsiMin = 50.0,
                        LongRsiMax = 78.0,
                        ShortRsiMin = 22.0,
                        ShortRsiMax = 50.0,
                        BreakevenActivationR = 0.90,
                        ProfitProtectActivationR = 1.60,
                        CooldownBarsAfterStop = 1,
                        CooldownBarsAfterWeakness = 1,
                        MaxHoldBars15m = 16,
                        MarketOpenMinuteEt = 570,
                        LastEntryMinuteEt = 945,
                        EodFlattenMinuteEt = 955,
                        MinimumBars15mForSignal = 9,
                        SqueezeLookbackBars = 4,
                        MinimumScore = 7,
                        DiagnosticsLabel = "default",
                    })),
                ]),

                new StrategyPlan("V24-5Minutes", 25_000.0,
                [
                    new StrategyVariant("default", () => new Strategies.StrategyV24(new Strategies.V24Config
                    {
                        RiskPerTradeDollars = 25.0,
                        AccountSize = 25_000.0,
                        MaxPositionNotionalPctOfAccount = 0.20,
                        MaxShares = 10_000,
                        CommissionPerShare = 0.005,
                        AngleThresholdDegrees = 12.0,
                        ExitReversalAngleDegrees = 6.0,
                        StopAtrMultiplier = 1.00,
                        TrailAtrMultiplier = 0.85,
                        MinimumRiskPerShare = 0.05,
                        MaxStructuralRiskAtr = 1.20,
                        PullbackMaxAtr = 0.80,
                        VwapStretchMaxAtr = 1.45,
                        BreakoutExtensionMaxAtr = 1.80,
                        TrendAdxMin = 14.0,
                        RvolMin = 0.80,
                        LongRsiMin = 50.0,
                        LongRsiMax = 78.0,
                        ShortRsiMin = 22.0,
                        ShortRsiMax = 50.0,
                        BreakevenActivationR = 0.90,
                        ProfitProtectActivationR = 1.60,
                        CooldownBarsAfterStop = 1,
                        CooldownBarsAfterWeakness = 1,
                        MaxHoldBars5m = 16,
                        MarketOpenMinuteEt = 570,
                        LastEntryMinuteEt = 945,
                        EodFlattenMinuteEt = 955,
                        MinimumBars5mForSignal = 9,
                        SqueezeLookbackBars = 4,
                        MinimumScore = 7,
                        DiagnosticsLabel = "default",
                    })),
                ]),

        ];
    }
}

