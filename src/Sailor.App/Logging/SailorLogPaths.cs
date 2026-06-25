namespace Sailor.App.Logging;

public static class SailorLogPaths
{
    public static string LogsRoot => EnsureDirectory(
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Logs")));

    public static string Backtest => EnsureDirectory(Path.Combine(LogsRoot, "Backtest"));

    public static string Live => EnsureDirectory(Path.Combine(LogsRoot, "Live"));

    public static string Paper => EnsureDirectory(Path.Combine(LogsRoot, "Paper"));

    public static string CreateBacktestLogFilePath()
    {
        string fileName = $"backtest_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        return Path.Combine(Backtest, fileName);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
