namespace Sailor.App.Broker.Orders;

public sealed record SailorCancelResult(
    string BrokerOrderId,
    bool Success,
    string Message,
    DateTimeOffset Time,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Warnings);
