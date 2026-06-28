namespace Sailor.App.MarketData.Live;

public interface ILiveMarketDataSnapshotProvider
{
    string ProviderName { get; }

    Task<LiveMarketDataSnapshotResult> CaptureSnapshotAsync(
        LiveMarketDataRequest request,
        CancellationToken cancellationToken);
}
