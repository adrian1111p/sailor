namespace Sailor.App.Broker.Orders;

public sealed class DryRunOrderRouter : IOrderRouter
{
    public string RouterName => "dry-run-order-router";

    public Task<SailorOrderReceipt> SubmitAsync(SailorOrderIntent intent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var receipt = new SailorOrderReceipt(
            intent.NormalizedIntentId,
            intent.NormalizedSymbol,
            BrokerOrderId: "DRY-RUN",
            SailorOrderStatus.DryRun,
            intent.Quantity,
            FilledQuantity: 0,
            AverageFillPrice: 0m,
            $"Dry-run only. Order intent was validated but not sent to broker: {intent.Side} {intent.OrderType} {intent.Quantity} {intent.NormalizedSymbol}.",
            SentToBroker: false,
            intent.CreatedAt,
            DateTimeOffset.Now,
            Events:
            [
                $"dry-run-submit intentId={intent.NormalizedIntentId}",
                $"symbol={intent.NormalizedSymbol} side={intent.Side} type={intent.OrderType} qty={intent.Quantity} limit={intent.LimitPrice?.ToString("F4") ?? "n/a"} tif={intent.NormalizedTimeInForce}"
            ],
            Warnings:
            [
                "No order was sent. Use --send-orders in paper mode with the optional IBApi build to submit to TWS paper."
            ]);

        return Task.FromResult(receipt);
    }

    public Task<SailorCancelResult> CancelAsync(string brokerOrderId, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SailorCancelResult(
            brokerOrderId,
            Success: true,
            $"Dry-run cancel only. brokerOrderId={brokerOrderId} reason={reason}",
            DateTimeOffset.Now,
            [$"dry-run-cancel brokerOrderId={brokerOrderId}"],
            ["No cancel request was sent to broker."]));
    }

    public Task<SailorFlattenResult> FlattenAsync(string symbol, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SailorFlattenResult(
            symbol.Trim().ToUpperInvariant(),
            Success: true,
            $"Dry-run flatten only. symbol={symbol} reason={reason}",
            DateTimeOffset.Now,
            [$"dry-run-flatten symbol={symbol}"],
            ["No flatten order was sent to broker."]));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
