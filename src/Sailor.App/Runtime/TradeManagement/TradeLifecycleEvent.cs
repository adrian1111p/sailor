namespace Sailor.App.Runtime.TradeManagement;

public sealed record TradeLifecycleEvent(
    string EventId,
    string TradeId,
    string Symbol,
    string EventType,
    TradeLifecycleStatus Status,
    SailorTradeOrigin Origin,
    int BrokerQuantity,
    decimal BrokerAveragePrice,
    DateTimeOffset ObservedUtc,
    string? ScannerSlotId,
    string? IntentId,
    string? BrokerOrderId,
    string? Message);
