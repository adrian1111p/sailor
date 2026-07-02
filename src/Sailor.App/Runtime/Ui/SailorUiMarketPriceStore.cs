using System.Globalization;

namespace Sailor.App.Runtime.Ui;

public sealed record SailorUiMarketPricePoint(
    string Symbol,
    decimal Price,
    DateTimeOffset ObservedUtc,
    string Source,
    bool IsStale,
    string StaleReason)
{
    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? "UNKNOWN" : Symbol.Trim().ToUpperInvariant();
}

public static class SailorUiMarketPriceStore
{
    public static readonly TimeSpan FreshWindow = TimeSpan.FromSeconds(20);

    public static IReadOnlyDictionary<string, SailorUiMarketPricePoint> LoadLatest(
        string modeLogRoot,
        DateTimeOffset nowUtc,
        IList<string>? warnings = null)
    {
        string snapshotDirectory = Path.Combine(modeLogRoot, "Snapshots");
        if (!Directory.Exists(snapshotDirectory))
        {
            return new Dictionary<string, SailorUiMarketPricePoint>(StringComparer.OrdinalIgnoreCase);
        }

        var latest = new Dictionary<string, SailorUiMarketPricePoint>(StringComparer.OrdinalIgnoreCase);
        string[] files;
        try
        {
            files = Directory.GetFiles(snapshotDirectory, "snapshot_*.csv");
        }
        catch (Exception ex)
        {
            warnings?.Add($"Market snapshot directory could not be read: {ex.Message}");
            return latest;
        }

        foreach (string file in files)
        {
            IReadOnlyList<Dictionary<string, string>> rows;
            try
            {
                rows = SailorUiCsv.Read(file);
            }
            catch (Exception ex)
            {
                warnings?.Add($"Market snapshot CSV could not be read: {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            foreach (Dictionary<string, string> row in rows)
            {
                string symbol = ReadCsv(row, "Symbol").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                DateTimeOffset observedUtc = ReadCsvDateTimeOffset(row, "TimestampUtc", new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero));
                decimal price = ResolvePrice(row, out string priceSource);
                if (price <= 0m)
                {
                    continue;
                }

                bool stale = nowUtc - observedUtc > FreshWindow;
                var point = new SailorUiMarketPricePoint(
                    symbol,
                    price,
                    observedUtc,
                    $"market-snapshot:{priceSource}",
                    stale,
                    stale ? $"market snapshot older than {FreshWindow.TotalSeconds:0}s" : "fresh market snapshot");

                if (!latest.TryGetValue(symbol, out SailorUiMarketPricePoint? existing)
                    || point.ObservedUtc > existing.ObservedUtc)
                {
                    latest[symbol] = point;
                }
            }
        }

        return latest;
    }

    private static decimal ResolvePrice(Dictionary<string, string> row, out string source)
    {
        decimal last = ReadCsvDecimal(row, "Last", 0m);
        if (last > 0m)
        {
            source = "Last";
            return last;
        }

        decimal bid = ReadCsvDecimal(row, "Bid", 0m);
        decimal ask = ReadCsvDecimal(row, "Ask", 0m);
        if (bid > 0m && ask > 0m)
        {
            source = "MidBidAsk";
            return (bid + ask) / 2m;
        }

        if (bid > 0m)
        {
            source = "Bid";
            return bid;
        }

        if (ask > 0m)
        {
            source = "Ask";
            return ask;
        }

        source = "n/a";
        return 0m;
    }

    private static string ReadCsv(Dictionary<string, string> row, string column, string fallback = "")
        => row.TryGetValue(column, out string? value) ? value : fallback;

    private static decimal ReadCsvDecimal(Dictionary<string, string> row, string column, decimal fallback)
        => decimal.TryParse(ReadCsv(row, column), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value) ? value : fallback;

    private static DateTimeOffset ReadCsvDateTimeOffset(Dictionary<string, string> row, string column, DateTimeOffset fallback)
        => DateTimeOffset.TryParse(ReadCsv(row, column), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value) ? value : fallback;
}
