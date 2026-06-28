using Sailor.App.MarketData.History;
using Sailor.App.MarketData.Live;
using Sailor.App.MarketData.Snapshots;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Scanner.Runtime;

public sealed class PaperScannerSnapshotProvider : IDisposable
{
    private readonly IHistoricalBarProvider _historyProvider;
    private readonly ILiveMarketDataSnapshotProvider _marketDataProvider;
    private readonly bool _disposeHistoryProvider;
    private readonly bool _disposeMarketDataProvider;

    public PaperScannerSnapshotProvider(
        IHistoricalBarProvider historyProvider,
        ILiveMarketDataSnapshotProvider marketDataProvider)
    {
        _historyProvider = historyProvider;
        _marketDataProvider = marketDataProvider;
        _disposeHistoryProvider = historyProvider is IDisposable;
        _disposeMarketDataProvider = marketDataProvider is IDisposable && !ReferenceEquals(historyProvider, marketDataProvider);
    }

    public string HistoryProviderName => _historyProvider.ProviderName;

    public string MarketDataProviderName => _marketDataProvider.ProviderName;

    public async Task<PaperScannerSymbolPreparation> PrepareHistoryAsync(
        PaperScannerOptions options,
        string symbol,
        int requestId,
        CancellationToken cancellationToken)
    {
        var request = HistoricalBarRequest.CreateOneMinute(
            options.Mode,
            symbol,
            TimeSpan.FromDays(Math.Max(1, options.HistoryDays)),
            requestId,
            options.UseRegularTradingHours,
            options.PrimaryExchange,
            options.MirrorHistoryToBacktest);

        HistoricalBarLoadResult result = await _historyProvider.GetOneMinuteHistoryAsync(request, cancellationToken);
        return PaperScannerSymbolPreparation.FromHistory(result);
    }

    public async Task<(SailorMarketSnapshot? Snapshot, string Message, IReadOnlyList<string> Warnings, IReadOnlyList<string> Events)> CaptureSnapshotAsync(
        PaperScannerOptions options,
        string symbol,
        int requestId,
        CancellationToken cancellationToken)
    {
        if (!options.CaptureSnapshots || !options.UseL1)
        {
            return (null, "Snapshot capture disabled for paper scanner.", Array.Empty<string>(), Array.Empty<string>());
        }

        var request = LiveMarketDataRequest.Create(
            options.Mode,
            symbol,
            requestId,
            useL1: options.UseL1,
            useL2: options.UseL2,
            depthLevels: options.DepthLevels,
            duration: TimeSpan.FromSeconds(Math.Max(1, options.SnapshotSeconds)),
            primaryExchange: options.PrimaryExchange,
            marketDataType: options.MarketDataType,
            useSmartDepth: options.UseSmartDepth,
            useLocalCacheFallback: !options.RequestIbkrMarketData);

        LiveMarketDataSnapshotResult result = await _marketDataProvider.CaptureSnapshotAsync(request, cancellationToken);
        return (result.Snapshot, result.Message, result.Warnings, result.Events);
    }

    public void Dispose()
    {
        if (_disposeHistoryProvider && _historyProvider is IDisposable disposableHistory)
        {
            disposableHistory.Dispose();
        }

        if (_disposeMarketDataProvider && _marketDataProvider is IDisposable disposableMarketData)
        {
            disposableMarketData.Dispose();
        }
    }
}
