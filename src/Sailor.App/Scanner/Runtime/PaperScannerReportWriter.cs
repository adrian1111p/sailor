using System.Globalization;
using Sailor.App.Backtest.Scanner.Points;
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
        writer.WriteLine("Rank,ScannerMode,Status,Symbol,SelectedSide,FinalScore,LegacyCandidateScore,LongScore,ShortScore,PositivePoints,NegativePoints,Close,MomentumPercent,Volume,VolumeRatio,Ema9,Sma20,Sma200,Vwap,HasL1,HasL2,SpreadBps,BookImbalance,LiquidityScore,SnapshotSource,LegacyBlockReasons,TopPositiveFactors,TopNegativeFactors,Reason");

        foreach (PaperScannerCandidate row in result.Candidates)
        {
            writer.WriteLine(string.Join(',',
                row.Rank.ToString(CultureInfo.InvariantCulture),
                Escape(row.ScannerMode),
                Escape(row.PointsCandidate is null ? string.Empty : row.PointsCandidate.Status.ToDisplayName()),
                row.Candidate.Symbol,
                row.Candidate.Side,
                Format(row.PointsCandidate?.FinalScore ?? row.Candidate.Score),
                Format(row.PointsCandidate is null ? row.Candidate.Score : null),
                Format(row.PointsCandidate?.LongScore.Score),
                Format(row.PointsCandidate?.ShortScore.Score),
                Format(row.PointsCandidate?.PositivePoints),
                Format(row.PointsCandidate?.NegativePoints),
                Format(row.Candidate.Close),
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
                Escape(row.PointsCandidate is null ? string.Empty : string.Join(" | ", row.PointsCandidate.LegacyBlockReasons)),
                Escape(row.PointsCandidate?.SelectedScore.TopPositiveFactors() ?? string.Empty),
                Escape(row.PointsCandidate?.SelectedScore.TopNegativeFactors() ?? string.Empty),
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
