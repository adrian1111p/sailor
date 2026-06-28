using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Models;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.MarketData.Live;

public sealed class LocalCachedMarketDataSnapshotProvider : ILiveMarketDataSnapshotProvider
{
    public string ProviderName => "local-csv-snapshot";

    public Task<LiveMarketDataSnapshotResult> CaptureSnapshotAsync(
        LiveMarketDataRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = new CsvBacktestDataProvider();
            BacktestDataSet dataSet = provider.LoadBars(request.NormalizedSymbol, "1m");
            BacktestBar last = dataSet.Bars[^1];

            decimal spread = CalculateSyntheticSpread(last.Close);
            decimal bid = Math.Max(0.0001m, last.Close - spread / 2m);
            decimal ask = last.Close + spread / 2m;
            long size = Math.Max(1, last.Volume / 390);

            var l1 = new L1QuoteSnapshot(
                DateTimeOffset.UtcNow,
                request.NormalizedSymbol,
                bid,
                ask,
                last.Close,
                size,
                size);

            L2OrderBookSnapshot? l2 = request.UseL2
                ? BuildSyntheticDepth(request, bid, ask, size)
                : null;

            var snapshot = new SailorMarketSnapshot(
                DateTimeOffset.UtcNow,
                request.NormalizedSymbol,
                SailorMarketSnapshotQuality.SyntheticBacktest,
                l1,
                l2,
                l2 is null ? 50m : 60m,
                ProviderName);

            string logPath = MarketSnapshotLogWriter.WriteSnapshot(request.Mode, request, snapshot);
            string[] warnings =
            [
                "Used local synthetic snapshot from backtest CSV data.",
                "This is useful for smoke testing SAILOR-026 commands, but it is not real IBKR L1/L2 data."
            ];

            return Task.FromResult(new LiveMarketDataSnapshotResult(
                request.NormalizedSymbol,
                Success: true,
                RemoteRequested: false,
                RemoteProviderAvailable: true,
                snapshot,
                $"Built synthetic L1{(request.UseL2 ? "/L2" : string.Empty)} snapshot from latest local 1m bar in {dataSet.SourcePath}.",
                warnings,
                Array.Empty<string>(),
                logPath));
        }
        catch (Exception ex)
        {
            return Task.FromResult(LiveMarketDataSnapshotResult.Failed(
                request,
                remoteRequested: false,
                remoteProviderAvailable: true,
                $"Local cached snapshot failed for {request.NormalizedSymbol}: {ex.Message}"));
        }
    }

    private static decimal CalculateSyntheticSpread(decimal close)
        => Math.Max(0.01m, close * 0.0006m);

    private static L2OrderBookSnapshot BuildSyntheticDepth(
        LiveMarketDataRequest request,
        decimal bid,
        decimal ask,
        long size)
    {
        var levels = new List<L2OrderBookLevel>(request.DepthLevels);
        decimal step = Math.Max(0.01m, (ask - bid) / 2m);
        for (int i = 0; i < request.DepthLevels; i++)
        {
            long levelSize = Math.Max(1, size - (i * Math.Max(1, size / 10)));
            levels.Add(new L2OrderBookLevel(
                i,
                Math.Max(0.0001m, bid - step * i),
                levelSize,
                ask + step * i,
                levelSize));
        }

        return new L2OrderBookSnapshot(DateTimeOffset.UtcNow, request.NormalizedSymbol, levels);
    }
}
