using System.Globalization;
using Sailor.App.Backtest.Models;

namespace Sailor.App.MarketData.History;

public static class HistoricalCacheWriter
{
    public static (string CachePath, string? BacktestMirrorPath) Write(
        HistoricalBarRequest request,
        IReadOnlyList<BacktestBar> bars)
    {
        IReadOnlyList<BacktestBar> normalizedBars = bars
            .OrderBy(bar => bar.Time)
            .ToArray();

        string cachePath = HistoricalCachePaths.GetCacheFilePath(
            request.Symbol,
            request.Timeframe,
            request.EndTimeUtc);

        WriteCsv(cachePath, normalizedBars);

        string? mirrorPath = null;
        if (request.MirrorToBacktestData)
        {
            mirrorPath = HistoricalCachePaths.GetBacktestMirrorFilePath(request.Symbol, request.Timeframe);
            WriteCsv(mirrorPath, normalizedBars);
        }

        return (cachePath, mirrorPath);
    }

    private static void WriteCsv(string path, IReadOnlyList<BacktestBar> bars)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine("Timestamp,Open,High,Low,Close,Volume");

        foreach (BacktestBar bar in bars)
        {
            writer.Write(bar.Time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.Open.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.High.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.Low.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(bar.Close.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(bar.Volume.ToString(CultureInfo.InvariantCulture));
        }
    }
}
