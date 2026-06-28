namespace Sailor.App.Broker.Orders;

public sealed record SailorOrderUpdate(
    string Symbol,
    string BrokerOrderId,
    SailorOrderStatus Status,
    int FilledQuantity,
    decimal AverageFillPrice,
    string Message,
    DateTimeOffset Time);
