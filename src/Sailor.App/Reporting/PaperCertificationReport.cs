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
    IReadOnlyList<string> Warnings,
    PaperCertificationReportSources Sources)
{
    public string ToSummaryString()
        => $"reportId={ReportId} mode={Mode} status={CertificationStatus} canPromote={CanPromoteToLiveReadiness} account={(string.IsNullOrWhiteSpace(Account) ? "not-configured" : Account)} profile={Profile} symbols={Symbols.Count} submitted={OrdersSubmitted} filled={OrdersFilled} rejected={OrdersRejected} decisions={StrategyDecisions} pnl={RealizedPnl:F2} endExposure={EndOpenExposureNotional:F2} reconciliation={ReconciliationStatus} incidents={DisconnectIncidentCount}";
}

public sealed record PaperCertificationReportSources(
    string? RuntimeLogPath,
    string LedgerPath,
    string PositionsPath,
    string? ReconciliationPath,
    string IncidentDirectory,
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
