using Sailor.App.Backtest;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Strategy.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed class PaperConductLoop
{
    private readonly Action<string> _log;
    private readonly OrderLedgerStore _ledger;
    private readonly TradeLifecycleRegistryStore _tradeRegistry;
    private readonly ScannerSlotManager? _scannerSlotManager;

    public PaperConductLoop(
        SailorRuntimeMode mode,
        Action<string> log,
        TradeLifecycleRegistryStore? tradeRegistry = null,
        ScannerSlotManager? scannerSlotManager = null)
    {
        _log = log;
        _ledger = new OrderLedgerStore(mode);
        _tradeRegistry = tradeRegistry ?? new TradeLifecycleRegistryStore(mode);
        _scannerSlotManager = scannerSlotManager;
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
        _log("SAILOR-054 lifecycle entry gates are active: default single-lifecycle, V21-V24 multi-cycle before LastEntryMinute, manual/unknown exit-only.");
        if (_scannerSlotManager is not null)
        {
            _log($"SAILOR-055 scanner slot replenishment gate is active: {_scannerSlotManager.ToDisplayString()}");
        }
        _log(healthMonitor.SafetyState.ToDisplayString());
        _log("");

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

            _log($"Iteration {iteration}/{request.MaxIterations} heartbeatUtc={DateTimeOffset.UtcNow:O} safety={healthMonitor.SafetyState.Mode}");

            foreach (PaperSymbolSession session in sessions.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                SailorStrategyFrame frame = session.NextFrame(runtimeState);
                var bar = frame.LatestBar;
                var indicators = frame.LatestIndicators;
                if (bar is null || indicators is null)
                {
                    _log($"{session.Symbol}: waiting for bars/indicators.");
                    continue;
                }

                SailorStrategyPositionContext positionBefore = session.ToPositionContext();
                SailorStrategyDecision decision;

                bool forceFlatDue = request.ForceFlatNow || MarketTime.GetEasternMinuteOfDay(frame.Time) >= request.RuntimeOptions.ForceFlatMinute;
                if (forceFlatDue && positionBefore.HasOpenPosition)
                {
                    decision = CreateForceFlatDecision(session, frame.Time);
                }
                else if (forceFlatDue)
                {
                    decision = SailorStrategyDecision.Hold(session.Symbol, $"Force-flat window reached at {frame.Time:O}; no open position.");
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
                if (isEntry && !healthMonitor.CanOpenEntries(request.CanOpenEntries))
                {
                    string warning = $"{session.Symbol}: entry blocked because runtime safety is {healthMonitor.SafetyState.Mode}. {healthMonitor.SafetyState.Reason}";
                    warnings.Add(warning);
                    _log($"WARN: {warning}");
                    continue;
                }

                if (isEntry)
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

                SailorOrderIntent? intent = CreateOrderIntent(request, decision, positionBefore, bar.Close);
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
                    && session.LifecyclePolicy.ShouldCloseEntryWindowAfterStrategyExit(session.TradeOrigin))
                {
                    string lifecycleCloseReason = $"SAILOR-054 {session.LifecyclePolicy.Mode.ToDisplayName()} closed the entry window after strategy exit receipt {receipt.Status}.";
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

    private static SailorOrderIntent? CreateOrderIntent(
        PaperRuntimeHostRequest request,
        SailorStrategyDecision decision,
        SailorStrategyPositionContext position,
        decimal fallbackPrice)
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
            request.RuntimeOptions.ProfileName,
            $"SAILOR-030/S034 {request.RuntimeOptions.ModeName} conduct loop: {decision.Reason}",
            request.DryRun,
            DateTimeOffset.Now,
            IntentId: string.Empty,
            TimeInForce: "DAY",
            Account: string.IsNullOrWhiteSpace(request.Account) ? null : request.Account.Trim());
    }
}
