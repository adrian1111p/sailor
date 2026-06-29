using System.Globalization;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Backtest.Scanner.Points;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Scanner.Runtime;

public static class PaperScannerHybridComparisonReportWriter
{
    public static PaperScannerHybridComparisonReportPaths Write(
        PaperScannerOptions options,
        IReadOnlyList<ScannerCandidate> legacyCandidates,
        IReadOnlyList<PointsScannerCandidate> pointsCandidates)
    {
        string root = options.Mode == SailorRuntimeMode.Live
            ? Path.Combine(SailorLogPaths.Live, "Scanner")
            : Path.Combine(SailorLogPaths.Paper, "Scanner");

        Directory.CreateDirectory(root);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string csvPath = Path.Combine(root, $"points_vs_legacy_{options.ProfileName}_{options.Timeframe}_{stamp}.csv");
        string mdPath = Path.Combine(root, $"points_vs_legacy_{options.ProfileName}_{options.Timeframe}_{stamp}.md");

        var legacyBySymbol = legacyCandidates.ToDictionary(candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase);
        var pointRows = pointsCandidates
            .Select((candidate, index) => (Candidate: candidate, Rank: index + 1))
            .ToArray();

        using (var writer = new StreamWriter(new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.Read)))
        {
            writer.WriteLine("Symbol,LegacyCandidate,LegacySide,LegacyScore,LegacyReason,PointsRank,PointsStatus,SelectedSide,PointsScore,LongScore,ShortScore,PositivePoints,NegativePoints,WouldTradeByLegacy,WouldTradeByPoints,LegacyBlockReasons,TopPositiveFactors,TopNegativeFactors");
            foreach ((PointsScannerCandidate points, int rank) in pointRows)
            {
                bool legacyCandidate = legacyBySymbol.TryGetValue(points.Symbol, out ScannerCandidate? legacy);
                bool wouldTradeByPoints = IsTradeEligibleByPoints(options, points);
                writer.WriteLine(string.Join(',',
                    Escape(points.Symbol),
                    legacyCandidate.ToString(CultureInfo.InvariantCulture),
                    Escape(legacy?.Side ?? string.Empty),
                    Format(legacy?.Score),
                    Escape(legacy?.Reason ?? string.Empty),
                    rank.ToString(CultureInfo.InvariantCulture),
                    Escape(points.Status.ToDisplayName()),
                    Escape(points.SelectedSide),
                    Format(points.FinalScore),
                    Format(points.LongScore.Score),
                    Format(points.ShortScore.Score),
                    Format(points.PositivePoints),
                    Format(points.NegativePoints),
                    legacyCandidate.ToString(CultureInfo.InvariantCulture),
                    wouldTradeByPoints.ToString(CultureInfo.InvariantCulture),
                    Escape(string.Join(" | ", points.LegacyBlockReasons)),
                    Escape(points.SelectedScore.TopPositiveFactors()),
                    Escape(points.SelectedScore.TopNegativeFactors())));
            }
        }

        using (var writer = new StreamWriter(new FileStream(mdPath, FileMode.Create, FileAccess.Write, FileShare.Read)))
        {
            writer.WriteLine($"# Points vs legacy scanner comparison — {options.ProfileName} {options.Timeframe}");
            writer.WriteLine();
            writer.WriteLine($"- Scanner mode: `{options.ScannerMode.ToConfigValue()}`");
            writer.WriteLine($"- Legacy selected candidates: {legacyCandidates.Count}");
            writer.WriteLine($"- Points ranked candidates: {pointsCandidates.Count}");
            writer.WriteLine($"- Points minimum trade score: {options.PointsMinimumTradeScore:F2}");
            writer.WriteLine($"- Weak entries allowed: {options.PointsAllowWeakEntry}");
            writer.WriteLine($"- Watch-only retained: {options.PointsRetainWatchOnly}");
            writer.WriteLine();
            writer.WriteLine("| Rank | Symbol | Legacy | Points status | Side | Score | Would trade by points | Legacy block reasons |");
            writer.WriteLine("|---:|---|---:|---|---|---:|---:|---|");
            foreach ((PointsScannerCandidate points, int rank) in pointRows)
            {
                bool legacyCandidate = legacyBySymbol.ContainsKey(points.Symbol);
                bool wouldTradeByPoints = IsTradeEligibleByPoints(options, points);
                string blocks = points.LegacyBlockReasons.Count == 0
                    ? "none"
                    : string.Join("<br>", points.LegacyBlockReasons.Select(EscapeMarkdown));
                writer.WriteLine($"| {rank} | {EscapeMarkdown(points.Symbol)} | {legacyCandidate} | {points.Status.ToDisplayName()} | {points.SelectedSide} | {points.FinalScore:F2} | {wouldTradeByPoints} | {blocks} |");
            }
        }

        return new PaperScannerHybridComparisonReportPaths(csvPath, mdPath);
    }

    private static bool IsTradeEligibleByPoints(PaperScannerOptions options, PointsScannerCandidate candidate)
    {
        if (candidate.FinalScore < options.PointsMinimumTradeScore)
        {
            return false;
        }

        return candidate.Status switch
        {
            PointsScannerStatus.Ready => true,
            PointsScannerStatus.WeakReady => options.PointsAllowWeakEntry,
            _ => false
        };
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

    private static string EscapeMarkdown(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);
}

public sealed record PaperScannerHybridComparisonReportPaths(string CsvPath, string MarkdownPath);
