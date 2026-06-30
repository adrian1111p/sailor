namespace Sailor.App.Runtime.TradeManagement;

public sealed record BrokerMirrorPositionRow(
    string Account,
    string Symbol,
    int Quantity,
    decimal AverageCost,
    string Source,
    DateTimeOffset ObservedUtc)
{
    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? "UNKNOWN" : Symbol.Trim().ToUpperInvariant();

    public string ToDisplayString()
    {
        string side = Quantity > 0 ? "LONG" : Quantity < 0 ? "SHORT" : "FLAT";
        return $"{NormalizedSymbol} {side} qty={Quantity} avg={AverageCost:F4} account={(string.IsNullOrWhiteSpace(Account) ? "n/a" : Account)} source={Source} observedUtc={ObservedUtc:O}";
    }
}

public sealed record BrokerMirrorOpenOrderRow(
    string Account,
    string Symbol,
    string BrokerOrderId,
    string Action,
    string OrderType,
    int Quantity,
    string Status,
    string OrderRef,
    DateTimeOffset ObservedUtc,
    string DisplayLine)
{
    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? "UNKNOWN" : Symbol.Trim().ToUpperInvariant();

    public string ToDisplayString()
        => $"{NormalizedSymbol} brokerOrder={BrokerOrderId} action={Action} type={OrderType} qty={Quantity} status={Status} orderRef={(string.IsNullOrWhiteSpace(OrderRef) ? "n/a" : OrderRef)} account={(string.IsNullOrWhiteSpace(Account) ? "n/a" : Account)} observedUtc={ObservedUtc:O}";
}

public sealed record BrokerMirrorExecutionRow(
    string Account,
    string Symbol,
    string BrokerOrderId,
    string Side,
    int Quantity,
    decimal Price,
    string ExecutionId,
    string OrderRef,
    DateTimeOffset ObservedUtc,
    string DisplayLine)
{
    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? "UNKNOWN" : Symbol.Trim().ToUpperInvariant();

    public string ToDisplayString()
        => $"{NormalizedSymbol} execution={ExecutionId} brokerOrder={BrokerOrderId} side={Side} qty={Quantity} price={Price:F4} orderRef={(string.IsNullOrWhiteSpace(OrderRef) ? "n/a" : OrderRef)} account={(string.IsNullOrWhiteSpace(Account) ? "n/a" : Account)} observedUtc={ObservedUtc:O}";
}
