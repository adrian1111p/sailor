using Sailor.App.Backtest.Models;

namespace Sailor.App.MarketData.History;

public sealed record HistoricalBarLoadResult(
    string Symbol,
    string Timeframe,
    bool Success,
    bool RemoteRequested,
    bool RemoteProviderAvailable,
    IReadOnlyList<BacktestBar> Bars,
    string CachePath,
    string? BacktestMirrorPath,
    string Message,
    IReadOnlyList<string> Warnings)
{
    public int BarCount => Bars.Count;

    public DateTimeOffset? FirstBarTime => Bars.Count == 0 ? null : Bars[0].Time;

    public DateTimeOffset? LastBarTime => Bars.Count == 0 ? null : Bars[^1].Time;

    public string ToDisplayString()
    {
        string first = FirstBarTime?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";
        string last = LastBarTime?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";
        string source = RemoteRequested
            ? RemoteProviderAvailable ? "IBKR" : "local-cache-fallback"
            : "local-cache";

        return $"{Symbol} {Timeframe}: success={Success} source={source} bars={BarCount} first={first} last={last} cache={CachePath}";
    }

    public static HistoricalBarLoadResult Failed(
        HistoricalBarRequest request,
        bool remoteRequested,
        bool remoteProviderAvailable,
        string cachePath,
        string message,
        IEnumerable<string>? warnings = null)
        => new(
            request.Symbol,
            request.Timeframe,
            Success: false,
            remoteRequested,
            remoteProviderAvailable,
            Bars: Array.Empty<BacktestBar>(),
            CachePath: cachePath,
            BacktestMirrorPath: null,
            Message: message,
            Warnings: warnings?.ToArray() ?? Array.Empty<string>());
}
