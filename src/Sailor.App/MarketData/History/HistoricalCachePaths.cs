using Sailor.App.Logging;

namespace Sailor.App.MarketData.History;

public static class HistoricalCachePaths
{
    public static string CacheRoot => EnsureDirectory(Path.Combine(RepositoryRoot, "cache", "history"));

    public static string BacktestDataRoot => EnsureDirectory(Path.Combine(RepositoryRoot, "backtest", "data"));

    public static string GetSessionDirectory(DateTimeOffset timeUtc)
        => EnsureDirectory(Path.Combine(CacheRoot, timeUtc.ToString("yyyy-MM-dd")));

    public static string GetSymbolDirectory(string symbol, DateTimeOffset timeUtc)
        => EnsureDirectory(Path.Combine(GetSessionDirectory(timeUtc), HistoricalBarRequest.NormalizeSymbol(symbol)));

    public static string GetCacheFilePath(string symbol, string timeframe, DateTimeOffset timeUtc)
        => Path.Combine(GetSymbolDirectory(symbol, timeUtc), $"{timeframe}.csv");

    public static string GetBacktestMirrorFilePath(string symbol, string timeframe)
        => Path.Combine(EnsureDirectory(Path.Combine(BacktestDataRoot, HistoricalBarRequest.NormalizeSymbol(symbol))), $"{timeframe}.csv");

    private static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
