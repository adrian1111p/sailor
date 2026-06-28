using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Models;

namespace Sailor.App.MarketData.History;

public sealed class LocalCsvHistoricalBarProvider : IHistoricalBarProvider
{
    private readonly CsvBacktestDataProvider _csvProvider = new();

    public string ProviderName => "local-csv-cache";

    public Task<HistoricalBarLoadResult> GetOneMinuteHistoryAsync(
        HistoricalBarRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string cachePath = HistoricalCachePaths.GetCacheFilePath(
            request.Symbol,
            request.Timeframe,
            request.EndTimeUtc);

        try
        {
            IReadOnlyList<BacktestBar> sourceBars = _csvProvider
                .LoadBars(request.Symbol, request.Timeframe)
                .Bars
                .OrderBy(bar => bar.Time)
                .ToArray();

            if (sourceBars.Count == 0)
            {
                return Task.FromResult(HistoricalBarLoadResult.Failed(
                    request,
                    remoteRequested: false,
                    remoteProviderAvailable: true,
                    cachePath,
                    $"Local CSV exists for {request.Symbol} but contains no bars."));
            }

            DateTimeOffset effectiveEndTime = sourceBars[^1].Time;
            DateTimeOffset effectiveStartTime = effectiveEndTime.Subtract(request.Lookback.Add(TimeSpan.FromDays(2)));

            IReadOnlyList<BacktestBar> bars = sourceBars
                .Where(bar => bar.Time <= effectiveEndTime && bar.Time >= effectiveStartTime)
                .OrderBy(bar => bar.Time)
                .ToArray();

            if (bars.Count == 0)
            {
                return Task.FromResult(HistoricalBarLoadResult.Failed(
                    request,
                    remoteRequested: false,
                    remoteProviderAvailable: true,
                    cachePath,
                    $"Local CSV exists for {request.Symbol} but no bars matched the requested lookback window ending at {effectiveEndTime:O}."));
            }

            (string writtenCachePath, string? mirrorPath) = HistoricalCacheWriter.Write(request, bars);
            var result = new HistoricalBarLoadResult(
                request.Symbol,
                request.Timeframe,
                Success: true,
                RemoteRequested: false,
                RemoteProviderAvailable: true,
                Bars: bars,
                CachePath: writtenCachePath,
                BacktestMirrorPath: mirrorPath,
                Message: $"Loaded {bars.Count} bars from existing Sailor CSV data and wrote SAILOR-025 cache. Local fallback used dataset end time {bars[^1].Time:O}.",
                Warnings: Array.Empty<string>());

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(HistoricalBarLoadResult.Failed(
                request,
                remoteRequested: false,
                remoteProviderAvailable: true,
                cachePath,
                $"Local CSV fallback failed for {request.Symbol}: {ex.Message}"));
        }
    }
}
