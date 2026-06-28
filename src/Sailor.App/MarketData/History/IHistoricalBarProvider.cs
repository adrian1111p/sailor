namespace Sailor.App.MarketData.History;

public interface IHistoricalBarProvider
{
    string ProviderName { get; }

    Task<HistoricalBarLoadResult> GetOneMinuteHistoryAsync(
        HistoricalBarRequest request,
        CancellationToken cancellationToken);
}
