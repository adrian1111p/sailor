namespace Sailor.App.Broker.Orders;

public sealed class DisabledBrokerOrderRouter : IOrderRouter
{
    private readonly string _message;

    public DisabledBrokerOrderRouter(string message)
    {
        _message = string.IsNullOrWhiteSpace(message) ? "Broker order router is disabled." : message.Trim();
    }

    public string RouterName => "disabled-broker-order-router";

    public Task<SailorOrderReceipt> SubmitAsync(SailorOrderIntent intent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var receipt = new SailorOrderReceipt(
            intent.NormalizedIntentId,
            intent.NormalizedSymbol,
            BrokerOrderId: string.Empty,
            SailorOrderStatus.Failed,
            intent.Quantity,
            FilledQuantity: 0,
            AverageFillPrice: 0m,
            _message,
            SentToBroker: false,
            intent.CreatedAt,
            DateTimeOffset.Now,
            Events: [$"blocked-submit intentId={intent.NormalizedIntentId}"],
            Warnings: [_message]);

        return Task.FromResult(receipt);
    }

    public Task<SailorCancelResult> CancelAsync(string brokerOrderId, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SailorCancelResult(
            brokerOrderId,
            Success: false,
            _message,
            DateTimeOffset.Now,
            Events: [$"blocked-cancel brokerOrderId={brokerOrderId}"],
            Warnings: [_message]));
    }

    public Task<SailorFlattenResult> FlattenAsync(string symbol, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SailorFlattenResult(
            symbol.Trim().ToUpperInvariant(),
            Success: false,
            _message,
            DateTimeOffset.Now,
            Events: [$"blocked-flatten symbol={symbol}"],
            Warnings: [_message]));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
