using Sailor.App.Backtest;
using Sailor.App.Backtest.DataFetcher;
using Sailor.App.Backtest.Engine;
using Sailor.App.Backtest.Indicators;
using Harvester.App.IBKR.Runtime;
using Harvester.App.Strategy;
using Harvester.App.Strategy.Conduct;
using Harvester.App.Strategy.Events;

namespace Sailor.App.Backtest.Runner;

internal sealed class L1L2BacktestReplayAdapter
{
    private const double DefaultCommissionPerUnit = 0.005;

    private readonly V3LiveConfig _config;
    private readonly int _depthLevels;
    private readonly double _initialCapital;
    private readonly RealityModelProfile _realityModelProfile;
    private readonly SelfLearningSignalAdapter? _selfLearning;
    private readonly TradingEventFactory _eventFactory;
    private readonly TradingEventBus _eventBus = new();
    private readonly TradingEventJournal _eventJournal = new();
    private readonly TradingStateCache _stateCache;
    private readonly OrderLifecycleCertificationTracker _lifecycleCertificationTracker = new();
    private readonly RuntimeArtifactReporter _runtimeArtifactReporter = new();

    public L1L2BacktestReplayAdapter(
        V3LiveConfig config,
        int depthLevels,
        double initialCapital,
        SelfLearningSignalAdapter? selfLearning)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _depthLevels = depthLevels;
        _initialCapital = initialCapital;
        _realityModelProfile = RealityModelProfileCatalog.Resolve(
            new RealityModelProfileInputs(
                initialCapital,
                DefaultCommissionPerUnit,
                _config.MaxSlippageBps,
                InitialMarginRate: 0,
                MaintenanceMarginRate: 0,
                MaxGrossExposurePctOfInitialCash: 1.0,
                MaxNetExposurePctOfInitialCash: 1.0,
                SecFeeRatePerDollar: 0,
                TafFeePerShare: 0,
                TafFeeCapPerOrder: 0,
                ExchangeFeePerShare: 0,
                MaxFillParticipationRate: 1.0,
                PriceIncrement: 0.01,
                EnforceQueuePriority: false,
                SettlementLagDays: 0,
                EnforceSettledCash: false),
            RealityModelProfileCatalog.StrictProfileId,
            RunMode.BacktestCompare);
        _selfLearning = selfLearning;
        _eventFactory = new TradingEventFactory(config);
        _stateCache = new TradingStateCache(_eventBus);
    }

    public L1L2BacktestReplayExecutionResult Run(
        string symbol,
        string timeframe,
        BacktestBar[] bars,
        StrategyDataSlice[] slices,
        L1L2CoverageStats coverage)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(slices);

        if (bars.Length == 0 || slices.Length == 0)
        {
            return new L1L2BacktestReplayExecutionResult([], [], coverage, CreateArtifacts(symbol, timeframe, null, null));
        }

        var outputDirectory = Path.Combine(
            "artifacts",
            "tmp",
            "l1l2_backtest",
            symbol.Trim().ToUpperInvariant(),
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{timeframe}");
        Directory.CreateDirectory(outputDirectory);

        using var journalSubscription = _eventBus.Subscribe(_eventJournal.Append);
        _eventJournal.Initialize(outputDirectory, bars[0].Timestamp);

        var featureBuilder = new V3LiveFeatureBuilder();
        var signalEngine = new V3LiveSignalEngine(_selfLearning);
        var replayExecutionEngine = CreateReplayExecutionEngine();
        var acceptedEntryIntents = new List<ExecutionIntent>();
        var trades = new List<BacktestTradeResult>();
        var pendingEntries = new Dictionary<string, PendingReplayEntry>(StringComparer.OrdinalIgnoreCase);
        ActiveReplayTrade? activeTrade = null;
        var signalCount = 0;

        for (var index = 0; index < slices.Length; index++)
        {
            var slice = slices[index];
            var currentBar = bars[index];
            var features = featureBuilder.Build(slice, _depthLevels, symbol, minReadyBars: 7);

            Publish(_eventFactory.CreateMarketEvent("backtest", "L1L2BacktestRunner.Market", symbol, slice.TimestampUtc, features));
            Publish(_eventFactory.CreateFeatureEvent("backtest", "L1L2BacktestRunner.Features", symbol, slice.TimestampUtc, features));

            foreach (var entry in pendingEntries.Values.Where(x => x.SubmitBarIndex == index).ToArray())
            {
                Publish(replayExecutionEngine.Submit(new ExecutionEngineSubmitRequest(
                    entry.Intent,
                    slice.TimestampUtc,
                    "L1L2BacktestRunner.Entry")));
            }

            var reconcileResult = replayExecutionEngine.Reconcile(new ExecutionEngineReconcileRequest(
                slice.TimestampUtc,
                Source: "L1L2BacktestRunner.Reconcile",
                DataSlice: slice,
                Symbol: symbol,
                DueDelistEvents: [],
                BorrowLocateProfile: new ReplayBorrowLocateProfileRow(slice.TimestampUtc, symbol, 0, true, 0, "l1l2-backtest")));
            Publish(reconcileResult.Events);

            ProcessReplayFills(
                reconcileResult.Events.OfType<FillEvent>().ToArray(),
                index,
                pendingEntries,
                ref activeTrade,
                trades);

            if (activeTrade is not null)
            {
                activeTrade.ObserveBar(currentBar);
                EvaluateAndDispatchConduct(replayExecutionEngine, slice, bars, index, features, activeTrade);
                if (activeTrade.IsClosed)
                {
                    trades.Add(activeTrade.ToTradeResult());
                    activeTrade = null;
                }
            }

            if (features.IsReady)
            {
                var decision = signalEngine.Evaluate(features, _config, symbol, historicalBars: slice.HistoricalBars);
                if (L1L2BacktestRunner.TryCreateSignalFromDecision(index, currentBar.Timestamp, features, decision, out var signal))
                {
                    signalCount++;

                    if (activeTrade is not null || pendingEntries.Count > 0 || index + 1 >= slices.Length)
                    {
                        continue;
                    }

                    var canonicalIntent = CreateEntryIntent(symbol, slice, features, signal);
                    acceptedEntryIntents.Add(canonicalIntent);
                    pendingEntries[canonicalIntent.IntentId] = new PendingReplayEntry(signal, canonicalIntent, index + 1);

                    Publish(_eventFactory.CreateSignalEvent(
                        "backtest",
                        "L1L2BacktestRunner.Signal",
                        symbol,
                        slice.TimestampUtc,
                        decision,
                        "accepted",
                        canonicalIntent.IntentId,
                        canonicalIntent));
                    Publish(_eventFactory.CreateRiskEvent(
                        "backtest",
                        "L1L2BacktestRunner.Risk",
                        symbol,
                        slice.TimestampUtc,
                        "approved",
                        signal.PositionSize,
                        signal.PositionSize,
                        signal.PositionSize * signal.RiskPerShare,
                        signal.RiskPerShare,
                        isApproved: true,
                        reason: "l1l2-signal-approved",
                        canonicalIntent));
                }
            }
        }

        if (activeTrade is not null)
        {
            ForceCloseOpenTrade(replayExecutionEngine, symbol, slices[^1], bars[^1], bars.Length - 1, activeTrade, trades);
        }

        var artifacts = ExportArtifacts(symbol, timeframe, trades, bars, outputDirectory);
        return new L1L2BacktestReplayExecutionResult(
            trades,
            acceptedEntryIntents,
            coverage,
            artifacts,
            signalCount,
            _stateCache.Snapshot());
    }

    private void EvaluateAndDispatchConduct(
        ReplayExecutionEngine replayExecutionEngine,
        StrategyDataSlice slice,
        BacktestBar[] bars,
        int index,
        V3LiveFeatureSnapshot features,
        ActiveReplayTrade activeTrade)
    {
        if (activeTrade.OpenQuantity <= 0)
        {
            return;
        }

        var state = activeTrade.ToFilledTradeState(features.Price > 0 ? features.Price : bars[index].Close);
        var frame = CreateConductFrame(features, bars, index);
        var evaluation = PostFillConductExecutor.Execute(
            state,
            frame,
            activeTrade.ConductPolicy,
            new DailyTradeConductOptions(
                ContinuationConfirmedOverride: false,
                SourcePolicy: "L1L2BacktestRunner"));

        activeTrade.ApplyStatePatch(evaluation.StatePatch);

        var riskMultiple = activeTrade.Signal.RiskPerShare > 0
            ? activeTrade.ComputeRiskMultiple(frame.CurrentPrice)
            : (double?)null;
        Publish(_eventFactory.CreateConductEvent(
            "backtest",
            "L1L2BacktestRunner.Conduct",
            activeTrade.Symbol,
            activeTrade.EntryIntentId,
            slice.TimestampUtc,
            evaluation.Decision.Kind.ToString(),
            evaluation.Decision.Reason,
            activeTrade.PolicyName,
            evaluation.Decision.Price ?? frame.CurrentPrice,
            riskMultiple,
            activeTrade.EntryIntent));

        foreach (var command in evaluation.Commands)
        {
            switch (command.Kind)
            {
                case ConductExecutionCommandKind.ReplaceProtectiveOrder:
                    ReplaceProtectiveOrder(replayExecutionEngine, slice, activeTrade, command);
                    break;

                case ConductExecutionCommandKind.CancelObsoleteExitOrder:
                    CancelProtectiveOrder(replayExecutionEngine, slice, activeTrade, command.SourcePolicy);
                    break;

                case ConductExecutionCommandKind.SubmitPartialExit:
                case ConductExecutionCommandKind.SubmitFullExit:
                    SubmitExitOrder(replayExecutionEngine, slice, activeTrade, command);
                    break;
            }
        }
    }

    private void ReplaceProtectiveOrder(
        ReplayExecutionEngine replayExecutionEngine,
        StrategyDataSlice slice,
        ActiveReplayTrade activeTrade,
        ConductExecutionCommand command)
    {
        CancelProtectiveOrder(replayExecutionEngine, slice, activeTrade, command.SourcePolicy);

        var protectiveIntent = CreateExitIntent(
            activeTrade,
            slice.TimestampUtc,
            Math.Max(1, command.Quantity),
            OrderType.Stop,
            stopPrice: command.StopPrice ?? activeTrade.StopPrice,
            source: $"L1L2BacktestRunner.Conduct:{command.SourcePolicy}:protective",
            detailReason: command.Reason);
        activeTrade.RegisterProtectiveIntent(protectiveIntent, command.Reason);
        Publish(replayExecutionEngine.Submit(new ExecutionEngineSubmitRequest(
            protectiveIntent,
            slice.TimestampUtc,
            "L1L2BacktestRunner.Protective")));
    }

    private void SubmitExitOrder(
        ReplayExecutionEngine replayExecutionEngine,
        StrategyDataSlice slice,
        ActiveReplayTrade activeTrade,
        ConductExecutionCommand command)
    {
        if (activeTrade.HasWorkingExitIntent)
        {
            return;
        }

        CancelProtectiveOrder(replayExecutionEngine, slice, activeTrade, command.SourcePolicy);

        var quantity = command.Kind == ConductExecutionCommandKind.SubmitFullExit
            ? (int)Math.Round(activeTrade.OpenQuantity, MidpointRounding.AwayFromZero)
            : Math.Clamp(command.Quantity, 1, Math.Max(1, (int)Math.Round(activeTrade.OpenQuantity, MidpointRounding.AwayFromZero)));
        var exitIntent = CreateExitIntent(
            activeTrade,
            slice.TimestampUtc,
            quantity,
            OrderType.Market,
            stopPrice: 0,
            source: $"L1L2BacktestRunner.Conduct:{command.SourcePolicy}:{command.Kind}",
            detailReason: command.Reason);
        activeTrade.RegisterExitIntent(exitIntent, command.Reason);
        Publish(replayExecutionEngine.Submit(new ExecutionEngineSubmitRequest(
            exitIntent,
            slice.TimestampUtc,
            "L1L2BacktestRunner.Exit")));
    }

    private void CancelProtectiveOrder(
        ReplayExecutionEngine replayExecutionEngine,
        StrategyDataSlice slice,
        ActiveReplayTrade activeTrade,
        string sourcePolicy)
    {
        if (string.IsNullOrWhiteSpace(activeTrade.ProtectiveIntentId))
        {
            return;
        }

        Publish(replayExecutionEngine.Cancel(new ExecutionEngineCancelRequest(
            activeTrade.ProtectiveIntentId,
            activeTrade.Symbol,
            slice.TimestampUtc,
            $"L1L2BacktestRunner.CancelProtective:{sourcePolicy}")));
        activeTrade.ClearProtectiveIntent();
    }

    private void ProcessReplayFills(
        IReadOnlyList<FillEvent> fills,
        int barIndex,
        Dictionary<string, PendingReplayEntry> pendingEntries,
        ref ActiveReplayTrade? activeTrade,
        List<BacktestTradeResult> trades)
    {
        foreach (var fill in fills.OrderBy(x => x.TimestampUtc))
        {
            if (activeTrade is null
                && pendingEntries.TryGetValue(fill.IntentId, out var pendingEntry))
            {
                activeTrade = new ActiveReplayTrade(pendingEntry, fill, barIndex);
                pendingEntries.Remove(fill.IntentId);
                if (activeTrade.IsClosed)
                {
                    trades.Add(activeTrade.ToTradeResult());
                    activeTrade = null;
                }
                continue;
            }

            if (activeTrade is null)
            {
                continue;
            }

            if (string.Equals(fill.IntentId, activeTrade.EntryIntentId, StringComparison.OrdinalIgnoreCase))
            {
                activeTrade.ApplyEntryFill(fill, barIndex);
            }
            else if (activeTrade.TryApplyExitFill(fill, barIndex))
            {
                if (activeTrade.IsClosed)
                {
                    trades.Add(activeTrade.ToTradeResult());
                    activeTrade = null;
                }
            }
        }
    }

    private void ForceCloseOpenTrade(
        ReplayExecutionEngine replayExecutionEngine,
        string symbol,
        StrategyDataSlice slice,
        BacktestBar lastBar,
        int barIndex,
        ActiveReplayTrade activeTrade,
        List<BacktestTradeResult> trades)
    {
        CancelProtectiveOrder(replayExecutionEngine, slice, activeTrade, "forced-close");

        var exitIntent = CreateExitIntent(
            activeTrade,
            slice.TimestampUtc,
            Math.Max(1, (int)Math.Round(activeTrade.OpenQuantity, MidpointRounding.AwayFromZero)),
            OrderType.Market,
            stopPrice: 0,
            source: "L1L2BacktestRunner.ForcedClose",
            detailReason: "end-of-data");
        activeTrade.RegisterExitIntent(exitIntent, "TIME_END_OF_DATA");
        Publish(replayExecutionEngine.Submit(new ExecutionEngineSubmitRequest(
            exitIntent,
            slice.TimestampUtc,
            "L1L2BacktestRunner.ForcedClose")));

        var reconcileResult = replayExecutionEngine.Reconcile(new ExecutionEngineReconcileRequest(
            slice.TimestampUtc,
            Source: "L1L2BacktestRunner.ForcedClose.Reconcile",
            DataSlice: slice,
            Symbol: symbol,
            DueDelistEvents: [],
            BorrowLocateProfile: new ReplayBorrowLocateProfileRow(slice.TimestampUtc, symbol, 0, true, 0, "l1l2-backtest")));
        Publish(reconcileResult.Events);
        foreach (var fill in reconcileResult.Events.OfType<FillEvent>().OrderBy(x => x.TimestampUtc))
        {
            if (activeTrade.TryApplyExitFill(fill, barIndex))
            {
                break;
            }
        }

        if (!activeTrade.IsClosed)
        {
            trades.Add(activeTrade.ForceClose(lastBar, barIndex, "TIME_END_OF_DATA"));
            return;
        }

        trades.Add(activeTrade.ToTradeResult());
    }

    private L1L2BacktestArtifacts ExportArtifacts(
        string symbol,
        string timeframe,
        IReadOnlyList<BacktestTradeResult> trades,
        BacktestBar[] bars,
        string outputDirectory)
    {
        string? comparisonReportPath = null;
        string? comparisonReportJsonPath = null;
        string? lifecycleCertificationPath = null;
        string? lifecycleCertificationSummaryPath = null;

        if (trades.Count > 0)
        {
            var enrichedBars = TechnicalIndicators.EnrichWithIndicators(bars);
            L1L2Enrichment.Enrich(symbol, enrichedBars, _depthLevels);
            var stats = BacktestEngine.ComputeStatistics(trades, _initialCapital);
            var summaryRow = new StrategyComparisonRow(
                Strategy: "L1L2Replay",
                Variant: "phase6",
                Symbols: 1,
                Trades: trades.Count,
                WinRate: stats.WinRate,
                ProfitFactor: stats.ProfitFactor,
                Sharpe: stats.Sharpe,
                TotalPnl: stats.TotalPnl,
                MaxDrawdown: stats.MaxDrawdown,
                AvgWin: stats.AvgWin,
                AvgLoss: stats.AvgLoss,
                ExpectancyR: stats.ExpectancyR,
                MeetsMinTrades: trades.Count >= 1,
                GovernorStops: stats.Governor.HaltedBucketCount,
                GovernorStopReason: stats.Governor.StopReason,
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

            comparisonReportPath = BacktestTradeReplayReportWriter.WriteComparisonReport(
                [summaryRow],
                trades.Select(trade => ("L1L2Replay", "phase6", symbol, trade)).ToArray(),
                new Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)>(StringComparer.OrdinalIgnoreCase)
                {
                    [symbol] = (enrichedBars, null, null, null, null)
                },
                Path.Combine(outputDirectory, "reports"),
                _selfLearning,
                _realityModelProfile);
            comparisonReportJsonPath = Path.ChangeExtension(comparisonReportPath, ".json");
        }

        var lifecycleRows = _lifecycleCertificationTracker.SnapshotRows();
        var lifecycleSummary = _lifecycleCertificationTracker.BuildSummary("backtest");
        var lifecycleArtifacts = _runtimeArtifactReporter.ExportOrderLifecycleCertificationArtifact(
            outputDirectory,
            "backtest",
            lifecycleRows,
            lifecycleSummary);
        lifecycleCertificationPath = lifecycleArtifacts?.RowsPath;
        lifecycleCertificationSummaryPath = lifecycleArtifacts?.SummaryPath;

        return CreateArtifacts(
            symbol,
            timeframe,
            comparisonReportPath,
            comparisonReportJsonPath,
            outputDirectory,
            lifecycleCertificationPath,
            lifecycleCertificationSummaryPath);
    }

    private L1L2BacktestArtifacts CreateArtifacts(
        string symbol,
        string timeframe,
        string? comparisonReportPath,
        string? comparisonReportJsonPath,
        string? outputDirectory = null,
        string? lifecycleCertificationPath = null,
        string? lifecycleCertificationSummaryPath = null)
        => new(
            OutputDirectory: outputDirectory ?? Path.Combine("artifacts", "tmp", "l1l2_backtest", symbol.Trim().ToUpperInvariant(), timeframe),
            TradingEventJournalPath: _eventJournal.CurrentFilePath,
            ComparisonReportPath: comparisonReportPath,
            ComparisonReportJsonPath: comparisonReportJsonPath,
            OrderLifecycleCertificationPath: lifecycleCertificationPath,
            OrderLifecycleCertificationSummaryPath: lifecycleCertificationSummaryPath,
            EventCount: _stateCache.EventCount);

    private ReplayExecutionEngine CreateReplayExecutionEngine()
    {
        var simulator = new ReplayExecutionSimulator(
            _realityModelProfile.InitialCash,
            _realityModelProfile.CommissionPerUnit,
            _realityModelProfile.SlippageBps,
            [],
            ReplayPriceNormalizationMode.Raw,
            initialMarginRate: _realityModelProfile.InitialMarginRate,
            maintenanceMarginRate: _realityModelProfile.MaintenanceMarginRate,
            maxGrossExposurePctOfInitialCash: _realityModelProfile.MaxGrossExposurePctOfInitialCash,
            maxNetExposurePctOfInitialCash: _realityModelProfile.MaxNetExposurePctOfInitialCash,
            secFeeRatePerDollar: _realityModelProfile.SecFeeRatePerDollar,
            tafFeePerShare: _realityModelProfile.TafFeePerShare,
            tafFeeCapPerOrder: _realityModelProfile.TafFeeCapPerOrder,
            exchangeFeePerShare: _realityModelProfile.ExchangeFeePerShare,
            maxFillParticipationRate: _realityModelProfile.MaxFillParticipationRate,
            replayPriceIncrement: _realityModelProfile.PriceIncrement,
            enforceQueuePriority: _realityModelProfile.EnforceQueuePriority,
            settlementLagDays: _realityModelProfile.SettlementLagDays,
            enforceSettledCash: _realityModelProfile.EnforceSettledCash);
        return new ReplayExecutionEngine(simulator, _config, string.Empty, "backtest", _lifecycleCertificationTracker, _realityModelProfile);
    }

    private ExecutionIntent CreateEntryIntent(
        string symbol,
        StrategyDataSlice slice,
        V3LiveFeatureSnapshot features,
        L1L2Signal signal)
    {
        var side = signal.Side == TradeSide.Long ? OrderSide.Buy : OrderSide.Sell;
        var liveIntent = new LiveOrderIntent(
            IntentId: $"BT-L1L2::{symbol}::{signal.Timestamp:yyyyMMddHHmmss}::{signal.BarIndex}::{side}",
            TimestampUtc: slice.TimestampUtc,
            Symbol: symbol,
            Side: side,
            OrderType: OrderType.Market,
            TimeInForce: OrderTimeInForce.Day,
            Quantity: signal.PositionSize,
            EntryPrice: signal.EntryPrice > 0 ? signal.EntryPrice : features.Price,
            StopPrice: signal.StopPrice,
            TakeProfitPrice: signal.Side == TradeSide.Long
                ? signal.EntryPrice + (_config.Tp2R * signal.RiskPerShare)
                : signal.EntryPrice - (_config.Tp2R * signal.RiskPerShare),
            EstimatedRiskDollars: signal.PositionSize * signal.RiskPerShare,
            Setup: signal.Setup,
            Source: "L1L2BacktestRunner.Signal",
            Purpose: LiveOrderIntentPurpose.Entry);
        return ExecutionIntentAdapter.AttachCanonicalIntent(
            liveIntent,
            _config,
            new ExecutionIntentBuildContext(
                Mode: "backtest",
                RuntimeProfile: "phase6-l1l2-replay",
                ExecutionAdapterId: "replay-execution",
                MarketDataAdapterId: "replay-market-data",
                RealityModelProfile: _realityModelProfile,
                ReasonCodes: ["l1l2", "phase6", "replay-entry"])).CanonicalIntent!;
    }

    private ExecutionIntent CreateExitIntent(
        ActiveReplayTrade activeTrade,
        DateTime timestampUtc,
        int quantity,
        OrderType orderType,
        double stopPrice,
        string source,
        string detailReason)
    {
        var side = activeTrade.Signal.Side == TradeSide.Long ? OrderSide.Sell : OrderSide.Buy;
        var liveIntent = new LiveOrderIntent(
            IntentId: $"{activeTrade.EntryIntentId}::EXIT::{Guid.NewGuid():N}",
            TimestampUtc: timestampUtc,
            Symbol: activeTrade.Symbol,
            Side: side,
            OrderType: orderType,
            TimeInForce: OrderTimeInForce.Day,
            Quantity: quantity,
            EntryPrice: activeTrade.EntryAverageFillPrice,
            StopPrice: stopPrice,
            TakeProfitPrice: 0,
            EstimatedRiskDollars: quantity * activeTrade.Signal.RiskPerShare,
            Setup: activeTrade.Signal.Setup,
            Source: source,
            Purpose: LiveOrderIntentPurpose.Exit);
        return ExecutionIntentAdapter.AttachCanonicalIntent(
            liveIntent,
            _config,
            new ExecutionIntentBuildContext(
                Mode: "backtest",
                RuntimeProfile: "phase6-l1l2-replay",
                ExecutionAdapterId: "replay-execution",
                MarketDataAdapterId: "replay-market-data",
                RealityModelProfile: _realityModelProfile,
                ParentIntentId: activeTrade.EntryIntentId,
                ReasonCodes: ["l1l2", "phase6", detailReason])).CanonicalIntent!;
    }

    private ConductMarketFrame CreateConductFrame(
        V3LiveFeatureSnapshot features,
        BacktestBar[] bars,
        int index)
    {
        var currentBar = bars[index];
        var completedCandles = index == 0
            ? Array.Empty<LiveCandle>()
            :
            [
                new LiveCandle(
                    bars[index - 1].Timestamp,
                    bars[index - 1].Open,
                    bars[index - 1].High,
                    bars[index - 1].Low,
                    bars[index - 1].Close,
                    bars[index - 1].Volume,
                    1)
            ];

        var candles = new V3LiveCandleSnapshot(
            string.Empty,
            currentBar.Timestamp,
            new Dictionary<int, V3LiveTimeframeCandleData>
            {
                [60] = new(
                    60,
                    completedCandles,
                    new LiveCandle(
                        currentBar.Timestamp,
                        currentBar.Open,
                        currentBar.High,
                        currentBar.Low,
                        currentBar.Close,
                        currentBar.Volume,
                        1))
            });

        return ConductMarketFrame.FromLiveInputs(features, candles, timestampUtc: currentBar.Timestamp);
    }

    private void Publish(ExecutionEngineSubmitResult submitResult) => Publish(submitResult.Events);

    private void Publish(ExecutionEngineOperationResult operationResult) => Publish(operationResult.Events);

    private void Publish(IEnumerable<ICanonicalEvent> events)
    {
        foreach (var canonicalEvent in events)
        {
            Publish(canonicalEvent);
        }
    }

    private void Publish(ICanonicalEvent canonicalEvent)
    {
        _eventBus.Publish(canonicalEvent);
    }

    internal sealed record PendingReplayEntry(
        L1L2Signal Signal,
        ExecutionIntent Intent,
        int SubmitBarIndex);

    internal sealed class ActiveReplayTrade
    {
        private const double QuantityEpsilon = 1e-9;
        private readonly Dictionary<string, string> _exitReasonsByIntentId = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _workingExitIntentIds = new(StringComparer.OrdinalIgnoreCase);

        internal ActiveReplayTrade(PendingReplayEntry pendingEntry, FillEvent entryFill, int barIndex)
        {
            PendingEntry = pendingEntry;
            Symbol = pendingEntry.Intent.Symbol;
            PolicyName = pendingEntry.Intent.Conduct.PolicyName;
            ConductPolicy = pendingEntry.Intent.Conduct.Policy;
            StopPrice = pendingEntry.Signal.StopPrice;
            ManagedTrailingStopPrice = pendingEntry.Signal.StopPrice;
            MostFavorablePrice = entryFill.FillPrice;
            MostAdversePrice = entryFill.FillPrice;
            ApplyEntryFill(entryFill, barIndex);
        }

        private PendingReplayEntry PendingEntry { get; }

        public string Symbol { get; }

        public L1L2Signal Signal => PendingEntry.Signal;

        public ExecutionIntent EntryIntent => PendingEntry.Intent;

        public string EntryIntentId => PendingEntry.Intent.IntentId;

        public string PolicyName { get; }

        public ConductPolicy ConductPolicy { get; }

        public string? ProtectiveIntentId { get; private set; }

        public DateTime EntryTimeUtc { get; private set; }

        public DateTime? ExitTimeUtc { get; private set; }

        public int EntryBarIndex { get; private set; } = -1;

        public int ExitBarIndex { get; private set; } = -1;

        public double EntryAverageFillPrice { get; private set; }

        public double EntryFilledQuantity { get; private set; }

        public double OpenQuantity { get; private set; }

        public double ClosedQuantity { get; private set; }

        public double WeightedExitNotional { get; private set; }

        public double RealizedGrossPnl { get; private set; }

        public double TotalCommission { get; private set; }

        public double MostFavorablePrice { get; private set; }

        public double MostAdversePrice { get; private set; }

        public double StopPrice { get; private set; }

        public double ManagedTrailingStopPrice { get; private set; }

        public bool BreakevenActivated { get; private set; }

        public bool Tp1Activated { get; private set; }

        public bool ProfitExtensionArmed { get; private set; }

        public bool IsClosed => OpenQuantity <= QuantityEpsilon && ClosedQuantity > QuantityEpsilon;

        public bool HasWorkingExitIntent => _workingExitIntentIds.Count > 0;

        public void ObserveBar(BacktestBar bar)
        {
            if (Signal.Side == TradeSide.Long)
            {
                MostFavorablePrice = Math.Max(MostFavorablePrice, bar.High);
                MostAdversePrice = Math.Min(MostAdversePrice, bar.Low);
            }
            else
            {
                MostFavorablePrice = Math.Min(MostFavorablePrice, bar.Low);
                MostAdversePrice = Math.Max(MostAdversePrice, bar.High);
            }
        }

        public void ApplyEntryFill(FillEvent fill, int barIndex)
        {
            var totalQuantity = EntryFilledQuantity + fill.FillQuantity;
            EntryAverageFillPrice = totalQuantity > 0
                ? ((EntryAverageFillPrice * EntryFilledQuantity) + (fill.FillPrice * fill.FillQuantity)) / totalQuantity
                : fill.FillPrice;
            EntryFilledQuantity = totalQuantity;
            OpenQuantity += fill.FillQuantity;
            TotalCommission += fill.CommissionUsd;
            EntryTimeUtc = EntryTimeUtc == default ? fill.TimestampUtc : EntryTimeUtc;
            EntryBarIndex = EntryBarIndex < 0 ? barIndex : EntryBarIndex;
        }

        public void RegisterProtectiveIntent(ExecutionIntent intent, string reason)
        {
            ProtectiveIntentId = intent.IntentId;
            _exitReasonsByIntentId[intent.IntentId] = reason;
        }

        public void ClearProtectiveIntent()
        {
            ProtectiveIntentId = null;
        }

        public void RegisterExitIntent(ExecutionIntent intent, string reason)
        {
            _workingExitIntentIds.Add(intent.IntentId);
            _exitReasonsByIntentId[intent.IntentId] = reason;
        }

        public bool TryApplyExitFill(FillEvent fill, int barIndex)
        {
            if (!_workingExitIntentIds.Contains(fill.IntentId)
                && !string.Equals(fill.IntentId, ProtectiveIntentId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var pnlPerShare = Signal.Side == TradeSide.Long
                ? fill.FillPrice - EntryAverageFillPrice
                : EntryAverageFillPrice - fill.FillPrice;
            RealizedGrossPnl += pnlPerShare * fill.FillQuantity;
            OpenQuantity = Math.Max(0, OpenQuantity - fill.FillQuantity);
            ClosedQuantity += fill.FillQuantity;
            WeightedExitNotional += fill.FillPrice * fill.FillQuantity;
            TotalCommission += fill.CommissionUsd;
            ExitTimeUtc = fill.TimestampUtc;
            ExitBarIndex = barIndex;

            if (!fill.IsPartialFill)
            {
                _workingExitIntentIds.Remove(fill.IntentId);
                if (string.Equals(fill.IntentId, ProtectiveIntentId, StringComparison.OrdinalIgnoreCase))
                {
                    ProtectiveIntentId = null;
                }
            }

            return true;
        }

        public void ApplyStatePatch(ConductStatePatch patch)
        {
            if (!double.IsNaN(patch.PeakPrice) && patch.PeakPrice > 0)
            {
                MostFavorablePrice = patch.PeakPrice;
            }

            StopPrice = patch.StopPrice;
            ManagedTrailingStopPrice = patch.TrailingStopPrice;
            BreakevenActivated = patch.BreakevenActivated;
            Tp1Activated = patch.Tp1Activated;
            ProfitExtensionArmed = patch.ProfitExtensionArmed;
        }

        public FilledTradeState ToFilledTradeState(double currentPrice)
        {
            var takeProfitPrice = Signal.Side == TradeSide.Long
                ? EntryAverageFillPrice + (Signal.RiskPerShare * 2.0)
                : EntryAverageFillPrice - (Signal.RiskPerShare * 2.0);
            var unrealizedPnl = Signal.Side == TradeSide.Long
                ? (currentPrice - EntryAverageFillPrice) * OpenQuantity
                : (EntryAverageFillPrice - currentPrice) * OpenQuantity;
            var unrealizedPeak = Signal.Side == TradeSide.Long
                ? Math.Max(0, (MostFavorablePrice - EntryAverageFillPrice) * OpenQuantity)
                : Math.Max(0, (EntryAverageFillPrice - MostFavorablePrice) * OpenQuantity);

            return new FilledTradeState(
                IntentId: EntryIntentId,
                Account: string.Empty,
                Symbol: Symbol,
                Profile: LiveStrategyProfile.Default,
                Side: Signal.Side == TradeSide.Long ? PositionSide.Long : PositionSide.Short,
                FilledQuantity: EntryFilledQuantity,
                OpenQuantity: OpenQuantity,
                AverageFillPrice: EntryAverageFillPrice,
                EntryUtc: EntryTimeUtc,
                EntryAtr14: Signal.AtrValue,
                RiskPerShare: Signal.RiskPerShare,
                StopPrice: StopPrice,
                TakeProfitPrice: takeProfitPrice,
                RealizedPnlUsd: RealizedGrossPnl,
                UnrealizedPnlUsd: unrealizedPnl,
                UnrealizedPnlPeakUsd: unrealizedPeak,
                MostFavorablePrice: MostFavorablePrice,
                MostAdversePrice: MostAdversePrice,
                ManagedTrailingStopPrice: ManagedTrailingStopPrice,
                BreakevenActivated: BreakevenActivated,
                Tp1Activated: Tp1Activated,
                ProfitExtensionArmed: ProfitExtensionArmed,
                StrategyBucket: Signal.Setup);
        }

        public double? ComputeRiskMultiple(double currentPrice)
        {
            if (Signal.RiskPerShare <= 0)
            {
                return null;
            }

            return Signal.Side == TradeSide.Long
                ? (currentPrice - EntryAverageFillPrice) / Signal.RiskPerShare
                : (EntryAverageFillPrice - currentPrice) / Signal.RiskPerShare;
        }

        public BacktestTradeResult ForceClose(BacktestBar lastBar, int barIndex, string reason)
        {
            var pnlPerShare = Signal.Side == TradeSide.Long
                ? lastBar.Close - EntryAverageFillPrice
                : EntryAverageFillPrice - lastBar.Close;
            var quantityToClose = Math.Max(0, OpenQuantity);
            RealizedGrossPnl += pnlPerShare * quantityToClose;
            ClosedQuantity += quantityToClose;
            WeightedExitNotional += lastBar.Close * quantityToClose;
            OpenQuantity = 0;
            ExitTimeUtc = lastBar.Timestamp;
            ExitBarIndex = barIndex;
            _exitReasonsByIntentId[$"FORCED::{barIndex}"] = reason;
            return ToTradeResult(reasonOverride: reason);
        }

        public BacktestTradeResult ToTradeResult(string? reasonOverride = null)
        {
            var exitPrice = ClosedQuantity > QuantityEpsilon
                ? WeightedExitNotional / ClosedQuantity
                : EntryAverageFillPrice;
            var pnl = RealizedGrossPnl - TotalCommission;
            var peakR = Signal.RiskPerShare > 0
                ? (Signal.Side == TradeSide.Long
                    ? (MostFavorablePrice - EntryAverageFillPrice) / Signal.RiskPerShare
                    : (EntryAverageFillPrice - MostFavorablePrice) / Signal.RiskPerShare)
                : 0.0;
            var pnlR = Signal.RiskPerShare > 0 && EntryFilledQuantity > QuantityEpsilon
                ? pnl / (Signal.RiskPerShare * EntryFilledQuantity)
                : 0.0;
            var exitReasonLabel = reasonOverride
                ?? _exitReasonsByIntentId.Values.LastOrDefault()
                ?? "TIME";

            return new BacktestTradeResult(
                EntryBar: EntryBarIndex,
                EntryTime: EntryTimeUtc,
                ExitBar: ExitBarIndex < 0 ? EntryBarIndex : ExitBarIndex,
                ExitTime: ExitTimeUtc ?? EntryTimeUtc,
                Side: Signal.Side,
                EntryPrice: EntryAverageFillPrice,
                ExitPrice: exitPrice,
                StopPrice: Signal.StopPrice,
                PositionSize: (int)Math.Round(Math.Max(EntryFilledQuantity, ClosedQuantity), MidpointRounding.AwayFromZero),
                Pnl: pnl,
                PnlR: pnlR,
                ExitReason: L1L2BacktestRunner.MapExitReason(exitReasonLabel),
                PeakR: peakR,
                BarsHeld: Math.Max(0, (ExitBarIndex < 0 ? EntryBarIndex : ExitBarIndex) - EntryBarIndex),
                SubStrategy: Signal.Setup);
        }
    }
}

internal sealed record L1L2BacktestReplayExecutionResult(
    IReadOnlyList<BacktestTradeResult> Trades,
    IReadOnlyList<ExecutionIntent> AcceptedEntryIntents,
    L1L2CoverageStats Coverage,
    L1L2BacktestArtifacts Artifacts,
    int SignalCount = 0,
    TradingStateCacheSnapshot? EventCacheSnapshot = null);

