namespace Sailor.App.Logging;

public static class SailorLogPaths
{
    public static string LogsRoot => EnsureDirectory(Path.Combine(RepositoryRoot, "logs"));

    public static string Backtest => EnsureDirectory(Path.Combine(LogsRoot, "Backtest"));

    public static string BacktestHtml => EnsureDirectory(Path.Combine(Backtest, "Html"));

    public static string Live => EnsureDirectory(Path.Combine(LogsRoot, "Live"));

    public static string Paper => EnsureDirectory(Path.Combine(LogsRoot, "Paper"));

    public static string CreateBacktestLogFilePath()
    {
        string fileName = $"backtest_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        return Path.Combine(Backtest, fileName);
    }

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
