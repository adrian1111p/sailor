using System.Globalization;
using System.Text.Json;
using Sailor.App.Broker.State;
using Sailor.App.Logging;
using Sailor.App.Runtime.Paper;
using Sailor.App.Scanner.ScanList;

namespace Sailor.App.Runtime.Live;

public sealed class LivePilotReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string ReportDirectory
        => EnsureDirectory(Path.Combine(SailorLogPaths.Live, "Pilot"));

    public string LatestJsonPath
        => Path.Combine(ReportDirectory, "live_pilot_latest.json");

    public string DailyCsvPath
        => Path.Combine(ReportDirectory, $"live_pilot_{DateTime.Now:yyyyMMdd}.csv");

    public LivePilotReportOutput Write(LivePilotReport report)
    {
        File.WriteAllText(LatestJsonPath, JsonSerializer.Serialize(report, JsonOptions));

        bool writeHeader = !File.Exists(DailyCsvPath);
        using var writer = new StreamWriter(new FileStream(DailyCsvPath, FileMode.Append, FileAccess.Write, FileShare.Read));
        if (writeHeader)
        {
            writer.WriteLine("generatedUtc,reportId,status,canPromote,account,symbol,profile,maxNotional,operatorWatchedTws,forceFlatRequired,readinessStatus,preReconciliationStatus,finalReconciliationStatus,endExposureZero,activeSymbols,decisions,intents,routedOrders,fills,scanListEvidenceId,scanListDataQuality,scanListTradeEligible,scanListMergedCandles,scannerMode,pointsReady,pointsWeakReady,pointsWatchOnly,livePointsGateStatus,selectedPointsStatus,selectedPointsScore,reason");
        }

        writer.WriteLine(string.Join(',',
            Csv(report.GeneratedUtc.ToString("O", CultureInfo.InvariantCulture)),
            Csv(report.ReportId),
            Csv(report.Status),
            report.CanPromote.ToString(CultureInfo.InvariantCulture),
            Csv(report.Account),
            Csv(report.Symbol),
            Csv(report.ProfileName),
            report.MaxNotional.ToString(CultureInfo.InvariantCulture),
            report.OperatorWatchedTws.ToString(CultureInfo.InvariantCulture),
            report.ForceFlatRequired.ToString(CultureInfo.InvariantCulture),
            Csv(report.ReadinessStatus),
            Csv(report.PreRunReconciliationStatus),
            Csv(report.FinalReconciliationStatus),
            report.EndExposureZero.ToString(CultureInfo.InvariantCulture),
            report.ActiveSymbols.Count.ToString(CultureInfo.InvariantCulture),
            report.DecisionCount.ToString(CultureInfo.InvariantCulture),
            report.OrderIntentCount.ToString(CultureInfo.InvariantCulture),
            report.RoutedOrderCount.ToString(CultureInfo.InvariantCulture),
            report.FilledOrAssumedFillCount.ToString(CultureInfo.InvariantCulture),
            Csv(report.ScanListEvidenceId ?? string.Empty),
            Csv(report.ScanListDataQualityStatus ?? string.Empty),
            report.ScanListTradeEligibleSymbols.ToString(CultureInfo.InvariantCulture),
            report.ScanListMergedCandles.ToString(CultureInfo.InvariantCulture),
            Csv(report.ScannerMode),
            report.ReadyPointsCandidates.ToString(CultureInfo.InvariantCulture),
            report.WeakReadyPointsCandidates.ToString(CultureInfo.InvariantCulture),
            report.WatchOnlyPointsCandidates.ToString(CultureInfo.InvariantCulture),
            Csv(report.LivePointsGateStatus),
            Csv(report.SelectedPointsStatus),
            report.SelectedPointsScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Csv(report.Reason)));

        return new LivePilotReportOutput(LatestJsonPath, DailyCsvPath, report);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}

public sealed record LivePilotReport(
    string ReportId,
    DateTimeOffset GeneratedUtc,
    string Status,
    bool CanPromote,
    string Reason,
    string Account,
    string Symbol,
    string ProfileName,
    decimal MaxNotional,
    bool OperatorWatchedTws,
    bool ForceFlatRequired,
    bool ShortEnabled,
    string ReadinessStatus,
    string ReadinessReason,
    string PreRunReconciliationStatus,
    string FinalReconciliationStatus,
    bool EndExposureZero,
    IReadOnlyList<string> ActiveSymbols,
    int DecisionCount,
    int OrderIntentCount,
    int RoutedOrderCount,
    int FilledOrAssumedFillCount,
    IReadOnlyList<string> Warnings,
    string? ScanListEvidenceId = null,
    string? ScanListEvidencePath = null,
    string? ScanListDataQualityStatus = null,
    string? ScanListDataQualityReason = null,
    int ScanListTradeEligibleSymbols = 0,
    int ScanListMergedCandles = 0,
    IReadOnlyList<string>? ScanListRetainedSymbols = null,
    string ScannerMode = "legacy-blocks",
    int PointsCandidates = 0,
    int ReadyPointsCandidates = 0,
    int WeakReadyPointsCandidates = 0,
    int WatchOnlyPointsCandidates = 0,
    int NotReadyPointsCandidates = 0,
    decimal MinimumTradeScore = 0m,
    string? PointsReportPath = null,
    string? LegacyComparisonReportPath = null,
    string? LegacyComparisonMarkdownReportPath = null,
    string LivePointsGateStatus = "n/a",
    string LivePointsGateReason = "n/a",
    string SelectedPointsStatus = "n/a",
    decimal? SelectedPointsScore = null)
{
    public static LivePilotReport From(
        LiveReadinessGateResult gate,
        LivePilotRestrictionResult restrictions,
        ReconciliationResult? preRunReconciliation,
        ReconciliationResult? finalReconciliation,
        PaperRuntimeHostResult? runtimeResult,
        IReadOnlyList<string> warnings,
        ScanListRuntimeEvidence? scanListEvidence = null,
        string? scanListEvidencePath = null,
        LivePointsPilotGateResult? livePointsGate = null)
    {
        bool endExposureZero = finalReconciliation is not null
            && finalReconciliation.BrokerPositions.All(position => position.IsFlat)
            && finalReconciliation.BrokerOpenOrders.Count == 0;

        bool scanListClean = scanListEvidence is null
                             || scanListEvidence.DataQualityStatus.Equals("Clean", StringComparison.OrdinalIgnoreCase);
        bool livePointsGateClean = livePointsGate is null || !livePointsGate.Required || livePointsGate.Allowed;
        bool canPromote = gate.LiveTradingAllowed
                          && restrictions.Passed
                          && scanListClean
                          && livePointsGateClean
                          && runtimeResult is not null
                          && finalReconciliation is not null
                          && finalReconciliation.Status == ReconciliationStatus.Matched
                          && endExposureZero;

        string status = canPromote ? "Completed" : "BlockedOrIncomplete";
        string reason;
        if (!gate.LiveTradingAllowed)
        {
            reason = gate.Reason;
        }
        else if (!restrictions.Passed)
        {
            reason = restrictions.BlockReason;
        }
        else if (!scanListClean && scanListEvidence is not null)
        {
            reason = $"Scan-list data quality is {scanListEvidence.DataQualityStatus}: {scanListEvidence.DataQualityReason}";
        }
        else if (!livePointsGateClean && livePointsGate is not null)
        {
            reason = livePointsGate.Reason;
        }
        else if (runtimeResult is null)
        {
            reason = "Live pilot conduct loop did not run.";
        }
        else if (finalReconciliation is null)
        {
            reason = "Final broker reconciliation was not available.";
        }
        else if (finalReconciliation.Status != ReconciliationStatus.Matched)
        {
            reason = $"Final broker reconciliation is {finalReconciliation.Status}.";
        }
        else if (!endExposureZero)
        {
            reason = "End exposure is not zero or broker has open orders.";
        }
        else
        {
            reason = "Live pilot completed with zero end exposure.";
        }

        string rawId = $"LPR-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        string id = rawId.Length <= 32 ? rawId : rawId.Substring(0, 32);
        return new LivePilotReport(
            id,
            DateTimeOffset.UtcNow,
            status,
            canPromote,
            reason,
            gate.Account,
            restrictions.Symbol,
            restrictions.ProfileName,
            gate.RequestedMaxNotional,
            restrictions.OperatorWatchedTws,
            restrictions.ForceFlatRequired,
            restrictions.ShortEnabled,
            gate.Status,
            gate.Reason,
            preRunReconciliation?.Status.ToString() ?? "n/a",
            finalReconciliation?.Status.ToString() ?? "n/a",
            endExposureZero,
            runtimeResult?.ActiveSymbols ?? Array.Empty<string>(),
            runtimeResult?.DecisionCount ?? 0,
            runtimeResult?.OrderIntentCount ?? 0,
            runtimeResult?.RoutedOrderCount ?? 0,
            runtimeResult?.FilledOrAssumedFillCount ?? 0,
            warnings,
            scanListEvidence?.EvidenceId,
            scanListEvidencePath,
            scanListEvidence?.DataQualityStatus,
            scanListEvidence?.DataQualityReason,
            scanListEvidence?.TradeEligibleSymbols ?? 0,
            scanListEvidence?.MergedCandles ?? 0,
            scanListEvidence?.TradeEligiblePreview ?? Array.Empty<string>(),
            scanListEvidence?.ScannerMode ?? "legacy-blocks",
            scanListEvidence?.PointsCandidates ?? 0,
            scanListEvidence?.ReadyCandidates ?? 0,
            scanListEvidence?.WeakReadyCandidates ?? 0,
            scanListEvidence?.WatchOnlyCandidates ?? 0,
            scanListEvidence?.NotReadyCandidates ?? 0,
            scanListEvidence?.MinimumTradeScore ?? 0m,
            scanListEvidence?.PointsReportPath,
            scanListEvidence?.LegacyComparisonReportPath,
            scanListEvidence?.LegacyComparisonMarkdownReportPath,
            livePointsGate is null ? "n/a" : (livePointsGate.Allowed ? "Passed" : "Blocked"),
            livePointsGate?.Reason ?? "n/a",
            livePointsGate?.SelectedPointsStatus ?? "n/a",
            livePointsGate?.SelectedPointsScore);
    }

    public string ToSummaryString()
        => $"reportId={ReportId} status={Status} canPromote={CanPromote} account={(string.IsNullOrWhiteSpace(Account) ? "not-configured" : Account)} symbol={Symbol} profile={ProfileName} maxNotional={MaxNotional:F2} readiness={ReadinessStatus} finalReconciliation={FinalReconciliationStatus} endExposureZero={EndExposureZero} decisions={DecisionCount} intents={OrderIntentCount} routedOrders={RoutedOrderCount} scanListDataQuality={ScanListDataQualityStatus ?? "n/a"} scanListTradeEligible={ScanListTradeEligibleSymbols} scannerMode={ScannerMode} pointsReady={ReadyPointsCandidates} livePointsGate={LivePointsGateStatus}";
}

public sealed record LivePilotReportOutput(
    string JsonPath,
    string CsvPath,
    LivePilotReport Report);

public sealed record LivePilotRestrictionResult(
    bool Passed,
    string Symbol,
    string ProfileName,
    bool OperatorWatchedTws,
    bool ForceFlatRequired,
    bool ShortEnabled,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Warnings,
    string BlockReason)
{
    public string ToSummaryString()
        => $"live-pilot-restrictions passed={Passed} symbol={(string.IsNullOrWhiteSpace(Symbol) ? "n/a" : Symbol)} profile={(string.IsNullOrWhiteSpace(ProfileName) ? "n/a" : ProfileName)} operatorWatchedTws={OperatorWatchedTws} forceFlatRequired={ForceFlatRequired} shortEnabled={ShortEnabled}";
}
