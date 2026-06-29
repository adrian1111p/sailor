namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListCandidateRetentionOptions(
    decimal PointsMinimumTradeScore,
    bool PointsAllowWeakEntry,
    bool PointsRetainWatchOnly)
{
    public static ScanListCandidateRetentionOptions FromScannerOptions(Sailor.App.Scanner.Runtime.PaperScannerOptions options)
        => new(
            options.PointsMinimumTradeScore,
            options.PointsAllowWeakEntry,
            options.PointsRetainWatchOnly);

    public string ToDisplayString()
        => $"pointsMinTradeScore={PointsMinimumTradeScore:F2} pointsAllowWeakEntry={PointsAllowWeakEntry} pointsRetainWatchOnly={PointsRetainWatchOnly}";
}
