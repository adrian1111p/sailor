using System.Reflection;
using Sailor.App.Backtest;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Broker.Ibkr;
using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.MarketData.Snapshots;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed class SevereDisconnectRecoveryOrchestrator
{
    private readonly SailorAppSettings _settings;
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly TradeLifecycleRegistryStore _tradeRegistry;
    private readonly StrategyLifecyclePolicyResolver _lifecyclePolicyResolver;
    private readonly ScannerSlotManager? _scannerSlotManager;
    private readonly Action<string> _log;
    private readonly SevereDisconnectRecoveryReportWriter _writer;
    private readonly SailorRuntimeMode _mode;

    public SevereDisconnectRecoveryOrchestrator(
        SailorAppSettings settings,
        IbkrConnectionOptions connectionOptions,
        TradeLifecycleRegistryStore tradeRegistry,
        StrategyLifecyclePolicyResolver lifecyclePolicyResolver,
        ScannerSlotManager? scannerSlotManager,
        Action<string> log,
        SailorRuntimeMode mode)
    {
        _settings = settings;
        _connectionOptions = connectionOptions;
        _tradeRegistry = tradeRegistry;
        _lifecyclePolicyResolver = lifecyclePolicyResolver;
        _scannerSlotManager = scannerSlotManager;
        _log = log;
        _mode = mode;
        _writer = new SevereDisconnectRecoveryReportWriter(mode);
    }

    public string LatestJsonPath => _writer.LatestJsonPath;

    public string DailyCsvPath => _writer.DailyCsvPath;

    public async Task<SevereDisconnectRecoveryResult> RecoverAsync(
        List<PaperSymbolSession> sessions,
        PaperRuntimeHostRequest request,
        SailorRuntimeState runtimeState,
        RuntimeHealthMonitor healthMonitor,
        ConnectionRecoveryService recoveryService,
        string triggerReason,
        CancellationToken cancellationToken)
    {
        var events = new List<string>();
        var warnings = new List<string>();
        IReadOnlyList<string> activeSymbolsBefore = sessions
            .Select(session => session.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int sessionsBefore = sessions.Count;

        runtimeState.SetStatus(SailorRuntimeStatus.Reconnecting, $"SAILOR-056 severe disconnect recovery after {triggerReason}.");

        ConnectionRecoveryResult connectionRecovery = await recoveryService.TryRecoverAsync(
            request.ReconcileBrokerStateAsync,
            activeSymbolsBefore,
            request.ReconnectAttempts,
            TimeSpan.FromSeconds(Math.Max(1, request.ReconnectBackoffSeconds)),
            cancellationToken).ConfigureAwait(false);

        events.AddRange(connectionRecovery.Events);
        warnings.AddRange(connectionRecovery.Warnings);

        ReconciliationResult? reconciliation = connectionRecovery.Reconciliation;
        bool brokerTruthAvailable = reconciliation is not null;
        bool cleanReconciliation = connectionRecovery.Recovered
            && reconciliation is not null
            && reconciliation.CanOpenNewEntries
            && reconciliation.Status.ToString().Equals("Matched", StringComparison.OrdinalIgnoreCase);

        BrokerStateMirrorSnapshot? mirror = null;
        if (reconciliation is not null)
        {
            try
            {
                var detector = new BrokerStateManualTradeDetector(_mode, _tradeRegistry);
                mirror = detector.MirrorAndDetect(
                    reconciliation,
                    request.Account,
                    brokerVerified: connectionRecovery.Recovered,
                    unknownBrokerPositionsAreIntradayManual: true,
                    markMissingActivePositionsAsManualClosed: true,
                    source: "sailor-056-severe-disconnect-recovery");

                events.Add($"broker-mirror: {mirror.ToSummaryString()}");
                foreach (BrokerMirrorDetection detection in mirror.Detections)
                {
                    events.Add($"broker-detection: {detection.ToDisplayString()}");
                }

                warnings.AddRange(mirror.Warnings);
            }
            catch (Exception ex)
            {
                string warning = $"SAILOR-056 broker mirror failed during recovery: {ex.GetType().Name}: {ex.Message}";
                warnings.Add(warning);
                events.Add(warning);
            }
        }

        IReadOnlyList<string> brokerPositionSymbols = reconciliation is null
            ? Array.Empty<string>()
            : reconciliation.BrokerPositions
                .Where(position => !position.IsFlat)
                .Select(position => NormalizeSymbol(position.Symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        IReadOnlyList<string> brokerOpenOrderSymbols = reconciliation is null
            ? Array.Empty<string>()
            : reconciliation.BrokerOpenOrders
                .Cast<object>()
                .Select(ReadSymbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(NormalizeSymbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        IReadOnlyList<string> recoverySymbols = BuildRecoverySymbols(sessions, reconciliation, brokerPositionSymbols, brokerOpenOrderSymbols);
        PaperScannerRunResult? historyRefresh = null;
        bool historyRefreshAttempted = false;
        if (brokerTruthAvailable
            && recoverySymbols.Count > 0
            && _settings.Runtime.Safety.SevereDisconnectRefreshHistoryBeforeResume)
        {
            historyRefreshAttempted = true;
            historyRefresh = await RefreshHistoryForRecoveryAsync(request, recoverySymbols, warnings, events, cancellationToken).ConfigureAwait(false);
        }

        bool sessionsRebuilt = false;
        if (reconciliation is not null && recoverySymbols.Count > 0)
        {
            IReadOnlyList<PaperSymbolSession> rebuiltSessions = RebuildSessionsFromBrokerTruth(
                sessions,
                request,
                reconciliation,
                recoverySymbols,
                historyRefresh,
                events,
                warnings);

            sessions.Clear();
            sessions.AddRange(rebuiltSessions);
            sessionsRebuilt = true;
            runtimeState.SetActiveSymbols(sessions.Select(session => session.Symbol));
        }

        DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
        int easternMinuteOfDay = MarketTime.GetEasternMinuteOfDay(observedUtc);
        bool beforeLastEntry = easternMinuteOfDay < request.RuntimeOptions.LastEntryMinute;
        bool canResumeEntries = cleanReconciliation
            && beforeLastEntry
            && _settings.Runtime.Safety.SevereDisconnectResumeEntriesOnlyAfterCleanReconciliation;
        bool scannerReplenishmentAllowed = canResumeEntries
            && _settings.Runtime.Safety.SevereDisconnectResumeScannerBeforeLastEntry;

        if (connectionRecovery.Recovered && !canResumeEntries)
        {
            string closeOnlyReason = beforeLastEntry
                ? "SAILOR-056 recovery completed but entries remain blocked because clean-reconciliation entry resume is disabled or reconciliation was not clean."
                : $"SAILOR-056 recovery completed at ET minute {easternMinuteOfDay}, at/after LastEntryMinute {request.RuntimeOptions.LastEntryMinute}. Entries and scanner replenishment remain blocked; exits/flatten/reconcile continue.";
            RuntimeIncident? closeOnlyIncident = healthMonitor.MarkCloseOnly(
                "severe-recovery-close-only",
                closeOnlyReason,
                new[]
                {
                    $"trigger={triggerReason}",
                    $"reconciliation={reconciliation?.Status.ToString() ?? "n/a"}",
                    $"sessionsAfter={sessions.Count}"
                });

            if (closeOnlyIncident is not null)
            {
                events.Add(closeOnlyIncident.ToDisplayString());
            }
        }

        if (scannerReplenishmentAllowed)
        {
            _scannerSlotManager?.RequestImmediateReplenishment();
            events.Add("scanner-replenishment-resume: immediate replenishment check requested because SAILOR-056 recovered before LastEntryMinute with clean reconciliation.");
        }
        else
        {
            events.Add("scanner-replenishment-resume: blocked until runtime is clean and before LastEntryMinute.");
        }

        var report = new SevereDisconnectRecoveryReport(
            observedUtc,
            _mode.ToDisplayName(),
            NormalizeReason(triggerReason),
            connectionRecovery.Recovered,
            reconciliation?.Status.ToString() ?? "n/a",
            brokerTruthAvailable,
            sessionsRebuilt,
            historyRefreshAttempted,
            historyRefresh?.HistorySuccessCount ?? 0,
            historyRefresh?.Preparations.Count ?? 0,
            easternMinuteOfDay,
            request.RuntimeOptions.LastEntryMinute,
            canResumeEntries,
            scannerReplenishmentAllowed,
            sessionsBefore,
            sessions.Count,
            activeSymbolsBefore,
            brokerPositionSymbols,
            brokerOpenOrderSymbols,
            sessions.Select(session => session.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase).ToArray(),
            events.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        SevereDisconnectRecoveryReport writtenReport = _writer.Write(report);
        runtimeState.SetStatus(
            SailorRuntimeStatus.Running,
            writtenReport.CanResumeEntries
                ? "SAILOR-056 recovery rebuilt sessions and resumed normal entry gates."
                : "SAILOR-056 recovery rebuilt sessions in exit-only/close-only mode.");

        return new SevereDisconnectRecoveryResult(writtenReport, connectionRecovery, reconciliation, mirror);
    }

    private async Task<PaperScannerRunResult?> RefreshHistoryForRecoveryAsync(
        PaperRuntimeHostRequest request,
        IReadOnlyList<string> recoverySymbols,
        List<string> warnings,
        List<string> events,
        CancellationToken cancellationToken)
    {
        try
        {
            PaperScannerOptions recoveryOptions = request.ScannerOptions with
            {
                Universe = string.Join(',', recoverySymbols),
                TopCount = Math.Max(1, recoverySymbols.Count),
                MaxSymbolsToPrepare = Math.Max(1, recoverySymbols.Count),
                CaptureSnapshots = request.ScannerOptions.CaptureSnapshots && request.ScannerOptions.RequestIbkrMarketData,
                RequestIbkrMarketData = request.ScannerOptions.RequestIbkrMarketData
            };

            using var scannerRunner = new PaperScannerRunner(_settings, _connectionOptions, recoveryOptions);
            PaperScannerRunResult result = await scannerRunner.RunAsync(recoveryOptions, cancellationToken).ConfigureAwait(false);
            events.Add($"history-refresh: {result.ToSummaryString()}");
            warnings.AddRange(result.Warnings.Select(warning => $"history-refresh:{warning}"));
            foreach (PaperScannerSymbolPreparation preparation in result.Preparations.Where(row => !row.HistorySuccess))
            {
                warnings.Add($"history-refresh:{preparation.Symbol}:{preparation.Message}");
            }

            return result;
        }
        catch (Exception ex)
        {
            string warning = $"SAILOR-056 history refresh failed: {ex.GetType().Name}: {ex.Message}";
            warnings.Add(warning);
            events.Add(warning);
            return null;
        }
    }

    private IReadOnlyList<PaperSymbolSession> RebuildSessionsFromBrokerTruth(
        IReadOnlyList<PaperSymbolSession> previousSessions,
        PaperRuntimeHostRequest request,
        ReconciliationResult reconciliation,
        IReadOnlyList<string> recoverySymbols,
        PaperScannerRunResult? scannerResult,
        List<string> events,
        List<string> warnings)
    {
        SailorStrategyProfile profile = SailorStrategyProfile.FromName(request.RuntimeOptions.ProfileName, _settings);
        var previousBySymbol = previousSessions
            .GroupBy(session => session.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var brokerBySymbol = reconciliation.BrokerPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => NormalizeSymbol(position.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(position => Math.Abs(position.Quantity)).First(), StringComparer.OrdinalIgnoreCase);
        var snapshotBySymbol = scannerResult?.Candidates
            .Where(candidate => candidate.Snapshot is not null)
            .GroupBy(candidate => NormalizeSymbol(candidate.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Snapshot, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, SailorMarketSnapshot?>(StringComparer.OrdinalIgnoreCase);
        TradeLifecycleRegistrySnapshot registrySnapshot = _tradeRegistry.LoadSnapshot();

        var rebuilt = new List<PaperSymbolSession>();
        foreach (string symbol in recoverySymbols.OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase))
        {
            string normalizedSymbol = NormalizeSymbol(symbol);
            previousBySymbol.TryGetValue(normalizedSymbol, out PaperSymbolSession? previousSession);
            brokerBySymbol.TryGetValue(normalizedSymbol, out BrokerPositionRow? brokerSeed);
            snapshotBySymbol.TryGetValue(normalizedSymbol, out SailorMarketSnapshot? snapshot);
            TradeLifecycle? lifecycle = FindRelevantLifecycle(registrySnapshot, normalizedSymbol);

            SailorTradeOrigin origin = ResolveRecoveryOrigin(lifecycle, previousSession, brokerSeed, hasOpenOrder: HasOpenOrder(reconciliation, normalizedSymbol));
            string? scannerSlotId = lifecycle?.ScannerSlotId ?? previousSession?.ScannerSlotId;
            if (origin == SailorTradeOrigin.ScannerOwned && string.IsNullOrWhiteSpace(scannerSlotId))
            {
                origin = brokerSeed is not null ? SailorTradeOrigin.SailorPreExisting : SailorTradeOrigin.UnknownBroker;
            }

            try
            {
                PaperSymbolSession session = PaperSymbolSession.Create(
                    request.RuntimeOptions.Mode,
                    normalizedSymbol,
                    request.RuntimeOptions.Timeframe,
                    profile,
                    _settings,
                    snapshot,
                    localSeed: null,
                    brokerSeed: brokerSeed,
                    tradeOrigin: origin,
                    scannerSlotId: scannerSlotId,
                    lifecyclePolicy: _lifecyclePolicyResolver.Resolve(profile.Name, origin),
                    maxIterations: request.MaxIterations,
                    runtimeLastEntryMinute: request.RuntimeOptions.LastEntryMinute,
                    runtimeForceFlatMinute: request.RuntimeOptions.ForceFlatMinute,
                    requireCurrentLiveBars: request.SendOrders && _settings.Runtime.Safety.RequireCurrentBarsForPaperSendOrders,
                    liveBarMaxAgeMinutes: request.LiveBarMaxAgeMinutes);

                rebuilt.Add(session);
                TradeLifecycle updated = _tradeRegistry.RegisterRuntimeSession(
                    normalizedSymbol,
                    profile.Name,
                    origin,
                    scannerSlotId,
                    brokerSeed?.Quantity ?? 0,
                    brokerSeed?.AverageCost ?? 0m,
                    request.RuntimeOptions.Timeframe,
                    request.Account,
                    $"SAILOR-056 severe disconnect recovery rebuilt session from broker truth. brokerPosition={brokerSeed is not null} openOrder={HasOpenOrder(reconciliation, normalizedSymbol)} previousSession={previousSession is not null}.");

                events.Add($"session-rebuilt: {normalizedSymbol} origin={origin.ToDisplayName()} slot={scannerSlotId ?? "n/a"} lifecycle={updated.TradeId} brokerQty={brokerSeed?.Quantity ?? 0}");
            }
            catch (Exception ex)
            {
                string warning = $"{normalizedSymbol}: SAILOR-056 could not rebuild session: {ex.GetType().Name}: {ex.Message}";
                warnings.Add(warning);
                events.Add(warning);
            }
        }

        return rebuilt;
    }

    private static IReadOnlyList<string> BuildRecoverySymbols(
        IReadOnlyList<PaperSymbolSession> sessions,
        ReconciliationResult? reconciliation,
        IReadOnlyList<string> brokerPositionSymbols,
        IReadOnlyList<string> brokerOpenOrderSymbols)
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in brokerPositionSymbols.Concat(brokerOpenOrderSymbols))
        {
            symbols.Add(NormalizeSymbol(symbol));
        }

        foreach (PaperSymbolSession session in sessions)
        {
            if (session.HasOpenPosition || session.ScannerSlotActive || session.TradeOrigin.CountsTowardScannerTarget())
            {
                symbols.Add(NormalizeSymbol(session.Symbol));
            }
        }

        if (reconciliation is not null)
        {
            foreach (SailorPosition local in reconciliation.LocalPositions.Where(position => !position.IsFlat))
            {
                symbols.Add(NormalizeSymbol(local.Symbol));
            }
        }

        return symbols.OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static SailorTradeOrigin ResolveRecoveryOrigin(
        TradeLifecycle? lifecycle,
        PaperSymbolSession? previousSession,
        BrokerPositionRow? brokerSeed,
        bool hasOpenOrder)
    {
        if (lifecycle is not null && lifecycle.Origin != SailorTradeOrigin.UnknownBroker)
        {
            return lifecycle.Origin;
        }

        if (previousSession is not null && previousSession.TradeOrigin != SailorTradeOrigin.UnknownBroker)
        {
            return previousSession.TradeOrigin;
        }

        if (brokerSeed is not null || hasOpenOrder)
        {
            return SailorTradeOrigin.UnknownBroker;
        }

        return SailorTradeOrigin.ExplicitRuntime;
    }

    private static TradeLifecycle? FindRelevantLifecycle(TradeLifecycleRegistrySnapshot snapshot, string normalizedSymbol)
        => snapshot.Trades
            .Where(trade => trade.NormalizedSymbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.IsActive)
            .ThenByDescending(trade => trade.UpdatedUtc)
            .FirstOrDefault();

    private static bool HasOpenOrder(ReconciliationResult reconciliation, string normalizedSymbol)
        => reconciliation.BrokerOpenOrders
            .Cast<object>()
            .Any(order => NormalizeSymbol(ReadSymbol(order)).Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase));

    private static string ReadSymbol(object source)
    {
        PropertyInfo? property = source.GetType().GetProperty("Symbol", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        object? value = property?.GetValue(source);
        return value?.ToString()?.Trim() ?? string.Empty;
    }

    private static string NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim().ToUpperInvariant();

    private static string NormalizeReason(string reason)
        => string.IsNullOrWhiteSpace(reason) ? "severe-disconnect" : reason.Trim();
}

public sealed record SevereDisconnectRecoveryResult(
    SevereDisconnectRecoveryReport Report,
    ConnectionRecoveryResult ConnectionRecovery,
    ReconciliationResult? Reconciliation,
    BrokerStateMirrorSnapshot? BrokerMirror);
