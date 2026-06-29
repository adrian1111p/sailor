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
    string? HybridComparisonReportPath = null)
{
    public int HistorySuccessCount => Preparations.Count(row => row.HistorySuccess);

    public int ReadyPointsCandidates => Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.Ready);

    public int WeakReadyPointsCandidates => Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.WeakReady);

    public int WatchOnlyPointsCandidates => Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.WatchOnly);

    public int NotReadyPointsCandidates => Candidates.Count(row => row.PointsCandidate?.Status == PointsScannerStatus.NotReady);

    public string ToSummaryString()
    {
        string pointsSummary = Options.ScannerMode is PointsScannerMode.PointsOnly or PointsScannerMode.HybridCompare
            ? $" ready={ReadyPointsCandidates} weakReady={WeakReadyPointsCandidates} watchOnly={WatchOnlyPointsCandidates} notReady={NotReadyPointsCandidates}"
            : string.Empty;
        string hybridSummary = string.IsNullOrWhiteSpace(HybridComparisonReportPath)
            ? string.Empty
            : $" hybridReport={HybridComparisonReportPath}";

        return $"resolved={ResolvedSymbols.Count} prepared={PreparedSymbols.Count} historyOk={HistorySuccessCount}/{Preparations.Count} " +
               $"scannerMode={Options.ScannerMode.ToConfigValue()} candidates={Candidates.Count}{pointsSummary} report={CandidateReportPath ?? "n/a"}{hybridSummary}";
    }
}
