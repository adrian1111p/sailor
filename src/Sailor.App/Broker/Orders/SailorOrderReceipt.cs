namespace Sailor.App.Broker.Orders;

public sealed record SailorOrderReceipt(
    string IntentId,
    string Symbol,
    string BrokerOrderId,
    SailorOrderStatus Status,
    int SubmittedQuantity,
    int FilledQuantity,
    decimal AverageFillPrice,
    string Message,
    bool SentToBroker,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Warnings)
{
    public bool Success => Status is not SailorOrderStatus.Failed and not SailorOrderStatus.Rejected;

    public string ToDisplayString()
    {
        string broker = string.IsNullOrWhiteSpace(BrokerOrderId) ? "n/a" : BrokerOrderId;
        string sent = SentToBroker ? "sent" : "not-sent";
        return $"intent={IntentId} symbol={Symbol} brokerOrderId={broker} status={Status} submitted={SubmittedQuantity} filled={FilledQuantity} avgFill={AverageFillPrice:F4} {sent} message={Message}";
    }
}
