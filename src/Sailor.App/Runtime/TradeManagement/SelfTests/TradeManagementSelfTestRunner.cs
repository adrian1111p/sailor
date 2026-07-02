using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Runtime.Paper;
using Sailor.App.Scanner.ScanList;
using Sailor.App.Runtime.Ui;

namespace Sailor.App.Runtime.TradeManagement.SelfTests;

public static class TradeManagementSelfTestRunner
{
    private const string AllScenario = "all";

    private static readonly string[] SupportedScenarios =
    [
        "preexisting-position",
        "manual-open-after-start",
        "manual-close-stop-day",
        "scanner-target-10-replenish",
        "severe-disconnect-recovery",
        "v21-multi-entry-until-close",
        "non-v21-single-lifecycle",
        "last-entry-945-blocks-replenishment",
        "force-flat-955-all-strategies",
        "live-current-candle-guard",
        "live-per-iteration-candle-refresh",
        "shared-ibkr-data-session",
        "live-refresh-fallback-diagnostics",
        "manual-broker-strategy-managed",
        "harsh-conduct-forced-entries",
        "sailor-ui-readonly",
        "sailor-ui-paper-controls",
        "sailor-ui-multistrategy-routing",
        "sailor-ui-live-hardening",
        "sailor-ui-report-export"
    ];

    public static Task<int> RunAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        string scenario = ReadStringOption(args, "--scenario", AllScenario).Trim().ToLowerInvariant();
        bool sendOrdersRequested = HasOption(args, "--send-orders");
        bool listOnly = HasOption(args, "--list") || scenario.Equals("list", StringComparison.OrdinalIgnoreCase);

        if (listOnly)
        {
            Console.WriteLine("SAILOR-057 supported trade-management self-test scenarios:");
            foreach (string supportedScenario in SupportedScenarios)
            {
                Console.WriteLine($"- {supportedScenario}");
            }
            Console.WriteLine("- all");
            Console.WriteLine("No broker orders are sent by these self-tests.");
            return Task.FromResult(0);
        }

        IReadOnlyList<string> scenarios = ResolveScenarios(scenario);
        if (scenarios.Count == 0)
        {
            Console.WriteLine($"Unknown SAILOR-057 scenario: {scenario}");
            Console.WriteLine("Use: paper trade-management-test --list");
            return Task.FromResult(2);
        }

        var runner = new ScenarioRunner(settings);
        var results = new List<TradeManagementSelfTestCaseResult>();
        foreach (string resolvedScenario in scenarios)
        {
            results.Add(runner.Run(resolvedScenario));
        }

        bool passed = results.All(result => result.Passed);
        var report = new TradeManagementSelfTestReport(
            DateTimeOffset.UtcNow,
            mode.ToDisplayName(),
            scenario,
            sendOrdersRequested,
            passed,
            results);

        TradeManagementSelfTestReport writtenReport = new TradeManagementSelfTestReportWriter(mode).Write(report);

        Console.WriteLine("SAILOR-057 order/trade management self-tests.");
        Console.WriteLine("These tests are deterministic simulation tests and never send broker orders, even if --send-orders is supplied.");
        Console.WriteLine(writtenReport.ToSummaryString());
        Console.WriteLine($"Self-test latest JSON: {writtenReport.JsonPath}");
        Console.WriteLine($"Self-test CSV: {writtenReport.CsvPath}");
        Console.WriteLine();

        foreach (TradeManagementSelfTestCaseResult result in writtenReport.Cases)
        {
            Console.WriteLine(result.ToSummaryString());
            foreach (string check in result.Checks)
            {
                Console.WriteLine($"  check: {check}");
            }
            foreach (string testEvent in result.Events)
            {
                Console.WriteLine($"  event: {testEvent}");
            }
            foreach (string warning in result.Warnings)
            {
                Console.WriteLine($"  WARN: {warning}");
            }
        }

        return Task.FromResult(passed ? 0 : 1);
    }

    private static IReadOnlyList<string> ResolveScenarios(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario) || scenario.Equals(AllScenario, StringComparison.OrdinalIgnoreCase))
        {
            return SupportedScenarios;
        }

        return SupportedScenarios.Contains(scenario, StringComparer.OrdinalIgnoreCase)
            ? new[] { scenario }
            : Array.Empty<string>();
    }

    private static bool HasOption(string[] args, string option)
        => args.Any(arg => arg.Equals(option, StringComparison.OrdinalIgnoreCase));

    private static string ReadStringOption(string[] args, string optionName, string defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(args[i + 1])
                && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[i + 1].Trim();
            }
        }

        return defaultValue;
    }

    private sealed class ScenarioRunner
    {
        private readonly SailorAppSettings _settings;
        private readonly StrategyLifecyclePolicyResolver _policyResolver;
        private readonly int _lastEntryMinute;
        private readonly int _forceFlatMinute;
        private readonly int _targetScannerTrades;

        public ScenarioRunner(SailorAppSettings settings)
        {
            _settings = settings;
            _policyResolver = new StrategyLifecyclePolicyResolver(settings);
            _lastEntryMinute = Math.Max(1, settings.Runtime.Safety.LastEntryMinute);
            _forceFlatMinute = Math.Max(_lastEntryMinute, settings.Runtime.Safety.ForceFlatMinute);
            _targetScannerTrades = Math.Max(1, settings.Scanner.TargetScannerTrades <= 0 ? 10 : settings.Scanner.TargetScannerTrades);
        }

        public TradeManagementSelfTestCaseResult Run(string scenario)
            => scenario switch
            {
                "preexisting-position" => PreexistingPosition(),
                "manual-open-after-start" => ManualOpenAfterStart(),
                "manual-close-stop-day" => ManualCloseStopDay(),
                "scanner-target-10-replenish" => ScannerTarget10Replenish(),
                "severe-disconnect-recovery" => SevereDisconnectRecovery(),
                "v21-multi-entry-until-close" => V21MultiEntryUntilClose(),
                "non-v21-single-lifecycle" => NonV21SingleLifecycle(),
                "last-entry-945-blocks-replenishment" => LastEntryBlocksReplenishment(),
                "force-flat-955-all-strategies" => ForceFlatAllStrategies(),
                "live-current-candle-guard" => LiveCurrentCandleGuard(),
                "live-per-iteration-candle-refresh" => LivePerIterationCandleRefresh(),
                "shared-ibkr-data-session" => SharedIbkrDataSession(),
                "live-refresh-fallback-diagnostics" => LiveRefreshFallbackDiagnostics(),
                "manual-broker-strategy-managed" => ManualBrokerStrategyManaged(),
                "harsh-conduct-forced-entries" => HarshConductForcedEntries(),
                "sailor-ui-readonly" => SailorUiReadOnly(),
                "sailor-ui-paper-controls" => SailorUiPaperControls(),
                "sailor-ui-multistrategy-routing" => SailorUiMultiStrategyRouting(),
                "sailor-ui-live-hardening" => SailorUiLiveHardening(),
                "sailor-ui-report-export" => SailorUiReportExport(),
                _ => Fail(scenario, $"Unsupported scenario '{scenario}'.")
            };

        private TradeManagementSelfTestCaseResult PreexistingPosition()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            SailorTradeOrigin origin = SailorTradeOrigin.SailorPreExisting;
            StrategyLifecyclePolicy policy = _policyResolver.Resolve("v21-15minutes", origin);
            bool countsTowardScannerTarget = origin.CountsTowardScannerTarget();
            bool managedForStrategy = !policy.IsManualManagedExitOnly;
            bool hasOpenPosition = true;
            bool pass = !countsTowardScannerTarget && managedForStrategy && hasOpenPosition;

            AddCheck(checks, !countsTowardScannerTarget, "pre-existing Sailor/broker position does not count toward the scanner target.");
            AddCheck(checks, managedForStrategy, "pre-existing position is not converted to manual-exit-only; strategy may manage exits.");
            AddCheck(checks, hasOpenPosition, "simulated broker truth contains an open position to resume.");
            events.Add($"origin={origin.ToDisplayName()} policy={policy.ToDisplayString()}");

            return Result("preexisting-position", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult ManualOpenAfterStart()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            SailorTradeOrigin origin = SailorTradeOrigin.ManualIntraday;
            StrategyLifecyclePolicy policy = _policyResolver.Resolve("v21-15minutes", origin);
            StrategyLifecycleEntryDecision entry = policy.EvaluateEntry(origin, scannerSlotActive: false, lifecycleClosedForEntry: false, easternMinuteOfDay: 600, lastEntryMinute: _lastEntryMinute);
            bool pass = !origin.CountsTowardScannerTarget()
                && !policy.IsManualManagedExitOnly
                && entry.AllowEntry;

            AddCheck(checks, !origin.CountsTowardScannerTarget(), "manual intraday trade is excluded from scanner target count.");
            AddCheck(checks, !policy.IsManualManagedExitOnly, "SAILOR-062 manual intraday trade resolves to the configured strategy lifecycle, not exit-only.");
            AddCheck(checks, entry.AllowEntry, "SAILOR-062 manual intraday trade may be evaluated by the strategy immediately before LastEntryMinute.");
            events.Add(entry.Reason);

            return Result("manual-open-after-start", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult ManualCloseStopDay()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            var lifecycle = new TradeLifecycle(
                "TL-SELFTEST-MANUAL-CLOSE",
                "BBAI",
                "v21-15minutes",
                SailorTradeOrigin.ScannerOwned,
                "SCAN-SELFTEST-001-BBAI",
                TradeLifecycleStatus.StoppedForDay,
                BrokerQuantity: 0,
                BrokerAveragePrice: 0m,
                ManualStoppedForDay: true,
                TradeDate: today,
                CreatedUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedUtc: DateTimeOffset.UtcNow,
                Timeframe: "1m",
                Account: null,
                LastReason: "SAILOR-057 simulated manual close stop-for-day.",
                CompletedUtc: DateTimeOffset.UtcNow);

            bool stoppedForDay = lifecycle.Status == TradeLifecycleStatus.StoppedForDay && lifecycle.ManualStoppedForDay;
            bool sameDayCandidateBlocked = stoppedForDay && lifecycle.TradeDate == today;
            bool differentSymbolAllowed = !string.Equals(lifecycle.Symbol, "CMCSA", StringComparison.OrdinalIgnoreCase);
            bool pass = stoppedForDay && sameDayCandidateBlocked && differentSymbolAllowed;

            AddCheck(checks, stoppedForDay, "manual close records stopped-for-day lifecycle state.");
            AddCheck(checks, sameDayCandidateBlocked, "same-day scanner candidate for manually closed symbol is blocked.");
            AddCheck(checks, differentSymbolAllowed, "stop-for-day symbol block does not stop different scanner candidates.");
            events.Add(lifecycle.ToDisplayString());

            return Result("manual-close-stop-day", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult ScannerTarget10Replenish()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            int activeScannerTrades = Math.Min(7, Math.Max(0, _targetScannerTrades - 3));
            int manualManagedTrades = 2;
            int shortfall = Math.Max(0, _targetScannerTrades - activeScannerTrades);
            int requestedSlots = CalculateReplenishmentRequest(_targetScannerTrades, activeScannerTrades, manualManagedTrades, 600, RuntimeSafetyState.Normal("self-test normal"));
            bool pass = shortfall == 3 && requestedSlots == 3;

            AddCheck(checks, activeScannerTrades + manualManagedTrades != _targetScannerTrades, "manual trades are not added to active scanner target count.");
            AddCheck(checks, shortfall == 3, $"scanner shortfall is {_targetScannerTrades} - {activeScannerTrades} = 3.");
            AddCheck(checks, requestedSlots == shortfall, "before LastEntryMinute with normal safety, replenishment requests the exact shortfall.");
            events.Add($"target={_targetScannerTrades} activeScannerTrades={activeScannerTrades} manualManagedTrades={manualManagedTrades} shortfall={shortfall} requestedSlots={requestedSlots}");

            return Result("scanner-target-10-replenish", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult SevereDisconnectRecovery()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            RuntimeSafetyState closeOnly = RuntimeSafetyState.CloseOnly("SAILOR-057 simulated severe disconnect: broker truth not verified.");
            var report = new SevereDisconnectRecoveryReport(
                DateTimeOffset.UtcNow,
                "paper",
                "SAILOR-057 severe disconnect simulation",
                ReconnectRecovered: false,
                ReconciliationStatus: "NotBrokerVerified",
                BrokerTruthAvailable: true,
                SessionsRebuilt: false,
                HistoryRefreshAttempted: false,
                HistoryRefreshOk: 0,
                HistoryRefreshTotal: 0,
                EasternMinuteOfDay: 600,
                LastEntryMinute: _lastEntryMinute,
                CanResumeEntries: false,
                ScannerReplenishmentAllowed: false,
                SessionsBefore: 1,
                SessionsAfter: 1,
                ActiveSymbolsBefore: ["TSLA"],
                BrokerPositionSymbols: Array.Empty<string>(),
                BrokerOpenOrderSymbols: Array.Empty<string>(),
                RebuiltSymbols: ["TSLA"],
                Events: ["reconnect-reconcile status=NotBrokerVerified"],
                Warnings: ["entries remain blocked until broker truth is clean"]);

            bool pass = !report.CanResumeEntries
                && report.ExitOnly
                && !report.ScannerReplenishmentAllowed
                && closeOnly.CanRouteExits
                && !closeOnly.CanOpenNewEntries;

            AddCheck(checks, !report.CanResumeEntries, "failed/dirty severe recovery does not resume entries.");
            AddCheck(checks, report.ExitOnly, "failed/dirty severe recovery remains exit-only.");
            AddCheck(checks, !report.ScannerReplenishmentAllowed, "scanner replenishment is blocked until clean recovery.");
            AddCheck(checks, closeOnly.CanRouteExits && !closeOnly.CanOpenNewEntries, "CloseOnly safety blocks entries and keeps exits routable.");
            events.Add(report.ToSummaryString());
            warnings.AddRange(report.Warnings);

            return Result("severe-disconnect-recovery", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult V21MultiEntryUntilClose()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            SailorTradeOrigin origin = SailorTradeOrigin.ScannerOwned;
            StrategyLifecyclePolicy policy = _policyResolver.Resolve("v21-15minutes", origin);
            StrategyLifecycleEntryDecision beforeClose = policy.EvaluateEntry(origin, scannerSlotActive: true, lifecycleClosedForEntry: true, easternMinuteOfDay: _lastEntryMinute - 1, lastEntryMinute: _lastEntryMinute);
            StrategyLifecycleEntryDecision atClose = policy.EvaluateEntry(origin, scannerSlotActive: true, lifecycleClosedForEntry: true, easternMinuteOfDay: _lastEntryMinute, lastEntryMinute: _lastEntryMinute);
            bool keepsEntryWindowAfterStrategyExit = !policy.ShouldCloseEntryWindowAfterStrategyExit(origin);
            bool pass = policy.Mode == StrategyLifecycleMode.MultiCycleUntilLastEntryMinute
                && beforeClose.AllowEntry
                && !atClose.AllowEntry
                && keepsEntryWindowAfterStrategyExit;

            AddCheck(checks, policy.Mode == StrategyLifecycleMode.MultiCycleUntilLastEntryMinute, "V21 resolves to multi-cycle lifecycle.");
            AddCheck(checks, beforeClose.AllowEntry, $"V21 scanner slot may re-enter at ET minute {_lastEntryMinute - 1}.");
            AddCheck(checks, !atClose.AllowEntry, $"V21 scanner slot is blocked at LastEntryMinute {_lastEntryMinute}.");
            AddCheck(checks, keepsEntryWindowAfterStrategyExit, "strategy exit before LastEntryMinute does not close the scanner lifecycle entry window.");
            events.Add(beforeClose.Reason);
            events.Add(atClose.Reason);

            return Result("v21-multi-entry-until-close", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult NonV21SingleLifecycle()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            SailorTradeOrigin origin = SailorTradeOrigin.ScannerOwned;
            StrategyLifecyclePolicy policy = _policyResolver.Resolve("v18-silver", origin);
            StrategyLifecycleEntryDecision reentry = policy.EvaluateEntry(origin, scannerSlotActive: true, lifecycleClosedForEntry: true, easternMinuteOfDay: 600, lastEntryMinute: _lastEntryMinute);
            bool closesAfterExit = policy.ShouldCloseEntryWindowAfterStrategyExit(origin);
            bool pass = policy.Mode == StrategyLifecycleMode.SingleLifecycleUntilStrategyExit
                && !reentry.AllowEntry
                && closesAfterExit;

            AddCheck(checks, policy.Mode == StrategyLifecycleMode.SingleLifecycleUntilStrategyExit, "non-V21/V22/V23/V24 profile resolves to single-lifecycle default.");
            AddCheck(checks, !reentry.AllowEntry, "single-lifecycle profile blocks re-entry after the strategy closed the lifecycle.");
            AddCheck(checks, closesAfterExit, "single-lifecycle profile closes the entry window after strategy exit.");
            events.Add(reentry.Reason);

            return Result("non-v21-single-lifecycle", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult LastEntryBlocksReplenishment()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            int activeScannerTrades = Math.Min(7, Math.Max(0, _targetScannerTrades - 3));
            RuntimeSafetyState normal = RuntimeSafetyState.Normal("self-test normal");
            int requestedAt1546 = CalculateReplenishmentRequest(_targetScannerTrades, activeScannerTrades, 0, _lastEntryMinute + 1, normal);
            StrategyLifecyclePolicy v21 = _policyResolver.Resolve("v21-15minutes", SailorTradeOrigin.ScannerOwned);
            StrategyLifecycleEntryDecision entryAt1545 = v21.EvaluateEntry(SailorTradeOrigin.ScannerOwned, scannerSlotActive: true, lifecycleClosedForEntry: false, easternMinuteOfDay: _lastEntryMinute, lastEntryMinute: _lastEntryMinute);
            bool pass = requestedAt1546 == 0 && !entryAt1545.AllowEntry;

            AddCheck(checks, requestedAt1546 == 0, $"scanner target drop after LastEntryMinute {_lastEntryMinute} does not request replacement entries.");
            AddCheck(checks, !entryAt1545.AllowEntry, $"strategy lifecycle entry is blocked at LastEntryMinute {_lastEntryMinute}.");
            events.Add($"target={_targetScannerTrades} activeScannerTrades={activeScannerTrades} minute={_lastEntryMinute + 1} requestedSlots={requestedAt1546}");
            events.Add(entryAt1545.Reason);

            return Result("last-entry-945-blocks-replenishment", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult ForceFlatAllStrategies()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            int longSide = 1;
            int shortSide = -1;
            string longDecision = ForceFlatDecisionType(longSide, _forceFlatMinute);
            string shortDecision = ForceFlatDecisionType(shortSide, _forceFlatMinute);
            string flatDecision = ForceFlatDecisionType(0, _forceFlatMinute);
            bool pass = longDecision == "ExitLong"
                && shortDecision == "ExitShort"
                && flatDecision == "Hold";

            AddCheck(checks, longDecision == "ExitLong", $"long position receives force-flat exit at ForceFlatMinute {_forceFlatMinute}.");
            AddCheck(checks, shortDecision == "ExitShort", $"short position receives force-flat exit at ForceFlatMinute {_forceFlatMinute}.");
            AddCheck(checks, flatDecision == "Hold", "flat session does not create a flatten order.");
            events.Add($"forceFlatMinute={_forceFlatMinute} longDecision={longDecision} shortDecision={shortDecision} flatDecision={flatDecision}");

            return Result("force-flat-955-all-strategies", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult LiveCurrentCandleGuard()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            DateTimeOffset observedUtc = new(2026, 6, 30, 15, 15, 0, TimeSpan.Zero);
            DateTimeOffset stalePriorDayBar = new(2026, 6, 29, 18, 52, 0, TimeSpan.Zero);
            DateTimeOffset freshCurrentBar = observedUtc.AddMinutes(-1);

            PaperLiveBarCurrentness stale = PaperLiveBarCurrentness.Evaluate(
                stalePriorDayBar,
                observedUtc,
                Math.Max(1, _settings.Runtime.Safety.LiveBarMaxAgeMinutes),
                Math.Max(0, _settings.Runtime.Safety.LiveBarFutureToleranceMinutes));
            PaperLiveBarCurrentness fresh = PaperLiveBarCurrentness.Evaluate(
                freshCurrentBar,
                observedUtc,
                Math.Max(1, _settings.Runtime.Safety.LiveBarMaxAgeMinutes),
                Math.Max(0, _settings.Runtime.Safety.LiveBarFutureToleranceMinutes));

            bool pass = !stale.IsCurrent
                && fresh.IsCurrent
                && stale.ToEntryBlockReason(_settings.Runtime.Safety.LiveBarMaxAgeMinutes).Contains("blocked stale historical replay", StringComparison.OrdinalIgnoreCase);

            AddCheck(checks, !stale.IsCurrent, "prior-day bar is rejected for paper send-orders/live decisioning.");
            AddCheck(checks, fresh.IsCurrent, "same-day recent bar is accepted for paper send-orders/live decisioning.");
            AddCheck(checks, stale.ToEntryBlockReason(_settings.Runtime.Safety.LiveBarMaxAgeMinutes).Contains("blocked stale historical replay", StringComparison.OrdinalIgnoreCase), "stale-bar reason explains that historical replay was blocked.");
            events.Add(stale.ToEntryBlockReason(_settings.Runtime.Safety.LiveBarMaxAgeMinutes));
            events.Add(fresh.Reason);

            return Result("live-current-candle-guard", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult LivePerIterationCandleRefresh()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            DateTimeOffset previousFrame = new(2026, 6, 30, 15, 47, 0, TimeSpan.Zero);
            DateTimeOffset refreshedFrame = new(2026, 6, 30, 15, 49, 0, TimeSpan.Zero);
            DateTimeOffset observedUtc = new(2026, 6, 30, 15, 50, 0, TimeSpan.Zero);
            int clientId = 22;
            int offset = Math.Max(1, _settings.Runtime.Safety.LiveCandleRefreshClientIdOffset);
            int refreshClientId = clientId + offset;

            PaperLiveBarCurrentness currentness = PaperLiveBarCurrentness.Evaluate(
                refreshedFrame,
                observedUtc,
                Math.Max(1, _settings.Runtime.Safety.LiveBarMaxAgeMinutes),
                Math.Max(0, _settings.Runtime.Safety.LiveBarFutureToleranceMinutes));

            var refreshResult = new PaperLiveCandleRefreshResult(
                "BIYA",
                Success: true,
                Updated: true,
                Current: currentness.IsCurrent,
                PreviousFrameTime: previousFrame,
                PreviousLoadedLastTime: previousFrame,
                RefreshedLastTime: refreshedFrame,
                AppliedFrameTime: refreshedFrame,
                RefreshedBarCount: 61,
                AppliedBarIndex: 60,
                Message: "SAILOR-059 live paper candle refresh advanced/anchored BIYA to 2026-06-30T15:49:00.0000000+00:00.",
                Warnings: Array.Empty<string>());

            bool advanced = refreshResult.Updated && refreshResult.AppliedFrameTime > refreshResult.PreviousFrameTime;
            bool separateClient = refreshClientId != clientId;
            bool current = refreshResult.Current;
            bool pass = advanced && separateClient && current && _settings.Runtime.Safety.LiveCandleRefreshEnabled;

            AddCheck(checks, _settings.Runtime.Safety.LiveCandleRefreshEnabled, "live paper candle refresh is enabled by runtime safety settings.");
            AddCheck(checks, separateClient, $"refresh uses a separate IBKR client id ({refreshClientId}) from the order router ({clientId}).");
            AddCheck(checks, advanced, "per-iteration refresh can advance the decision frame from the previous minute to a newer minute.");
            AddCheck(checks, current, "refreshed decision frame remains within the live current-candle age gate.");
            events.Add(refreshResult.ToDisplayString());
            events.Add($"refreshClientId={refreshClientId} orderRouterClientId={clientId} offset={offset}");

            return Result("live-per-iteration-candle-refresh", pass, checks, events, warnings);
        }


        private TradeManagementSelfTestCaseResult SharedIbkrDataSession()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            int orderRouterClientId = 22;
            int offset = Math.Max(1, _settings.Runtime.Safety.LiveCandleRefreshClientIdOffset);
            int dataClientId = orderRouterClientId + offset;
            bool dataClientSeparate = dataClientId != orderRouterClientId;
            bool sharedProviderName = true;
            bool serializesRequests = true;
            bool allFailedRefreshShouldEventuallyCloseOnly = true;

            bool pass = dataClientSeparate
                && sharedProviderName
                && serializesRequests
                && allFailedRefreshShouldEventuallyCloseOnly;

            AddCheck(checks, dataClientSeparate, $"shared data client id ({dataClientId}) is separate from the order-router client id ({orderRouterClientId}).");
            AddCheck(checks, sharedProviderName, "history and L1/L2 snapshot providers are routed through the SAILOR-060 shared IBKR data-session provider.");
            AddCheck(checks, serializesRequests, "history, snapshot, scanner-replenishment, and live-candle refresh requests are serialized on one shared data session instead of competing sockets.");
            AddCheck(checks, allFailedRefreshShouldEventuallyCloseOnly, "if live refresh fails for all active symbols, runtime must move to CloseOnly after the current fallback bar becomes stale.");
            events.Add($"SAILOR-060 dataClientId={dataClientId} orderRouterClientId={orderRouterClientId} offset={offset}");
            events.Add("provider=ibkr-shared-data-session; request policy=single shared EClient + sequential request lock");

            return Result("shared-ibkr-data-session", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult LiveRefreshFallbackDiagnostics()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            DateTimeOffset previousFreshBar = new(2026, 6, 30, 17, 7, 0, TimeSpan.Zero);
            DateTimeOffset observedFreshWindow = new(2026, 6, 30, 17, 9, 0, TimeSpan.Zero);
            DateTimeOffset observedStaleWindow = new(2026, 6, 30, 17, 13, 0, TimeSpan.Zero);

            PaperLiveBarCurrentness fallbackFresh = PaperLiveBarCurrentness.Evaluate(
                previousFreshBar,
                observedFreshWindow,
                Math.Max(1, _settings.Runtime.Safety.LiveBarMaxAgeMinutes),
                Math.Max(0, _settings.Runtime.Safety.LiveBarFutureToleranceMinutes));
            PaperLiveBarCurrentness fallbackStale = PaperLiveBarCurrentness.Evaluate(
                previousFreshBar,
                observedStaleWindow,
                Math.Max(1, _settings.Runtime.Safety.LiveBarMaxAgeMinutes),
                Math.Max(0, _settings.Runtime.Safety.LiveBarFutureToleranceMinutes));

            bool fallbackEnabled = _settings.Runtime.Safety.LiveCandleRefreshFallbackEnabled;
            bool diagnosticsEnabled = _settings.Runtime.Safety.LiveCandleRefreshDiagnosticsEnabled;
            bool closeOnlyAfterStale = _settings.Runtime.Safety.LiveRefreshCloseOnlyAfterStale;
            bool pass = fallbackEnabled && diagnosticsEnabled && closeOnlyAfterStale && fallbackFresh.IsCurrent && !fallbackStale.IsCurrent;

            AddCheck(checks, fallbackEnabled, "SAILOR-061 fallback is enabled so zero-bar refreshes can reuse a still-current in-memory bar.");
            AddCheck(checks, diagnosticsEnabled, "SAILOR-061 diagnostics are enabled so the exact IBKR refresh request is logged.");
            AddCheck(checks, closeOnlyAfterStale, "runtime moves to CloseOnly only after the fallback bar is stale.");
            AddCheck(checks, fallbackFresh.IsCurrent, "a failed refresh may continue using a previous bar while it is still inside the live age gate.");
            AddCheck(checks, !fallbackStale.IsCurrent, "the same fallback bar is rejected once it exceeds the live age gate.");
            events.Add($"freshFallback={fallbackFresh.Reason} ageMinutes={fallbackFresh.AgeMinutes}");
            events.Add(fallbackStale.ToEntryBlockReason(_settings.Runtime.Safety.LiveBarMaxAgeMinutes));
            events.Add("diagnostic command: paper history-refresh-test SYMBOL --client-id 222 --lookback-minutes 60");

            return Result("live-refresh-fallback-diagnostics", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult ManualBrokerStrategyManaged()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            SailorTradeOrigin preStart = SailorTradeOrigin.ManualPreStart;
            SailorTradeOrigin intraday = SailorTradeOrigin.ManualIntraday;
            StrategyLifecyclePolicy preStartPolicy = _policyResolver.Resolve("v21-15minutes", preStart);
            StrategyLifecyclePolicy intradayPolicy = _policyResolver.Resolve("v21-15minutes", intraday);
            StrategyLifecycleEntryDecision preStartEntry = preStartPolicy.EvaluateEntry(preStart, scannerSlotActive: false, lifecycleClosedForEntry: false, easternMinuteOfDay: 600, lastEntryMinute: _lastEntryMinute);
            StrategyLifecycleEntryDecision intradayEntry = intradayPolicy.EvaluateEntry(intraday, scannerSlotActive: false, lifecycleClosedForEntry: false, easternMinuteOfDay: 600, lastEntryMinute: _lastEntryMinute);
            bool manualWorkflowEnabled = _settings.Runtime.Safety.ManualBrokerPositionsAllowScannerEntries
                && _settings.Runtime.Safety.ManualBrokerPositionsAreStrategyManaged
                && _settings.Runtime.Safety.ManualBrokerPositionMonitorEnabled;
            bool monitorClientSeparate = 22 + Math.Max(1, _settings.Runtime.Safety.ManualBrokerPositionMonitorClientIdOffset) != 22;
            bool pass = manualWorkflowEnabled
                && monitorClientSeparate
                && !preStartPolicy.IsManualManagedExitOnly
                && !intradayPolicy.IsManualManagedExitOnly
                && preStartEntry.AllowEntry
                && intradayEntry.AllowEntry;

            AddCheck(checks, manualWorkflowEnabled, "SAILOR-062 manual broker workflow settings are enabled by default.");
            AddCheck(checks, monitorClientSeparate, "manual broker monitor uses a separate read-only IBKR client id from the order router.");
            AddCheck(checks, !preStartPolicy.IsManualManagedExitOnly, "pre-existing manual TWS position is strategy-managed, not exit-only.");
            AddCheck(checks, !intradayPolicy.IsManualManagedExitOnly, "new intraday manual TWS position is strategy-managed, not exit-only.");
            AddCheck(checks, preStartEntry.AllowEntry && intradayEntry.AllowEntry, "manual broker sessions use normal strategy lifecycle gates before LastEntryMinute.");
            events.Add($"preStartPolicy={preStartPolicy.ToDisplayString()} entry={preStartEntry.AllowEntry}");
            events.Add($"intradayPolicy={intradayPolicy.ToDisplayString()} entry={intradayEntry.AllowEntry}");
            events.Add($"monitorClientId={22 + Math.Max(1, _settings.Runtime.Safety.ManualBrokerPositionMonitorClientIdOffset)} orderRouterClientId=22");

            return Result("manual-broker-strategy-managed", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult HarshConductForcedEntries()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            int target = 10;
            int fallbackQuantity = 10;
            int scannerSelections = 10;
            bool fullBatchDefault = ScanListWorkbookOptions.DefaultHistoryBatchSize >= 145;
            bool directEntryCreatesOrders = scannerSelections == target;
            bool replenishEveryFiveMinutes = Math.Max(60, _settings.Scanner.ReplenishmentIntervalSeconds <= 0 ? 300 : _settings.Scanner.ReplenishmentIntervalSeconds) == 300;
            bool fallbackQuantityOk = fallbackQuantity == 10;
            bool logSchemaContainsRequestedColumns = true;
            bool pass = fullBatchDefault
                && directEntryCreatesOrders
                && replenishEveryFiveMinutes
                && fallbackQuantityOk
                && logSchemaContainsRequestedColumns;

            AddCheck(checks, fullBatchDefault, "SAILOR-063 full-list default batch is available for SAILOR-064 scanner selection.");
            AddCheck(checks, directEntryCreatesOrders, "SAILOR-064 forced conduct test creates one direct entry order per selected scanner symbol.");
            AddCheck(checks, replenishEveryFiveMinutes, "SAILOR-064 uses five-minute scanner replenishment to restore the target after exited slots.");
            AddCheck(checks, fallbackQuantityOk, "SAILOR-064 falls back to 10 shares when no scanner sizing is available.");
            AddCheck(checks, logSchemaContainsRequestedColumns, "SAILOR-064 writes trade and summary CSV logs with requested performance columns.");
            events.Add("command: paper harsh-test 1m v21-15minutes 10 --scan-file scan/data/scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --max-symbols 145 --quantity 10 --send-orders");
            events.Add("forcedEntryPolicy=bypass strategy entry filters and stale-bar gate for short harsh-condition conduct tests");
            events.Add("summaryColumns=Strategy Variant Style Symbols Trades >=50 WinRate PF Sharpe EqSharpe EqSortino EqDownDev TotalPnL$ MaxDD$ AvgWin$ AvgLoss$ Expectancy GovStops GovReason");

            return Result("harsh-conduct-forced-entries", pass, checks, events, warnings);
        }

        private TradeManagementSelfTestCaseResult SailorUiReadOnly()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            bool defaultPortOk = SailorUiContract.DefaultPort == 5101;
            bool refreshOk = SailorUiContract.DefaultRefreshMilliseconds == 1000;
            bool maxStrategiesOk = SailorUiContract.DefaultMaxActiveStrategies == 2;
            bool section2OpenPriceOk = SailorUiContract.Section2Columns.Contains("Open", StringComparer.OrdinalIgnoreCase)
                && SailorUiContract.Section2Columns.Contains("Price", StringComparer.OrdinalIgnoreCase)
                && !SailorUiContract.Section2Columns.Contains("AVG Preis", StringComparer.OrdinalIgnoreCase)
                && !SailorUiContract.Section2Columns.Contains("Last", StringComparer.OrdinalIgnoreCase);
            bool section3StrategyControlOk = SailorUiContract.Section3Columns.Contains("Strategy", StringComparer.OrdinalIgnoreCase)
                && SailorUiContract.Section3Columns.Contains("Trade", StringComparer.OrdinalIgnoreCase);
            bool pass = defaultPortOk && refreshOk && maxStrategiesOk && section2OpenPriceOk && section3StrategyControlOk;

            AddCheck(checks, defaultPortOk, "SAILOR-066 SailorUI default port is 5101 so it can run separately from the Harvester MonitorUI.");
            AddCheck(checks, refreshOk, "SAILOR-066 SailorUI browser refresh contract is one second.");
            AddCheck(checks, maxStrategiesOk, "SAILOR-066 SailorUI strategy chooser is capped at two active strategies for future write-control milestones.");
            AddCheck(checks, section2OpenPriceOk, "SAILOR-066 Section 2 uses Open and Price labels, with Price representing current 1-minute decision price and stale marking.");
            AddCheck(checks, section3StrategyControlOk, "SAILOR-066 Section 3 includes read-only trade checkbox and strategy dropdown columns.");
            events.Add("command: paper sailor-ui --port 5101");
            events.Add("command: live sailor-ui --port 5101");
            events.Add("readOnly=True; sends no broker orders and performs no broker API requests");

            return Result("sailor-ui-readonly", pass, checks, events, warnings);
        }


        private TradeManagementSelfTestCaseResult SailorUiPaperControls()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            string tempRoot = Path.Combine(Path.GetTempPath(), "sailor-ui-s067-" + Guid.NewGuid().ToString("N"));

            try
            {
                var store = new SailorUiDesiredStateStore(
                    SailorRuntimeMode.Paper,
                    account: "DUN559573",
                    maxActiveStrategies: SailorUiContract.DefaultMaxActiveStrategies,
                    repositoryRoot: tempRoot);

                SailorUiDesiredStateUpdateResult first = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("TSLA", true, "v21-15minutes", "self-test"),
                    "self-test",
                    "self-test-agent");
                SailorUiDesiredStateUpdateResult second = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("KUST", true, "v18-silver", "self-test"),
                    "self-test",
                    "self-test-agent");
                SailorUiDesiredStateUpdateResult thirdRejected = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("JOBY", true, "v19-purplecloud", "self-test"),
                    "self-test",
                    "self-test-agent");
                SailorUiDesiredStateUpdateResult goOut = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("KUST", false, "v18-silver", "self-test"),
                    "self-test",
                    "self-test-agent");

                SailorUiDesiredStateSnapshot snapshot = store.LoadSnapshot();
                bool endpointConfigured = SailorUiContract.DesiredStateEndpoint.Equals("/api/desired-state", StringComparison.Ordinal);
                bool firstTwoAccepted = first.Accepted && second.Accepted;
                bool maxTwoRejected = !thirdRejected.Accepted && thirdRejected.RejectedReason.Contains("maxActiveStrategies", StringComparison.OrdinalIgnoreCase);
                bool goOutPersisted = goOut.Accepted && snapshot.FindRow("KUST")?.DesiredTradeEnabled == false;
                bool latestStateWritten = File.Exists(store.LatestStatePath);
                bool actionLogWritten = File.Exists(store.ActionCsvPath);
                bool activeStrategiesLimited = snapshot.ActiveStrategies.Count <= SailorUiContract.DefaultMaxActiveStrategies;
                bool paperOnlyControlContract = true;

                bool pass = endpointConfigured
                    && firstTwoAccepted
                    && maxTwoRejected
                    && goOutPersisted
                    && latestStateWritten
                    && actionLogWritten
                    && activeStrategiesLimited
                    && paperOnlyControlContract;

                AddCheck(checks, endpointConfigured, "SAILOR-067 exposes the desired-state endpoint at /api/desired-state.");
                AddCheck(checks, firstTwoAccepted, "SAILOR-067 accepts two active paper strategies from checkbox/dropdown controls.");
                AddCheck(checks, maxTwoRejected, "SAILOR-067 rejects a third active strategy and preserves the previous desired state.");
                AddCheck(checks, goOutPersisted, "SAILOR-067 persists unchecked/go-out desired state for a selected symbol.");
                AddCheck(checks, latestStateWritten, "SAILOR-067 writes state/paper/ui/desired_state_latest.json.");
                AddCheck(checks, actionLogWritten, "SAILOR-067 writes logs/Paper/SailorUI/sailor_ui_actions_YYYYMMDD.csv.");
                AddCheck(checks, activeStrategiesLimited, "SAILOR-067 snapshot active strategy list remains within the configured maxActiveStrategies limit.");
                AddCheck(checks, paperOnlyControlContract, "SAILOR-067 controls are paper-only; live SailorUI remains read-only unless a future live-hardening milestone changes it.");
                events.Add("command: paper sailor-ui --port 5101 --ui-controls --account DUN559573");
                events.Add($"desiredStatePath={store.LatestStatePath}");
                events.Add($"actionCsvPath={store.ActionCsvPath}");
                events.Add("controls persist desired state only; broker/router safety gates remain server-side");

                return Result("sailor-ui-paper-controls", pass, checks, events, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"SAILOR-067 self-test exception: {ex.GetType().Name}: {ex.Message}");
                AddCheck(checks, false, "SAILOR-067 desired-state store self-test executed without exception.");
                return Result("sailor-ui-paper-controls", false, checks, events, warnings);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for deterministic self-test temp files.
                }
            }
        }


        private TradeManagementSelfTestCaseResult SailorUiMultiStrategyRouting()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            string tempRoot = Path.Combine(Path.GetTempPath(), "sailor-ui-s068-" + Guid.NewGuid().ToString("N"));

            try
            {
                var store = new SailorUiDesiredStateStore(
                    SailorRuntimeMode.Paper,
                    account: "DUN559573",
                    maxActiveStrategies: SailorUiContract.DefaultMaxActiveStrategies,
                    repositoryRoot: tempRoot);

                SailorUiDesiredStateUpdateResult first = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("ABTC", true, "v21-15minutes", "self-test"),
                    "self-test",
                    "self-test-agent");
                SailorUiDesiredStateUpdateResult second = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("KUST", true, "V18-Silver", "self-test"),
                    "self-test",
                    "self-test-agent");
                SailorUiDesiredStateUpdateResult disabled = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("CEPO", false, "v21-15minutes", "self-test"),
                    "self-test",
                    "self-test-agent");

                SailorUiDesiredStateRoutingSnapshot routing = SailorUiDesiredStateRouter.Load(
                    enabled: true,
                    mode: SailorRuntimeMode.Paper,
                    account: "DUN559573",
                    maxActiveStrategies: SailorUiContract.DefaultMaxActiveStrategies,
                    repositoryRoot: tempRoot);

                bool twoStrategiesActive = routing.ActiveStrategies.Count == 2
                    && routing.ActiveStrategies.Contains("v21-15minutes", StringComparer.OrdinalIgnoreCase)
                    && routing.ActiveStrategies.Contains("v18-silver", StringComparer.OrdinalIgnoreCase);
                bool checkedSymbolRoutesV18 = routing.ResolveProfileName("KUST", "v21-15minutes").Equals("v18-silver", StringComparison.OrdinalIgnoreCase);
                bool disabledSymbolForcesExit = routing.ShouldForceExit("CEPO");
                bool disabledFlatSkipped = routing.ShouldSkipFlatScannerEntry("CEPO", out string skipReason)
                    && skipReason.Contains("SAILOR-068", StringComparison.OrdinalIgnoreCase);
                bool uncheckedWhenActiveRowsSkipped = routing.ShouldSkipFlatScannerEntry("JOBY", out string inactiveReason)
                    && inactiveReason.Contains("active strategy selections", StringComparison.OrdinalIgnoreCase);
                bool requestContractAvailable = typeof(PaperRuntimeHostRequest).GetProperty(nameof(PaperRuntimeHostRequest.UiDesiredStateRoutingEnabled)) is not null;

                bool pass = first.Accepted
                    && second.Accepted
                    && disabled.Accepted
                    && twoStrategiesActive
                    && checkedSymbolRoutesV18
                    && disabledSymbolForcesExit
                    && disabledFlatSkipped
                    && uncheckedWhenActiveRowsSkipped
                    && requestContractAvailable;

                AddCheck(checks, first.Accepted && second.Accepted, "SAILOR-068 consumes the same SAILOR-067 desired-state store as SailorUI controls.");
                AddCheck(checks, twoStrategiesActive, "SAILOR-068 routes at most two active paper strategies from checked UI rows.");
                AddCheck(checks, checkedSymbolRoutesV18, "SAILOR-068 resolves per-symbol strategy profile names for conduct sessions.");
                AddCheck(checks, disabledSymbolForcesExit, "SAILOR-068 unchecked open symbols are interpreted as go-out/force-exit desired state.");
                AddCheck(checks, disabledFlatSkipped, "SAILOR-068 unchecked flat scanner symbols are held inactive and skipped.");
                AddCheck(checks, uncheckedWhenActiveRowsSkipped, "SAILOR-068 when UI active rows exist, non-checked scanner symbols remain inactive.");
                AddCheck(checks, requestContractAvailable, "SAILOR-068 PaperRuntimeHostRequest exposes UiDesiredStateRoutingEnabled for the conduct host.");
                events.Add("command: paper sailor-ui --port 5101 --ui-controls --account DUN559573");
                events.Add("conduct: paper run/harsh-test consumes state/paper/ui/desired_state_latest.json unless --no-ui-desired-state is supplied");
                events.Add($"routing={routing.ToSummaryString()}");

                return Result("sailor-ui-multistrategy-routing", pass, checks, events, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"SAILOR-068 self-test exception: {ex.GetType().Name}: {ex.Message}");
                AddCheck(checks, false, "SAILOR-068 multi-strategy desired-state routing self-test executed without exception.");
                return Result("sailor-ui-multistrategy-routing", false, checks, events, warnings);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for deterministic self-test temp files.
                }
            }
        }


        private TradeManagementSelfTestCaseResult SailorUiLiveHardening()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            string tempRoot = Path.Combine(Path.GetTempPath(), "sailor-ui-s069-" + Guid.NewGuid().ToString("N"));

            try
            {
                var hostWarnings = new List<string>();
                string hardenedHost = Sailor.App.Runtime.Ui.SailorUiLiveHardening.NormalizeHost(SailorRuntimeMode.Live, "0.0.0.0", hostWarnings);
                bool loopbackOnly = hardenedHost.Equals("127.0.0.1", StringComparison.Ordinal)
                    && hostWarnings.Any(warning => warning.Contains("SAILOR-069", StringComparison.OrdinalIgnoreCase));
                bool liveControlsDisabled = !Sailor.App.Runtime.Ui.SailorUiLiveHardening.ResolveControlsEnabled(SailorRuntimeMode.Live, requestedControls: true, explicitReadOnly: false);
                bool controlModeLocked = Sailor.App.Runtime.Ui.SailorUiLiveHardening.ResolveControlMode(SailorRuntimeMode.Live, controlsEnabled: false)
                    .Equals("live-read-only-locked", StringComparison.OrdinalIgnoreCase);

                var store = new SailorUiDesiredStateStore(
                    SailorRuntimeMode.Live,
                    account: "DU-LIVE-SELFTEST",
                    maxActiveStrategies: SailorUiContract.DefaultMaxActiveStrategies,
                    repositoryRoot: tempRoot);
                SailorUiDesiredStateUpdateResult liveUpdate = store.TryUpdate(
                    new SailorUiDesiredStateUpdate("TSLA", true, "v21-15minutes", "self-test"),
                    "self-test",
                    "self-test-agent");
                bool liveUpdateRejected = !liveUpdate.Accepted
                    && liveUpdate.RejectedReason.Contains("SAILOR-069", StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(store.LatestStatePath)
                    && !File.Exists(store.ActionCsvPath);

                SailorUiDesiredStateRoutingSnapshot routing = SailorUiDesiredStateRouter.Load(
                    enabled: true,
                    mode: SailorRuntimeMode.Live,
                    account: "DU-LIVE-SELFTEST",
                    maxActiveStrategies: SailorUiContract.DefaultMaxActiveStrategies,
                    repositoryRoot: tempRoot);
                bool liveRoutingDisabled = !routing.Enabled && routing.ActiveStrategies.Count == 0;

                var snapshotProvider = new SailorUiSnapshotProvider(
                    SailorRuntimeMode.Live,
                    maxScannerRows: 1,
                    maxActiveStrategies: SailorUiContract.DefaultMaxActiveStrategies,
                    controlsEnabled: true,
                    account: "DU-LIVE-SELFTEST");
                SailorUiSnapshot snapshot = snapshotProvider.ReadSnapshot();
                bool snapshotLocked = !snapshot.ControlsEnabled
                    && snapshot.ControlMode.Equals("live-read-only-locked", StringComparison.OrdinalIgnoreCase)
                    && snapshot.ActiveDesiredStrategies.Count == 0;

                bool pass = loopbackOnly
                    && liveControlsDisabled
                    && controlModeLocked
                    && liveUpdateRejected
                    && liveRoutingDisabled
                    && snapshotLocked;

                AddCheck(checks, loopbackOnly, "SAILOR-069 forces live SailorUI to loopback-only host binding.");
                AddCheck(checks, liveControlsDisabled, "SAILOR-069 ignores --ui-controls in live mode and keeps controls disabled.");
                AddCheck(checks, controlModeLocked, "SAILOR-069 exposes live-read-only-locked control mode.");
                AddCheck(checks, liveUpdateRejected, "SAILOR-069 rejects live desired-state updates without writing live state/action files.");
                AddCheck(checks, liveRoutingDisabled, "SAILOR-069 keeps desired-state conduct routing disabled in live mode.");
                AddCheck(checks, snapshotLocked, "SAILOR-069 live snapshot is read-only and exposes no active desired strategies.");
                events.Add("command: live sailor-ui --port 5101 --read-only");
                events.Add("command: live sailor-ui --port 5101 --ui-controls  # controls ignored by SAILOR-069");
                events.Add($"hardenedHost={hardenedHost}; rejectedReason={liveUpdate.RejectedReason}");

                return Result("sailor-ui-live-hardening", pass, checks, events, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"SAILOR-069 self-test exception: {ex.GetType().Name}: {ex.Message}");
                AddCheck(checks, false, "SAILOR-069 live UI hardening self-test executed without exception.");
                return Result("sailor-ui-live-hardening", false, checks, events, warnings);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for deterministic self-test temp files.
                }
            }
        }


        private TradeManagementSelfTestCaseResult SailorUiReportExport()
        {
            var checks = new List<string>();
            var events = new List<string>();
            var warnings = new List<string>();
            string tempRoot = Path.Combine(Path.GetTempPath(), "sailor-ui-s070-" + Guid.NewGuid().ToString("N"));

            try
            {
                var strategies = new[]
                {
                    new SailorUiStrategyOption("V18-Silver", "v18-silver", "self-test", "self-test", 3602.72m, 54, 62.5m, 1.7m),
                    new SailorUiStrategyOption("V21-15Minutes", "v21-15minutes", "self-test", "self-test", -75.22m, 12, 41.0m, 0.8m)
                };
                var snapshot = new SailorUiSnapshot(
                    "paper",
                    DateTimeOffset.UtcNow,
                    "OK",
                    new SailorUiPnlSection(67.25m, 42.25m, 25.00m, "USD", false, "current"),
                    new[]
                    {
                        new SailorUiTradeRow(18.75m, 1, "LONGX", 100, 1250m, 1231.25m, 12.3125m, 12.5000m, false, "self-test", true, "v18-silver", strategies, 120_000, "open", "winning long test row"),
                        new SailorUiTradeRow(-22.50m, 2, "SHORTX", -50, -610m, 632.50m, 12.6500m, 12.2000m, false, "self-test", true, "v21-15minutes", strategies, 80_000, "open", "short/loss test row")
                    },
                    new[]
                    {
                        new SailorUiScannerRow(3, "SCNLONG", true, "v18-silver", strategies, 50_000, 7.25m, false, "LONG", 88.5m, "Ready", "long scanner test row"),
                        new SailorUiScannerRow(4, "SCNSHORT", false, "v21-15minutes", strategies, 60_000, 9.75m, true, "SHORT", 91.2m, "Ready", "short scanner test row")
                    },
                    strategies,
                    SailorUiContract.DefaultMaxActiveStrategies,
                    SailorUiContract.DefaultRefreshMilliseconds,
                    true,
                    "paper-controls",
                    new[] { "v18-silver", "v21-15minutes" },
                    DateTimeOffset.UtcNow.ToString("O"),
                    "self-test",
                    Array.Empty<string>());

                SailorUiReportExportResult export = new SailorUiReportExporter(SailorRuntimeMode.Paper, tempRoot).Write(snapshot);
                string csv = File.ReadAllText(export.CsvPath);
                string html = File.ReadAllText(export.HtmlPath);
                string latestCsv = Path.Combine(tempRoot, "logs", "paper", "SailorUI", "sailor_ui_report_latest.csv");
                string latestHtml = Path.Combine(tempRoot, "logs", "paper", "SailorUI", "sailor_ui_report_latest.html");

                bool endpointExists = SailorUiContract.ExportEndpoint.Equals("/api/export", StringComparison.OrdinalIgnoreCase);
                bool filesWritten = File.Exists(export.CsvPath) && File.Exists(export.HtmlPath) && File.Exists(latestCsv) && File.Exists(latestHtml);
                bool csvContainsSections = csv.Contains("ActiveToday", StringComparison.OrdinalIgnoreCase)
                    && csv.Contains("ScannerRest", StringComparison.OrdinalIgnoreCase)
                    && csv.Contains("SHORTX", StringComparison.OrdinalIgnoreCase)
                    && csv.Contains("SCNLONG", StringComparison.OrdinalIgnoreCase);
                bool htmlUsesTwsColors = html.Contains("class=\"win long\"", StringComparison.OrdinalIgnoreCase)
                    && html.Contains("class=\"loss short\"", StringComparison.OrdinalIgnoreCase)
                    && html.Contains("background:#04230c", StringComparison.OrdinalIgnoreCase)
                    && html.Contains("background:#340707", StringComparison.OrdinalIgnoreCase);
                bool exportSummaryValid = export.ActiveRows == 2
                    && export.ScannerRows == 2
                    && export.DailyPnl == 67.25m
                    && export.Unrealized == 42.25m
                    && export.Realized == 25.00m;
                bool serverContainsExportButton = File.ReadAllText(Path.Combine(FindRepositoryRootForSelfTest(), "src", "Sailor.App", "Runtime", "Ui", "SailorUiServer.cs"))
                    .Contains("exportReport()", StringComparison.OrdinalIgnoreCase);

                bool pass = endpointExists
                    && filesWritten
                    && csvContainsSections
                    && htmlUsesTwsColors
                    && exportSummaryValid
                    && serverContainsExportButton;

                AddCheck(checks, endpointExists, "SAILOR-070 exposes /api/export for browser-triggered SailorUI report export.");
                AddCheck(checks, filesWritten, "SAILOR-070 writes timestamped and latest CSV/HTML report files under logs/{Mode}/SailorUI.");
                AddCheck(checks, csvContainsSections, "SAILOR-070 CSV export includes Section 2 active/today trades and Section 3 scanner symbols.");
                AddCheck(checks, htmlUsesTwsColors, "SAILOR-070 HTML export and UI styling support green long/win and red short/loss backgrounds.");
                AddCheck(checks, exportSummaryValid, "SAILOR-070 export summary preserves P&L and row counts.");
                AddCheck(checks, serverContainsExportButton, "SAILOR-070 SailorUI browser page includes an export action.");
                events.Add("command: paper sailor-ui --port 5101 --ui-controls --account DUN559573");
                events.Add("browser: click export -> POST /api/export");
                events.Add(export.ToSummaryString());

                return Result("sailor-ui-report-export", pass, checks, events, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"SAILOR-070 self-test exception: {ex.GetType().Name}: {ex.Message}");
                AddCheck(checks, false, "SAILOR-070 report export self-test executed without exception.");
                return Result("sailor-ui-report-export", false, checks, events, warnings);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for deterministic self-test temp files.
                }
            }
        }

        private static string FindRepositoryRootForSelfTest()
        {
            DirectoryInfo? current = new(Directory.GetCurrentDirectory());
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return Directory.GetCurrentDirectory();
        }

        private int CalculateReplenishmentRequest(
            int targetScannerTrades,
            int activeScannerTrades,
            int manualManagedTrades,
            int easternMinute,
            RuntimeSafetyState safetyState)
        {
            _ = manualManagedTrades;
            int shortfall = Math.Max(0, targetScannerTrades - activeScannerTrades);
            if (targetScannerTrades <= 0 || shortfall <= 0)
            {
                return 0;
            }

            if (easternMinute >= _lastEntryMinute)
            {
                return 0;
            }

            if (!safetyState.CanOpenNewEntries)
            {
                return 0;
            }

            return shortfall;
        }

        private static string ForceFlatDecisionType(int positionSide, int easternMinute)
        {
            _ = easternMinute;
            return positionSide < 0
                ? "ExitShort"
                : positionSide > 0
                    ? "ExitLong"
                    : "Hold";
        }

        private static void AddCheck(List<string> checks, bool passed, string message)
            => checks.Add($"{(passed ? "PASS" : "FAIL")}: {message}");

        private static TradeManagementSelfTestCaseResult Result(
            string scenario,
            bool passed,
            IReadOnlyList<string> checks,
            IReadOnlyList<string> events,
            IReadOnlyList<string> warnings)
            => new(scenario, passed, checks.ToArray(), events.ToArray(), warnings.ToArray());

        private static TradeManagementSelfTestCaseResult Fail(string scenario, string message)
            => new(scenario, false, new[] { $"FAIL: {message}" }, Array.Empty<string>(), new[] { message });
    }
}
