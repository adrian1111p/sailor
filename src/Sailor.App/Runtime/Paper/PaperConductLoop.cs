using Sailor.App.Backtest;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Runtime.Ui;
using Sailor.App.Strategy.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed class PaperConductLoop
{
    private readonly Action<string> _log;
    private readonly OrderLedgerStore _ledger;
    private readonly TradeLifecycleRegistryStore _tradeRegistry;
    private readonly ScannerSlotManager? _scannerSlotManager;
    private readonly SevereDisconnectRecoveryOrchestrator? _severeDisconnectRecoveryOrchestrator;
    private readonly ManualBrokerPositionRuntimeSync? _manualBrokerPositionRuntimeSync;
    private readonly SailorAppSettings _settings;
    private readonly StrategyLifecyclePolicyResolver _lifecyclePolicyResolver;

    public PaperConductLoop(
        SailorRuntimeMode mode,
        Action<string> log,
        SailorAppSettings settings,
        StrategyLifecyclePolicyResolver lifecyclePolicyResolver,
        TradeLifecycleRegistryStore? tradeRegistry = null,
        ScannerSlotManager? scannerSlotManager = null,
        SevereDisconnectRecoveryOrchestrator? severeDisconnectRecoveryOrchestrator = null,
        ManualBrokerPositionRuntimeSync? manualBrokerPositionRuntimeSync = null)
    {
        _log = log;
        _settings = settings;
        _lifecyclePolicyResolver = lifecyclePolicyResolver;
        _ledger = new OrderLedgerStore(mode);
        _tradeRegistry = tradeRegistry ?? new TradeLifecycleRegistryStore(mode);
        _scannerSlotManager = scannerSlotManager;
        _severeDisconnectRecoveryOrchestrator = severeDisconnectRecoveryOrchestrator;
        _manualBrokerPositionRuntimeSync = manualBrokerPositionRuntimeSync;
    }

    public async Task<PaperRuntimeHostResult> RunAsync(
        List<PaperSymbolSession> sessions,
        IOrderRouter router,
        PaperRuntimeHostRequest request,
        SailorRuntimeState runtimeState,
        RuntimeHealthMonitor healthMonitor,
        ConnectionRecoveryService recoveryService,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        int decisionCount = 0;
        int intentCount = 0;
        int routedCount = 0;
        int filledOrAssumedFillCount = 0;
        bool recoveryAttempted = false;
        HarshConductTradeLogWriter? harshLogWriter = request.HarshConductTestEnabled
            ? new HarshConductTradeLogWriter(request.RuntimeOptions.Mode)
            : null;
        HarshConductTradeTracker? harshTracker = request.HarshConductTestEnabled
            ? new HarshConductTradeTracker()
            : null;
        int harshGovernanceStops = 0;
        string harshGovernanceReason = "none";

        if (sessions.Count == 0)
        {
            warnings.Add("No active symbol sessions were created.");
            return new PaperRuntimeHostResult(Array.Empty<string>(), 0, 0, 0, 0, warnings);
        }

        _log($"{request.RuntimeOptions.ModeName.ToUpperInvariant()} conduct loop");
        _log("------------------");
        _log($"cadence={request.CadenceSeconds}s iterations={request.MaxIterations} sendOrders={request.SendOrders} dryRun={request.DryRun} canOpenEntries={request.CanOpenEntries} forceFlatNow={request.ForceFlatNow} enforceMaxNotional={request.EnforceMaxOrderNotional} maxNotional={request.MaxOrderNotional:F2}");
        _log($"reconnectAttempts={request.ReconnectAttempts} reconnectBackoffSeconds={request.ReconnectBackoffSeconds} simulateDisconnectAtIteration={request.SimulateDisconnectAtIteration}");
        _log($"router={router.RouterName}");
        if (request.HarshConductTestEnabled)
        {
            _log($"SAILOR-064 harsh conduct test is active: targetTrades={request.HarshConductTargetTrades} defaultQuantity={request.HarshConductDefaultQuantity} replenishEverySeconds={request.HarshConductReplenishmentIntervalSeconds} bypassStrategyEntries=True bypassStaleGate=True.");
            if (harshLogWriter is not null)
            {
                _log($"SAILOR-064 trade log CSV: {harshLogWriter.TradeCsvPath}");
                _log($"SAILOR-064 latest trade log CSV: {harshLogWriter.LatestTradeCsvPath}");
                _log($"SAILOR-064 summary CSV: {harshLogWriter.SummaryCsvPath}");
                _log($"SAILOR-064 latest summary CSV: {harshLogWriter.LatestSummaryCsvPath}");
            }
        }
        _log("SAILOR-054/062 lifecycle entry gates are active: default single-lifecycle, V21-V24 multi-cycle before LastEntryMinute, manual/unknown broker positions strategy-managed.");
        if (_scannerSlotManager is not null)
        {
            _log($"SAILOR-055 scanner slot replenishment gate is active: {_scannerSlotManager.ToDisplayString()}");
        }
        if (_severeDisconnectRecoveryOrchestrator is not null)
        {
            _log($"SAILOR-056 severe disconnect recovery orchestrator is active. Recovery JSON: {_severeDisconnectRecoveryOrchestrator.LatestJsonPath}");
        }
        if (request.SendOrders && request.BlockStaleHistoricalReplay)
        {
            _log($"SAILOR-058 live-paper stale historical replay guard is active: maxBarAgeMinutes={request.LiveBarMaxAgeMinutes} futureToleranceMinutes={request.LiveBarFutureToleranceMinutes}.");
        }

        using PaperLiveCandleRefreshService? liveCandleRefreshService = request.SendOrders && request.LiveCandleRefreshEnabled
            ? new PaperLiveCandleRefreshService(request)
            : null;

        if (liveCandleRefreshService is not null)
        {
            _log($"SAILOR-059 live paper per-iteration candle refresh is active: {liveCandleRefreshService.ToDisplayString()}.");
            _log("SAILOR-060 shared IBKR live market-data/history session is active: scanner/history/snapshot/refresh data requests use one serialized data client and do not reuse the order-router client id.");
            _log($"SAILOR-061 live refresh fallback and diagnostics are active: fallback={request.LiveCandleRefreshFallbackEnabled} diagnostics={request.LiveCandleRefreshDiagnosticsEnabled} closeOnlyAfterStale={request.LiveRefreshCloseOnlyAfterStale}.");
        }
        if (_manualBrokerPositionRuntimeSync is not null && request.ManualBrokerPositionsAreStrategyManaged)
        {
            _log($"SAILOR-062 manual TWS broker workflow is active: scannerEntriesAllowed={request.ManualBrokerPositionsAllowScannerEntries} strategyManaged={request.ManualBrokerPositionsAreStrategyManaged} monitorEnabled={request.ManualBrokerPositionMonitorEnabled} monitorEverySeconds={request.ManualBrokerPositionMonitorIntervalSeconds} monitorClientId={request.ConnectionOptions.ClientId + Math.Max(1, request.ManualBrokerPositionMonitorClientIdOffset)}.");
        }
        if (request.UiDesiredStateRoutingEnabled)
        {
            _log($"SAILOR-068 multi-strategy conduct routing is active: maxStrategies={request.UiDesiredStateMaxActiveStrategies}; unchecked open symbols are routed to exit/flatten, unchecked flat symbols are held inactive.");
        }

        _log(healthMonitor.SafetyState.ToDisplayString());
        _log("");

        DateTimeOffset? lastManualBrokerSyncUtc = null;

        for (int iteration = 1; iteration <= request.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runtimeState.SetStatus(SailorRuntimeStatus.Running, $"SAILOR-031 conduct iteration {iteration}.");

            if (request.SimulateDisconnectAtIteration == iteration)
            {
                RuntimeIncident? simulatedIncident = healthMonitor.MarkCloseOnly(
                    "simulated-disconnect",
                    $"Simulated broker disconnect requested at conduct iteration {iteration}.",
                    new[] { $"iteration={iteration}", "operator-test=--simulate-disconnect-at" });

                if (simulatedIncident is not null)
                {
                    warnings.Add(simulatedIncident.Message);
                    _log(simulatedIncident.ToDisplayString());
                    _log(healthMonitor.SafetyState.ToDisplayString());
                }

                await TryRecoverIfNeededAsync($"simulated disconnect at iteration {iteration}").ConfigureAwait(false);
            }

            DateTimeOffset heartbeatUtc = DateTimeOffset.UtcNow;
            _log($"Iteration {iteration}/{request.MaxIterations} heartbeatUtc={heartbeatUtc:O} safety={healthMonitor.SafetyState.Mode}");

            SailorUiDesiredStateRoutingSnapshot desiredRouting = SailorUiDesiredStateRouter.Load(
                request.UiDesiredStateRoutingEnabled,
                request.RuntimeOptions.Mode,
                request.Account,
                request.UiDesiredStateMaxActiveStrategies);
            if (request.UiDesiredStateRoutingEnabled)
            {
                _log($"SAILOR-068 desired-state routing iteration={iteration} {desiredRouting.ToSummaryString()}");
                foreach (string desiredWarning in desiredRouting.Warnings)
                {
                    warnings.Add(desiredWarning);
                    _log($"WARN: {desiredWarning}");
                }
            }

            if (_manualBrokerPositionRuntimeSync is not null
                && request.SendOrders
                && request.ManualBrokerPositionsAreStrategyManaged
                && request.ManualBrokerPositionMonitorEnabled
                && request.ReconcileBrokerStateAsync is not null
                && ShouldRunManualBrokerSync(iteration, heartbeatUtc, lastManualBrokerSyncUtc, request))
            {
                lastManualBrokerSyncUtc = heartbeatUtc;
                try
                {
                    ReconciliationResult brokerTruth = await request.ReconcileBrokerStateAsync(cancellationToken).ConfigureAwait(false);
                    if (ManualBrokerOrderWorkflow.AllowsStrategyEntries(brokerTruth))
                    {
                        ManualBrokerPositionSyncReport syncReport = _manualBrokerPositionRuntimeSync.Synchronize(sessions, request, brokerTruth, runtimeState);
                        _log($"SAILOR-062 {syncReport.ToSummaryString()}");
                        foreach (string syncEvent in syncReport.Events.Take(30))
                        {
                            _log($"manual-broker-sync: {syncEvent}");
                        }
                        if (syncReport.Events.Count > 30)
                        {
                            _log($"manual-broker-sync: ... {syncReport.Events.Count - 30} more");
                        }
                        foreach (string syncWarning in syncReport.Warnings)
                        {
                            warnings.Add(syncWarning);
                            _log($"WARN: {syncWarning}");
                        }
                    }
                    else
                    {
                        string warning = $"SAILOR-062 manual broker monitor did not accept broker state for strategy management: {ManualBrokerOrderWorkflow.ToEntryGateReason(brokerTruth)}";
                        warnings.Add(warning);
                        _log($"WARN: {warning}");
                    }
                }
                catch (Exception ex)
                {
                    string warning = $"SAILOR-062 manual broker monitor failed without stopping scanner/conduct loop: {ex.GetType().Name}: {ex.Message}";
                    warnings.Add(warning);
                    _log($"WARN: {warning}");
                }
            }

            if (liveCandleRefreshService is not null)
            {
                if (healthMonitor.SafetyState.CanRequestMarketData)
                {
                    IReadOnlyList<PaperLiveCandleRefreshResult> refreshResults = await liveCandleRefreshService
                        .RefreshAsync(sessions, request, iteration, heartbeatUtc, cancellationToken)
                        .ConfigureAwait(false);

                    LogLiveCandleRefreshResults(iteration, refreshResults, warnings);
                    if (request.SendOrders && refreshResults.Count > 0)
                    {
                        bool everyRefreshFailed = refreshResults.All(result => !result.Success);
                        bool anyFallbackOrRefreshCurrent = refreshResults.Any(result => result.Success && result.Current);
                        bool allDecisionFramesStale = sessions.All(session =>
                        {
                            PaperLiveBarCurrentness currentness = session.AssessLiveBarCurrentness(
                                heartbeatUtc,
                                Math.Max(1, request.LiveBarMaxAgeMinutes),
                                Math.Max(0, request.LiveBarFutureToleranceMinutes));
                            return !currentness.IsCurrent;
                        });

                        if (everyRefreshFailed && !anyFallbackOrRefreshCurrent && (!request.LiveRefreshCloseOnlyAfterStale || allDecisionFramesStale))
                        {
                            string message = $"SAILOR-061 live data refresh failed for all {refreshResults.Count} active symbol(s) and no current fallback bar remains usable. Runtime moved to CloseOnly; entries remain blocked until the shared IBKR data session is healthy.";
                            RuntimeIncident? incident = healthMonitor.MarkCloseOnly(
                                "shared-data-refresh-failed",
                                message,
                                refreshResults.SelectMany(result => result.Warnings).Append(message));
                            if (incident is not null)
                            {
                                warnings.Add(incident.Message);
                                _log(incident.ToDisplayString());
                                _log(healthMonitor.SafetyState.ToDisplayString());
                            }
                        }
                        else if (everyRefreshFailed && !anyFallbackOrRefreshCurrent)
                        {
                            string message = $"SAILOR-061 live data refresh failed for all {refreshResults.Count} active symbol(s), but at least one decision frame is still inside the live-bar age gate; runtime remains {healthMonitor.SafetyState.Mode} until stale expiry.";
                            warnings.Add(message);
                            _log($"WARN: {message}");
                        }
                    }
                }
                else
                {
                    _log($"SAILOR-059 candle-refresh skipped iteration={iteration}: runtime safety {healthMonitor.SafetyState.Mode} blocks market-data requests. {healthMonitor.SafetyState.Reason}");
                }
            }

            foreach (PaperSymbolSession session in sessions.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                SailorStrategyFrame frame = session.NextFrame(runtimeState, advanceCursor: liveCandleRefreshService is null);
                var bar = frame.LatestBar;
                var indicators = frame.LatestIndicators;
                if (bar is null || indicators is null)
                {
                    _log($"{session.Symbol}: waiting for bars/indicators.");
                    continue;
                }

                SailorStrategyPositionContext positionBefore = session.ToPositionContext();
                SailorStrategyDecision decision;

                if (request.UiDesiredStateRoutingEnabled)
                {
                    SailorUiDesiredStateRow? desiredRow = desiredRouting.FindRow(session.Symbol);
                    if (desiredRow?.DesiredTradeEnabled == true)
                    {
                        string desiredProfile = desiredRouting.ResolveProfileName(session.Symbol, session.ProfileName);
                        StrategyLifecyclePolicy nextPolicy = _lifecyclePolicyResolver.Resolve(desiredProfile, session.TradeOrigin);
                        if (session.TrySwitchStrategyProfile(_settings, desiredProfile, nextPolicy, out string switchMessage))
                        {
                            _log(switchMessage);
                        }
                    }
                }

                DateTimeOffset decisionClock = request.SendOrders ? DateTimeOffset.UtcNow : frame.Time;
                bool forceFlatDue = request.ForceFlatNow || MarketTime.GetEasternMinuteOfDay(decisionClock) >= request.RuntimeOptions.ForceFlatMinute;
                PaperLiveBarCurrentness? liveBarCurrentness = request.SendOrders && request.BlockStaleHistoricalReplay
                    ? session.AssessLiveBarCurrentness(
                        decisionClock,
                        Math.Max(1, request.LiveBarMaxAgeMinutes),
                        Math.Max(0, request.LiveBarFutureToleranceMinutes))
                    : null;

                bool harshForceEntry = request.HarshConductTestEnabled
                    && session.ScannerSlotActive
                    && !session.LifecycleClosedForEntry
                    && !positionBefore.HasOpenPosition;
                bool desiredForceExit = request.UiDesiredStateRoutingEnabled
                    && desiredRouting.ShouldForceExit(session.Symbol)
                    && positionBefore.HasOpenPosition;
                string desiredSkipReason = string.Empty;
                bool desiredSkipFlat = request.UiDesiredStateRoutingEnabled
                    && !positionBefore.HasOpenPosition
                    && desiredRouting.ShouldSkipFlatScannerEntry(session.Symbol, out desiredSkipReason);

                if (forceFlatDue && positionBefore.HasOpenPosition)
                {
                    decision = CreateForceFlatDecision(session, decisionClock);
                }
                else if (forceFlatDue)
                {
                    decision = SailorStrategyDecision.Hold(session.Symbol, $"Force-flat window reached at runtimeClock={decisionClock:O}; no open position.");
                }
                else if (desiredForceExit)
                {
                    decision = CreateUiDesiredStateExitDecision(session);
                }
                else if (desiredSkipFlat)
                {
                    if (session.ScannerSlotActive && !session.LifecycleClosedForEntry)
                    {
                        session.MarkLifecycleClosedAfterStrategyExit(desiredSkipReason);
                    }
                    decision = SailorStrategyDecision.Hold(session.Symbol, desiredSkipReason);
                }
                else if (harshForceEntry)
                {
                    decision = CreateHarshConductEntryDecision(session, request);
                }
                else if (liveBarCurrentness is { IsCurrent: false })
                {
                    string staleReason = liveBarCurrentness.ToEntryBlockReason(Math.Max(1, request.LiveBarMaxAgeMinutes));
                    decision = SailorStrategyDecision.Hold(session.Symbol, positionBefore.HasOpenPosition
                        ? staleReason + " Existing position remains managed for broker reconciliation/force-flat only until fresh bars are available."
                        : staleReason);
                }
                else if (!healthMonitor.CanOpenEntries(request.CanOpenEntries) && !positionBefore.HasOpenPosition)
                {
                    decision = SailorStrategyDecision.Hold(session.Symbol, $"Runtime safety is {healthMonitor.SafetyState.Mode}; new entries are blocked: {healthMonitor.SafetyState.Reason}");
                }
                else
                {
                    decision = await session.Strategy.EvaluateAsync(frame, positionBefore, cancellationToken).ConfigureAwait(false);
                }

                decisionCount++;
                _log($"{session.Symbol}: bar={indicators.BarIndex} time={frame.Time:O} close={bar.Close:F4} posBefore={session.PositionDisplay()} decision={decision.Type} qty={decision.Quantity} reason={decision.Reason}");

                if (!decision.CreatesOrder)
                {
                    continue;
                }

                bool isEntry = decision.Type is SailorStrategyDecisionType.EnterLong or SailorStrategyDecisionType.EnterShort;
                bool isExitOrFlatten = decision.Type is SailorStrategyDecisionType.ExitLong or SailorStrategyDecisionType.ExitShort or SailorStrategyDecisionType.Flatten;
                if (isEntry && !healthMonitor.CanOpenEntries(request.CanOpenEntries) && !request.HarshConductTestEnabled)
                {
                    string warning = $"{session.Symbol}: entry blocked because runtime safety is {healthMonitor.SafetyState.Mode}. {healthMonitor.SafetyState.Reason}";
                    warnings.Add(warning);
                    _log($"WARN: {warning}");
                    continue;
                }

                if (isEntry && !request.HarshConductTestEnabled)
                {
                    int easternMinuteOfDay = MarketTime.GetEasternMinuteOfDay(frame.Time);
                    StrategyLifecycleEntryDecision entryDecision = session.EvaluateEntryPolicy(easternMinuteOfDay, request.RuntimeOptions.LastEntryMinute);
                    if (!entryDecision.AllowEntry)
                    {
                        string warning = $"{session.Symbol}: entry blocked by lifecycle policy {session.LifecyclePolicy.Mode.ToDisplayName()}. {entryDecision.Reason}";
                        warnings.Add(warning);
                        _log($"WARN: {warning}");
                        continue;
                    }
                }

                if (isExitOrFlatten && !healthMonitor.SafetyState.CanRouteExits)
                {
                    string warning = $"{session.Symbol}: exit/flatten order blocked because runtime safety is {healthMonitor.SafetyState.Mode}. {healthMonitor.SafetyState.Reason}";
                    warnings.Add(warning);
                    _log($"WARN: {warning}");
                    continue;
                }


                if (decision.Type == SailorStrategyDecisionType.EnterShort && !request.RuntimeOptions.AllowShort)
                {
                    string warning = $"{session.Symbol}: short entry blocked because allowShort=false.";
                    warnings.Add(warning);
                    _log($"WARN: {warning}");
                    continue;
                }

                if (isEntry && request.EnforceMaxOrderNotional && request.MaxOrderNotional > 0m)
                {
                    int entryQuantity = Math.Max(1, decision.Quantity > 0 ? decision.Quantity : request.Quantity);
                    decimal referencePrice = decision.LimitPrice is > 0m ? decision.LimitPrice.Value : bar.Close;
                    decimal estimatedNotional = Math.Abs(referencePrice * entryQuantity);
                    if (estimatedNotional > request.MaxOrderNotional)
                    {
                        string warning = $"{session.Symbol}: entry blocked because estimated notional {estimatedNotional:F2} exceeds live-pilot max notional {request.MaxOrderNotional:F2}.";
                        warnings.Add(warning);
                        _log($"WARN: {warning}");
                        continue;
                    }
                }

                SailorOrderIntent? intent = CreateOrderIntent(request, decision, positionBefore, bar.Close, session.ProfileName);
                if (intent is null)
                {
                    string warning = $"{session.Symbol}: decision {decision.Type} did not produce a routable intent.";
                    warnings.Add(warning);
                    _log($"WARN: {warning}");
                    continue;
                }

                intentCount++;
                _log(intent.ToDisplayString());

                SailorOrderReceipt receipt;
                try
                {
                    receipt = await router.SubmitAsync(intent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    string message = $"Order router threw {ex.GetType().Name}: {ex.Message}";
                    receipt = new SailorOrderReceipt(
                        intent.NormalizedIntentId,
                        intent.NormalizedSymbol,
                        BrokerOrderId: string.Empty,
                        SailorOrderStatus.Failed,
                        intent.Quantity,
                        FilledQuantity: 0,
                        AverageFillPrice: 0m,
                        message,
                        SentToBroker: false,
                        intent.CreatedAt,
                        DateTimeOffset.Now,
                        Events: Array.Empty<string>(),
                        Warnings: new[] { message });
                }

                routedCount++;
                string ledgerPath = _ledger.Append(intent, receipt);
                _log(receipt.ToDisplayString());
                _log($"Ledger JSONL: {ledgerPath}");

                foreach (string receiptEvent in receipt.Events)
                {
                    _log($"event: {receiptEvent}");
                }

                foreach (string receiptWarning in receipt.Warnings)
                {
                    warnings.Add(receiptWarning);
                    _log($"WARN: {receiptWarning}");
                }

                RuntimeIncident? receiptIncident = healthMonitor.ObserveOrderReceipt(receipt);
                if (receiptIncident is not null)
                {
                    warnings.Add(receiptIncident.Message);
                    _log(receiptIncident.ToDisplayString());
                    _log(healthMonitor.SafetyState.ToDisplayString());
                    await TryRecoverIfNeededAsync($"order receipt for {intent.NormalizedSymbol}").ConfigureAwait(false);
                }

                int positionQuantityBeforeReceipt = positionBefore.Quantity;
                bool positionUpdated = session.ApplyReceipt(intent, receipt, bar.Close, request.DryRun, indicators.BarIndex, out string updateMessage);
                if (positionUpdated)
                {
                    filledOrAssumedFillCount++;
                    _log(updateMessage);
                }
                else
                {
                    _log(updateMessage);
                }

                if (harshLogWriter is not null && harshTracker is not null)
                {
                    decimal realizedPnl = harshTracker.Record(intent, receipt, positionBefore, session.PositionQuantity, bar.Close, request.DryRun);
                    harshLogWriter.AppendTrade(new HarshConductTradeEvent(
                        DateTimeOffset.UtcNow,
                        request.RuntimeOptions.ModeName,
                        session.ProfileName,
                        session.ProfileName,
                        request.HarshConductTestEnabled ? "S064-harsh-conduct" : "normal-conduct",
                        iteration,
                        session.Symbol,
                        session.ScannerSelectedSide ?? "n/a",
                        decision.Type,
                        intent.Side,
                        intent.OrderType,
                        intent.Quantity,
                        bar.Close,
                        receipt.AverageFillPrice,
                        receipt.Status == SailorOrderStatus.DryRun ? intent.Quantity : receipt.FilledQuantity,
                        receipt.Status,
                        receipt.SentToBroker,
                        positionQuantityBeforeReceipt,
                        session.PositionQuantity,
                        realizedPnl,
                        decision.Reason));
                }

                TradeLifecycle lifecycle = _tradeRegistry.ApplyOrderReceipt(
                    intent,
                    receipt,
                    session.TradeOrigin,
                    session.PositionQuantity,
                    session.AveragePrice,
                    scannerSlotId: session.ScannerSlotId,
                    sourceMessage: $"SAILOR-053 conduct loop decision={decision.Type} origin={session.TradeOrigin.ToDisplayName()} positionUpdated={positionUpdated}. {updateMessage}");
                _log($"Trade lifecycle: {lifecycle.ToDisplayString()}");

                if (positionUpdated
                    && isExitOrFlatten
                    && positionBefore.HasOpenPosition
                    && !session.HasOpenPosition
                    && (request.HarshConductTestEnabled || session.LifecyclePolicy.ShouldCloseEntryWindowAfterStrategyExit(session.TradeOrigin)))
                {
                    string lifecycleCloseReason = request.HarshConductTestEnabled
                        ? $"SAILOR-064 closed this scanner slot after exit receipt {receipt.Status}; replenishment must use a different ranked symbol."
                        : $"SAILOR-054 {session.LifecyclePolicy.Mode.ToDisplayName()} closed the entry window after strategy exit receipt {receipt.Status}.";
                    session.MarkLifecycleClosedAfterStrategyExit(lifecycleCloseReason);
                    _log($"{session.Symbol}: {lifecycleCloseReason}");
                }
            }

            if (_scannerSlotManager is not null)
            {
                ScannerSlotReplenishmentReport? slotReport = await _scannerSlotManager.TryReplenishIfDueAsync(
                    sessions,
                    request,
                    healthMonitor,
                    cancellationToken).ConfigureAwait(false);
                if (slotReport is not null)
                {
                    _log("SAILOR-055 scanner slot replenishment report");
                    _log(slotReport.ToSummaryString());
                    if (slotReport.NewSlotsCreated > 0)
                    {
                        runtimeState.SetActiveSymbols(sessions.Select(session => session.Symbol));
                    }
                    if (!string.IsNullOrWhiteSpace(slotReport.JsonPath))
                    {
                        _log($"Scanner slot latest JSON: {slotReport.JsonPath}");
                    }
                    if (!string.IsNullOrWhiteSpace(slotReport.CsvPath))
                    {
                        _log($"Scanner slot CSV: {slotReport.CsvPath}");
                    }
                    foreach (string blockedSymbol in slotReport.BlockedSymbols.Take(30))
                    {
                        _log($"scanner-slot-blocked: {blockedSymbol}");
                    }
                    if (slotReport.BlockedSymbols.Count > 30)
                    {
                        _log($"scanner-slot-blocked: ... {slotReport.BlockedSymbols.Count - 30} more");
                    }
                }
            }

            _log("");

            if (iteration < request.MaxIterations && request.CadenceSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(request.CadenceSeconds), cancellationToken).ConfigureAwait(false);
            }
        }

        static bool ShouldRunManualBrokerSync(
            int iteration,
            DateTimeOffset heartbeatUtc,
            DateTimeOffset? lastSyncUtc,
            PaperRuntimeHostRequest hostRequest)
        {
            if (iteration == 1)
            {
                return true;
            }

            int intervalSeconds = Math.Max(1, hostRequest.ManualBrokerPositionMonitorIntervalSeconds);
            return !lastSyncUtc.HasValue || heartbeatUtc - lastSyncUtc.Value >= TimeSpan.FromSeconds(intervalSeconds);
        }

        void LogLiveCandleRefreshResults(
            int iteration,
            IReadOnlyList<PaperLiveCandleRefreshResult> refreshResults,
            List<string> loopWarnings)
        {
            if (refreshResults.Count == 0)
            {
                _log($"SAILOR-059 candle-refresh iteration={iteration} requested=0 updated=0 unchanged=0 stale=0 failed=0");
                return;
            }

            int updated = refreshResults.Count(result => result.Updated);
            int failed = refreshResults.Count(result => !result.Success);
            int stale = refreshResults.Count(result => result.Success && !result.Current);
            int unchanged = refreshResults.Count - updated - failed - stale;
            _log($"SAILOR-059 candle-refresh iteration={iteration} requested={refreshResults.Count} updated={updated} unchanged={Math.Max(0, unchanged)} stale={stale} failed={failed}");

            foreach (PaperLiveCandleRefreshResult result in refreshResults)
            {
                foreach (string refreshWarning in result.Warnings)
                {
                    loopWarnings.Add(refreshWarning);
                    _log($"WARN: {refreshWarning}");
                }

                if (result.Updated || !result.Success || !result.Current || result.Message.Contains("SAILOR-061", StringComparison.OrdinalIgnoreCase))
                {
                    _log($"candle-refresh: {result.ToDisplayString()}");
                }
            }
        }

        async Task TryRecoverIfNeededAsync(string reason)
        {
            if (!healthMonitor.SafetyState.IsDegraded)
            {
                return;
            }

            if (!request.SendOrders)
            {
                _log($"SAILOR-031 degraded state observed in dry-run/local mode after {reason}. New entries are blocked, but no broker reconnect is attempted.");
                return;
            }

            if (recoveryAttempted)
            {
                _log($"SAILOR-031 reconnect was already attempted in this run. Runtime remains {healthMonitor.SafetyState.Mode} after {reason}.");
                return;
            }

            recoveryAttempted = true;

            if (_severeDisconnectRecoveryOrchestrator is not null)
            {
                SevereDisconnectRecoveryResult severeRecovery = await _severeDisconnectRecoveryOrchestrator.RecoverAsync(
                    sessions,
                    request,
                    runtimeState,
                    healthMonitor,
                    recoveryService,
                    reason,
                    cancellationToken).ConfigureAwait(false);

                _log("SAILOR-056 severe disconnect recovery report");
                _log(severeRecovery.Report.ToSummaryString());
                _log($"Severe recovery latest JSON: {severeRecovery.Report.JsonPath}");
                _log($"Severe recovery CSV: {severeRecovery.Report.CsvPath}");

                foreach (string recoveryEvent in severeRecovery.Report.Events)
                {
                    _log($"recovery-event: {recoveryEvent}");
                }

                foreach (string recoveryWarning in severeRecovery.Report.Warnings)
                {
                    warnings.Add(recoveryWarning);
                    _log($"WARN: {recoveryWarning}");
                }

                return;
            }

            runtimeState.SetStatus(SailorRuntimeStatus.Reconnecting, $"SAILOR-031 reconnect/reconcile after {reason}.");

            ConnectionRecoveryResult recoveryResult = await recoveryService.TryRecoverAsync(
                request.ReconcileBrokerStateAsync,
                sessions.Select(session => session.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                request.ReconnectAttempts,
                TimeSpan.FromSeconds(Math.Max(1, request.ReconnectBackoffSeconds)),
                cancellationToken).ConfigureAwait(false);

            _log(recoveryResult.ToDisplayString());
            foreach (string recoveryEvent in recoveryResult.Events)
            {
                _log($"recovery-event: {recoveryEvent}");
            }

            foreach (string recoveryWarning in recoveryResult.Warnings)
            {
                warnings.Add(recoveryWarning);
                _log($"WARN: {recoveryWarning}");
            }

            runtimeState.SetStatus(
                SailorRuntimeStatus.Running,
                recoveryResult.Recovered
                    ? "SAILOR-031 recovery succeeded; normal runtime safety resumed."
                    : "SAILOR-031 recovery did not produce clean reconciliation; runtime remains close-only.");
        }

        IReadOnlyList<string> activeSymbols = sessions.Select(session => session.Symbol).ToArray();
        if (harshLogWriter is not null && harshTracker is not null)
        {
            HarshConductSummary summary = harshTracker.BuildSummary(
                request.RuntimeOptions.ProfileName,
                request.RuntimeOptions.ProfileName,
                "S064-harsh-conduct",
                activeSymbols,
                harshGovernanceStops,
                harshGovernanceReason);
            harshLogWriter.WriteSummary(summary);
            _log($"SAILOR-064 summary Strategy={summary.Strategy} Variant={summary.Variant} Style={summary.Style} Symbols={summary.Symbols} Trades={summary.Trades} >=50={summary.AtLeast50} WinRate={summary.WinRate:P2} PF={summary.ProfitFactor:F2} TotalPnL$={summary.TotalPnl:F2} MaxDD$={summary.MaxDrawdown:F2} GovStops={summary.GovernanceStops} GovReason={summary.GovernanceReason}");
            _log($"SAILOR-064 latest summary CSV: {harshLogWriter.LatestSummaryCsvPath}");
        }

        return new PaperRuntimeHostResult(
            activeSymbols,
            decisionCount,
            intentCount,
            routedCount,
            filledOrAssumedFillCount,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static SailorStrategyDecision CreateForceFlatDecision(PaperSymbolSession session, DateTimeOffset frameTime)
    {
        SailorStrategyDecisionType type = session.PositionSide < 0
            ? SailorStrategyDecisionType.ExitShort
            : SailorStrategyDecisionType.ExitLong;

        return new SailorStrategyDecision(
            type,
            session.Symbol,
            session.AbsoluteQuantity,
            SailorOrderType.Market,
            null,
            $"SAILOR-030 force-flat at/after {frameTime:O} ET minute threshold.");
    }

    private static SailorStrategyDecision CreateUiDesiredStateExitDecision(PaperSymbolSession session)
    {
        SailorStrategyDecisionType type = session.PositionSide < 0
            ? SailorStrategyDecisionType.ExitShort
            : SailorStrategyDecisionType.ExitLong;

        return new SailorStrategyDecision(
            type,
            session.Symbol,
            session.AbsoluteQuantity,
            SailorOrderType.Market,
            null,
            "SAILOR-068 SailorUI desired state unchecked this symbol; route existing paper position out/flat.");
    }

    private static SailorStrategyDecision CreateHarshConductEntryDecision(PaperSymbolSession session, PaperRuntimeHostRequest request)
    {
        string side = string.IsNullOrWhiteSpace(session.ScannerSelectedSide)
            ? "LONG"
            : session.ScannerSelectedSide.Trim().ToUpperInvariant();
        SailorStrategyDecisionType type = side.Equals("SHORT", StringComparison.OrdinalIgnoreCase)
            ? SailorStrategyDecisionType.EnterShort
            : SailorStrategyDecisionType.EnterLong;
        int quantity = Math.Max(1, request.Quantity > 0 ? request.Quantity : Math.Max(1, request.HarshConductDefaultQuantity));
        return new SailorStrategyDecision(
            type,
            session.Symbol,
            quantity,
            SailorOrderType.Market,
            null,
            $"SAILOR-064 forced harsh-conduct entry from scanner side={side}; strategy entry filters, stale-bar gate, and scanner block reasons are bypassed for short test execution.");
    }

    private static SailorOrderIntent? CreateOrderIntent(
        PaperRuntimeHostRequest request,
        SailorStrategyDecision decision,
        SailorStrategyPositionContext position,
        decimal fallbackPrice,
        string profileName)
    {
        SailorOrderSide side = decision.Type switch
        {
            SailorStrategyDecisionType.EnterLong => SailorOrderSide.Buy,
            SailorStrategyDecisionType.EnterShort => SailorOrderSide.SellShort,
            SailorStrategyDecisionType.ExitLong => SailorOrderSide.Sell,
            SailorStrategyDecisionType.ExitShort => SailorOrderSide.BuyToCover,
            SailorStrategyDecisionType.Flatten when position.PositionSide > 0 => SailorOrderSide.Sell,
            SailorStrategyDecisionType.Flatten when position.PositionSide < 0 => SailorOrderSide.BuyToCover,
            _ => SailorOrderSide.Flatten
        };

        if (side == SailorOrderSide.Flatten)
        {
            return null;
        }

        int quantity = decision.Type is SailorStrategyDecisionType.EnterLong or SailorStrategyDecisionType.EnterShort
            ? Math.Max(1, decision.Quantity > 0 ? decision.Quantity : request.Quantity)
            : Math.Max(1, position.AbsoluteQuantity > 0 ? position.AbsoluteQuantity : decision.Quantity);

        decimal? limitPrice = decision.OrderType == SailorOrderType.Limit
            ? decision.LimitPrice ?? fallbackPrice
            : decision.LimitPrice;

        return new SailorOrderIntent(
            request.RuntimeOptions.Mode,
            decision.Symbol,
            side,
            decision.OrderType,
            quantity,
            limitPrice,
            string.IsNullOrWhiteSpace(profileName) ? request.RuntimeOptions.ProfileName : profileName.Trim(),
            $"SAILOR-030/S034 {request.RuntimeOptions.ModeName} conduct loop: {decision.Reason}",
            request.DryRun,
            DateTimeOffset.Now,
            IntentId: string.Empty,
            TimeInForce: "DAY",
            Account: string.IsNullOrWhiteSpace(request.Account) ? null : request.Account.Trim());
    }
}
