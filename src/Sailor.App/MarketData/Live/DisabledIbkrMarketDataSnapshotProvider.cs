namespace Sailor.App.MarketData.Live;

public sealed class DisabledIbkrMarketDataSnapshotProvider : ILiveMarketDataSnapshotProvider
{
    private readonly ILiveMarketDataSnapshotProvider _fallbackProvider;

    public DisabledIbkrMarketDataSnapshotProvider(ILiveMarketDataSnapshotProvider fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
    }

    public string ProviderName => "ibkr-market-data-disabled-local-fallback";

    public async Task<LiveMarketDataSnapshotResult> CaptureSnapshotAsync(
        LiveMarketDataRequest request,
        CancellationToken cancellationToken)
    {
        LiveMarketDataSnapshotResult fallback = await _fallbackProvider.CaptureSnapshotAsync(request, cancellationToken);
        string[] warnings =
        [
            "Real IBKR L1/L2 adapter is not compiled in this default build.",
            "Use: dotnet run -p:EnableIbkrApi=true --project src/Sailor.App/Sailor.App.csproj -- paper quotes TSLA",
            "Use: dotnet run -p:EnableIbkrApi=true --project src/Sailor.App/Sailor.App.csproj -- paper depth TSLA",
            "No market data subscription was opened by this default build. No orders were sent."
        ];

        return fallback with
        {
            RemoteRequested = true,
            RemoteProviderAvailable = false,
            Message = fallback.Success
                ? "IBKR L1/L2 adapter is disabled in this build; used local synthetic snapshot fallback."
                : "IBKR L1/L2 adapter is disabled in this build and the local fallback did not provide a snapshot.",
            Warnings = fallback.Warnings.Concat(warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
