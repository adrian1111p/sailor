using Sailor.App.Runtime.Common;
using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListCycleResult(
    int CycleIndex,
    int TotalCycles,
    DateTimeOffset ObservedUtc,
    ScanListWorkbookResult Workbook,
    ScanListReloadResult Reload,
    IReadOnlyList<ScanListHistoryBatch> PlannedHistoryBatches,
    ScanListHistoryBatch? DueHistoryBatch,
    PaperScannerRunResult ScannerResult,
    IReadOnlyList<string> TradeEligibleSymbols,
    RuntimeSafetyState SafetyState,
    ScanListRuntimeEvidence Evidence,
    string EvidenceJsonPath,
    string EvidenceCsvPath,
    int MemoryCandleSymbols,
    int MemoryCandleCount,
    int MergedSymbols,
    int MergedCandleCount,
    IReadOnlyList<string> Warnings)
{
    public bool HasDueHistoryBatch => DueHistoryBatch is not null;

    public string ToSummaryString()
        => $"cycle={CycleIndex}/{TotalCycles} workbookSymbols={Workbook.SymbolCount} active={Reload.ActiveSymbols.Count} " +
           $"added={Reload.AddedSymbols.Count} removed={Reload.RemovedSymbols.Count} dueBatch={(DueHistoryBatch?.BatchNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")} " +
           $"prepared={ScannerResult.PreparedSymbols.Count} candidates={ScannerResult.Candidates.Count} tradeEligible={TradeEligibleSymbols.Count} " +
           $"memoryCandles={MemoryCandleCount} mergedCandles={MergedCandleCount} safety={SafetyState.Mode}";
}
