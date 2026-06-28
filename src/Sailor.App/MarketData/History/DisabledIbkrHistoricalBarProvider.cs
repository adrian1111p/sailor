namespace Sailor.App.MarketData.History;

public sealed class DisabledIbkrHistoricalBarProvider : IHistoricalBarProvider
{
    private readonly IHistoricalBarProvider _fallbackProvider;

    public DisabledIbkrHistoricalBarProvider(IHistoricalBarProvider fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
    }

    public string ProviderName => "ibkr-api-disabled-local-fallback";

    public async Task<HistoricalBarLoadResult> GetOneMinuteHistoryAsync(
        HistoricalBarRequest request,
        CancellationToken cancellationToken)
    {
        HistoricalBarLoadResult fallback = await _fallbackProvider.GetOneMinuteHistoryAsync(request, cancellationToken);

        string message = fallback.Success
            ? "IBApi history adapter is not compiled in this build; used existing local CSV data and wrote SAILOR-025 cache."
            : "IBApi history adapter is not compiled in this build and local CSV fallback did not provide data.";

        string[] warnings =
        [
            "Real IBKR historical download requires building Sailor with the optional IBApi adapter enabled.",
            "Use: dotnet run -p:EnableIbkrApi=true --project src/Sailor.App/Sailor.App.csproj -- paper history 1m TSLA --ibapi",
            "This default build remains dependency-free and sends no IBKR requests."
        ];

        return fallback with
        {
            RemoteRequested = true,
            RemoteProviderAvailable = false,
            Message = message,
            Warnings = fallback.Warnings.Concat(warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
