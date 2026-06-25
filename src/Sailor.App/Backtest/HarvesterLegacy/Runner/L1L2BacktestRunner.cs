using System.Text.Json;
using Sailor.App.Backtest.DataFetcher;
using Sailor.App.Backtest.Engine;
using Sailor.App.Backtest.Strategies;
using Harvester.App.IBKR.Runtime;
using Harvester.App.Strategy;
using Harvester.App.Strategy.Events;

namespace Sailor.App.Backtest.Runner;

/// <summary>
/// L1/L2-enhanced backtest runner that replays historical tick data through the
/// same V3LiveFeatureBuilder + V3LiveSignalEngine pipeline used in live trading.
///
/// This bridges the gap between the bar-only backtest and the live system:
///   - Loads stored L1 bid-ask ticks and trade ticks from CSV
///   - Synthesizes TopTickRow[] and DepthRow[] per bar window via L1L2Synthesizer
///   - Builds StrategyDataSlice per bar (same structure as live)
///   - Runs V3LiveFeatureBuilder.Build() to compute full feature snapshots with L1/L2
///   - Evaluates signals via V3LiveSignalEngine (with L2 directional gates, L1 spread/size bonuses)
///   - Routes entries and exits through ReplayExecutionEngine + PostFillConductExecutor
///
/// The result is a backtest that exercises the exact same signal + exit code paths
/// as the live system, with real historical microstructure data.
/// </summary>
public sealed class L1L2BacktestRunner
{
    private readonly V3LiveConfig _config;
    private readonly int _depthLevels;
    private readonly double _initialCapital;
    private readonly SelfLearningSignalAdapter? _selfLearning;
    private readonly string? _selfLearningDir;
    private readonly bool _useDefaultSelfLearningSearch;

    public L1L2BacktestRunner(V3LiveConfig? config = null, int depthLevels = 5, double initialCapital = 25_000.0,
        SelfLearningSignalAdapter? selfLearning = null, string? selfLearningDir = null)
    {
        _config = config ?? new V3LiveConfig();
        _depthLevels = depthLevels;
        _initialCapital = initialCapital;
        _selfLearningDir = selfLearningDir;
        _useDefaultSelfLearningSearch = string.IsNullOrWhiteSpace(selfLearningDir);
        _selfLearning = selfLearning ?? AutoLoadSelfLearning();
    }

    private SelfLearningSignalAdapter? AutoLoadSelfLearning()
    {
        var adapter = new SelfLearningSignalAdapter();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var loaded = _useDefaultSelfLearningSearch
            ? adapter.TryLoadFromDirectories(SelfLearningSignalAdapter.GetDefaultSearchDirectories(), logger)
            : adapter.TryLoadFromDirectory(_selfLearningDir!, logger);

        if (loaded)
        {
            Console.WriteLine($"[L1L2-BT] Self-learning loaded: StopMult={adapter.StopDistanceMultiplier:F3} PosSizeMult={adapter.PositionSizeMultiplier:F3}");
            return adapter;
        }
        return null;
    }

    /// <summary>
    /// Run a full L1/L2-enhanced backtest for one symbol.
    /// Requires bar data and L1 tick data to be available in backtest/data/{SYMBOL}/.
    /// </summary>
    public L1L2BacktestResult Run(string symbol, string timeframe = "1m")
    {
        // â”€â”€ 1. Load bar data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var bars = BacktestDataFetcher.TryLoadBars(symbol, timeframe);
        if (bars == null || bars.Length == 0)
            throw new InvalidOperationException($"No {timeframe} bar data found for {symbol}");

        // â”€â”€ 2. Load L1 tick data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var bidAskTicks = CsvTickStorage.BidAskExists(symbol) ? CsvTickStorage.LoadBidAskTicks(symbol) : [];
        var tradeTicks = CsvTickStorage.TradesExist(symbol) ? CsvTickStorage.LoadTradeTicks(symbol) : [];

        var hasTicks = bidAskTicks.Length > 0 || tradeTicks.Length > 0;
        if (!hasTicks)
        {
            Console.Error.WriteLine($"[L1L2-BT] WARNING: No L1 tick data for {symbol}. " +
                                    $"Running with empty L1/L2 snapshots (L2 gates will be bypassed).");
        }

        // â”€â”€ 3. Compute coverage â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var coverage = L1L2Synthesizer.ComputeCoverage(bars, bidAskTicks, tradeTicks);

        // â”€â”€ 4. Build StrategyDataSlices â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var slices = L1L2Synthesizer.BuildSlicesForAllBars(symbol, bars, bidAskTicks, tradeTicks, _depthLevels);

        // â”€â”€ 5. Route the backtest through canonical replay + conduct â”€â”€â”€
        var replayResult = new L1L2BacktestReplayAdapter(_config, _depthLevels, _initialCapital, _selfLearning)
            .Run(symbol, timeframe, bars, slices, coverage);
        var trades = replayResult.Trades;

        // â”€â”€ 6. Compute statistics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var stats = BacktestEngine.ComputeStatistics(trades, _initialCapital);
        var equityCurve = BacktestEngine.BuildEquityCurve(trades, _initialCapital);

        return new L1L2BacktestResult(
            Symbol: symbol,
            TriggerTf: timeframe,
            Trades: trades,
            EquityCurve: equityCurve,
            Stats: stats,
            Coverage: coverage,
            SignalCount: replayResult.SignalCount,
            Config: _config,
            AcceptedExecutionIntents: replayResult.AcceptedEntryIntents,
            Artifacts: replayResult.Artifacts,
            EventCacheSnapshot: replayResult.EventCacheSnapshot);
    }

    /// <summary>
    /// Run the L1/L2-enhanced backtest for multiple symbols.
    /// </summary>
    public List<L1L2BacktestResult> RunMultiple(IEnumerable<string> symbols, string timeframe = "1m")
    {
        var results = new List<L1L2BacktestResult>();
        foreach (var symbol in symbols)
        {
            try
            {
                var result = Run(symbol, timeframe);
                results.Add(result);
                Console.WriteLine($"[L1L2-BT] {symbol}: {result.Stats.TotalTrades} trades, " +
                                  $"PF={result.Stats.ProfitFactor:F2}, " +
                                  $"PnL=${result.Stats.TotalPnl:F2}, " +
                                  $"L1 coverage={result.Coverage.BidAskCoveragePct:F0}%");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[L1L2-BT] ERROR: {symbol}: {ex.Message}");
            }
        }
        return results;
    }

    /// <summary>
    /// Produce self-learning recommendations from backtest trade results.
    /// Converts BacktestTradeResult â†’ ReplayFillRow/ReplayCostDeltaArtifactRow,
    /// feeds them into ReplaySelfLearningEngine V2.1 GLM, and writes a
    /// recommendations JSON file. Creates a closed backtest feedback loop.
    /// </summary>
    /// <param name="results">Results from RunMultiple (or single Run calls).</param>
    /// <param name="outputDir">Directory to write recommendations JSON. Defaults to exports/.</param>
    /// <returns>The generated recommendation row, or null if insufficient data.</returns>
    public static SelfLearningV2RecommendationRow? ProduceSelfLearningRecommendations(
        IReadOnlyList<L1L2BacktestResult> results, string? outputDir = null)
    {
        var allTrades = results.SelectMany(r => r.Trades.Select(t => (r.Symbol, Trade: t))).ToList();
        if (allTrades.Count < 10)
        {
            Console.Error.WriteLine($"[L1L2-BT] Self-learning skipped: only {allTrades.Count} trades (need â‰¥10)");
            return null;
        }

        // Convert BacktestTradeResult â†’ ReplayFillRow (entry fill + exit fill per trade)
        var fills = new List<ReplayFillRow>();
        var costDeltas = new List<ReplayCostDeltaArtifactRow>();

        foreach (var (symbol, trade) in allTrades)
        {
            var sideStr = trade.Side == TradeSide.Long ? "BUY" : "SELL";
            var exitSideStr = trade.Side == TradeSide.Long ? "SELL" : "BUY";
            var commission = 0.005 * trade.PositionSize;

            // Entry fill
            fills.Add(new ReplayFillRow(
                TimestampUtc: trade.EntryTime,
                Symbol: symbol,
                Side: sideStr,
                Quantity: trade.PositionSize,
                RequestedQuantity: trade.PositionSize,
                RemainingQuantity: 0,
                IsPartial: false,
                SubmittedAtUtc: trade.EntryTime,
                OrderType: "MKT",
                FillPrice: trade.EntryPrice,
                Commission: commission,
                RealizedPnlDelta: 0,
                Source: trade.SubStrategy));

            // Exit fill
            fills.Add(new ReplayFillRow(
                TimestampUtc: trade.ExitTime,
                Symbol: symbol,
                Side: exitSideStr,
                Quantity: trade.PositionSize,
                RequestedQuantity: trade.PositionSize,
                RemainingQuantity: 0,
                IsPartial: false,
                SubmittedAtUtc: trade.ExitTime,
                OrderType: "MKT",
                FillPrice: trade.ExitPrice,
                Commission: commission,
                RealizedPnlDelta: trade.Pnl,
                Source: trade.SubStrategy));

            // Cost delta for exit
            costDeltas.Add(new ReplayCostDeltaArtifactRow(
                TimestampUtc: trade.ExitTime,
                Symbol: symbol,
                Side: exitSideStr,
                OrderType: "MKT",
                Quantity: trade.PositionSize,
                FillPrice: trade.ExitPrice,
                ReferencePrice: trade.EntryPrice,
                EstimatedCommission: commission,
                RealizedCommission: commission,
                CommissionDelta: 0,
                EstimatedSlippage: null,
                RealizedSlippage: null,
                SlippageDelta: null,
                Source: trade.SubStrategy));
        }

        // Build synthetic equity curve as performance packets
        var packets = new List<ReplayPerformancePacketRow>();
        var equity = 25_000.0;
        var cash = 25_000.0;
        var cumReturn = 0.0;
        var peakEquity = equity;

        foreach (var (symbol, trade) in allTrades)
        {
            equity += trade.Pnl;
            cash += trade.Pnl;
            var periodReturn = equity > 0 ? trade.Pnl / (equity - trade.Pnl) : 0;
            cumReturn = equity / 25_000.0 - 1.0;
            peakEquity = Math.Max(peakEquity, equity);
            var drawdown = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0;

            packets.Add(new ReplayPerformancePacketRow(
                TimestampUtc: trade.ExitTime,
                Equity: equity,
                PeriodReturn: periodReturn,
                CumulativeReturn: cumReturn,
                Drawdown: drawdown,
                BenchmarkReturn: 0,
                Alpha: cumReturn,
                RealizedPnl: trade.Pnl,
                UnrealizedPnl: 0,
                Cash: cash));
        }

        // Run the V2.1 GLM self-learning engine
        var engine = new ReplaySelfLearningEngine();
        var analysis = engine.Analyze(fills, costDeltas, packets);
        var recommendations = ReplaySelfLearningEngine.BuildRecommendations(analysis, existingBias: null);

        // Write to file
        var dir = outputDir ?? "exports";
        Directory.CreateDirectory(dir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(dir, $"strategy_replay_self_learning_v2_recommendations_bt_{timestamp}.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(new[] { recommendations }, options));

        Console.WriteLine($"[L1L2-BT] Self-learning recommendations written: {path}");
        Console.WriteLine($"[L1L2-BT]   StopMult={recommendations.SuggestedStopDistanceMultiplier:F3} " +
                          $"PosSizeMult={recommendations.SuggestedPositionSizeMultiplier:F3}");

        return recommendations;
    }

    internal static bool TryCreateSignalFromDecision(
        int barIndex,
        DateTime barTimestamp,
        V3LiveFeatureSnapshot features,
        V3LiveSignalDecision decision,
        out L1L2Signal signal)
    {
        signal = null!;

        if (!decision.HasSignal || decision.SelectedSignal is null)
        {
            return false;
        }

        var selectedSignal = decision.SelectedSignal;
        var setup = string.IsNullOrWhiteSpace(selectedSignal.SubStrategy)
            ? decision.Setup
            : selectedSignal.SubStrategy;
        var entryPrice = selectedSignal.EntryPrice > 0 ? selectedSignal.EntryPrice : features.Price;
        var stopPrice = selectedSignal.StopPrice;
        var riskPerShare = selectedSignal.RiskPerShare > 0
            ? selectedSignal.RiskPerShare
            : Math.Abs(entryPrice - stopPrice);
        var positionSize = selectedSignal.PositionSize;
        var atrValue = selectedSignal.AtrValue > 0 ? selectedSignal.AtrValue : features.Atr14;

        if (entryPrice <= 0
            || stopPrice <= 0
            || riskPerShare <= 0
            || positionSize <= 0)
        {
            return false;
        }

        signal = new L1L2Signal(
            BarIndex: barIndex,
            Timestamp: barTimestamp,
            Side: selectedSignal.Side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: positionSize,
            AtrValue: atrValue,
            Setup: setup,
            L1Snapshot: features.L1,
            L2Snapshot: features.L2);
        return true;
    }

    internal static ExitReason MapExitReason(string cascadeReason)
    {
        var normalized = (cascadeReason ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Contains("L1") && normalized.Contains("MISSING")) return ExitReason.L1MissingExit;
        if (normalized.Contains("TP1")) return ExitReason.Tp1;
        if (normalized.Contains("TP2")) return ExitReason.Tp2;
        if (normalized.Contains("TRAIL")) return ExitReason.Trailing;
        if (normalized.Contains("GIVEBACK")) return ExitReason.Giveback;
        if (normalized.Contains("HARD") || normalized.Contains("STOP")) return ExitReason.HardStop;
        if (normalized.Contains("TIME") || normalized.Contains("END_OF_DATA")) return ExitReason.TimeStop;
        return ExitReason.TimeStop;
    }
}

// â”€â”€ Result Models â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>Signal generated by the V3Live signal engine during L1/L2-enhanced backtest.</summary>
public sealed record L1L2Signal(
    int BarIndex,
    DateTime Timestamp,
    TradeSide Side,
    double EntryPrice,
    double StopPrice,
    double RiskPerShare,
    int PositionSize,
    double AtrValue,
    string Setup,
    V3LiveL1Snapshot L1Snapshot,
    V3LiveL2Snapshot L2Snapshot);

/// <summary>Complete result of an L1/L2-enhanced backtest run.</summary>
public sealed record L1L2BacktestResult(
    string Symbol,
    string TriggerTf,
    IReadOnlyList<BacktestTradeResult> Trades,
    IReadOnlyList<(DateTime Time, double Equity)> EquityCurve,
    BacktestStatistics Stats,
    L1L2CoverageStats Coverage,
    int SignalCount,
    V3LiveConfig Config,
    IReadOnlyList<ExecutionIntent>? AcceptedExecutionIntents = null,
    L1L2BacktestArtifacts? Artifacts = null,
    TradingStateCacheSnapshot? EventCacheSnapshot = null)
{
    public string SummaryTable()
    {
        var lines = new List<string>
        {
            $"{"â”€â”€ L1/L2-Enhanced Backtest â”€â”€",-42}",
            $"{"Metric",-25} {"Value",15}",
            new string('â”€', 42),
            $"{"Symbol",-25} {Symbol,15}",
            $"{"Trigger TF",-25} {TriggerTf,15}",
            $"{"Total Signals",-25} {SignalCount,15}",
            $"{"Total Trades",-25} {Stats.TotalTrades,15}",
            $"{"Winners",-25} {Stats.Winners,15}",
            $"{"Losers",-25} {Stats.Losers,15}",
            $"{"Win Rate",-25} {Stats.WinRate,14:P1}",
            $"{"Avg Win ($)",-25} {Stats.AvgWin,15:F2}",
            $"{"Avg Loss ($)",-25} {Stats.AvgLoss,15:F2}",
            $"{"Profit Factor",-25} {Stats.ProfitFactor,15:F2}",
            $"{"Expectancy R",-25} {Stats.ExpectancyR,15:F2}",
            $"{"Total PnL ($)",-25} {Stats.TotalPnl,15:F2}",
            $"{"Max Drawdown ($)",-25} {Stats.MaxDrawdown,15:F2}",
            $"{"Max Drawdown (%)",-25} {Stats.MaxDrawdownPct,14:P1}",
            $"{"Sharpe",-25} {Stats.Sharpe,15:F2}",
            $"{"Sortino",-25} {Stats.Sortino,15:F2}",
            $"{"Downside Deviation",-25} {Stats.DownsideDeviation,14:P2}",
            $"{"Equity Curve Sharpe",-25} {Stats.EquityCurveSharpe,15:F2}",
            $"{"Equity Curve Sortino",-25} {Stats.EquityCurveSortino,15:F2}",
            $"{"Equity Curve Downside",-25} {Stats.EquityCurveDownsideDeviation,14:P2}",
            $"{"Avg Bars Held",-25} {Stats.AvgBarsHeld,15:F1}",
            new string('â”€', 42),
            $"{"L1 BidAsk Coverage",-25} {Coverage.BidAskCoveragePct,14:F1}%",
            $"{"L1 Trade Coverage",-25} {Coverage.TradeCoveragePct,14:F1}%",
            $"{"Total BidAsk Ticks",-25} {Coverage.TotalBidAskTicks,15:N0}",
            $"{"Total Trade Ticks",-25} {Coverage.TotalTradeTicks,15:N0}",
        };
        return string.Join(Environment.NewLine, lines);
    }
}

public sealed record L1L2BacktestArtifacts(
    string OutputDirectory,
    string? TradingEventJournalPath = null,
    string? ComparisonReportPath = null,
    string? ComparisonReportJsonPath = null,
    string? OrderLifecycleCertificationPath = null,
    string? OrderLifecycleCertificationSummaryPath = null,
    int EventCount = 0);

