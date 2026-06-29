using Sailor.App.Backtest.Profiles;
using Sailor.App.Broker.Ibkr.Orders;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed class PaperRuntimeHost
{
    private readonly SailorAppSettings _settings;
    private readonly Action<string> _log;

    public PaperRuntimeHost(SailorAppSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
    }

    public async Task<PaperRuntimeHostResult> RunAsync(
        PaperRuntimeHostRequest request,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        SailorRuntimeState runtimeState = new(request.RuntimeOptions.Mode);
        runtimeState.SetStatus(SailorRuntimeStatus.Scanning, "SAILOR-031 scanner/activation phase.");

        var incidentReporter = new RuntimeIncidentReporter(request.RuntimeOptions.Mode);
        var healthMonitor = new RuntimeHealthMonitor(request.RuntimeOptions.Mode, incidentReporter, request.CanOpenEntries);
        var recoveryService = new ConnectionRecoveryService(healthMonitor, _log);

        _log(request.RuntimeOptions.Mode == SailorRuntimeMode.Live
            ? "SAILOR-034 implementation: live pilot conduct loop."
            : "SAILOR-030 implementation: paper conduct loop.");
        _log("This runtime slice runs the scanner, activates selected symbols, builds strategy frames on a cadence, converts strategy decisions to order intents, and routes them through the configured order router.");
        _log(request.RuntimeOptions.Mode == SailorRuntimeMode.Live
            ? "Live pilot mode requires the live-readiness gate, one explicit symbol, small max notional, close-only safety, and final broker reconciliation."
            : "Dry-run mode assumes fills locally so the conduct/exit path can be exercised without broker orders. Send-orders mode requires broker reconciliation and only updates local session position after actual filled quantity is reported.");
        _log("");
        _log("SAILOR-031 implementation: disconnection and degraded-state handling.");
        _log("Runtime health starts in Normal only when the pre-run broker/reconciliation gate is clean. Any disconnect, degraded broker signal, or routing failure moves the runtime to CloseOnly and blocks new entries.");
        _log($"Incident JSONL: {incidentReporter.DailyJsonlPath}");
        _log($"Latest incident JSON: {incidentReporter.LatestIncidentPath}");
        _log(healthMonitor.SafetyState.ToDisplayString());
        _log("");

        using var scannerRunner = new PaperScannerRunner(_settings, request.ConnectionOptions, request.ScannerOptions);
        _log($"History provider: {scannerRunner.HistoryProviderName}");
        _log($"Market data provider: {scannerRunner.MarketDataProviderName}");
        _log("");

        PaperScannerRunResult scannerResult;
        try
        {
            scannerResult = await scannerRunner.RunAsync(request.ScannerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            string scannerFailure = $"Scanner/activation failed without crashing runtime: {ex.GetType().Name}: {ex.Message}";
            warnings.Add(scannerFailure);
            RuntimeIncident? scannerFailureIncident = healthMonitor.MarkCloseOnly(
                "scanner-failed",
                scannerFailure,
                new[] { scannerFailure });

            if (scannerFailureIncident is not null)
            {
                _log(scannerFailureIncident.ToDisplayString());
            }

            _log($"WARN: {scannerFailure}");
            scannerResult = new PaperScannerRunResult(
                request.ScannerOptions,
                string.IsNullOrWhiteSpace(request.RuntimeOptions.Universe) ? Array.Empty<string>() : new[] { request.RuntimeOptions.Universe.Trim().ToUpperInvariant() },
                Array.Empty<string>(),
                Array.Empty<PaperScannerSymbolPreparation>(),
                Array.Empty<PaperScannerCandidate>(),
                CandidateReportPath: null,
                Warnings: new[] { scannerFailure });
        }

        _log("Scanner/activation summary");
        _log("--------------------------");
        _log(scannerResult.ToSummaryString());
        _log($"Resolved symbols: {string.Join(", ", scannerResult.ResolvedSymbols.Take(80))}{(scannerResult.ResolvedSymbols.Count > 80 ? ", ..." : string.Empty)}");
        _log($"Prepared symbols: {string.Join(", ", scannerResult.PreparedSymbols.Take(80))}{(scannerResult.PreparedSymbols.Count > 80 ? ", ..." : string.Empty)}");

        if (!string.IsNullOrWhiteSpace(scannerResult.CandidateReportPath))
        {
            _log($"Scanner CSV report: {scannerResult.CandidateReportPath}");
        }

        if (!string.IsNullOrWhiteSpace(scannerResult.HybridComparisonReportPath))
        {
            _log($"Hybrid comparison CSV report: {scannerResult.HybridComparisonReportPath}");
        }

        foreach (string warning in scannerResult.Warnings)
        {
            warnings.Add(warning);
            _log($"WARN: {warning}");
        }

        RuntimeIncident? scannerIncident = healthMonitor.ObserveMessages(scannerResult.Warnings, "scanner/activation");
        if (scannerIncident is not null)
        {
            warnings.Add(scannerIncident.Message);
            _log(scannerIncident.ToDisplayString());
            _log(healthMonitor.SafetyState.ToDisplayString());
        }

        _log("");

        IReadOnlyList<PaperSymbolSession> sessions = CreateSessions(request, scannerResult, warnings);
        if (sessions.Count == 0)
        {
            warnings.Add("No symbols were activated. Conduct loop did not start.");
            return new PaperRuntimeHostResult(Array.Empty<string>(), 0, 0, 0, 0, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        runtimeState.SetActiveSymbols(sessions.Select(session => session.Symbol));
        runtimeState.SetStatus(SailorRuntimeStatus.Running, "SAILOR-030 active symbol sessions created.");

        _log("Active symbol sessions");
        _log("----------------------");
        foreach (PaperSymbolSession session in sessions)
        {
            _log($"{session.Symbol}: data={session.DataSourcePath} snapshotL1={session.MarketSnapshot?.HasL1 == true} snapshotL2={session.MarketSnapshot?.HasL2 == true} seedPosition={session.PositionDisplay()} strategy={session.Strategy.Name}");
        }

        _log("");

        await using IOrderRouter router = IbkrOrderRouterFactory.Create(
            request.SendOrders,
            request.ConnectionOptions,
            request.PrimaryExchange,
            request.WaitSeconds);

        var conductLoop = new PaperConductLoop(request.RuntimeOptions.Mode, _log);
        PaperRuntimeHostResult loopResult = await conductLoop.RunAsync(
            sessions,
            router,
            request,
            runtimeState,
            healthMonitor,
            recoveryService,
            cancellationToken).ConfigureAwait(false);

        runtimeState.SetStatus(SailorRuntimeStatus.Stopped, "SAILOR-030 conduct loop finished.");
        _log($"Final {request.RuntimeOptions.ModeName} session state");
        _log("-------------------------");
        foreach (PaperSymbolSession session in sessions)
        {
            _log(session.PositionDisplay());
        }

        _log(runtimeState.ToDisplayString());
        _log(healthMonitor.SafetyState.ToDisplayString());
        if (healthMonitor.LastIncident is not null)
        {
            _log($"Latest incident: {healthMonitor.LastIncident.ToDisplayString()}");
            _log($"Latest incident JSON: {incidentReporter.LatestIncidentPath}");
        }

        return loopResult with
        {
            Warnings = warnings.Concat(loopResult.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private IReadOnlyList<PaperSymbolSession> CreateSessions(
        PaperRuntimeHostRequest request,
        PaperScannerRunResult scannerResult,
        List<string> warnings)
    {
        SailorStrategyProfile profile = SailorStrategyProfile.FromName(request.RuntimeOptions.ProfileName, _settings);
        int maxActive = Math.Max(1, Math.Min(request.RuntimeOptions.TopCount, _settings.Runtime.Safety.MaxActiveSymbols <= 0 ? request.RuntimeOptions.TopCount : _settings.Runtime.Safety.MaxActiveSymbols));

        var selected = scannerResult.Candidates
            .Take(maxActive)
            .Select(candidate => new ActiveSymbolSeed(candidate.Symbol, candidate.Snapshot))
            .ToList();

        if (selected.Count == 0)
        {
            IReadOnlyList<string> fallbackSymbols = scannerResult.PreparedSymbols.Count > 0
                ? scannerResult.PreparedSymbols
                : scannerResult.ResolvedSymbols;

            selected.AddRange(fallbackSymbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxActive)
                .Select(symbol => new ActiveSymbolSeed(symbol, null)));

            if (selected.Count > 0)
            {
                string warning = "Scanner returned no ranked candidates; SAILOR-030 activated prepared fallback symbols so the conduct loop can still be smoke-tested.";
                warnings.Add(warning);
                _log($"WARN: {warning}");
            }
        }

        var localBySymbol = request.Reconciliation.LocalPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var brokerBySymbol = request.Reconciliation.BrokerPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var sessions = new List<PaperSymbolSession>();
        foreach (ActiveSymbolSeed selectedSymbol in selected)
        {
            string normalizedSymbol = selectedSymbol.Symbol.Trim().ToUpperInvariant();
            localBySymbol.TryGetValue(normalizedSymbol, out SailorPosition? localSeed);
            brokerBySymbol.TryGetValue(normalizedSymbol, out BrokerPositionRow? brokerSeed);

            try
            {
                sessions.Add(PaperSymbolSession.Create(
                    request.RuntimeOptions.Mode,
                    normalizedSymbol,
                    request.RuntimeOptions.Timeframe,
                    profile,
                    _settings,
                    selectedSymbol.Snapshot,
                    localSeed,
                    brokerSeed,
                    request.MaxIterations));
            }
            catch (Exception ex)
            {
                string warning = $"{normalizedSymbol}: could not create paper symbol session: {ex.Message}";
                warnings.Add(warning);
                _log($"WARN: {warning}");
            }
        }

        return sessions;
    }

    private sealed record ActiveSymbolSeed(string Symbol, Sailor.App.MarketData.Snapshots.SailorMarketSnapshot? Snapshot);
}
