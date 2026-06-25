using System.Globalization;
using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.DataFetcher;

/// <summary>
/// CSV-based bar storage for backtest data.
/// Reads/writes OHLCV bars in the same format as the Python data_fetcher.py.
/// Storage path: backtest/data/{SYMBOL}/{timeframe}.csv
/// </summary>
public static class CsvBarStorage
{
    private static readonly string DataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "backtest", "data");
    private static readonly string PriceBucketDataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "backtest", "data_by_price");

    /// <summary>Resolve the base data directory (supports both bin/ and repo root execution).</summary>
    public static string ResolveDataDir()
    {
        // Try relative from binary output (bin/Debug/net9.0/)
        var fromBin = Path.GetFullPath(DataDir);
        if (Directory.Exists(fromBin)) return fromBin;

        // Try from repo root
        var fromRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "backtest", "data"));
        if (Directory.Exists(fromRoot)) return fromRoot;

        // Fallback: create at working directory
        var fallback = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "backtest", "data"));
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>Resolve the price-bucket data root used by manifest-based datasets.</summary>
    public static string? ResolvePriceBucketDataDir()
    {
        var fromBin = Path.GetFullPath(PriceBucketDataDir);
        if (Directory.Exists(fromBin)) return fromBin;

        var fromRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "backtest", "data_by_price"));
        if (Directory.Exists(fromRoot)) return fromRoot;

        return null;
    }

    /// <summary>Check if data exists for a symbol + timeframe.</summary>
    public static bool Exists(string symbol, string timeframe)
    {
        return FindExistingDataFilePath(symbol, $"{timeframe}.csv") is not null;
    }

    /// <summary>Load previously saved CSV data. Returns BacktestBar[].</summary>
    public static BacktestBar[] LoadBars(string symbol, string timeframe)
    {
        var path = FindExistingDataFilePath(symbol, $"{timeframe}.csv");
        if (path is null)
            throw new FileNotFoundException(
                $"No data for {symbol.ToUpperInvariant()} {timeframe}.csv under {ResolveDataDir()} or {ResolvePriceBucketDataDir() ?? "<missing data_by_price root>"}");

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return [];

        // Parse header to find column indices
        var header = lines[0].Split(',');
        int tsIdx = Array.FindIndex(header, h => h.Trim().Equals("Timestamp", StringComparison.OrdinalIgnoreCase));
        int oIdx = Array.FindIndex(header, h => h.Trim().Equals("Open", StringComparison.OrdinalIgnoreCase));
        int hIdx = Array.FindIndex(header, h => h.Trim().Equals("High", StringComparison.OrdinalIgnoreCase));
        int lIdx = Array.FindIndex(header, h => h.Trim().Equals("Low", StringComparison.OrdinalIgnoreCase));
        int cIdx = Array.FindIndex(header, h => h.Trim().Equals("Close", StringComparison.OrdinalIgnoreCase));
        int vIdx = Array.FindIndex(header, h => h.Trim().Equals("Volume", StringComparison.OrdinalIgnoreCase));

        // If Timestamp is the index column (first col without header "Timestamp"),
        // it may be at position 0 unnamed
        if (tsIdx < 0) tsIdx = 0;

        var bars = new List<BacktestBar>();
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 5) continue;

            if (!DateTime.TryParse(parts[tsIdx], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts))
                continue;

            if (!double.TryParse(parts[oIdx >= 0 ? oIdx : 1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double open)) continue;
            if (!double.TryParse(parts[hIdx >= 0 ? hIdx : 2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double high)) continue;
            if (!double.TryParse(parts[lIdx >= 0 ? lIdx : 3], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double low)) continue;
            if (!double.TryParse(parts[cIdx >= 0 ? cIdx : 4], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double close)) continue;
            if (!double.TryParse(parts[vIdx >= 0 ? vIdx : 5], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double volume)) continue;

            bars.Add(new BacktestBar(ts, open, high, low, close, volume));
        }

        return bars.OrderBy(b => b.Timestamp).ToArray();
    }

    /// <summary>Save bars as CSV in backtest/data/{SYMBOL}/{timeframe}.csv.</summary>
    public static void SaveBars(string symbol, string timeframe, BacktestBar[] bars)
    {
        var dir = Path.Combine(ResolveDataDir(), symbol.ToUpperInvariant());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{timeframe}.csv");

        using var writer = new StreamWriter(path);
        writer.WriteLine("Timestamp,Open,High,Low,Close,Volume");
        foreach (var b in bars.OrderBy(x => x.Timestamp))
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:sszzz},{1},{2},{3},{4},{5}",
                b.Timestamp, b.Open, b.High, b.Low, b.Close, b.Volume));
        }
        Console.WriteLine($"  Saved {path} ({bars.Length} rows)");
    }

    /// <summary>List available symbols (directories in data/).</summary>
    public static string[] ListSymbols()
    {
        return EnumerateAllSymbolDataDirectories()
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>List available timeframes for a symbol.</summary>
    public static string[] ListTimeframes(string symbol)
    {
        return EnumerateSymbolDataDirectories(symbol)
            .SelectMany(symDir => Directory.GetFiles(symDir, "*.csv"))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tf => tf, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IEnumerable<string> EnumerateSymbolDataDirectories(string symbol)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var primaryDir = Path.Combine(ResolveDataDir(), normalizedSymbol);
        if (Directory.Exists(primaryDir))
        {
            var fullPrimary = Path.GetFullPath(primaryDir);
            if (seen.Add(fullPrimary))
                yield return fullPrimary;
        }

        var bucketRoot = ResolvePriceBucketDataDir();
        if (string.IsNullOrWhiteSpace(bucketRoot) || !Directory.Exists(bucketRoot))
            yield break;

        foreach (var bucketDir in Directory.GetDirectories(bucketRoot))
        {
            var candidateDir = Path.Combine(bucketDir, normalizedSymbol);
            if (!Directory.Exists(candidateDir))
                continue;

            var fullCandidate = Path.GetFullPath(candidateDir);
            if (seen.Add(fullCandidate))
                yield return fullCandidate;
        }
    }

    internal static IEnumerable<string> EnumerateAllSymbolDataDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dataDir = ResolveDataDir();
        if (Directory.Exists(dataDir))
        {
            foreach (var symbolDir in Directory.GetDirectories(dataDir))
            {
                var fullDir = Path.GetFullPath(symbolDir);
                if (seen.Add(fullDir))
                    yield return fullDir;
            }
        }

        var bucketRoot = ResolvePriceBucketDataDir();
        if (string.IsNullOrWhiteSpace(bucketRoot) || !Directory.Exists(bucketRoot))
            yield break;

        foreach (var bucketDir in Directory.GetDirectories(bucketRoot))
        {
            foreach (var symbolDir in Directory.GetDirectories(bucketDir))
            {
                var fullDir = Path.GetFullPath(symbolDir);
                if (seen.Add(fullDir))
                    yield return fullDir;
            }
        }
    }

    internal static string? FindExistingDataFilePath(string symbol, string fileName)
    {
        foreach (var symbolDir in EnumerateSymbolDataDirectories(symbol))
        {
            var path = Path.Combine(symbolDir, fileName);
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}

