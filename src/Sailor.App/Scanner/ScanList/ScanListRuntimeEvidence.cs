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
    IReadOnlyList<string> Warnings,
    int CycleIndex = 1,
    int TotalCycles = 1,
    int DueHistoryBatch = 0,
    int DueHistorySymbols = 0,
    int PreparedSymbols = 0,
    int HistorySuccessCount = 0,
    int MemoryCandleSymbols = 0,
    int MemoryCandles = 0,
    int MergedSymbols = 0,
    int MergedCandles = 0)
{
    public string ToSummaryString()
        => $"evidenceId={EvidenceId} mode={Mode} cycle={CycleIndex}/{TotalCycles} workbookSymbols={WorkbookSymbols} active={ActiveSymbols} " +
           $"added={AddedSymbols} removed={RemovedSymbols} retainedRemoved={RetainedRemovedSymbols} tradeEligible={TradeEligibleSymbols} " +
           $"historyBatches={HistoryBatches} dueBatch={(DueHistoryBatch <= 0 ? "none" : DueHistoryBatch.ToString(System.Globalization.CultureInfo.InvariantCulture))} dueSymbols={DueHistorySymbols} " +
           $"prepared={PreparedSymbols} historyOk={HistorySuccessCount} memoryCandles={MemoryCandles} mergedCandles={MergedCandles} safety={SafetyMode}";
}
