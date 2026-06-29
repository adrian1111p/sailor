namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListRuntimeEvidence(
    string EvidenceId,
    string Mode,
    string File,
    string Sheet,
    string SymbolColumn,
    DateTimeOffset ObservedUtc,
    int RefreshSeconds,
    int TradeTop,
    int HistoryBatchSize,
    int HistoryBatchIntervalMinutes,
    int WorkbookSymbols,
    int ActiveSymbols,
    int AddedSymbols,
    int RemovedSymbols,
    int RetainedRemovedSymbols,
    int TradeEligibleSymbols,
    int HistoryBatches,
    string SafetyMode,
    string SafetyReason,
    IReadOnlyList<string> TradeEligiblePreview,
    IReadOnlyList<string> AddedPreview,
    IReadOnlyList<string> RemovedPreview,
    IReadOnlyList<string> Warnings)
{
    public string ToSummaryString()
        => $"evidenceId={EvidenceId} mode={Mode} workbookSymbols={WorkbookSymbols} active={ActiveSymbols} added={AddedSymbols} removed={RemovedSymbols} retainedRemoved={RetainedRemovedSymbols} tradeEligible={TradeEligibleSymbols} historyBatches={HistoryBatches} safety={SafetyMode}";
}
