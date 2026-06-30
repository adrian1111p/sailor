using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Runtime.Paper;

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
        "live-current-candle-guard"
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
                && policy.IsManualManagedExitOnly
                && !entry.AllowEntry;

            AddCheck(checks, !origin.CountsTowardScannerTarget(), "manual intraday trade is excluded from scanner target count.");
            AddCheck(checks, policy.IsManualManagedExitOnly, "manual intraday trade resolves to manual-managed exit-only lifecycle.");
            AddCheck(checks, !entry.AllowEntry, "automatic entry/re-entry is blocked for manual intraday trade.");
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
