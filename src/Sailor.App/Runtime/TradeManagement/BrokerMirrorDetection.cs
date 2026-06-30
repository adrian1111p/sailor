namespace Sailor.App.Runtime.TradeManagement;

public sealed record BrokerMirrorDetection(
    BrokerMirrorDetectionType Type,
    string Symbol,
    string Message,
    DateTimeOffset ObservedUtc,
    string? TradeId = null,
    SailorTradeOrigin? Origin = null,
    TradeLifecycleStatus? Status = null,
    int BrokerQuantity = 0,
    decimal BrokerAveragePrice = 0m,
    string? BrokerOrderId = null,
    string? ExecutionId = null)
{
    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? "UNKNOWN" : Symbol.Trim().ToUpperInvariant();

    public string ToDisplayString()
    {
        string trade = string.IsNullOrWhiteSpace(TradeId) ? "trade=n/a" : $"trade={TradeId}";
        string origin = Origin is null ? "origin=n/a" : $"origin={Origin.Value.ToDisplayName()}";
        string status = Status is null ? "status=n/a" : $"status={Status.Value.ToDisplayName()}";
        string order = string.IsNullOrWhiteSpace(BrokerOrderId) ? string.Empty : $" brokerOrder={BrokerOrderId}";
        string execution = string.IsNullOrWhiteSpace(ExecutionId) ? string.Empty : $" execution={ExecutionId}";
        return $"{Type.ToDisplayName()} symbol={NormalizedSymbol} qty={BrokerQuantity} avg={BrokerAveragePrice:F4} {trade} {origin} {status}{order}{execution} message={Message}";
    }
}
