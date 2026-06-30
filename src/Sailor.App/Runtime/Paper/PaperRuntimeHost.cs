using Sailor.App.Backtest.Profiles;
using Sailor.App.Broker.Ibkr.Orders;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
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

        var tradeRegistry = new TradeLifecycleRegistryStore(request.RuntimeOptions.Mode);
        _log("SAILOR-051 trade lifecycle registry and ownership model.");
        _log("This milestone records scanner-owned/explicit/pre-existing lifecycle ownership. It does not yet change order routing, broker discovery, or session replenishment behavior.");
        _log($"Trade registry latest JSON: {tradeRegistry.LatestJsonPath}");
        _log($"Trade registry event JSONL: {tradeRegistry.DailyJsonlPath}");
        _log("");

        bool hasBrokerMirrorInput = request.Reconciliation.BrokerPositions.Count > 0
            || request.Reconciliation.BrokerOpenOrders.Count > 0
            || request.Reconciliation.BrokerExecutions.Count > 0;
        if (request.SendOrders || hasBrokerMirrorInput)
        {
            var brokerDetector = new BrokerStateManualTradeDetector(request.RuntimeOptions.Mode, tradeRegistry);
            BrokerStateMirrorSnapshot mirror = brokerDetector.MirrorAndDetect(
                request.Reconciliation,
                request.Account,
                brokerVerified: true,
                unknownBrokerPositionsAreIntradayManual: false,
                markMissingActivePositionsAsManualClosed: true,
                source: "pre-run-reconciliation");

            _log("SAILOR-052 broker state mirror and manual trade detector.");
            _log("Pre-run broker positions/open orders/executions were mirrored before creating strategy sessions.");
            _log($"Broker mirror latest JSON: {brokerDetector.LatestJsonPath}");
            _log($"Broker mirror event JSONL: {brokerDetector.DailyJsonlPath}");
            _log(mirror.ToSummaryString());
            foreach (BrokerMirrorDetection detection in mirror.Detections)
            {
                _log($"broker-detection: {detection.ToDisplayString()}");
            }

            foreach (string mirrorWarning in mirror.Warnings)
            {
                warnings.Add(mirrorWarning);
            }

            _log("");
        }

        var sessionManager = new DynamicTradeSessionManager(_settings, _log);
        DynamicTradeSessionPlan sessionPlan = sessionManager.BuildPlan(request, scannerResult, tradeRegistry);

        _log("SAILOR-053 dynamic trade session manager.");
        _log("This milestone builds one conduct-session plan from scanner-selected symbols, broker/manual/pre-existing positions, local Sailor positions, and active lifecycle registry rows.");
        _log(sessionPlan.ToSummaryString());
        foreach (DynamicTradeSessionSeed seed in sessionPlan.Seeds)
        {
            _log($"dynamic-session: {seed.ToDisplayString()}");
        }

        foreach (string planWarning in sessionPlan.Warnings)
        {
            warnings.Add(planWarning);
            _log($"WARN: {planWarning}");
        }

        _log("");

        var lifecyclePolicyResolver = new StrategyLifecyclePolicyResolver(_settings);
        _log("SAILOR-054 strategy lifecycle policies.");
        _log("V21/V22/V23/V24 are multi-cycle only before the universal LastEntryMinute; default profiles are single-lifecycle; manual/unknown broker trades are exit-only.");
        _log(lifecyclePolicyResolver.ToSummaryString());
        _log($"universalLastEntryMinute={request.RuntimeOptions.LastEntryMinute} universalForceFlatMinute={request.RuntimeOptions.ForceFlatMinute}");
        _log("");

        List<PaperSymbolSession> sessions = CreateSessions(request, sessionPlan.Seeds, warnings, tradeRegistry, lifecyclePolicyResolver);
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
            _log($"{session.Symbol}: data={session.DataSourcePath} snapshotL1={session.MarketSnapshot?.HasL1 == true} snapshotL2={session.MarketSnapshot?.HasL2 == true} seedPosition={session.PositionDisplay()} strategy={session.Strategy.Name} policy={session.LifecyclePolicy.ToDisplayString()}");
        }

        _log("");

        var scannerSlotManager = new ScannerSlotManager(
            _settings,
            request.ConnectionOptions,
            request.ReplenishmentScannerOptions ?? request.ScannerOptions,
            tradeRegistry,
            lifecyclePolicyResolver,
            _log,
            request.RuntimeOptions.Mode);
        _log("SAILOR-055 scanner slot target and 5-minute replenishment.");
        _log("Scanner-owned sessions count toward the scanner target; manual/pre-existing/unknown sessions are managed separately and never reduce scanner shortfall.");
        _log(scannerSlotManager.ToDisplayString());
        ScannerSlotReplenishmentReport initialSlotReport = scannerSlotManager.WriteStatusReport(sessions, "initial scanner slot report before conduct loop");
        _log(initialSlotReport.ToSummaryString());
        _log($"Scanner slot latest JSON: {initialSlotReport.JsonPath}");
        _log($"Scanner slot CSV: {initialSlotReport.CsvPath}");
        _log("");

        await using IOrderRouter router = IbkrOrderRouterFactory.Create(
            request.SendOrders,
            request.ConnectionOptions,
            request.PrimaryExchange,
            request.WaitSeconds);

        SevereDisconnectRecoveryOrchestrator? severeRecoveryOrchestrator = _settings.Runtime.Safety.SevereDisconnectRecoveryEnabled
            ? new SevereDisconnectRecoveryOrchestrator(
                _settings,
                request.ConnectionOptions,
                tradeRegistry,
                lifecyclePolicyResolver,
                scannerSlotManager,
                _log,
                request.RuntimeOptions.Mode)
            : null;

        _log("SAILOR-056 severe disconnect recovery orchestrator.");
        _log(_settings.Runtime.Safety.SevereDisconnectRecoveryEnabled
            ? "Severe disconnect recovery will reconnect, rebuild broker-truth sessions, refresh history, and resume entries only after clean reconciliation before LastEntryMinute."
            : "Severe disconnect recovery orchestrator is disabled by Runtime.Safety.SevereDisconnectRecoveryEnabled=false; legacy reconnect-only behavior remains active.");
        if (severeRecoveryOrchestrator is not null)
        {
            _log($"Severe recovery latest JSON: {severeRecoveryOrchestrator.LatestJsonPath}");
            _log($"Severe recovery CSV: {severeRecoveryOrchestrator.DailyCsvPath}");
        }
        _log("");

        var conductLoop = new PaperConductLoop(request.RuntimeOptions.Mode, _log, tradeRegistry, scannerSlotManager, severeRecoveryOrchestrator);
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

    private List<PaperSymbolSession> CreateSessions(
        PaperRuntimeHostRequest request,
        IReadOnlyList<DynamicTradeSessionSeed> seeds,
        List<string> warnings,
        TradeLifecycleRegistryStore tradeRegistry,
        StrategyLifecyclePolicyResolver lifecyclePolicyResolver)
    {
        SailorStrategyProfile profile = SailorStrategyProfile.FromName(request.RuntimeOptions.ProfileName, _settings);

        var localBySymbol = request.Reconciliation.LocalPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var brokerBySymbol = request.Reconciliation.BrokerPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var sessions = new List<PaperSymbolSession>();
        foreach (DynamicTradeSessionSeed seed in seeds)
        {
            string normalizedSymbol = seed.NormalizedSymbol;
            localBySymbol.TryGetValue(normalizedSymbol, out SailorPosition? localSeed);
            brokerBySymbol.TryGetValue(normalizedSymbol, out BrokerPositionRow? brokerSeed);

            try
            {
                SailorTradeOrigin origin = seed.Origin;
                if (brokerSeed is not null && !brokerSeed.IsFlat && origin == SailorTradeOrigin.ScannerOwned)
                {
                    origin = SailorTradeOrigin.SailorPreExisting;
                }
                else if (localSeed is not null && !localSeed.IsFlat && origin == SailorTradeOrigin.ScannerOwned)
                {
                    origin = SailorTradeOrigin.SailorPreExisting;
                }

                PaperSymbolSession session = PaperSymbolSession.Create(
                    request.RuntimeOptions.Mode,
                    normalizedSymbol,
                    request.RuntimeOptions.Timeframe,
                    profile,
                    _settings,
                    seed.Snapshot,
                    localSeed,
                    brokerSeed,
                    origin,
                    seed.ScannerSlotId,
                    lifecyclePolicyResolver.Resolve(profile.Name, origin),
                    request.MaxIterations);

                StrategyLifecyclePolicy lifecyclePolicy = session.LifecyclePolicy;

                TradeLifecycle lifecycle = tradeRegistry.RegisterRuntimeSession(
                    normalizedSymbol,
                    profile.Name,
                    origin,
                    seed.ScannerSlotId,
                    session.PositionQuantity,
                    session.AveragePrice,
                    request.RuntimeOptions.Timeframe,
                    request.Account,
                    $"SAILOR-053 dynamic session registered. {seed.Reason}");

                _log($"{normalizedSymbol}: tradeLifecycle={lifecycle.TradeId} origin={lifecycle.Origin.ToDisplayName()} status={lifecycle.Status.ToDisplayName()} scannerSlot={lifecycle.ScannerSlotId ?? "n/a"} lifecycle={lifecyclePolicy.Mode.ToDisplayName()} dynamicReason={seed.Reason}");
                sessions.Add(session);
            }
            catch (Exception ex)
            {
                string warning = $"{normalizedSymbol}: could not create paper symbol session from dynamic plan: {ex.Message}";
                warnings.Add(warning);
                _log($"WARN: {warning}");
            }
        }

        return sessions;
    }

}
