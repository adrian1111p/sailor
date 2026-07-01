using System.Reflection;
using Sailor.App.Broker.State;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.TradeManagement;

public sealed class BrokerStateManualTradeDetector
{
    private readonly SailorRuntimeMode _mode;
    private readonly TradeLifecycleRegistryStore _tradeRegistry;
    private readonly BrokerStateMirrorStore _mirrorStore;

    public BrokerStateManualTradeDetector(SailorRuntimeMode mode, TradeLifecycleRegistryStore tradeRegistry)
    {
        _mode = mode;
        _tradeRegistry = tradeRegistry;
        _mirrorStore = new BrokerStateMirrorStore(mode);
    }

    public string LatestJsonPath => _mirrorStore.LatestJsonPath;

    public string DailyJsonlPath => _mirrorStore.DailyJsonlPath;

    public BrokerStateMirrorSnapshot MirrorAndDetect(
        ReconciliationResult reconciliation,
        string? account,
        bool brokerVerified,
        bool unknownBrokerPositionsAreIntradayManual,
        bool markMissingActivePositionsAsManualClosed,
        string source)
    {
        DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
        TradeLifecycleRegistrySnapshot before = _tradeRegistry.LoadSnapshot();
        var detections = new List<BrokerMirrorDetection>();
        var warnings = new List<string>();

        IReadOnlyList<BrokerMirrorPositionRow> mirroredPositions = reconciliation.BrokerPositions
            .Select(position => new BrokerMirrorPositionRow(
                position.Account,
                position.Symbol,
                position.Quantity,
                position.AverageCost,
                position.Source,
                position.ObservedUtc))
            .OrderBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<BrokerMirrorOpenOrderRow> mirroredOpenOrders = reconciliation.BrokerOpenOrders
            .Select(ToMirrorOpenOrder)
            .OrderBy(order => order.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(order => order.BrokerOrderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<BrokerMirrorExecutionRow> mirroredExecutions = reconciliation.BrokerExecutions
            .Select(ToMirrorExecution)
            .OrderBy(execution => execution.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(execution => execution.ExecutionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nonFlatBrokerPositions = mirroredPositions
            .Where(position => position.Quantity != 0 && !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(position => position.NormalizedSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(position => Math.Abs(position.Quantity)).First(), StringComparer.OrdinalIgnoreCase);

        foreach (BrokerMirrorPositionRow brokerPosition in nonFlatBrokerPositions.Values.OrderBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            TradeLifecycle? existing = FindMostRelevantLifecycle(before.Trades, brokerPosition.NormalizedSymbol);
            bool hasKnownLifecycle = existing is not null;
            SailorTradeOrigin origin = hasKnownLifecycle
                ? existing!.Origin
                : unknownBrokerPositionsAreIntradayManual
                    ? SailorTradeOrigin.ManualIntraday
                    : SailorTradeOrigin.ManualPreStart;

            TradeLifecycle lifecycle = _tradeRegistry.ApplyBrokerMirrorPosition(
                brokerPosition.NormalizedSymbol,
                existing?.ProfileName ?? "broker-mirror",
                origin,
                brokerPosition.Quantity,
                brokerPosition.AverageCost,
                brokerPosition.Account,
                source,
                hasKnownLifecycle
                    ? $"SAILOR-052 broker mirror synchronized existing lifecycle from broker position qty={brokerPosition.Quantity} avg={brokerPosition.AverageCost:F4}."
                    : $"SAILOR-052 broker mirror registered {(unknownBrokerPositionsAreIntradayManual ? "manual intraday" : "manual pre-start")} broker position qty={brokerPosition.Quantity} avg={brokerPosition.AverageCost:F4}.");

            detections.Add(new BrokerMirrorDetection(
                hasKnownLifecycle
                    ? BrokerMirrorDetectionType.SailorOwnedPositionSynchronized
                    : unknownBrokerPositionsAreIntradayManual
                        ? BrokerMirrorDetectionType.ManualIntradayPositionRegistered
                        : BrokerMirrorDetectionType.ManualPreStartPositionRegistered,
                brokerPosition.NormalizedSymbol,
                hasKnownLifecycle
                    ? "Broker position was synchronized to an existing Sailor lifecycle."
                    : unknownBrokerPositionsAreIntradayManual
                        ? "Broker position was not known by Sailor and was classified as manual intraday/external."
                        : "Broker position was not known by Sailor at mirror start and was classified as manual pre-start/external.",
                observedUtc,
                lifecycle.TradeId,
                lifecycle.Origin,
                lifecycle.Status,
                brokerPosition.Quantity,
                brokerPosition.AverageCost));
        }

        if (markMissingActivePositionsAsManualClosed && brokerVerified)
        {
            foreach (TradeLifecycle active in before.Trades.Where(trade => trade.IsActive && trade.BrokerQuantity != 0))
            {
                if (nonFlatBrokerPositions.ContainsKey(active.NormalizedSymbol))
                {
                    continue;
                }

                TradeLifecycle closed = _tradeRegistry.MarkBrokerMirrorClosedManually(
                    active.NormalizedSymbol,
                    $"SAILOR-052 broker mirror detected that previously active lifecycle {active.TradeId} is flat/missing in broker positions. Manual close stop-for-day applied.");

                detections.Add(new BrokerMirrorDetection(
                    BrokerMirrorDetectionType.ManualCloseDetected,
                    active.NormalizedSymbol,
                    "A previously active Sailor lifecycle is no longer present in the broker position snapshot; treating as manual close and stop-for-day.",
                    observedUtc,
                    closed.TradeId,
                    closed.Origin,
                    closed.Status,
                    BrokerQuantity: 0,
                    BrokerAveragePrice: 0m));
            }
        }

        if (brokerVerified && nonFlatBrokerPositions.Count == 0 && before.Trades.Any(trade => trade.IsActive && trade.BrokerQuantity != 0))
        {
            detections.Add(new BrokerMirrorDetection(
                BrokerMirrorDetectionType.BrokerFlatConfirmed,
                "ALL",
                "Broker snapshot reported no non-flat positions.",
                observedUtc));
        }

        TradeLifecycleRegistrySnapshot afterPositionSync = _tradeRegistry.LoadSnapshot();
        foreach (BrokerMirrorOpenOrderRow openOrder in mirroredOpenOrders)
        {
            bool mapped = IsKnownBrokerOrderOrRef(afterPositionSync.Trades, openOrder.BrokerOrderId, openOrder.OrderRef);
            if (!mapped)
            {
                detections.Add(new BrokerMirrorDetection(
                    BrokerMirrorDetectionType.ExternalOpenOrderDetected,
                    openOrder.NormalizedSymbol,
                    $"Broker open order is not mapped to a Sailor lifecycle or order ref: {openOrder.ToDisplayString()}",
                    observedUtc,
                    BrokerOrderId: openOrder.BrokerOrderId,
                    BrokerQuantity: openOrder.Quantity));
            }
        }

        foreach (BrokerMirrorExecutionRow execution in mirroredExecutions)
        {
            bool mapped = IsKnownBrokerOrderOrRef(afterPositionSync.Trades, execution.BrokerOrderId, execution.OrderRef);
            if (!mapped)
            {
                detections.Add(new BrokerMirrorDetection(
                    BrokerMirrorDetectionType.ExternalExecutionDetected,
                    execution.NormalizedSymbol,
                    $"Broker execution is not mapped to a Sailor lifecycle or order ref: {execution.ToDisplayString()}",
                    observedUtc,
                    BrokerOrderId: execution.BrokerOrderId,
                    ExecutionId: execution.ExecutionId,
                    BrokerQuantity: execution.Quantity,
                    BrokerAveragePrice: execution.Price));
            }
        }

        warnings.AddRange(reconciliation.Warnings);
        if (!brokerVerified)
        {
            warnings.Add("Broker mirror was run without broker verification; detector did not classify missing active broker positions as manual closes.");
        }

        var snapshot = new BrokerStateMirrorSnapshot(
            _mode.ToDisplayName(),
            account ?? string.Empty,
            source,
            reconciliation.Status.ToString(),
            brokerVerified,
            reconciliation.CanOpenNewEntries || ManualBrokerOrderWorkflow.AllowsStrategyEntries(reconciliation),
            observedUtc,
            mirroredPositions,
            mirroredOpenOrders,
            mirroredExecutions,
            detections.ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            _tradeRegistry.LatestJsonPath);

        return _mirrorStore.Save(snapshot);
    }

    private static TradeLifecycle? FindMostRelevantLifecycle(IEnumerable<TradeLifecycle> trades, string normalizedSymbol)
        => trades
            .Where(trade => trade.NormalizedSymbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.IsActive)
            .ThenByDescending(trade => trade.UpdatedUtc)
            .FirstOrDefault();

    private static bool IsKnownBrokerOrderOrRef(IEnumerable<TradeLifecycle> trades, string? brokerOrderId, string? orderRef)
    {
        string normalizedBrokerOrderId = NormalizeOptional(brokerOrderId) ?? string.Empty;
        string normalizedOrderRef = NormalizeOptional(orderRef) ?? string.Empty;

        foreach (TradeLifecycle trade in trades)
        {
            if (!string.IsNullOrWhiteSpace(normalizedBrokerOrderId) &&
                string.Equals(trade.LastBrokerOrderId, normalizedBrokerOrderId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(normalizedOrderRef) &&
                (string.Equals(trade.LastOrderIntentId, normalizedOrderRef, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trade.LastBrokerOrderId, normalizedOrderRef, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static BrokerMirrorOpenOrderRow ToMirrorOpenOrder(object order)
    {
        string account = GetString(order, "Account");
        string symbol = GetString(order, "Symbol").ToUpperInvariant();
        string brokerOrderId = FirstNonEmpty(GetString(order, "BrokerOrderId"), GetString(order, "OrderId"));
        string action = GetString(order, "Action");
        string orderType = GetString(order, "OrderType");
        int quantity = GetInt(order, "Quantity");
        string status = GetString(order, "Status");
        string orderRef = GetString(order, "OrderRef");
        DateTimeOffset observedUtc = GetDateTimeOffset(order, "ObservedUtc");
        string display = InvokeDisplay(order);
        return new BrokerMirrorOpenOrderRow(account, symbol, brokerOrderId, action, orderType, quantity, status, orderRef, observedUtc, display);
    }

    private static BrokerMirrorExecutionRow ToMirrorExecution(object execution)
    {
        string account = GetString(execution, "Account");
        string symbol = GetString(execution, "Symbol").ToUpperInvariant();
        string brokerOrderId = FirstNonEmpty(GetString(execution, "BrokerOrderId"), GetString(execution, "OrderId"));
        string side = GetString(execution, "Side");
        int quantity = GetInt(execution, "Quantity");
        decimal price = GetDecimal(execution, "Price");
        string executionId = FirstNonEmpty(GetString(execution, "ExecutionId"), GetString(execution, "ExecId"));
        string orderRef = GetString(execution, "OrderRef");
        DateTimeOffset observedUtc = GetDateTimeOffset(execution, "ObservedUtc");
        string display = InvokeDisplay(execution);
        return new BrokerMirrorExecutionRow(account, symbol, brokerOrderId, side, quantity, price, executionId, orderRef, observedUtc, display);
    }

    private static string GetString(object source, string propertyName)
    {
        object? value = GetValue(source, propertyName);
        return value?.ToString()?.Trim() ?? string.Empty;
    }

    private static int GetInt(object source, string propertyName)
    {
        object? value = GetValue(source, propertyName);
        return value switch
        {
            int i => i,
            long l => Convert.ToInt32(l),
            decimal d => Convert.ToInt32(Math.Round(d, MidpointRounding.AwayFromZero)),
            double db => Convert.ToInt32(Math.Round(db, MidpointRounding.AwayFromZero)),
            _ when int.TryParse(value?.ToString(), out int parsed) => parsed,
            _ => 0
        };
    }

    private static decimal GetDecimal(object source, string propertyName)
    {
        object? value = GetValue(source, propertyName);
        return value switch
        {
            decimal d => d,
            double db => Convert.ToDecimal(db),
            float f => Convert.ToDecimal(f),
            int i => i,
            long l => l,
            _ when decimal.TryParse(value?.ToString(), out decimal parsed) => parsed,
            _ => 0m
        };
    }

    private static DateTimeOffset GetDateTimeOffset(object source, string propertyName)
    {
        object? value = GetValue(source, propertyName);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => DateTimeOffset.UtcNow
        };
    }

    private static object? GetValue(object source, string propertyName)
    {
        PropertyInfo? property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetValue(source);
    }

    private static string InvokeDisplay(object source)
    {
        MethodInfo? method = source.GetType().GetMethod("ToDisplayLine", BindingFlags.Public | BindingFlags.Instance, binder: null, types: Type.EmptyTypes, modifiers: null);
        return method?.Invoke(source, Array.Empty<object>())?.ToString() ?? source.ToString() ?? string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
