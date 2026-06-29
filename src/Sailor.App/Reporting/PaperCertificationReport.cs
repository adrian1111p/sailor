using Sailor.App.Broker.State;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Reporting;

public sealed record PaperCertificationReport(
    string ReportId,
    string Mode,
    string Account,
    string Profile,
    IReadOnlyList<string> Symbols,
    DateTimeOffset GeneratedUtc,
    DateTimeOffset? SessionStartUtc,
    DateTimeOffset? SessionEndUtc,
    string CertificationStatus,
    bool CanPromoteToLiveReadiness,
    string PromotionBlockReason,
    int OrdersSubmitted,
    int OrdersFilled,
    int OrdersRejected,
    int OrderIntents,
    int RoutedOrders,
    int PositionsOpened,
    int PositionsClosed,
    string ForceFlatResult,
    int DisconnectIncidentCount,
    IReadOnlyList<RuntimeIncident> DisconnectIncidents,
    string ReconciliationStatus,
    bool ReconciliationClean,
    string L1L2Health,
    decimal RealizedPnl,
    int StrategyDecisions,
    int EndOpenQuantity,
    decimal EndOpenExposureNotional,
    bool EndExposureIsZero,
    IReadOnlyList<SailorPosition> EndOpenPositions,
    PaperScanListEvidenceSummary? ScanListEvidence,
    IReadOnlyList<string> Warnings,
    PaperCertificationReportSources Sources)
{
    public string ToSummaryString()
        => $"reportId={ReportId} mode={Mode} status={CertificationStatus} canPromote={CanPromoteToLiveReadiness} account={(string.IsNullOrWhiteSpace(Account) ? "not-configured" : Account)} profile={Profile} symbols={Symbols.Count} submitted={OrdersSubmitted} filled={OrdersFilled} rejected={OrdersRejected} decisions={StrategyDecisions} pnl={RealizedPnl:F2} endExposure={EndOpenExposureNotional:F2} reconciliation={ReconciliationStatus} incidents={DisconnectIncidentCount} scanList={(ScanListEvidence is null ? "none" : ScanListEvidence.ToSummaryString())}";
}

public sealed record PaperScanListEvidenceSummary(
    string EvidenceId,
    string File,
    string Sheet,
    int WorkbookSymbols,
    int ActiveSymbols,
    int TradeEligibleSymbols,
    IReadOnlyList<string> TradeEligiblePreview,
    int HistoryBatchSize,
    int HistoryBatchIntervalMinutes,
    int HistoryBatches,
    int DueHistoryBatch,
    int PreparedSymbols,
    int HistorySuccessCount,
    int MemoryCandles,
    int MergedCandles,
    string SafetyMode,
    string SafetyReason,
    string EvidencePath,
    string DataQualityStatus,
    string DataQualityReason,
    int DataReadySymbols,
    int CriticalDataGaps,
    int MergeConflictCount,
    int StaleSelectedSymbols,
    DateTimeOffset? LatestSelectedCandleUtc,
    double? LatestSelectedCandleAgeMinutes,
    IReadOnlyList<string> NotReadySelectedSymbols,
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
    public bool DataQualityClean => TradeEligibleSymbols == 0
        || string.Equals(DataQualityStatus, "Clean", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> SafeWatchCandidatePreview => WatchCandidatePreview ?? Array.Empty<string>();

    public string ToSummaryString()
        => $"{File}#{Sheet} workbook={WorkbookSymbols} active={ActiveSymbols} tradeEligible={TradeEligibleSymbols} historyOk={HistorySuccessCount}/{PreparedSymbols} mergedCandles={MergedCandles} dataQuality={DataQualityStatus} safety={SafetyMode} scannerMode={ScannerMode} pointsCandidates={PointsCandidates} ready={ReadyCandidates} weakReady={WeakReadyCandidates} watchOnly={WatchOnlyCandidates} notReady={NotReadyCandidates} minScore={MinimumTradeScore:F2}";
}

public sealed record PaperCertificationReportSources(
    string? RuntimeLogPath,
    string LedgerPath,
    string PositionsPath,
    string? ReconciliationPath,
    string IncidentDirectory,
    string? ScanListEvidencePath,
    string ReportJsonPath,
    string ReportMarkdownPath,
    string ReportCsvPath);

public sealed record PaperCertificationReportOutput(
    string JsonPath,
    string MarkdownPath,
    string CsvPath,
    PaperCertificationReport Report)
{
    public string ToDisplayString()
        => $"Paper certification report generated: status={Report.CertificationStatus} canPromote={Report.CanPromoteToLiveReadiness} json={JsonPath} markdown={MarkdownPath} csv={CsvPath}";
}
