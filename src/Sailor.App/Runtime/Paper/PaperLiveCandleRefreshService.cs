using Sailor.App.Broker.Ibkr;
using Sailor.App.MarketData.History;

namespace Sailor.App.Runtime.Paper;

public sealed class PaperLiveCandleRefreshService : IDisposable
{
    private readonly IHistoricalBarProvider _historyProvider;
    private readonly IbkrConnectionOptions _refreshConnectionOptions;
    private readonly TimeSpan _lookback;
    private bool _disposed;

    public PaperLiveCandleRefreshService(PaperRuntimeHostRequest request)
    {
        int clientIdOffset = Math.Max(1, request.LiveCandleRefreshClientIdOffset);
        _refreshConnectionOptions = request.ConnectionOptions with
        {
            ClientId = request.ConnectionOptions.ClientId + clientIdOffset,
            SendOrders = false,
            UseL2 = false
        };

        _historyProvider = HistoricalBarProviderFactory.Create(
            request.ScannerOptions.RequestIbkrHistory,
            _refreshConnectionOptions);

        int lookbackMinutes = Math.Max(15, request.LiveCandleRefreshLookbackMinutes);
        _lookback = TimeSpan.FromMinutes(lookbackMinutes);
    }

    public string ProviderName => _historyProvider.ProviderName;

    public int ClientId => _refreshConnectionOptions.ClientId;

    public string ToDisplayString()
        => $"provider={ProviderName} clientId={ClientId} lookbackMinutes={(int)_lookback.TotalMinutes}";

    public async Task<IReadOnlyList<PaperLiveCandleRefreshResult>> RefreshAsync(
        IReadOnlyList<PaperSymbolSession> sessions,
        PaperRuntimeHostRequest request,
        int iteration,
        DateTimeOffset observedUtc,
        CancellationToken cancellationToken)
    {
        var results = new List<PaperLiveCandleRefreshResult>();
        int requestId = Math.Max(31_000, request.LiveCandleRefreshRequestIdBase) + (iteration * 1_000);

        foreach (PaperSymbolSession session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HistoricalBarRequest historicalRequest = HistoricalBarRequest.CreateOneMinute(
                request.RuntimeOptions.Mode,
                session.Symbol,
                _lookback,
                requestId++,
                request.ScannerOptions.UseRegularTradingHours,
                request.PrimaryExchange,
                mirrorToBacktestData: request.ScannerOptions.MirrorHistoryToBacktest);

            HistoricalBarLoadResult loadResult = await _historyProvider
                .GetOneMinuteHistoryAsync(historicalRequest, cancellationToken)
                .ConfigureAwait(false);

            var warnings = new List<string>(loadResult.Warnings.Select(warning => $"{session.Symbol}: {warning}"));
            if (!loadResult.Success)
            {
                results.Add(PaperLiveCandleRefreshResult.Failed(
                    session.Symbol,
                    session.LastFrameTime,
                    session.LastLoadedBarTime,
                    $"SAILOR-059 live paper candle refresh failed: {loadResult.Message}",
                    warnings));
                continue;
            }

            PaperLiveCandleRefreshResult applyResult = session.ApplyLiveCandleRefresh(
                loadResult.Bars,
                observedUtc,
                Math.Max(1, request.LiveBarMaxAgeMinutes),
                Math.Max(0, request.LiveBarFutureToleranceMinutes));

            if (warnings.Count > 0)
            {
                applyResult = applyResult with { Warnings = applyResult.Warnings.Concat(warnings).ToArray() };
            }

            results.Add(applyResult);
        }

        return results;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_historyProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
