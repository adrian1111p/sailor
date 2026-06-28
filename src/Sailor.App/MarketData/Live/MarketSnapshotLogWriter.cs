using System.Globalization;
using Sailor.App.MarketData.Snapshots;
using Sailor.App.Runtime.Common;

namespace Sailor.App.MarketData.Live;

public static class MarketSnapshotLogWriter
{
    public static string WriteSnapshot(
        SailorRuntimeMode mode,
        LiveMarketDataRequest request,
        SailorMarketSnapshot snapshot)
    {
        string root = mode == SailorRuntimeMode.Live
            ? Path.Combine("logs", "Live", "Snapshots")
            : Path.Combine("logs", "Paper", "Snapshots");

        Directory.CreateDirectory(root);
        string path = Path.Combine(root, $"snapshot_{request.NormalizedSymbol}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        using var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        writer.WriteLine("TimestampUtc,Symbol,Source,Quality,Bid,Ask,Last,BidSize,AskSize,Spread,SpreadBps,L1Imbalance,L2Levels,L2TotalBidSize,L2TotalAskSize,L2Imbalance,LiquidityScore");
        writer.WriteLine(string.Join(',',
            snapshot.Time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            snapshot.Symbol,
            snapshot.Source,
            snapshot.Quality,
            Format(snapshot.L1?.Bid),
            Format(snapshot.L1?.Ask),
            Format(snapshot.L1?.Last),
            snapshot.L1?.BidSize.ToString(CultureInfo.InvariantCulture) ?? "0",
            snapshot.L1?.AskSize.ToString(CultureInfo.InvariantCulture) ?? "0",
            Format(snapshot.L1?.Spread),
            Format(snapshot.L1?.SpreadBps),
            Format(snapshot.L1?.SizeImbalance),
            snapshot.L2?.Levels.Count.ToString(CultureInfo.InvariantCulture) ?? "0",
            snapshot.L2?.TotalBidSize.ToString(CultureInfo.InvariantCulture) ?? "0",
            snapshot.L2?.TotalAskSize.ToString(CultureInfo.InvariantCulture) ?? "0",
            Format(snapshot.L2?.BookImbalance),
            Format(snapshot.LiquidityScore)));

        if (snapshot.L2 is not null)
        {
            writer.WriteLine();
            writer.WriteLine("Level,BidPrice,BidSize,AskPrice,AskSize");
            foreach (L2OrderBookLevel level in snapshot.L2.Levels)
            {
                writer.WriteLine(string.Join(',',
                    level.Level.ToString(CultureInfo.InvariantCulture),
                    Format(level.BidPrice),
                    level.BidSize.ToString(CultureInfo.InvariantCulture),
                    Format(level.AskPrice),
                    level.AskSize.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return path;
    }

    private static string Format(decimal? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "0";
}
