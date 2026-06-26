using System.Globalization;
using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Data;

public sealed class CsvBacktestDataProvider
{
    public BacktestDataSet LoadBars(string symbol, string timeframe)
    {
        string normalizedSymbol = symbol.Trim().ToUpperInvariant();
        string normalizedTimeframe = string.IsNullOrWhiteSpace(timeframe)
            ? "1m"
            : timeframe.Trim();

        string? path = FindDataFilePath(normalizedSymbol, normalizedTimeframe);
        if (path is null)
        {
            string available = string.Join(", ", ListSymbols().Take(25));
            throw new FileNotFoundException(
                $"No CSV data found for {normalizedSymbol} {normalizedTimeframe}. " +
                $"Expected file: backtest/data/{normalizedSymbol}/{normalizedTimeframe}.csv. " +
                $"Available symbols sample: {available}");
        }

        IReadOnlyList<BacktestBar> bars = ParseBars(path, normalizedSymbol)
            .OrderBy(b => b.Time)
            .ToArray();

        if (bars.Count == 0)
        {
            throw new InvalidOperationException($"CSV file exists but no valid bars could be parsed: {path}");
        }

        return new BacktestDataSet(
            Symbol: normalizedSymbol,
            Timeframe: normalizedTimeframe,
            SourcePath: path,
            Bars: bars);
    }

    public IReadOnlyList<string> ListSymbols()
    {
        var symbols = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        string dataRoot = ResolveDataRoot();
        if (Directory.Exists(dataRoot))
        {
            foreach (string directory in Directory.GetDirectories(dataRoot))
            {
                string? name = Path.GetFileName(directory);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    symbols.Add(name.ToUpperInvariant());
                }
            }
        }

        string? priceBucketRoot = ResolvePriceBucketDataRoot();
        if (priceBucketRoot is not null && Directory.Exists(priceBucketRoot))
        {
            foreach (string bucketDirectory in Directory.GetDirectories(priceBucketRoot))
            {
                foreach (string symbolDirectory in Directory.GetDirectories(bucketDirectory))
                {
                    string? name = Path.GetFileName(symbolDirectory);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        symbols.Add(name.ToUpperInvariant());
                    }
                }
            }
        }

        return symbols.ToArray();
    }

    public IReadOnlyList<string> ListTimeframes(string symbol)
    {
        string normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var timeframes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string symbolDirectory in EnumerateSymbolDirectories(normalizedSymbol))
        {
            foreach (string file in Directory.GetFiles(symbolDirectory, "*.csv"))
            {
                string timeframe = Path.GetFileNameWithoutExtension(file);
                if (!string.Equals(timeframe, "l1_bidask", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(timeframe, "l1_trades", StringComparison.OrdinalIgnoreCase))
                {
                    timeframes.Add(timeframe);
                }
            }
        }

        return timeframes.ToArray();
    }

    private static IReadOnlyList<BacktestBar> ParseBars(string path, string symbol)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            return Array.Empty<BacktestBar>();
        }

        string[] header = lines[0].Split(',');
        int timestampIndex = FindColumn(header, "Timestamp", fallbackIndex: 0);
        int openIndex = FindColumn(header, "Open", fallbackIndex: 1);
        int highIndex = FindColumn(header, "High", fallbackIndex: 2);
        int lowIndex = FindColumn(header, "Low", fallbackIndex: 3);
        int closeIndex = FindColumn(header, "Close", fallbackIndex: 4);
        int volumeIndex = FindColumn(header, "Volume", fallbackIndex: 5);

        var bars = new List<BacktestBar>(capacity: lines.Length - 1);

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length <= Math.Max(volumeIndex, closeIndex))
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(
                    parts[timestampIndex],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset timestamp))
            {
                continue;
            }

            if (!TryParseDecimal(parts[openIndex], out decimal open)) continue;
            if (!TryParseDecimal(parts[highIndex], out decimal high)) continue;
            if (!TryParseDecimal(parts[lowIndex], out decimal low)) continue;
            if (!TryParseDecimal(parts[closeIndex], out decimal close)) continue;

            long volume = 0;
            if (volumeIndex >= 0 && volumeIndex < parts.Length)
            {
                _ = long.TryParse(
                    parts[volumeIndex],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out volume);
            }

            bars.Add(new BacktestBar(
                Time: timestamp,
                Symbol: symbol,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume));
        }

        return bars;
    }

    private string? FindDataFilePath(string symbol, string timeframe)
    {
        string fileName = $"{timeframe}.csv";

        foreach (string symbolDirectory in EnumerateSymbolDirectories(symbol))
        {
            string candidate = Path.Combine(symbolDirectory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSymbolDirectories(string symbol)
    {
        string dataRoot = ResolveDataRoot();
        string primary = Path.Combine(dataRoot, symbol);
        if (Directory.Exists(primary))
        {
            yield return primary;
        }

        string? priceBucketRoot = ResolvePriceBucketDataRoot();
        if (priceBucketRoot is null || !Directory.Exists(priceBucketRoot))
        {
            yield break;
        }

        foreach (string bucketDirectory in Directory.GetDirectories(priceBucketRoot))
        {
            string candidate = Path.Combine(bucketDirectory, symbol);
            if (Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string ResolveDataRoot()
    {
        string fromCurrentDirectory = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "backtest",
            "data"));

        if (Directory.Exists(fromCurrentDirectory))
        {
            return fromCurrentDirectory;
        }

        string fromAppBase = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "backtest",
            "data"));

        if (Directory.Exists(fromAppBase))
        {
            return fromAppBase;
        }

        return fromCurrentDirectory;
    }

    private static string? ResolvePriceBucketDataRoot()
    {
        string fromCurrentDirectory = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "backtest",
            "data_by_price"));

        if (Directory.Exists(fromCurrentDirectory))
        {
            return fromCurrentDirectory;
        }

        string fromAppBase = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "backtest",
            "data_by_price"));

        return Directory.Exists(fromAppBase) ? fromAppBase : null;
    }

    private static int FindColumn(string[] header, string name, int fallbackIndex)
    {
        int index = Array.FindIndex(
            header,
            value => value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

        return index >= 0 ? index : fallbackIndex;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        return decimal.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out result);
    }
}
