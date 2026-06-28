using System.Globalization;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Scanner.Runtime;

public static class PaperScannerReportWriter
{
    public static string WriteCandidates(PaperScannerRunResult result)
    {
        string root = result.Options.Mode == SailorRuntimeMode.Live
            ? Path.Combine(SailorLogPaths.Live, "Scanner")
            : Path.Combine(SailorLogPaths.Paper, "Scanner");

        Directory.CreateDirectory(root);
        string path = Path.Combine(root, $"scanner_{result.Options.ProfileName}_{result.Options.Timeframe}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        using var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        writer.WriteLine("Rank,Symbol,Side,Close,Score,MomentumPercent,Volume,VolumeRatio,Ema9,Sma20,Sma200,Vwap,HasL1,HasL2,SpreadBps,BookImbalance,LiquidityScore,SnapshotSource,Reason");

        foreach (PaperScannerCandidate row in result.Candidates)
        {
            writer.WriteLine(string.Join(',',
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.Candidate.Symbol,
                row.Candidate.Side,
                Format(row.Candidate.Close),
                Format(row.Candidate.Score),
                Format(row.Candidate.MomentumPercent),
                row.Candidate.Volume.ToString(CultureInfo.InvariantCulture),
                Format(row.Candidate.VolumeRatio),
                Format(row.Candidate.Ema9),
                Format(row.Candidate.Sma20),
                Format(row.Candidate.Sma200),
                Format(row.Candidate.Vwap),
                row.HasL1.ToString(CultureInfo.InvariantCulture),
                row.HasL2.ToString(CultureInfo.InvariantCulture),
                Format(row.SpreadBps),
                Format(row.BookImbalance),
                Format(row.LiquidityScore),
                Escape(row.Snapshot?.Source ?? "n/a"),
                Escape(row.Candidate.Reason)));
        }

        return path;
    }

    private static string Format(decimal? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
