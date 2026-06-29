using System.Globalization;
using System.Text.Json;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;
using Sailor.App.Scanner.ScanList;

namespace Sailor.App.Reporting;

public sealed class PaperSessionReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SailorRuntimeMode _mode;

    public PaperSessionReportWriter(SailorRuntimeMode mode)
    {
        _mode = mode;
        string logRoot = mode == SailorRuntimeMode.Live ? SailorLogPaths.Live : SailorLogPaths.Paper;
        ReportDirectory = EnsureDirectory(Path.Combine(logRoot, "Reports"));
        LatestJsonPath = Path.Combine(ReportDirectory, "paper_certification_latest.json");
        LatestMarkdownPath = Path.Combine(ReportDirectory, "paper_certification_latest.md");
        DailyCsvPath = Path.Combine(ReportDirectory, $"paper_certification_{DateTime.Now:yyyyMMdd}.csv");
    }

    public string ReportDirectory { get; }

    public string LatestJsonPath { get; }

    public string LatestMarkdownPath { get; }

    public string DailyCsvPath { get; }

    public PaperCertificationReport BuildLatestReport()
    {
        RuntimeLogSnapshot runtimeLog = LoadLatestRuntimeLog();
        var ledgerStore = new OrderLedgerStore(_mode);
        OrderLedgerSnapshot ledger = ledgerStore.Load();
        var positionStore = new PositionStore(_mode);
        IReadOnlyList<SailorPosition> endPositions = positionStore
            .RebuildFromLedger(ledger)
            .Where(position => !position.IsFlat)
            .OrderBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reconciliationService = new ReconciliationService(_mode);
        ReconciliationResult? reconciliation = reconciliationService.LoadLastReconciliation();
        var incidentReporter = new RuntimeIncidentReporter(_mode);
        IReadOnlyList<RuntimeIncident> disconnectIncidents = LoadDisconnectIncidents(
            incidentReporter.IncidentDirectory,
            runtimeLog.SessionStartUtc,
            runtimeLog.SessionEndUtc);
        PaperScanListEvidenceSummary? scanListEvidence = LoadLatestScanListEvidence();

        IReadOnlyList<OrderLedgerRecord> sessionOrders = SelectSessionOrders(ledger, runtimeLog);
        PositionAndPnlSummary positionSummary = CalculatePositionAndPnl(sessionOrders);

        int submitted = sessionOrders.Count(row => row.SentToBroker || row.SubmittedQuantity > 0);
        int filled = sessionOrders.Count(row => row.FilledQuantity != 0 || row.Status.Equals(nameof(SailorOrderStatus.Filled), StringComparison.OrdinalIgnoreCase));
        int rejected = sessionOrders.Count(row => row.Status.Equals(nameof(SailorOrderStatus.Rejected), StringComparison.OrdinalIgnoreCase));
        int endOpenQuantity = endPositions.Sum(position => Math.Abs(position.Quantity));
        decimal endExposure = endPositions.Sum(position => position.Notional);
        bool endExposureIsZero = endOpenQuantity == 0 && endExposure == 0m;
        bool reconciliationClean = reconciliation is not null
            && reconciliation.Status == ReconciliationStatus.Matched
            && reconciliation.Rows.All(row => !row.IsCritical)
            && reconciliation.BrokerOpenOrders.Count == 0;
        bool scanListCertificationClean = scanListEvidence is null || scanListEvidence.DataQualityClean;

        var warnings = new List<string>();
        warnings.AddRange(runtimeLog.Warnings);
        if (runtimeLog.RuntimeLogPath is null)
        {
            warnings.Add("No paper_run runtime log was found. The report was generated from ledger/reconciliation/incident state only.");
        }

        if (sessionOrders.Count == 0)
        {
            warnings.Add("No order ledger rows were mapped to the latest paper run.");
        }

        if (scanListEvidence is not null && !scanListEvidence.SafetyMode.Equals("Normal", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Latest scan-list evidence is not clean: safety={scanListEvidence.SafetyMode}, reason={scanListEvidence.SafetyReason}");
        }

        if (scanListEvidence is not null && !scanListEvidence.DataQualityClean)
        {
            warnings.Add($"Latest scan-list data quality blocks certification: status={scanListEvidence.DataQualityStatus}, reason={scanListEvidence.DataQualityReason}");
        }

        if (!endExposureIsZero)
        {
            warnings.Add($"End exposure is non-zero: quantity={endOpenQuantity}, notional={endExposure:F2}. Session cannot be promoted.");
        }

        if (!reconciliationClean)
        {
            warnings.Add(reconciliation is null
                ? "No broker reconciliation JSON exists. Promotion is blocked until paper reconcile is clean."
                : $"Latest broker reconciliation is not clean: {reconciliation.Status}.");
        }

        string promotionBlockReason;
        if (!endExposureIsZero)
        {
            promotionBlockReason = $"Blocked: all open exposure at end must be zero, but quantity={endOpenQuantity} and notional={endExposure:F2}.";
        }
        else if (!reconciliationClean)
        {
            promotionBlockReason = reconciliation is null
                ? "Blocked: no broker-verified reconciliation report is available."
                : $"Blocked: latest broker reconciliation status is {reconciliation.Status}.";
        }
        else if (!scanListCertificationClean && scanListEvidence is not null)
        {
            promotionBlockReason = $"Blocked: scan-list data quality is {scanListEvidence.DataQualityStatus}: {scanListEvidence.DataQualityReason}";
        }
        else if (disconnectIncidents.Any(incident => incident.SafetyState.IsDegraded))
        {
            promotionBlockReason = "Review required: degraded/disconnect incidents were observed during the latest paper session.";
        }
        else
        {
            promotionBlockReason = "Paper certification criteria passed for live-readiness gate consumption.";
        }

        bool canPromote = endExposureIsZero && reconciliationClean && scanListCertificationClean && !disconnectIncidents.Any(incident => incident.SafetyState.IsDegraded);
        string status = canPromote ? "Passed" : "Blocked";

        return new PaperCertificationReport(
            ReportId: CreateReportId(),
            Mode: _mode.ToDisplayName(),
            Account: runtimeLog.Account,
            Profile: runtimeLog.Profile,
            Symbols: runtimeLog.Symbols,
            GeneratedUtc: DateTimeOffset.UtcNow,
            SessionStartUtc: runtimeLog.SessionStartUtc,
            SessionEndUtc: runtimeLog.SessionEndUtc,
            CertificationStatus: status,
            CanPromoteToLiveReadiness: canPromote,
            PromotionBlockReason: promotionBlockReason,
            OrdersSubmitted: submitted,
            OrdersFilled: filled,
            OrdersRejected: rejected,
            OrderIntents: runtimeLog.IntentCount > 0 ? runtimeLog.IntentCount : sessionOrders.Count,
            RoutedOrders: runtimeLog.RoutedOrderCount > 0 ? runtimeLog.RoutedOrderCount : sessionOrders.Count,
            PositionsOpened: positionSummary.PositionsOpened,
            PositionsClosed: positionSummary.PositionsClosed,
            ForceFlatResult: BuildForceFlatResult(runtimeLog, endExposureIsZero),
            DisconnectIncidentCount: disconnectIncidents.Count,
            DisconnectIncidents: disconnectIncidents,
            ReconciliationStatus: reconciliation?.Status.ToString() ?? "Missing",
            ReconciliationClean: reconciliationClean,
            L1L2Health: BuildL1L2Health(runtimeLog),
            RealizedPnl: positionSummary.RealizedPnl,
            StrategyDecisions: runtimeLog.DecisionCount,
            EndOpenQuantity: endOpenQuantity,
            EndOpenExposureNotional: endExposure,
            EndExposureIsZero: endExposureIsZero,
            EndOpenPositions: endPositions,
            ScanListEvidence: scanListEvidence,
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Sources: new PaperCertificationReportSources(
                runtimeLog.RuntimeLogPath,
                ledger.Path,
                positionStore.Path,
                reconciliation?.ReconciliationPath,
                incidentReporter.IncidentDirectory,
                scanListEvidence?.EvidencePath,
                LatestJsonPath,
                LatestMarkdownPath,
                DailyCsvPath));
    }

    public PaperCertificationReportOutput WriteLatestReport(PaperCertificationReport report)
    {
        Directory.CreateDirectory(ReportDirectory);
        File.WriteAllText(LatestJsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(LatestMarkdownPath, ToMarkdown(report));
        AppendCsv(report);
        return new PaperCertificationReportOutput(LatestJsonPath, LatestMarkdownPath, DailyCsvPath, report);
    }

    private RuntimeLogSnapshot LoadLatestRuntimeLog()
    {
        string runtimeDirectory = Path.Combine(_mode == SailorRuntimeMode.Live ? SailorLogPaths.Live : SailorLogPaths.Paper, "Runtime");
        if (!Directory.Exists(runtimeDirectory))
        {
            return RuntimeLogSnapshot.Empty(runtimeDirectory, "Runtime log directory does not exist yet.");
        }

        FileInfo? latest = new DirectoryInfo(runtimeDirectory)
            .EnumerateFiles($"{_mode.ToDisplayName()}_run_*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return RuntimeLogSnapshot.Empty(runtimeDirectory, "No paper_run runtime log found.");
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(latest.FullName);
        }
        catch (Exception ex)
        {
            return RuntimeLogSnapshot.Empty(latest.FullName, $"Could not read latest runtime log: {ex.GetType().Name}: {ex.Message}");
        }

        DateTimeOffset? first = ExtractFirstTimestamp(lines);
        DateTimeOffset? last = ExtractLastTimestamp(lines);
        IReadOnlyList<string> symbols = ExtractSymbols(lines);
        string profile = ExtractValue(lines, "profile=", "unknown");
        string account = ExtractValue(lines, "account=", "not-configured");

        return new RuntimeLogSnapshot(
            latest.FullName,
            lines,
            first,
            last,
            account,
            profile,
            symbols,
            ExtractIntValue(lines, "decisions=", 0),
            ExtractIntValue(lines, "intents=", 0),
            ExtractIntValue(lines, "routedOrders=", 0),
            ExtractIntValue(lines, "activeSymbols=", symbols.Count),
            ExtractBoolValue(lines, "L1="),
            ExtractBoolValue(lines, "L2="),
            ExtractBoolValue(lines, "captureSnapshots="),
            ExtractActiveSnapshotCount(lines, "snapshotL1=True"),
            ExtractActiveSnapshotCount(lines, "snapshotL2=True"),
            lines.Where(line => line.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    private IReadOnlyList<OrderLedgerRecord> SelectSessionOrders(OrderLedgerSnapshot ledger, RuntimeLogSnapshot runtimeLog)
    {
        HashSet<string> intentIds = ExtractIntentIds(runtimeLog.Lines);
        if (intentIds.Count > 0)
        {
            return ledger.Records
                .Where(row => intentIds.Contains(row.IntentId))
                .OrderBy(row => row.CreatedUtc)
                .ToArray();
        }

        if (runtimeLog.SessionStartUtc is not null && runtimeLog.SessionEndUtc is not null)
        {
            DateTimeOffset start = runtimeLog.SessionStartUtc.Value.AddMinutes(-1);
            DateTimeOffset end = runtimeLog.SessionEndUtc.Value.AddMinutes(2);
            return ledger.Records
                .Where(row => row.CreatedUtc >= start && row.CreatedUtc <= end)
                .OrderBy(row => row.CreatedUtc)
                .ToArray();
        }

        return Array.Empty<OrderLedgerRecord>();
    }

    private PaperScanListEvidenceSummary? LoadLatestScanListEvidence()
    {
        string scanListPath = Path.Combine(_mode == SailorRuntimeMode.Live ? SailorLogPaths.Live : SailorLogPaths.Paper, "ScanList", "scanlist_latest.json");
        if (!File.Exists(scanListPath))
        {
            return null;
        }

        try
        {
            ScanListRuntimeEvidence? evidence = JsonSerializer.Deserialize<ScanListRuntimeEvidence>(File.ReadAllText(scanListPath), JsonOptions);
            if (evidence is null)
            {
                return null;
            }

            return new PaperScanListEvidenceSummary(
                evidence.EvidenceId,
                evidence.File,
                evidence.Sheet,
                evidence.WorkbookSymbols,
                evidence.ActiveSymbols,
                evidence.TradeEligibleSymbols,
                evidence.TradeEligiblePreview,
                evidence.HistoryBatchSize,
                evidence.HistoryBatchIntervalMinutes,
                evidence.HistoryBatches,
                evidence.DueHistoryBatch,
                evidence.PreparedSymbols,
                evidence.HistorySuccessCount,
                evidence.MemoryCandles,
                evidence.MergedCandles,
                evidence.SafetyMode,
                evidence.SafetyReason,
                scanListPath,
                evidence.DataQualityStatus,
                evidence.DataQualityReason,
                evidence.DataReadySymbols,
                evidence.CriticalDataGaps,
                evidence.MergeConflictCount,
                evidence.StaleSelectedSymbols,
                evidence.LatestSelectedCandleUtc,
                evidence.LatestSelectedCandleAgeMinutes,
                evidence.SafeNotReadySelectedSymbols);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<RuntimeIncident> LoadDisconnectIncidents(
        string incidentDirectory,
        DateTimeOffset? sessionStartUtc,
        DateTimeOffset? sessionEndUtc)
    {
        if (!Directory.Exists(incidentDirectory))
        {
            return Array.Empty<RuntimeIncident>();
        }

        DateTimeOffset start = sessionStartUtc?.AddMinutes(-1) ?? DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset end = sessionEndUtc?.AddMinutes(2) ?? DateTimeOffset.UtcNow.AddMinutes(2);
        var incidents = new List<RuntimeIncident>();

        foreach (string path in Directory.EnumerateFiles(incidentDirectory, "incidents_*.jsonl").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    RuntimeIncident? incident = JsonSerializer.Deserialize<RuntimeIncident>(line, JsonOptions);
                    if (incident is null)
                    {
                        continue;
                    }

                    if (incident.ObservedUtc < start || incident.ObservedUtc > end)
                    {
                        continue;
                    }

                    if (IsDisconnectIncident(incident))
                    {
                        incidents.Add(incident);
                    }
                }
                catch
                {
                    // Incident history is audit evidence, but one damaged JSONL line must not block the certification report.
                }
            }
        }

        return incidents
            .OrderBy(incident => incident.ObservedUtc)
            .ToArray();
    }

    private static bool IsDisconnectIncident(RuntimeIncident incident)
    {
        string kind = incident.Kind ?? string.Empty;
        return kind.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("degraded", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || incident.SafetyState.IsDegraded;
    }

    private static PositionAndPnlSummary CalculatePositionAndPnl(IReadOnlyList<OrderLedgerRecord> records)
    {
        var bySymbol = new Dictionary<string, PnlPositionAccumulator>(StringComparer.OrdinalIgnoreCase);
        int opened = 0;
        int closed = 0;
        decimal realizedPnl = 0m;

        foreach (OrderLedgerRecord row in records.OrderBy(row => row.CreatedUtc))
        {
            int signedQuantity = row.SignedFilledQuantity();
            if (signedQuantity == 0 || row.AverageFillPrice <= 0m)
            {
                continue;
            }

            string symbol = string.IsNullOrWhiteSpace(row.Symbol) ? "UNKNOWN" : row.Symbol.Trim().ToUpperInvariant();
            if (!bySymbol.TryGetValue(symbol, out PnlPositionAccumulator? accumulator))
            {
                accumulator = new PnlPositionAccumulator();
                bySymbol[symbol] = accumulator;
            }

            int before = accumulator.Quantity;
            decimal realized = accumulator.Apply(signedQuantity, row.AverageFillPrice);
            realizedPnl += realized;

            if (before == 0 && accumulator.Quantity != 0)
            {
                opened++;
            }

            if (before != 0 && accumulator.Quantity == 0)
            {
                closed++;
            }
        }

        return new PositionAndPnlSummary(opened, closed, realizedPnl);
    }

    private static string BuildForceFlatResult(RuntimeLogSnapshot runtimeLog, bool endExposureIsZero)
    {
        bool forceFlatMentioned = runtimeLog.Lines.Any(line => line.Contains("force-flat", StringComparison.OrdinalIgnoreCase));
        bool forceFlatNow = runtimeLog.Lines.Any(line => line.Contains("forceFlatNow=True", StringComparison.OrdinalIgnoreCase));
        bool forceFlatOrder = runtimeLog.Lines.Any(line =>
            line.Contains("SAILOR-030 force-flat", StringComparison.OrdinalIgnoreCase)
            || line.Contains("decision=Exit", StringComparison.OrdinalIgnoreCase)
            || line.Contains("decision=Flatten", StringComparison.OrdinalIgnoreCase));

        if (forceFlatOrder)
        {
            return endExposureIsZero
                ? "Force-flat/exit path observed and end exposure is zero."
                : "Force-flat/exit path observed but end exposure is still non-zero.";
        }

        if (forceFlatNow || forceFlatMentioned)
        {
            return endExposureIsZero
                ? "Force-flat condition was present; no remaining exposure at report time."
                : "Force-flat condition was present, but exposure remains non-zero.";
        }

        return endExposureIsZero
            ? "Not required; all open exposure at end is zero."
            : "Not completed; open exposure remains non-zero.";
    }

    private static string BuildL1L2Health(RuntimeLogSnapshot runtimeLog)
    {
        string l1 = runtimeLog.RequestedL1 is null ? "unknown" : runtimeLog.RequestedL1.Value.ToString(CultureInfo.InvariantCulture);
        string l2 = runtimeLog.RequestedL2 is null ? "unknown" : runtimeLog.RequestedL2.Value.ToString(CultureInfo.InvariantCulture);
        string capture = runtimeLog.CaptureSnapshots is null ? "unknown" : runtimeLog.CaptureSnapshots.Value.ToString(CultureInfo.InvariantCulture);
        int active = Math.Max(runtimeLog.ActiveSymbolCount, runtimeLog.Symbols.Count);

        return $"requested L1={l1}, L2={l2}, captureSnapshots={capture}; active snapshot coverage L1={runtimeLog.ActiveL1SnapshotCount}/{active}, L2={runtimeLog.ActiveL2SnapshotCount}/{active}";
    }

    private string ToMarkdown(PaperCertificationReport report)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine("# Sailor Paper Certification Report");
        writer.WriteLine();
        writer.WriteLine($"Generated UTC: {report.GeneratedUtc:O}");
        writer.WriteLine($"Report ID: {report.ReportId}");
        writer.WriteLine($"Certification status: **{report.CertificationStatus}**");
        writer.WriteLine($"Can promote to live-readiness gate: **{report.CanPromoteToLiveReadiness}**");
        writer.WriteLine($"Promotion block/reason: {report.PromotionBlockReason}");
        writer.WriteLine();
        writer.WriteLine("## Session");
        writer.WriteLine();
        writer.WriteLine($"- Mode: {report.Mode}");
        writer.WriteLine($"- Account: {(string.IsNullOrWhiteSpace(report.Account) ? "not-configured" : report.Account)}");
        writer.WriteLine($"- Profile: {report.Profile}");
        writer.WriteLine($"- Symbols: {(report.Symbols.Count == 0 ? "none" : string.Join(", ", report.Symbols))}");
        writer.WriteLine($"- Session start UTC: {report.SessionStartUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "n/a"}");
        writer.WriteLine($"- Session end UTC: {report.SessionEndUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "n/a"}");
        writer.WriteLine();
        writer.WriteLine("## Orders and positions");
        writer.WriteLine();
        writer.WriteLine($"- Orders submitted: {report.OrdersSubmitted}");
        writer.WriteLine($"- Orders filled: {report.OrdersFilled}");
        writer.WriteLine($"- Orders rejected: {report.OrdersRejected}");
        writer.WriteLine($"- Order intents: {report.OrderIntents}");
        writer.WriteLine($"- Routed orders: {report.RoutedOrders}");
        writer.WriteLine($"- Positions opened: {report.PositionsOpened}");
        writer.WriteLine($"- Positions closed: {report.PositionsClosed}");
        writer.WriteLine($"- P&L: {report.RealizedPnl:F2}");
        writer.WriteLine($"- End open quantity: {report.EndOpenQuantity}");
        writer.WriteLine($"- End open exposure notional: {report.EndOpenExposureNotional:F2}");
        writer.WriteLine($"- End exposure zero: {report.EndExposureIsZero}");
        writer.WriteLine($"- Force-flat result: {report.ForceFlatResult}");
        writer.WriteLine();
        writer.WriteLine("## Safety evidence");
        writer.WriteLine();
        writer.WriteLine($"- Reconciliation status: {report.ReconciliationStatus}");
        writer.WriteLine($"- Reconciliation clean: {report.ReconciliationClean}");
        writer.WriteLine($"- L1/L2 health: {report.L1L2Health}");
        writer.WriteLine($"- Strategy decisions: {report.StrategyDecisions}");
        writer.WriteLine($"- Disconnect/degraded incidents: {report.DisconnectIncidentCount}");

        if (report.DisconnectIncidents.Count > 0)
        {
            foreach (RuntimeIncident incident in report.DisconnectIncidents)
            {
                writer.WriteLine($"  - {incident.ToDisplayString()}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Scan-list evidence");
        writer.WriteLine();
        if (report.ScanListEvidence is null)
        {
            writer.WriteLine("No scan-list evidence was found for the latest session.");
        }
        else
        {
            PaperScanListEvidenceSummary scan = report.ScanListEvidence;
            writer.WriteLine($"- File: {scan.File}");
            writer.WriteLine($"- Sheet: {scan.Sheet}");
            writer.WriteLine($"- Workbook symbols: {scan.WorkbookSymbols}");
            writer.WriteLine($"- Active symbols: {scan.ActiveSymbols}");
            writer.WriteLine($"- Trade-eligible symbols: {scan.TradeEligibleSymbols} {(scan.TradeEligiblePreview.Count == 0 ? string.Empty : $"({string.Join(", ", scan.TradeEligiblePreview)})")}");
            writer.WriteLine($"- History batching: size={scan.HistoryBatchSize}, intervalMinutes={scan.HistoryBatchIntervalMinutes}, batches={scan.HistoryBatches}, dueBatch={(scan.DueHistoryBatch <= 0 ? "none" : scan.DueHistoryBatch.ToString(CultureInfo.InvariantCulture))}");
            writer.WriteLine($"- Prepared/history OK: {scan.PreparedSymbols}/{scan.HistorySuccessCount}");
            writer.WriteLine($"- Memory/merged candles: {scan.MemoryCandles}/{scan.MergedCandles}");
            writer.WriteLine($"- Data quality: {scan.DataQualityStatus} - {scan.DataQualityReason}");
            writer.WriteLine($"- Data-ready selected symbols: {scan.DataReadySymbols}/{scan.TradeEligibleSymbols}");
            writer.WriteLine($"- Critical data gaps: {scan.CriticalDataGaps}");
            writer.WriteLine($"- Merge conflicts: {scan.MergeConflictCount}");
            writer.WriteLine($"- Stale selected symbols: {scan.StaleSelectedSymbols}");
            writer.WriteLine($"- Latest selected candle UTC: {scan.LatestSelectedCandleUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "n/a"}");
            writer.WriteLine($"- Latest selected candle age minutes: {scan.LatestSelectedCandleAgeMinutes?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/a"}");
            writer.WriteLine($"- Not-ready selected symbols: {(scan.NotReadySelectedSymbols.Count == 0 ? "none" : string.Join(", ", scan.NotReadySelectedSymbols))}");
            writer.WriteLine($"- Safety: {scan.SafetyMode} - {scan.SafetyReason}");
            writer.WriteLine($"- Evidence: {scan.EvidencePath}");
        }

        writer.WriteLine();
        writer.WriteLine("## End open positions");
        writer.WriteLine();
        if (report.EndOpenPositions.Count == 0)
        {
            writer.WriteLine("None.");
        }
        else
        {
            foreach (SailorPosition position in report.EndOpenPositions)
            {
                writer.WriteLine($"- {position.ToDisplayLine()}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Warnings");
        writer.WriteLine();
        if (report.Warnings.Count == 0)
        {
            writer.WriteLine("None.");
        }
        else
        {
            foreach (string warning in report.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Source artifacts");
        writer.WriteLine();
        writer.WriteLine($"- Runtime log: {report.Sources.RuntimeLogPath ?? "n/a"}");
        writer.WriteLine($"- Ledger: {report.Sources.LedgerPath}");
        writer.WriteLine($"- Positions: {report.Sources.PositionsPath}");
        writer.WriteLine($"- Reconciliation: {report.Sources.ReconciliationPath ?? "n/a"}");
        writer.WriteLine($"- Incidents: {report.Sources.IncidentDirectory}");
        writer.WriteLine($"- Scan-list evidence: {report.Sources.ScanListEvidencePath ?? "n/a"}");
        writer.WriteLine($"- JSON report: {report.Sources.ReportJsonPath}");
        writer.WriteLine($"- Markdown report: {report.Sources.ReportMarkdownPath}");
        writer.WriteLine($"- CSV report: {report.Sources.ReportCsvPath}");
        return writer.ToString();
    }

    private void AppendCsv(PaperCertificationReport report)
    {
        bool writeHeader = !File.Exists(DailyCsvPath);
        using var writer = new StreamWriter(new FileStream(DailyCsvPath, FileMode.Append, FileAccess.Write, FileShare.Read));
        if (writeHeader)
        {
            writer.WriteLine("generatedUtc,reportId,mode,account,profile,symbols,status,canPromote,ordersSubmitted,ordersFilled,ordersRejected,positionsOpened,positionsClosed,forceFlatResult,disconnectIncidents,reconciliationStatus,reconciliationClean,l1l2Health,realizedPnl,strategyDecisions,endOpenQuantity,endExposureNotional,endExposureIsZero,scanListFile,scanListSheet,scanListTradeEligible,scanListMergedCandles,scanListSafety,scanListDataQuality,scanListDataReady,scanListCriticalDataGaps,scanListMergeConflicts,scanListStaleSelected,promotionBlockReason,jsonPath,markdownPath");
        }

        writer.WriteLine(string.Join(',',
            Csv(report.GeneratedUtc.ToString("O", CultureInfo.InvariantCulture)),
            Csv(report.ReportId),
            Csv(report.Mode),
            Csv(report.Account),
            Csv(report.Profile),
            Csv(string.Join(';', report.Symbols)),
            Csv(report.CertificationStatus),
            report.CanPromoteToLiveReadiness.ToString(CultureInfo.InvariantCulture),
            report.OrdersSubmitted.ToString(CultureInfo.InvariantCulture),
            report.OrdersFilled.ToString(CultureInfo.InvariantCulture),
            report.OrdersRejected.ToString(CultureInfo.InvariantCulture),
            report.PositionsOpened.ToString(CultureInfo.InvariantCulture),
            report.PositionsClosed.ToString(CultureInfo.InvariantCulture),
            Csv(report.ForceFlatResult),
            report.DisconnectIncidentCount.ToString(CultureInfo.InvariantCulture),
            Csv(report.ReconciliationStatus),
            report.ReconciliationClean.ToString(CultureInfo.InvariantCulture),
            Csv(report.L1L2Health),
            report.RealizedPnl.ToString(CultureInfo.InvariantCulture),
            report.StrategyDecisions.ToString(CultureInfo.InvariantCulture),
            report.EndOpenQuantity.ToString(CultureInfo.InvariantCulture),
            report.EndOpenExposureNotional.ToString(CultureInfo.InvariantCulture),
            report.EndExposureIsZero.ToString(CultureInfo.InvariantCulture),
            Csv(report.ScanListEvidence?.File ?? string.Empty),
            Csv(report.ScanListEvidence?.Sheet ?? string.Empty),
            (report.ScanListEvidence?.TradeEligibleSymbols ?? 0).ToString(CultureInfo.InvariantCulture),
            (report.ScanListEvidence?.MergedCandles ?? 0).ToString(CultureInfo.InvariantCulture),
            Csv(report.ScanListEvidence?.SafetyMode ?? string.Empty),
            Csv(report.ScanListEvidence?.DataQualityStatus ?? string.Empty),
            (report.ScanListEvidence?.DataReadySymbols ?? 0).ToString(CultureInfo.InvariantCulture),
            (report.ScanListEvidence?.CriticalDataGaps ?? 0).ToString(CultureInfo.InvariantCulture),
            (report.ScanListEvidence?.MergeConflictCount ?? 0).ToString(CultureInfo.InvariantCulture),
            (report.ScanListEvidence?.StaleSelectedSymbols ?? 0).ToString(CultureInfo.InvariantCulture),
            Csv(report.PromotionBlockReason),
            Csv(report.Sources.ReportJsonPath),
            Csv(report.Sources.ReportMarkdownPath)));
    }

    private static DateTimeOffset? ExtractFirstTimestamp(IReadOnlyList<string> lines)
    {
        DateTimeOffset[] timestamps = ExtractTimestamps(lines).OrderBy(value => value).ToArray();
        return timestamps.Length == 0 ? null : timestamps[0];
    }

    private static DateTimeOffset? ExtractLastTimestamp(IReadOnlyList<string> lines)
    {
        DateTimeOffset[] timestamps = ExtractTimestamps(lines).OrderByDescending(value => value).ToArray();
        return timestamps.Length == 0 ? null : timestamps[0];
    }

    private static IEnumerable<DateTimeOffset> ExtractTimestamps(IReadOnlyList<string> lines)
    {
        foreach (string line in lines)
        {
            foreach (string key in new[] { "heartbeatUtc=", "observedUtc=", "submitted=", "updatedUtc=" })
            {
                string value = ReadToken(line, key);
                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
                {
                    yield return parsed.ToUniversalTime();
                }
            }
        }
    }

    private static IReadOnlyList<string> ExtractSymbols(IReadOnlyList<string> lines)
    {
        var symbols = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            if (line.StartsWith("Resolved symbols:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Prepared symbols:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string symbol in line.Split(':', 2)[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (IsSymbolToken(symbol))
                    {
                        symbols.Add(symbol.ToUpperInvariant());
                    }
                }
            }

            int colon = line.IndexOf(':');
            if (colon > 0 && line.Contains("snapshotL1=", StringComparison.OrdinalIgnoreCase))
            {
                string symbol = line[..colon].Trim();
                if (IsSymbolToken(symbol))
                {
                    symbols.Add(symbol.ToUpperInvariant());
                }
            }
        }

        return symbols.ToArray();
    }

    private static HashSet<string> ExtractIntentIds(IReadOnlyList<string> lines)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            foreach (string key in new[] { "intent=", "id=" })
            {
                string value = ReadToken(line, key);
                if (value.StartsWith("SI-", StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(value);
                }
            }
        }

        return ids;
    }

    private static string ExtractValue(IReadOnlyList<string> lines, string key, string fallback)
    {
        foreach (string line in lines)
        {
            string value = ReadToken(line, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static int ExtractIntValue(IReadOnlyList<string> lines, string key, int fallback)
    {
        foreach (string line in lines.Reverse())
        {
            string value = ReadToken(line, key);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static bool? ExtractBoolValue(IReadOnlyList<string> lines, string key)
    {
        foreach (string line in lines)
        {
            string value = ReadToken(line, key);
            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int ExtractActiveSnapshotCount(IReadOnlyList<string> lines, string token)
        => lines.Count(line => line.Contains("snapshotL1=", StringComparison.OrdinalIgnoreCase)
                               && line.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string ReadToken(string line, string key)
    {
        int index = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        int start = index + key.Length;
        int end = start;
        while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != ',' && line[end] != ';')
        {
            end++;
        }

        return line[start..end].Trim().Trim('"');
    }

    private static bool IsSymbolToken(string value)
        => value.Length is >= 1 and <= 10
           && value.All(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-');

    private static string CreateReportId()
    {
        string value = $"PCR-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        return value.Length <= 36 ? value : value[..36];
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

    private sealed record RuntimeLogSnapshot(
        string? RuntimeLogPath,
        IReadOnlyList<string> Lines,
        DateTimeOffset? SessionStartUtc,
        DateTimeOffset? SessionEndUtc,
        string Account,
        string Profile,
        IReadOnlyList<string> Symbols,
        int DecisionCount,
        int IntentCount,
        int RoutedOrderCount,
        int ActiveSymbolCount,
        bool? RequestedL1,
        bool? RequestedL2,
        bool? CaptureSnapshots,
        int ActiveL1SnapshotCount,
        int ActiveL2SnapshotCount,
        IReadOnlyList<string> Warnings)
    {
        public static RuntimeLogSnapshot Empty(string path, string warning)
            => new(
                RuntimeLogPath: null,
                Lines: Array.Empty<string>(),
                SessionStartUtc: null,
                SessionEndUtc: null,
                Account: "not-configured",
                Profile: "unknown",
                Symbols: Array.Empty<string>(),
                DecisionCount: 0,
                IntentCount: 0,
                RoutedOrderCount: 0,
                ActiveSymbolCount: 0,
                RequestedL1: null,
                RequestedL2: null,
                CaptureSnapshots: null,
                ActiveL1SnapshotCount: 0,
                ActiveL2SnapshotCount: 0,
                Warnings: new[] { warning, $"Looked under: {path}" });
    }

    private sealed class PnlPositionAccumulator
    {
        public int Quantity { get; private set; }

        public decimal AveragePrice { get; private set; }

        public decimal Apply(int signedQuantity, decimal fillPrice)
        {
            if (signedQuantity == 0)
            {
                return 0m;
            }

            if (Quantity == 0 || Math.Sign(Quantity) == Math.Sign(signedQuantity))
            {
                int newQuantity = Quantity + signedQuantity;
                decimal oldNotional = Math.Abs(Quantity) * AveragePrice;
                decimal newNotional = Math.Abs(signedQuantity) * fillPrice;
                Quantity = newQuantity;
                AveragePrice = Quantity == 0 ? 0m : (oldNotional + newNotional) / Math.Abs(Quantity);
                return 0m;
            }

            int closingQuantity = Math.Min(Math.Abs(Quantity), Math.Abs(signedQuantity));
            decimal realized = Quantity > 0
                ? (fillPrice - AveragePrice) * closingQuantity
                : (AveragePrice - fillPrice) * closingQuantity;

            int newPosition = Quantity + signedQuantity;
            if (newPosition == 0)
            {
                Quantity = 0;
                AveragePrice = 0m;
            }
            else if (Math.Sign(newPosition) == Math.Sign(Quantity))
            {
                Quantity = newPosition;
            }
            else
            {
                Quantity = newPosition;
                AveragePrice = fillPrice;
            }

            return realized;
        }
    }

    private sealed record PositionAndPnlSummary(int PositionsOpened, int PositionsClosed, decimal RealizedPnl);
}
