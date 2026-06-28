using Sailor.App.Backtest;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Runtime.Common;
using Sailor.App.Strategy.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed class PaperConductLoop
{
    private readonly Action<string> _log;
    private readonly OrderLedgerStore _ledger;

    public PaperConductLoop(SailorRuntimeMode mode, Action<string> log)
    {
        _log = log;
        _ledger = new OrderLedgerStore(mode);
    }

    public async Task<PaperRuntimeHostResult> RunAsync(
        IReadOnlyList<PaperSymbolSession> sessions,
        IOrderRouter router,
        PaperRuntimeHostRequest request,
        SailorRuntimeState runtimeState,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        int decisionCount = 0;
        int intentCount = 0;
        int routedCount = 0;
        int filledOrAssumedFillCount = 0;

        if (sessions.Count == 0)
        {
            warnings.Add("No active symbol sessions were created.");
            return new PaperRuntimeHostResult(Array.Empty<string>(), 0, 0, 0, 0, warnings);
        }

        _log("Paper conduct loop");
        _log("------------------");
        _log($"cadence={request.CadenceSeconds}s iterations={request.MaxIterations} sendOrders={request.SendOrders} dryRun={request.DryRun} canOpenEntries={request.CanOpenEntries} forceFlatNow={request.ForceFlatNow}");
        _log($"router={router.RouterName}");
        _log("");

        for (int iteration = 1; iteration <= request.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runtimeState.SetStatus(SailorRuntimeStatus.Running, $"SAILOR-030 conduct iteration {iteration}.");

            _log($"Iteration {iteration}/{request.MaxIterations} heartbeatUtc={DateTimeOffset.UtcNow:O}");

            foreach (PaperSymbolSession session in sessions)
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
                if (isEntry && !request.CanOpenEntries)
                {
                    string warning = $"{session.Symbol}: entry blocked because reconciliation/runtime gate does not allow new entries.";
                    warnings.Add(warning);
                    _log($"WARN: {warning}");
                    continue;
                }

                if (isEntry && MarketTime.GetEasternMinuteOfDay(frame.Time) >= request.RuntimeOptions.LastEntryMinute)
                {
                    string warning = $"{session.Symbol}: entry blocked because bar time is after last-entry minute {request.RuntimeOptions.LastEntryMinute} ET.";
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

                SailorOrderReceipt receipt = await router.SubmitAsync(intent, cancellationToken).ConfigureAwait(false);
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

                if (session.ApplyReceipt(intent, receipt, bar.Close, request.DryRun, indicators.BarIndex, out string updateMessage))
                {
                    filledOrAssumedFillCount++;
                    _log(updateMessage);
                }
                else
                {
                    _log(updateMessage);
                }
            }

            _log("");

            if (iteration < request.MaxIterations && request.CadenceSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(request.CadenceSeconds), cancellationToken).ConfigureAwait(false);
            }
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
            $"SAILOR-030 paper conduct loop: {decision.Reason}",
            request.DryRun,
            DateTimeOffset.Now,
            IntentId: string.Empty,
            TimeInForce: "DAY",
            Account: string.IsNullOrWhiteSpace(request.Account) ? null : request.Account.Trim());
    }
}
