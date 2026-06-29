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
    int MergedCandles = 0,
    string DataQualityStatus = "Unknown",
    string DataQualityReason = "Data quality was not evaluated.",
    int DataReadySymbols = 0,
    int CriticalDataGaps = 0,
    int MergeConflictCount = 0,
    int StaleSelectedSymbols = 0,
    DateTimeOffset? LatestSelectedCandleUtc = null,
    double? LatestSelectedCandleAgeMinutes = null,
    IReadOnlyList<string>? NotReadySelectedSymbols = null,
    string ScannerMode = "legacy-blocks",
    int PointsCandidates = 0,
    int ReadyCandidates = 0,
    int WeakReadyCandidates = 0,
    int WatchOnlyCandidates = 0,
    int NotReadyCandidates = 0,
    decimal MinimumTradeScore = 0m,
    string? PointsReportPath = null,
    string? LegacyComparisonReportPath = null,
    string? LegacyComparisonMarkdownReportPath = null,
    int WatchCandidateSymbols = 0,
    IReadOnlyList<string>? WatchCandidatePreview = null)
{
    public IReadOnlyList<string> SafeNotReadySelectedSymbols => NotReadySelectedSymbols ?? Array.Empty<string>();

    public IReadOnlyList<string> SafeWatchCandidatePreview => WatchCandidatePreview ?? Array.Empty<string>();

    public string ToSummaryString()
        => $"evidenceId={EvidenceId} mode={Mode} cycle={CycleIndex}/{TotalCycles} workbookSymbols={WorkbookSymbols} active={ActiveSymbols} " +
           $"added={AddedSymbols} removed={RemovedSymbols} retainedRemoved={RetainedRemovedSymbols} tradeEligible={TradeEligibleSymbols} " +
           $"historyBatches={HistoryBatches} dueBatch={(DueHistoryBatch <= 0 ? "none" : DueHistoryBatch.ToString(System.Globalization.CultureInfo.InvariantCulture))} dueSymbols={DueHistorySymbols} " +
           $"prepared={PreparedSymbols} historyOk={HistorySuccessCount} memoryCandles={MemoryCandles} mergedCandles={MergedCandles} " +
           $"dataQuality={DataQualityStatus} criticalGaps={CriticalDataGaps} conflicts={MergeConflictCount} stale={StaleSelectedSymbols} " +
           $"scannerMode={ScannerMode} pointsCandidates={PointsCandidates} ready={ReadyCandidates} weakReady={WeakReadyCandidates} watchOnly={WatchOnlyCandidates} notReady={NotReadyCandidates} minScore={MinimumTradeScore:F2} safety={SafetyMode}";
}
