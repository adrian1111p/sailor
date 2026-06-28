namespace Sailor.App.Broker.Ibkr;

public interface IIbkrConnectionSession : IAsyncDisposable
{
    IbkrConnectionSnapshot Snapshot { get; }

    Task<IbkrConnectionResult> ConnectAsync(
        IbkrConnectionOptions options,
        CancellationToken cancellationToken);

    Task<IbkrConnectionSnapshot> DisconnectAsync(string reason, CancellationToken cancellationToken);
}
