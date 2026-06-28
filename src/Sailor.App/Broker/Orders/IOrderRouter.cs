namespace Sailor.App.Broker.Orders;

public interface IOrderRouter : IAsyncDisposable
{
    string RouterName { get; }

    Task<SailorOrderReceipt> SubmitAsync(SailorOrderIntent intent, CancellationToken cancellationToken);

    Task<SailorCancelResult> CancelAsync(string brokerOrderId, string reason, CancellationToken cancellationToken);

    Task<SailorFlattenResult> FlattenAsync(string symbol, string reason, CancellationToken cancellationToken);
}
