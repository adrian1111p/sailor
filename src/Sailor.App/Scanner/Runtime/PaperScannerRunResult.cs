using Sailor.App.Backtest.Scanner.Points;

namespace Sailor.App.Scanner.Runtime;

public sealed record PaperScannerRunResult(
    PaperScannerOptions Options,
    IReadOnlyList<string> ResolvedSymbols,
    IReadOnlyList<string> PreparedSymbols,
    IReadOnlyList<PaperScannerSymbolPreparation> Preparations,
    IReadOnlyList<PaperScannerCandidate> Candidates,
    string? CandidateReportPath,
    IReadOnlyList<string> Warnings,
    string? HybridComparisonReportPath = null,
    string? HybridComparisonMarkdownReportPath = null,
    int PointsCandidateTotalOverride = -1,
    int ReadyPointsCandidatesOverride = -1,
    int WeakReadyPointsCandidatesOverride = -1,
    int WatchOnlyPointsCandidatesOverride = -1,
    int NotReadyPointsCandidatesOverride = -1)
{
    public int HistorySuccessCount => Preparations.Count(row => row.HistorySuccess);

    public int PointsCandidates => PointsCandidateTotalOverride >= 0
        ? PointsCandidateTotalOverride
        : Candidates.Count(row => row.PointsCandidate is not null);

    public int ReadyPointsCandidates => ReadyPointsCandidatesOverride >= 0
        ? ReadyPointsCandidatesOverride
        : Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.Ready);

    public int WeakReadyPointsCandidates => WeakReadyPointsCandidatesOverride >= 0
        ? WeakReadyPointsCandidatesOverride
        : Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.WeakReady);

    public int WatchOnlyPointsCandidates => WatchOnlyPointsCandidatesOverride >= 0
        ? WatchOnlyPointsCandidatesOverride
        : Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.WatchOnly);

    public int NotReadyPointsCandidates => NotReadyPointsCandidatesOverride >= 0
        ? NotReadyPointsCandidatesOverride
        : Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.NotReady);

    public string ToSummaryString()
    {
        string pointsSummary = Options.ScannerMode is PointsScannerMode.PointsOnly or PointsScannerMode.HybridCompare
            ? $" pointsCandidates={PointsCandidates} ready={ReadyPointsCandidates} weakReady={WeakReadyPointsCandidates} watchOnly={WatchOnlyPointsCandidates} notReady={NotReadyPointsCandidates}"
            : string.Empty;
        string hybridSummary = string.IsNullOrWhiteSpace(HybridComparisonReportPath)
            ? string.Empty
            : $" hybridReport={HybridComparisonReportPath}" +
              (string.IsNullOrWhiteSpace(HybridComparisonMarkdownReportPath) ? string.Empty : $" hybridMarkdown={HybridComparisonMarkdownReportPath}");

        return $"resolved={ResolvedSymbols.Count} prepared={PreparedSymbols.Count} historyOk={HistorySuccessCount}/{Preparations.Count} " +
               $"scannerMode={Options.ScannerMode.ToConfigValue()} candidates={Candidates.Count}{pointsSummary} report={CandidateReportPath ?? "n/a"}{hybridSummary}";
    }
}
