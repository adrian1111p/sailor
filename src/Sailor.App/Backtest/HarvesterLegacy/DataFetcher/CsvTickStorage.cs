using System.Globalization;

namespace Sailor.App.Backtest.DataFetcher;

/// <summary>
/// CSV persistence for historical L1 tick data (bid-ask quotes and trade ticks).
/// Stored alongside bar data in backtest/data/{SYMBOL}/ as:
///   - l1_bidask.csv  (bid/ask prices and sizes per tick)
///   - l1_trades.csv  (last trade price, size, exchange)
///
/// Format mirrors the IBKR reqHistoricalTicks output for round-trip fidelity.
/// </summary>
public static class CsvTickStorage
{
    private const string BidAskFileName = "l1_bidask.csv";
    private const string TradesFileName = "l1_trades.csv";

    // â”€â”€ Bid-Ask Ticks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public static bool BidAskExists(string symbol)
    {
        return CsvBarStorage.FindExistingDataFilePath(symbol, BidAskFileName) is not null;
    }

    public static BidAskTick[] LoadBidAskTicks(string symbol)
    {
        var path = CsvBarStorage.FindExistingDataFilePath(symbol, BidAskFileName);
        if (path is null) return [];

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return [];

        var ticks = new List<BidAskTick>();
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 6) continue;

            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var bid)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ask)) continue;
            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var bidSize)) continue;
            if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var askSize)) continue;

            ticks.Add(new BidAskTick(ts, bid, ask, bidSize, askSize));
        }
        return ticks.OrderBy(t => t.TimestampUtc).ToArray();
    }

    public static void SaveBidAskTicks(string symbol, BidAskTick[] ticks)
    {
        var dir = Path.Combine(CsvBarStorage.ResolveDataDir(), symbol.ToUpperInvariant());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, BidAskFileName);

        using var writer = new StreamWriter(path);
        writer.WriteLine("TimestampUtc,Bid,Ask,BidSize,AskSize,SpreadBps");
        foreach (var t in ticks.OrderBy(x => x.TimestampUtc))
        {
            var mid = (t.Bid + t.Ask) / 2.0;
            var spreadBps = mid > 0 ? (t.Ask - t.Bid) / mid * 10_000.0 : 0;
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fffZ},{1:F4},{2:F4},{3:F0},{4:F0},{5:F1}",
                t.TimestampUtc, t.Bid, t.Ask, t.BidSize, t.AskSize, spreadBps));
        }
        Console.WriteLine($"  Saved {path} ({ticks.Length} bid-ask ticks)");
    }

    public static void AppendBidAskTicks(string symbol, BidAskTick[] newTicks)
    {
        var existing = BidAskExists(symbol) ? LoadBidAskTicks(symbol) : [];
        var lastTs = existing.Length > 0 ? existing[^1].TimestampUtc : DateTime.MinValue;
        var deduped = newTicks.Where(t => t.TimestampUtc > lastTs).ToArray();
        if (deduped.Length == 0) return;

        var merged = existing.Concat(deduped).OrderBy(t => t.TimestampUtc).ToArray();
        SaveBidAskTicks(symbol, merged);
    }

    public static void MergeBidAskTicks(string symbol, BidAskTick[] newTicks)
    {
        if (newTicks.Length == 0)
        {
            return;
        }

        var existing = BidAskExists(symbol) ? LoadBidAskTicks(symbol) : [];
        var rangeStart = newTicks.Min(t => t.TimestampUtc);
        var rangeEnd = newTicks.Max(t => t.TimestampUtc);
        var retained = existing.Where(t => t.TimestampUtc < rangeStart || t.TimestampUtc > rangeEnd);
        var merged = retained.Concat(newTicks).OrderBy(t => t.TimestampUtc).ToArray();
        SaveBidAskTicks(symbol, merged);
    }

    // â”€â”€ Trade Ticks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public static bool TradesExist(string symbol)
    {
        return CsvBarStorage.FindExistingDataFilePath(symbol, TradesFileName) is not null;
    }

    public static TradeTick[] LoadTradeTicks(string symbol)
    {
        var path = CsvBarStorage.FindExistingDataFilePath(symbol, TradesFileName);
        if (path is null) return [];

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return [];

        var ticks = new List<TradeTick>();
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 4) continue;

            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var price)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) continue;
            var exchange = parts.Length > 3 ? parts[3] : "";

            ticks.Add(new TradeTick(ts, price, size, exchange));
        }
        return ticks.OrderBy(t => t.TimestampUtc).ToArray();
    }

    public static void SaveTradeTicks(string symbol, TradeTick[] ticks)
    {
        var dir = Path.Combine(CsvBarStorage.ResolveDataDir(), symbol.ToUpperInvariant());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, TradesFileName);

        using var writer = new StreamWriter(path);
        writer.WriteLine("TimestampUtc,Price,Size,Exchange");
        foreach (var t in ticks.OrderBy(x => x.TimestampUtc))
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fffZ},{1:F4},{2:F0},{3}",
                t.TimestampUtc, t.Price, t.Size, t.Exchange));
        }
        Console.WriteLine($"  Saved {path} ({ticks.Length} trade ticks)");
    }

    public static void AppendTradeTicks(string symbol, TradeTick[] newTicks)
    {
        var existing = TradesExist(symbol) ? LoadTradeTicks(symbol) : [];
        var lastTs = existing.Length > 0 ? existing[^1].TimestampUtc : DateTime.MinValue;
        var deduped = newTicks.Where(t => t.TimestampUtc > lastTs).ToArray();
        if (deduped.Length == 0) return;

        var merged = existing.Concat(deduped).OrderBy(t => t.TimestampUtc).ToArray();
        SaveTradeTicks(symbol, merged);
    }

    public static void MergeTradeTicks(string symbol, TradeTick[] newTicks)
    {
        if (newTicks.Length == 0)
        {
            return;
        }

        var existing = TradesExist(symbol) ? LoadTradeTicks(symbol) : [];
        var rangeStart = newTicks.Min(t => t.TimestampUtc);
        var rangeEnd = newTicks.Max(t => t.TimestampUtc);
        var retained = existing.Where(t => t.TimestampUtc < rangeStart || t.TimestampUtc > rangeEnd);
        var merged = retained.Concat(newTicks).OrderBy(t => t.TimestampUtc).ToArray();
        SaveTradeTicks(symbol, merged);
    }

    // â”€â”€ Metadata â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns the time range of stored tick data for a symbol.</summary>
    public static (DateTime Start, DateTime End)? GetTickRange(string symbol)
    {
        var bidAsk = BidAskExists(symbol) ? LoadBidAskTicks(symbol) : [];
        var trades = TradesExist(symbol) ? LoadTradeTicks(symbol) : [];

        if (bidAsk.Length == 0 && trades.Length == 0) return null;

        var allTimestamps = bidAsk.Select(t => t.TimestampUtc)
            .Concat(trades.Select(t => t.TimestampUtc));
        return (allTimestamps.Min(), allTimestamps.Max());
    }

    public static string[] ListSymbolsWithTicks()
    {
        return CsvBarStorage.EnumerateAllSymbolDataDirectories()
            .Where(d =>
                File.Exists(Path.Combine(d, BidAskFileName)) ||
                File.Exists(Path.Combine(d, TradesFileName)))
            .Select(d => Path.GetFileName(d))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToArray();
    }
}

// â”€â”€ Tick Data Models â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>A single bid-ask quote snapshot at a point in time.</summary>
public sealed record BidAskTick(
    DateTime TimestampUtc,
    double Bid,
    double Ask,
    double BidSize,
    double AskSize);

/// <summary>A single trade execution at a point in time.</summary>
public sealed record TradeTick(
    DateTime TimestampUtc,
    double Price,
    double Size,
    string Exchange);

