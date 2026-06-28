namespace Sailor.App.Scanner.Runtime;

public sealed record PaperScannerRunResult(
    PaperScannerOptions Options,
    IReadOnlyList<string> ResolvedSymbols,
    IReadOnlyList<string> PreparedSymbols,
    IReadOnlyList<PaperScannerSymbolPreparation> Preparations,
    IReadOnlyList<PaperScannerCandidate> Candidates,
    string? CandidateReportPath,
    IReadOnlyList<string> Warnings)
{
    public int HistorySuccessCount => Preparations.Count(row => row.HistorySuccess);

    public string ToSummaryString()
        => $"resolved={ResolvedSymbols.Count} prepared={PreparedSymbols.Count} historyOk={HistorySuccessCount}/{Preparations.Count} candidates={Candidates.Count} report={CandidateReportPath ?? "n/a"}";
}
