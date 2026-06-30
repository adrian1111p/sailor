using System.Globalization;
using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Backtest.Scanner.Points;
using Sailor.App.Broker.Ibkr;
using Sailor.App.Broker.Ibkr.Orders;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.Logging;
using Sailor.App.MarketData.History;
using Sailor.App.MarketData.Live;
using Sailor.App.Reporting;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.Live;
using Sailor.App.Runtime.Paper;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Runtime.TradeManagement.SelfTests;
using Sailor.App.Scanner.Runtime;
using Sailor.App.Scanner.ScanList;
using Sailor.App.Scanner.Universe;

namespace Sailor.App.Runtime.Commands;

public static class SailorRuntimeCommandRunner
{
    public static async Task RunAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        string subcommand = args.Length >= 1
            ? args[0].Trim().ToLowerInvariant()
            : "help";

        switch (subcommand)
        {
            case "connect":
                await RunConnectSkeletonAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "scan":
                await RunScanSkeletonAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "scan-list":
                await RunScanListSkeletonAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "scan-points":
                await RunScanPointsDiagnosticsAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "scan-points-test":
            case "points-test":
                await RunScanPointsSelfTestAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "history":
                await RunHistoryAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "quotes":
            case "quote":
                await RunMarketSnapshotAsync(mode, args.Skip(1).ToArray(), settings, forceDepth: false);
                break;

            case "depth":
            case "book":
                await RunMarketSnapshotAsync(mode, args.Skip(1).ToArray(), settings, forceDepth: true);
                break;

            case "snapshot":
                await RunMarketSnapshotAsync(mode, args.Skip(1).ToArray(), settings, forceDepth: null);
                break;

            case "run":
                await RunStrategySkeletonAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "order":
                await RunManualOrderAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "status":
            case "positions":
                await RunStatusAsync(mode, settings);
                break;

            case "trades":
                await RunTradesAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "broker":
                await RunBrokerAsync(mode, args.Skip(1).ToArray(), settings);
                break;
            case "trade-management-test":
            case "trade-test":
                Environment.ExitCode = await TradeManagementSelfTestRunner.RunAsync(mode, args.Skip(1).ToArray(), settings);
                break;


            case "reconcile":
                await RunReconcileAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "report":
                await RunPaperReportAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "readiness":
            case "gate":
                await RunLiveReadinessAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "flatten":
                await RunFlattenSkeletonAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "help":
            case "--help":
            case "-h":
                PrintHelp(mode, settings);
                break;

            default:
                Console.WriteLine($"Unknown {mode.ToDisplayName()} subcommand: {subcommand}");
                PrintHelp(mode, settings);
                break;
        }
    }

    private static async Task RunConnectSkeletonAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions options = CreateOptions(mode, Array.Empty<string>(), settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        var connectionOptions = IbkrConnectionOptions.FromRuntimeOptions(
            options,
            modeSettings.Account,
            modeSettings.ConnectTimeoutSeconds);

        string logFilePath = CreateRuntimeLogFilePath(mode, "connect");

        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} IBKR connection session started");
        Log(writer, options.ToCompactString());
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, "");

        if (mode == SailorRuntimeMode.Live)
        {
            bool readOnly = args.Any(arg => arg.Equals("--read-only", StringComparison.OrdinalIgnoreCase));
            LiveReadinessGateResult gate = EvaluateLiveReadiness(
                settings,
                "live connect",
                args,
                requiresTrading: false,
                readOnly: readOnly,
                account: modeSettings.Account ?? string.Empty);
            LogLiveReadinessGate(writer, gate);
            Log(writer, "");

            if (!readOnly)
            {
                Log(writer, "SAILOR-033 requires live connect to be started explicitly as read-only: live connect --read-only");
                Log(writer, "No live connection opened. No orders sent.");
                Log(writer, $"Runtime log: {logFilePath}");
                return;
            }
        }

        foreach (string line in IbkrConnectionChecklist.BuildPreflightLines(connectionOptions))
        {
            Log(writer, line);
        }

        Log(writer, "");
        Log(writer, "SAILOR-024 implementation: TCP connection session probe.");
        Log(writer, "This verifies that TWS/Gateway is reachable and establishes the Sailor connection state contract.");
        Log(writer, "It intentionally does not request market data and does not send orders.");
        Log(writer, "Full IBApi nextValidId/managedAccounts handshake remains the next adapter layer.");
        Log(writer, "");

        await using var session = new IbkrConnectionProbeSession();
        IbkrConnectionResult result = await session.ConnectAsync(connectionOptions, CancellationToken.None);

        foreach (string message in result.Messages)
        {
            Log(writer, message);
        }

        Log(writer, "");
        Log(writer, result.ToDisplayString());

        foreach (string line in IbkrConnectionChecklist.BuildPostConnectLines(result))
        {
            Log(writer, line);
        }

        if (result.Success)
        {
            IbkrConnectionSnapshot disconnected = await session.DisconnectAsync("SAILOR-024 connect command completed", CancellationToken.None);
            Log(writer, "");
            Log(writer, $"Disconnected cleanly: {disconnected.ToDisplayString()}");
        }

        Log(writer, "");
        Log(writer, $"Runtime log: {logFilePath}");
    }


    private static async Task RunHistoryAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        string timeframe = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.Trim() ?? "1m";
        string target = args
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault()?.Trim() ?? settings.DefaultUniverse;

        if (!string.Equals(timeframe, "1m", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SAILOR-025 supports only 1m historical data.");
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} history 1m TSLA");
            return;
        }

        int top = ReadIntOption(args, "--top", GetModeSettings(mode, settings).DefaultTopCount);
        int days = ReadIntOption(args, "--days", 5);
        bool requestIbkr = !args.Any(arg => arg.Equals("--local-cache", StringComparison.OrdinalIgnoreCase));
        bool mirrorToBacktest = !args.Any(arg => arg.Equals("--no-backtest-copy", StringComparison.OrdinalIgnoreCase));
        bool useRth = !args.Any(arg => arg.Equals("--all-hours", StringComparison.OrdinalIgnoreCase));
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");

        SailorRuntimeOptions options = CreateOptions(mode, [timeframe, settings.DefaultProfile, top.ToString(), target], settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        var connectionOptions = IbkrConnectionOptions.FromRuntimeOptions(
            options,
            modeSettings.Account,
            modeSettings.ConnectTimeoutSeconds);

        string logFilePath = CreateRuntimeLogFilePath(mode, "history");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} historical 1m data loader started");
        Log(writer, options.ToCompactString());
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, $"target={target} top={top} days={days} requestIbkr={requestIbkr} useRth={useRth} mirrorToBacktest={mirrorToBacktest} primaryExchange={primaryExchange}");
        Log(writer, "");

        IReadOnlyList<string> symbols = ResolveHistorySymbols(target, top);
        if (symbols.Count == 0)
        {
            Log(writer, "No symbols resolved for history request.");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        Log(writer, $"Symbols: {string.Join(", ", symbols)}");
        Log(writer, "");

        if (requestIbkr)
        {
            await using var probeSession = new IbkrConnectionProbeSession();
            Log(writer, "Preflight TCP probe before historical request.");
            IbkrConnectionResult probeResult = await probeSession.ConnectAsync(connectionOptions, CancellationToken.None);
            foreach (string message in probeResult.Messages)
            {
                Log(writer, message);
            }

            Log(writer, probeResult.ToDisplayString());
            if (probeResult.Success)
            {
                _ = await probeSession.DisconnectAsync("SAILOR-025 history preflight complete", CancellationToken.None);
            }
            else
            {
                Log(writer, "TCP preflight failed. SAILOR-025 will continue only if local-cache fallback can provide bars.");
            }

            Log(writer, "");
        }

        IHistoricalBarProvider provider = HistoricalBarProviderFactory.Create(requestIbkr, connectionOptions);
        Log(writer, $"Historical provider: {provider.ProviderName}");
        Log(writer, "");

        int successCount = 0;
        int totalBars = 0;
        int requestId = 25_000;
        TimeSpan lookback = TimeSpan.FromDays(Math.Max(1, days));

        foreach (string symbol in symbols)
        {
            HistoricalBarRequest request = HistoricalBarRequest.CreateOneMinute(
                mode,
                symbol,
                lookback,
                requestId++,
                useRth,
                primaryExchange,
                mirrorToBacktest);

            Log(writer, $"Request: {request.ToDisplayString()}");
            HistoricalBarLoadResult result = await provider.GetOneMinuteHistoryAsync(request, CancellationToken.None);
            Log(writer, result.ToDisplayString());
            Log(writer, result.Message);

            if (!string.IsNullOrWhiteSpace(result.BacktestMirrorPath))
            {
                Log(writer, $"Backtest mirror: {result.BacktestMirrorPath}");
            }

            foreach (string warning in result.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }

            if (result.Success)
            {
                successCount++;
                totalBars += result.BarCount;
            }

            Log(writer, "");
        }

        Log(writer, $"sailor {options.ModeName} historical 1m data loader finished: symbolsOk={successCount}/{symbols.Count} bars={totalBars}");
        Log(writer, $"Cache root: {HistoricalCachePaths.CacheRoot}");
        Log(writer, $"Runtime log: {logFilePath}");
        Log(writer, "");
        Log(writer, "Next command after successful cache write:");
        Log(writer, "dotnet run --project src\\Sailor.App\\Sailor.App.csproj -- backtest TSLA 1m v21-15minutes");
    }


    private static async Task RunMarketSnapshotAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings,
        bool? forceDepth)
    {
        string symbol = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.Trim().ToUpperInvariant() ?? "TSLA";
        if (string.IsNullOrWhiteSpace(symbol))
        {
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} quotes TSLA");
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} depth TSLA");
            return;
        }

        SailorRuntimeOptions options = CreateOptions(mode, ["1m", settings.DefaultProfile, "1", symbol], settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        var connectionOptions = IbkrConnectionOptions.FromRuntimeOptions(
            options,
            modeSettings.Account,
            modeSettings.ConnectTimeoutSeconds);

        bool localCache = args.Any(arg => arg.Equals("--local-cache", StringComparison.OrdinalIgnoreCase));
        bool useDepth = forceDepth ?? options.UseL2;
        if (args.Any(arg => arg.Equals("--with-depth", StringComparison.OrdinalIgnoreCase)))
        {
            useDepth = true;
        }

        if (args.Any(arg => arg.Equals("--no-depth", StringComparison.OrdinalIgnoreCase)))
        {
            useDepth = false;
        }

        int seconds = ReadIntOption(args, "--seconds", 10);
        int depthLevels = ReadIntOption(args, "--levels", settings.L1L2.DepthLevels <= 0 ? 5 : settings.L1L2.DepthLevels);
        int marketDataType = ReadIntOption(args, "--market-data-type", 1);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        bool smartDepth = args.Any(arg => arg.Equals("--smart-depth", StringComparison.OrdinalIgnoreCase));
        bool requestIbkr = !localCache;

        var request = LiveMarketDataRequest.Create(
            mode,
            symbol,
            requestId: 26_000,
            useL1: true,
            useL2: useDepth,
            depthLevels: depthLevels,
            duration: TimeSpan.FromSeconds(seconds),
            primaryExchange: primaryExchange,
            marketDataType: marketDataType,
            useSmartDepth: smartDepth,
            useLocalCacheFallback: localCache);

        string action = useDepth ? $"depth_{request.NormalizedSymbol}" : $"quotes_{request.NormalizedSymbol}";
        string logFilePath = CreateRuntimeLogFilePath(mode, action);
        await using var writer = CreateWriter(logFilePath);

        Log(writer, $"sailor {options.ModeName} live L1/L2 snapshot stream started");
        Log(writer, options.ToCompactString());
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, request.ToDisplayString());
        Log(writer, $"requestIbkr={requestIbkr} command={(useDepth ? "depth" : "quotes")}");
        Log(writer, "");
        Log(writer, "SAILOR-026 implementation: market-data snapshot capture only.");
        Log(writer, "It subscribes to L1 and optionally L2 when the optional IBApi build is enabled.");
        Log(writer, "No scanner is run and no orders are sent.");
        Log(writer, "");

        ILiveMarketDataSnapshotProvider provider = LiveMarketDataSnapshotProviderFactory.Create(requestIbkr, connectionOptions);
        try
        {
            Log(writer, $"Market data provider: {provider.ProviderName}");
            Log(writer, "");

            LiveMarketDataSnapshotResult result = await provider.CaptureSnapshotAsync(request, CancellationToken.None);
            Log(writer, result.ToDisplayString());
            Log(writer, result.Message);

            if (result.Snapshot is not null)
            {
                Log(writer, result.Snapshot.ToCompactString());
                Log(writer, $"HasL1={result.Snapshot.HasL1} HasL2={result.Snapshot.HasL2} Source={result.Snapshot.Source} Time={result.Snapshot.Time:O}");
            }

            if (!string.IsNullOrWhiteSpace(result.SnapshotLogPath))
            {
                Log(writer, $"Snapshot log: {result.SnapshotLogPath}");
            }

            if (result.Events.Count > 0)
            {
                Log(writer, "");
                Log(writer, "IBKR/market data events");
                Log(writer, "-----------------------");
                foreach (string row in result.Events)
                {
                    Log(writer, row);
                }
            }

            if (result.Warnings.Count > 0)
            {
                Log(writer, "");
                Log(writer, "Warnings");
                Log(writer, "--------");
                foreach (string warning in result.Warnings)
                {
                    Log(writer, $"WARN: {warning}");
                }
            }
        }
        finally
        {
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        Log(writer, "");
        Log(writer, $"Runtime log: {logFilePath}");
        Log(writer, "No orders sent.");
    }


    private static async Task RunScanPointsDiagnosticsAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        var effectiveArgs = new List<string>(args);
        if (!HasOption(effectiveArgs.ToArray(), "--scanner-mode"))
        {
            effectiveArgs.Add("--scanner-mode");
            effectiveArgs.Add("points-only");
        }

        if (!HasOption(effectiveArgs.ToArray(), "--no-depth") && !HasOption(effectiveArgs.ToArray(), "--with-depth"))
        {
            effectiveArgs.Add("--no-depth");
        }

        Console.WriteLine("SAILOR-047 scan-points diagnostics: scanner-only points audit. No conduct loop and no orders are started.");
        await RunScanListSkeletonAsync(mode, effectiveArgs.ToArray(), settings).ConfigureAwait(false);
    }



    private static async Task RunScanPointsSelfTestAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions runtimeOptions = CreateOptions(mode, args, settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        string account = ReadStringOption(args, "--account", modeSettings.Account ?? string.Empty);
        ScanListWorkbookOptions workbookOptions = CreateScanListWorkbookOptions(args, runtimeOptions.TopCount);
        string logFilePath = CreateRuntimeLogFilePath(mode, "scan_points_test");

        await using var writer = CreateWriter(logFilePath);
        Log(writer, "SAILOR-048 points scanner self-test: legacy no-selection versus points ranked candidates.");
        Log(writer, runtimeOptions.ToCompactString());
        Log(writer, workbookOptions.ToDisplayString());
        Log(writer, "This diagnostic starts no conduct loop and sends no orders. It runs legacy-blocks and points-only selection back to back and reports whether points-only makes the selection explainable.");

        bool localCache = args.Any(arg => arg.Equals("--local-cache", StringComparison.OrdinalIgnoreCase));
        bool requestIbkrHistory = !localCache && !args.Any(arg => arg.Equals("--no-history-refresh", StringComparison.OrdinalIgnoreCase));
        bool mirrorHistoryToBacktest = !args.Any(arg => arg.Equals("--no-backtest-copy", StringComparison.OrdinalIgnoreCase));
        bool useRth = !args.Any(arg => arg.Equals("--all-hours", StringComparison.OrdinalIgnoreCase));
        bool captureSnapshots = !args.Any(arg => arg.Equals("--no-quotes", StringComparison.OrdinalIgnoreCase));
        bool useL2 = runtimeOptions.UseL2 || args.Any(arg => arg.Equals("--with-depth", StringComparison.OrdinalIgnoreCase));
        if (args.Any(arg => arg.Equals("--no-depth", StringComparison.OrdinalIgnoreCase)))
        {
            useL2 = false;
        }

        bool requestIbkrMarketData = captureSnapshots && !localCache;
        int days = ReadIntOption(args, "--days", 5);
        int marketDataType = ReadIntOption(args, "--market-data-type", 1);
        int snapshotSeconds = ReadIntOption(args, "--seconds", 3);
        int depthLevels = ReadIntOption(args, "--levels", settings.L1L2.DepthLevels <= 0 ? 5 : settings.L1L2.DepthLevels);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        bool smartDepth = args.Any(arg => arg.Equals("--smart-depth", StringComparison.OrdinalIgnoreCase));
        decimal pointsMinimumTradeScore = ReadDecimalOption(args, "--points-min-trade-score", settings.Scanner.PointsMinimumTradeScore);
        bool pointsAllowWeakEntry = ReadBooleanOption(args, "--points-allow-weak-entry", settings.Scanner.PointsAllowWeakEntry);
        bool pointsRetainWatchOnly = ReadBooleanOption(args, "--points-retain-watch-only", settings.Scanner.PointsRetainWatchOnly);
        int maxSymbols = ReadIntOption(args, "--max-symbols", Math.Max(1, workbookOptions.HistoryBatchSize));

        var connectionOptions = new IbkrConnectionOptions(
            mode,
            runtimeOptions.Host,
            runtimeOptions.Port,
            runtimeOptions.ClientId,
            account,
            modeSettings.ConnectTimeoutSeconds,
            runtimeOptions.UseL1,
            runtimeOptions.UseL2,
            SendOrders: false,
            runtimeOptions.AllowShort);

        var legacyOptions = new PaperScannerOptions(
            mode,
            runtimeOptions.Timeframe,
            runtimeOptions.ProfileName,
            runtimeOptions.Universe,
            runtimeOptions.TopCount,
            maxSymbols,
            days,
            requestIbkrHistory,
            mirrorHistoryToBacktest,
            useRth,
            captureSnapshots,
            requestIbkrMarketData,
            runtimeOptions.UseL1,
            useL2,
            snapshotSeconds,
            depthLevels,
            marketDataType,
            primaryExchange,
            smartDepth,
            PointsScannerMode.LegacyBlocks,
            pointsMinimumTradeScore,
            pointsAllowWeakEntry,
            pointsRetainWatchOnly);

        var pointsOptions = legacyOptions with { ScannerMode = PointsScannerMode.PointsOnly };

        Log(writer, "");
        Log(writer, "Legacy-blocks pass");
        Log(writer, "------------------");
        ScanListRunResult legacyResult = await RunScanListSelectionForConductAsync(
            mode,
            settings,
            connectionOptions,
            legacyOptions,
            workbookOptions,
            args,
            writer,
            "scan-points-test legacy-blocks").ConfigureAwait(false);

        Log(writer, "");
        Log(writer, "Points-only pass");
        Log(writer, "----------------");
        ScanListRunResult pointsResult = await RunScanListSelectionForConductAsync(
            mode,
            settings,
            connectionOptions,
            pointsOptions,
            workbookOptions,
            args,
            writer,
            "scan-points-test points-only").ConfigureAwait(false);

        ScanListCycleResult? legacyCycle = legacyResult.LatestCycle;
        ScanListCycleResult? pointsCycle = pointsResult.LatestCycle;
        int legacyCandidates = legacyCycle?.ScannerResult.Candidates.Count ?? 0;
        int pointsCandidates = pointsCycle?.ScannerResult.Candidates.Count ?? 0;
        int pointsTotal = pointsCycle?.ScannerResult.PointsCandidates ?? 0;
        bool pointsHasRankedCandidates = pointsCandidates > 0 && pointsTotal > 0;
        bool pointsNotWorseThanLegacy = pointsCandidates >= legacyCandidates;
        bool pointsHasReport = !string.IsNullOrWhiteSpace(pointsCycle?.ScannerResult.CandidateReportPath);
        bool strictLegacyZeroRequested = HasOption(args, "--expect-legacy-zero");
        bool legacyZeroOk = !strictLegacyZeroRequested || legacyCandidates == 0;
        bool passed = pointsHasRankedCandidates && pointsNotWorseThanLegacy && pointsHasReport && legacyZeroOk;

        Log(writer, "");
        Log(writer, "SAILOR-048 self-test result");
        Log(writer, "---------------------------");
        Log(writer, $"legacyCandidates={legacyCandidates} pointsCandidates={pointsCandidates} pointsTotal={pointsTotal} pointsReport={pointsCycle?.ScannerResult.CandidateReportPath ?? "n/a"}");
        Log(writer, $"checks: pointsHasRankedCandidates={pointsHasRankedCandidates} pointsNotWorseThanLegacy={pointsNotWorseThanLegacy} pointsHasReport={pointsHasReport} legacyZeroCheck={(strictLegacyZeroRequested ? legacyZeroOk.ToString() : "not-requested")}");
        Log(writer, passed
            ? "Result: Passed - points-only mode produced ranked, reportable candidates without relying on legacy hard scanner blocks."
            : "Result: Failed - points-only mode did not produce the expected ranked candidate evidence. Inspect the scanner reports above.");
        Log(writer, $"Runtime log: {logFilePath}");
        Log(writer, "No orders sent.");
    }

    private static async Task RunScanListSkeletonAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        string[] positional = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
        string timeframe = positional.Length >= 1 ? positional[0].Trim() : settings.DefaultTimeframe;
        string profileName = positional.Length >= 2 ? positional[1].Trim() : settings.DefaultProfile;
        int topCount = positional.Length >= 3 && int.TryParse(positional[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedTop) && parsedTop > 0
            ? parsedTop
            : settings.Scanner.DefaultTopCount;

        string file = ReadStringOption(args, "--file", ReadStringOption(args, "--scan-file", ScanListWorkbookOptions.DefaultFilePath));
        string sheet = ReadStringOption(args, "--sheet", ReadStringOption(args, "--scan-sheet", ScanListWorkbookOptions.DefaultSheetName));
        string symbolColumn = ReadStringOption(args, "--symbol-column", ScanListWorkbookOptions.DefaultSymbolColumn);
        int scanRefreshSeconds = ReadIntOption(args, "--scan-refresh-seconds", ScanListWorkbookOptions.DefaultRefreshSeconds);
        int historyBatchSize = ReadIntOption(args, "--history-batch-size", ScanListWorkbookOptions.DefaultHistoryBatchSize);
        int historyBatchIntervalMinutes = ReadIntOption(args, "--history-batch-interval-minutes", ScanListWorkbookOptions.DefaultHistoryBatchIntervalMinutes);
        int tradeTop = Math.Max(10, ReadIntOption(args, "--trade-top", ReadIntOption(args, "--keep-trade-top", Math.Max(10, topCount))));
        int scanCycles = Math.Max(1, ReadIntOption(args, "--scan-cycles", ReadIntOption(args, "--cycles", 1)));
        bool waitBetweenCycles = scanCycles > 1 && !args.Any(arg => arg.Equals("--no-scan-cycle-wait", StringComparison.OrdinalIgnoreCase));
        string universeArgument = SymbolUniverseProviderFactory.BuildXlsxUniverseArgument(file, sheet, symbolColumn);

        var workbookOptions = new ScanListWorkbookOptions(
            file,
            sheet,
            symbolColumn,
            scanRefreshSeconds,
            tradeTop,
            historyBatchSize,
            historyBatchIntervalMinutes);

        var scanArgs = new List<string>
        {
            timeframe,
            profileName,
            topCount.ToString(CultureInfo.InvariantCulture),
            universeArgument
        };

        scanArgs.AddRange(args.Where(arg => arg.StartsWith("--", StringComparison.Ordinal) &&
            !arg.Equals("--file", StringComparison.OrdinalIgnoreCase) &&
            !arg.Equals("--scan-file", StringComparison.OrdinalIgnoreCase) &&
            !arg.Equals("--sheet", StringComparison.OrdinalIgnoreCase) &&
            !arg.Equals("--scan-sheet", StringComparison.OrdinalIgnoreCase) &&
            !arg.Equals("--symbol-column", StringComparison.OrdinalIgnoreCase)));

        SailorRuntimeOptions runtimeOptions = CreateOptions(mode, scanArgs.ToArray(), settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        string account = ReadStringOption(args, "--account", modeSettings.Account ?? string.Empty);
        var connectionOptions = new IbkrConnectionOptions(
            mode,
            runtimeOptions.Host,
            runtimeOptions.Port,
            runtimeOptions.ClientId,
            account,
            modeSettings.ConnectTimeoutSeconds,
            runtimeOptions.UseL1,
            runtimeOptions.UseL2,
            SendOrders: false,
            runtimeOptions.AllowShort);

        bool localCache = args.Any(arg => arg.Equals("--local-cache", StringComparison.OrdinalIgnoreCase));
        bool requestIbkrHistory = !localCache && !args.Any(arg => arg.Equals("--no-history-refresh", StringComparison.OrdinalIgnoreCase));
        bool mirrorHistoryToBacktest = !args.Any(arg => arg.Equals("--no-backtest-copy", StringComparison.OrdinalIgnoreCase));
        bool useRth = !args.Any(arg => arg.Equals("--all-hours", StringComparison.OrdinalIgnoreCase));
        bool captureSnapshots = !args.Any(arg => arg.Equals("--no-quotes", StringComparison.OrdinalIgnoreCase));
        bool useL2 = runtimeOptions.UseL2 || args.Any(arg => arg.Equals("--with-depth", StringComparison.OrdinalIgnoreCase));
        if (args.Any(arg => arg.Equals("--no-depth", StringComparison.OrdinalIgnoreCase)))
        {
            useL2 = false;
        }

        bool requestIbkrMarketData = captureSnapshots && !localCache;
        int days = ReadIntOption(args, "--days", 5);
        int marketDataType = ReadIntOption(args, "--market-data-type", 1);
        int snapshotSeconds = ReadIntOption(args, "--seconds", 3);
        int depthLevels = ReadIntOption(args, "--levels", settings.L1L2.DepthLevels <= 0 ? 5 : settings.L1L2.DepthLevels);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        bool smartDepth = args.Any(arg => arg.Equals("--smart-depth", StringComparison.OrdinalIgnoreCase));
        PointsScannerMode scannerMode = ReadScannerMode(args, settings);
        decimal pointsMinimumTradeScore = ReadDecimalOption(args, "--points-min-trade-score", settings.Scanner.PointsMinimumTradeScore);
        bool pointsAllowWeakEntry = ReadBooleanOption(args, "--points-allow-weak-entry", settings.Scanner.PointsAllowWeakEntry);
        bool pointsRetainWatchOnly = ReadBooleanOption(args, "--points-retain-watch-only", settings.Scanner.PointsRetainWatchOnly);

        int defaultMaxSymbols = Math.Max(1, historyBatchSize);
        int maxSymbols = ReadIntOption(args, "--max-symbols", defaultMaxSymbols);

        var scannerOptions = new PaperScannerOptions(
            mode,
            runtimeOptions.Timeframe,
            runtimeOptions.ProfileName,
            runtimeOptions.Universe,
            runtimeOptions.TopCount,
            maxSymbols,
            days,
            requestIbkrHistory,
            mirrorHistoryToBacktest,
            useRth,
            captureSnapshots,
            requestIbkrMarketData,
            runtimeOptions.UseL1,
            useL2,
            snapshotSeconds,
            depthLevels,
            marketDataType,
            primaryExchange,
            smartDepth,
            scannerMode,
            pointsMinimumTradeScore,
            pointsAllowWeakEntry,
            pointsRetainWatchOnly);

        var runRequest = new ScanListRunRequest(
            mode,
            workbookOptions,
            scannerOptions,
            scanCycles,
            scanRefreshSeconds,
            tradeTop,
            waitBetweenCycles);

        string logFilePath = CreateRuntimeLogFilePath(mode, "scan_list");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {runtimeOptions.ModeName} scan-list runtime started");
        Log(writer, runtimeOptions.ToCompactString());
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, scannerOptions.ToDisplayString());
        Log(writer, workbookOptions.ToDisplayString());
        Log(writer, $"scanCycles={runRequest.SafeCycles} waitBetweenCycles={waitBetweenCycles} scanRefreshSeconds={runRequest.SafeScanRefreshSeconds} tradeTop={runRequest.SafeTradeTop}");
        Log(writer, ScanListCandidateRetentionOptions.FromScannerOptions(scannerOptions).ToDisplayString());
        Log(writer, "");
        Log(writer, "SAILOR-041 scan-list observation/runtime evidence for audit Steps 13, 14, and 15.");
        Log(writer, "The command keeps a ScanListMemoryStore alive across scan cycles, reloads the workbook, detects intraday additions/removals, selects the due history batch, and retains the best scanner-rated symbols for later paper/live entry eligibility.");
        Log(writer, "The runtime still sends no orders. It writes scan-list runtime evidence and keeps the safety state CloseOnly whenever history, market data, or broker/server state is not clean.");
        Log(writer, "History scheduler default: 45 symbols per batch and 10 minutes between batches. Workbook refresh default: 300 seconds. Trade retention default: at least top 10 symbols.");
        Log(writer, "Realtime candle accumulator and historical/realtime merge foundation are active; dry-run/local modes may show zero realtime candles until a realtime source is attached.");
        Log(writer, "");

        if (mode == SailorRuntimeMode.Live)
        {
            LiveReadinessGateResult gate = EvaluateLiveReadiness(
                settings,
                "live scan-list",
                args,
                requiresTrading: false,
                readOnly: true,
                account: account);
            LogLiveReadinessGate(writer, gate);
            Log(writer, "SAILOR-041 live scan-list observation is read-only. No live order router is created and no live trading gate is required.");
            Log(writer, "");
        }

        using var scanListRuntime = new ScanListRuntime(settings, connectionOptions);
        ScanListRunResult runResult = await scanListRuntime.RunAsync(runRequest, CancellationToken.None);

        Log(writer, $"History provider: {runResult.HistoryProviderName}");
        Log(writer, $"Market data provider: {runResult.MarketDataProviderName}");
        Log(writer, "");
        Log(writer, "Scan-list runtime result");
        Log(writer, "------------------------");
        Log(writer, runResult.ToSummaryString());
        Log(writer, "");

        foreach (ScanListCycleResult cycle in runResult.Cycles)
        {
            Log(writer, $"Cycle {cycle.CycleIndex}/{cycle.TotalCycles}");
            Log(writer, "----------------");
            Log(writer, cycle.ToSummaryString());
            Log(writer, cycle.Workbook.ToSummaryString());
            Log(writer, cycle.Reload.ToSummaryString());
            Log(writer, $"dueHistoryBatch={(cycle.DueHistoryBatch?.ToDisplayLine() ?? "none due")}");
            Log(writer, $"scanner={cycle.ScannerResult.ToSummaryString()}");
            Log(writer, $"memoryEvidence={cycle.Evidence.ToSummaryString()}");
            Log(writer, $"dataQuality={cycle.Evidence.DataQualityStatus} reason={cycle.Evidence.DataQualityReason}");
            Log(writer, $"notReadySelected={(cycle.Evidence.SafeNotReadySelectedSymbols.Count == 0 ? "none" : string.Join(",", cycle.Evidence.SafeNotReadySelectedSymbols))}");
            Log(writer, $"safety={cycle.SafetyState.ToDisplayString()}");
            Log(writer, $"Scan-list evidence JSON: {cycle.EvidenceJsonPath}");
            Log(writer, $"Scan-list evidence CSV:  {cycle.EvidenceCsvPath}");
            Log(writer, "");

            Log(writer, "Scan-list memory and reload state");
            Log(writer, "---------------------------------");
            Log(writer, $"Added symbols: {FormatSymbols(cycle.Reload.AddedSymbols, 40)}");
            Log(writer, $"Removed symbols: {FormatSymbols(cycle.Reload.RemovedSymbols, 40)}");
            Log(writer, $"Retained removed symbols: {FormatSymbols(cycle.Reload.RetainedRemovedSymbols, 40)}");
            Log(writer, "");

            Log(writer, "History batch schedule");
            Log(writer, "----------------------");
            foreach (ScanListHistoryBatch batch in cycle.PlannedHistoryBatches.Take(8))
            {
                Log(writer, batch.ToDisplayLine());
            }
            if (cycle.PlannedHistoryBatches.Count > 8)
            {
                Log(writer, $"... {cycle.PlannedHistoryBatches.Count - 8} more batches");
            }
            Log(writer, "");

            if (cycle.ScannerResult.Candidates.Count == 0)
            {
                Log(writer, "No scanner candidates found for this scan-list cycle.");
            }
            else
            {
                Log(writer, $"Top scanner candidates retained as trade/watch evidence every {scanRefreshSeconds}s");
                Log(writer, "----------------------------------------------------------------");
                foreach (PaperScannerCandidate candidate in cycle.ScannerResult.Candidates.Take(tradeTop))
                {
                    Log(writer, candidate.ToDisplayLine());
                }
            }

            if (cycle.TradeEligibleSymbols.Count > 0)
            {
                Log(writer, "");
                Log(writer, $"Trade-eligible retained symbols: {FormatSymbols(cycle.TradeEligibleSymbols, 80)}");
            }

            if (cycle.WatchCandidateSymbols.Count > 0)
            {
                Log(writer, "");
                Log(writer, $"Watch-only retained symbols: {FormatSymbols(cycle.WatchCandidateSymbols, 80)}");
            }

            if (!string.IsNullOrWhiteSpace(cycle.ScannerResult.CandidateReportPath))
            {
                Log(writer, "");
                Log(writer, $"Scanner CSV report: {cycle.ScannerResult.CandidateReportPath}");
            }

            if (!string.IsNullOrWhiteSpace(cycle.ScannerResult.HybridComparisonReportPath))
            {
                Log(writer, $"Hybrid comparison CSV report: {cycle.ScannerResult.HybridComparisonReportPath}");
            }

            if (!string.IsNullOrWhiteSpace(cycle.ScannerResult.HybridComparisonMarkdownReportPath))
            {
                Log(writer, $"Hybrid comparison Markdown report: {cycle.ScannerResult.HybridComparisonMarkdownReportPath}");
            }

            if (cycle.Warnings.Count > 0)
            {
                Log(writer, "");
                Log(writer, "Warnings");
                Log(writer, "--------");
                foreach (string warning in cycle.Warnings.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    Log(writer, $"WARN: {warning}");
                }
            }

            Log(writer, "");
        }

        Log(writer, $"sailor {runtimeOptions.ModeName} scan-list finished");
        Log(writer, $"Runtime log: {logFilePath}");
        Log(writer, "No orders sent.");
    }


    private static async Task RunScanSkeletonAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions runtimeOptions = CreateOptions(mode, args, settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        var connectionOptions = IbkrConnectionOptions.FromRuntimeOptions(
            runtimeOptions,
            modeSettings.Account,
            modeSettings.ConnectTimeoutSeconds);

        bool localCache = args.Any(arg => arg.Equals("--local-cache", StringComparison.OrdinalIgnoreCase));
        bool requestIbkrHistory = !localCache && !args.Any(arg => arg.Equals("--no-history-refresh", StringComparison.OrdinalIgnoreCase));
        bool mirrorHistoryToBacktest = !args.Any(arg => arg.Equals("--no-backtest-copy", StringComparison.OrdinalIgnoreCase));
        bool useRth = !args.Any(arg => arg.Equals("--all-hours", StringComparison.OrdinalIgnoreCase));
        bool captureSnapshots = !args.Any(arg => arg.Equals("--no-quotes", StringComparison.OrdinalIgnoreCase));
        bool useL2 = runtimeOptions.UseL2 || args.Any(arg => arg.Equals("--with-depth", StringComparison.OrdinalIgnoreCase));
        if (args.Any(arg => arg.Equals("--no-depth", StringComparison.OrdinalIgnoreCase)))
        {
            useL2 = false;
        }

        bool requestIbkrMarketData = captureSnapshots && !localCache;
        int days = ReadIntOption(args, "--days", 5);
        int marketDataType = ReadIntOption(args, "--market-data-type", 1);
        int snapshotSeconds = ReadIntOption(args, "--seconds", 3);
        int depthLevels = ReadIntOption(args, "--levels", settings.L1L2.DepthLevels <= 0 ? 5 : settings.L1L2.DepthLevels);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        bool smartDepth = args.Any(arg => arg.Equals("--smart-depth", StringComparison.OrdinalIgnoreCase));
        PointsScannerMode scannerMode = ReadScannerMode(args, settings);
        decimal pointsMinimumTradeScore = ReadDecimalOption(args, "--points-min-trade-score", settings.Scanner.PointsMinimumTradeScore);
        bool pointsAllowWeakEntry = ReadBooleanOption(args, "--points-allow-weak-entry", settings.Scanner.PointsAllowWeakEntry);
        bool pointsRetainWatchOnly = ReadBooleanOption(args, "--points-retain-watch-only", settings.Scanner.PointsRetainWatchOnly);

        int defaultMaxSymbols = localCache
            ? int.MaxValue
            : Math.Max(runtimeOptions.TopCount, runtimeOptions.TopCount * 3);
        int maxSymbols = ReadIntOption(args, "--max-symbols", defaultMaxSymbols);

        var scannerOptions = new PaperScannerOptions(
            mode,
            runtimeOptions.Timeframe,
            runtimeOptions.ProfileName,
            runtimeOptions.Universe,
            runtimeOptions.TopCount,
            maxSymbols,
            days,
            requestIbkrHistory,
            mirrorHistoryToBacktest,
            useRth,
            captureSnapshots,
            requestIbkrMarketData,
            runtimeOptions.UseL1,
            useL2,
            snapshotSeconds,
            depthLevels,
            marketDataType,
            primaryExchange,
            smartDepth,
            scannerMode,
            pointsMinimumTradeScore,
            pointsAllowWeakEntry,
            pointsRetainWatchOnly);

        string logFilePath = CreateRuntimeLogFilePath(mode, "scan");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {runtimeOptions.ModeName} scanner from live/history snapshots started");
        Log(writer, runtimeOptions.ToCompactString());
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, scannerOptions.ToDisplayString());
        Log(writer, "");
        Log(writer, "SAILOR-027 implementation: paper/live scanner adapter.");
        Log(writer, "It prepares 1m history, runs the existing Sailor scanner engine, then enriches selected candidates with L1/L2 snapshots.");
        Log(writer, "No strategy loop is started and no orders are sent.");
        Log(writer, "");

        if (mode == SailorRuntimeMode.Live)
        {
            LiveReadinessGateResult gate = EvaluateLiveReadiness(
                settings,
                "live scan",
                args,
                requiresTrading: false,
                readOnly: true,
                account: modeSettings.Account ?? string.Empty);
            LogLiveReadinessGate(writer, gate);
            Log(writer, "SAILOR-033 live scan is read-only. No live order router is created.");
            Log(writer, "");
        }

        using var runner = new PaperScannerRunner(settings, connectionOptions, scannerOptions);
        Log(writer, $"History provider: {runner.HistoryProviderName}");
        Log(writer, $"Market data provider: {runner.MarketDataProviderName}");
        Log(writer, "");

        PaperScannerRunResult result = await runner.RunAsync(scannerOptions, CancellationToken.None);

        Log(writer, "Universe and preparation summary");
        Log(writer, "--------------------------------");
        Log(writer, result.ToSummaryString());
        Log(writer, $"Resolved symbols: {result.ResolvedSymbols.Count}");
        Log(writer, $"Prepared symbols: {string.Join(", ", result.PreparedSymbols.Take(80))}{(result.PreparedSymbols.Count > 80 ? ", ..." : string.Empty)}");
        Log(writer, "");

        if (result.Preparations.Count > 0)
        {
            Log(writer, "History preparation");
            Log(writer, "-------------------");
            foreach (PaperScannerSymbolPreparation row in result.Preparations)
            {
                Log(writer, row.ToDisplayString());
            }

            Log(writer, "");
        }

        if (result.Candidates.Count == 0)
        {
            Log(writer, "No scanner candidates found for the selected profile/timeframe/universe.");
        }
        else
        {
            Log(writer, "Paper scanner candidates");
            Log(writer, "------------------------");
            foreach (PaperScannerCandidate candidate in result.Candidates)
            {
                Log(writer, candidate.ToDisplayLine());
                if (!string.IsNullOrWhiteSpace(candidate.SnapshotMessage))
                {
                    Log(writer, $"    snapshot: {candidate.SnapshotMessage}");
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(result.CandidateReportPath))
        {
            Log(writer, "");
            Log(writer, $"Scanner CSV report: {result.CandidateReportPath}");
        }

        if (result.Warnings.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Warnings");
            Log(writer, "--------");
            foreach (string warning in result.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }

        Log(writer, "");
        Log(writer, $"sailor {runtimeOptions.ModeName} scanner finished");
        Log(writer, $"Runtime log: {logFilePath}");
        Log(writer, "No orders sent.");
    }


    private static bool HasScanListInput(string[] args)
        => args.Any(arg => arg.Equals("--scan-file", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--file", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--scan-sheet", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--sheet", StringComparison.OrdinalIgnoreCase));

    private static ScanListWorkbookOptions CreateScanListWorkbookOptions(string[] args, int requestedTopCount)
    {
        string file = ReadStringOption(args, "--scan-file", ReadStringOption(args, "--file", ScanListWorkbookOptions.DefaultFilePath));
        string sheet = ReadStringOption(args, "--scan-sheet", ReadStringOption(args, "--sheet", ScanListWorkbookOptions.DefaultSheetName));
        string symbolColumn = ReadStringOption(args, "--symbol-column", ScanListWorkbookOptions.DefaultSymbolColumn);
        int scanRefreshSeconds = ReadIntOption(args, "--scan-refresh-seconds", ScanListWorkbookOptions.DefaultRefreshSeconds);
        int historyBatchSize = ReadIntOption(args, "--history-batch-size", ScanListWorkbookOptions.DefaultHistoryBatchSize);
        int historyBatchIntervalMinutes = ReadIntOption(args, "--history-batch-interval-minutes", ScanListWorkbookOptions.DefaultHistoryBatchIntervalMinutes);
        int tradeTop = Math.Max(10, ReadIntOption(args, "--trade-top", ReadIntOption(args, "--keep-trade-top", Math.Max(10, requestedTopCount))));

        return new ScanListWorkbookOptions(
            file,
            sheet,
            symbolColumn,
            scanRefreshSeconds,
            tradeTop,
            historyBatchSize,
            historyBatchIntervalMinutes);
    }

    private static async Task<ScanListRunResult> RunScanListSelectionForConductAsync(
        SailorRuntimeMode mode,
        SailorAppSettings settings,
        IbkrConnectionOptions connectionOptions,
        PaperScannerOptions baseScannerOptions,
        ScanListWorkbookOptions workbookOptions,
        string[] args,
        StreamWriter writer,
        string commandName)
    {
        int scanCycles = Math.Max(1, ReadIntOption(args, "--scan-cycles", ReadIntOption(args, "--cycles", 1)));
        bool waitBetweenCycles = scanCycles > 1
            && !args.Any(arg => arg.Equals("--no-scan-cycle-wait", StringComparison.OrdinalIgnoreCase));
        int maxSymbols = ReadIntOption(args, "--max-symbols", Math.Max(1, workbookOptions.HistoryBatchSize));

        string workbookUniverse = SymbolUniverseProviderFactory.BuildXlsxUniverseArgument(
            workbookOptions.FilePath,
            workbookOptions.SheetName,
            workbookOptions.SymbolColumn);

        PaperScannerOptions scanListScannerOptions = baseScannerOptions with
        {
            Universe = workbookUniverse,
            TopCount = workbookOptions.TradeTop,
            MaxSymbolsToPrepare = maxSymbols
        };

        var request = new ScanListRunRequest(
            mode,
            workbookOptions,
            scanListScannerOptions,
            scanCycles,
            workbookOptions.RefreshSeconds,
            workbookOptions.TradeTop,
            waitBetweenCycles);

        Log(writer, "");
        Log(writer, $"SAILOR-040 dynamic scan-list selection for {commandName}.");
        Log(writer, workbookOptions.ToDisplayString());
        Log(writer, $"scanCycles={request.SafeCycles} waitBetweenCycles={waitBetweenCycles} tradeTop={request.SafeTradeTop} maxSymbols={maxSymbols}");
        Log(writer, ScanListCandidateRetentionOptions.FromScannerOptions(scanListScannerOptions).ToDisplayString());
        Log(writer, "The scan-list runtime reloads the workbook, schedules due history batches, merges history/realtime candles, and retains the best scanner-rated symbols before the conduct loop.");
        Log(writer, "Order routing remains controlled by the existing paper/live gates. Scan-list selection only controls entry eligibility; exits/flatten remain allowed for managed positions.");

        using var scanListRuntime = new ScanListRuntime(settings, connectionOptions);
        ScanListRunResult runResult = await scanListRuntime.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
        ScanListCycleResult? latest = runResult.Cycles.LastOrDefault();
        Log(writer, runResult.ToSummaryString());
        if (latest is not null)
        {
            Log(writer, $"latestScanListCycle={latest.ToSummaryString()}");
            Log(writer, $"latestScanListEvidence={latest.Evidence.ToSummaryString()}");
            Log(writer, $"latestScanListDataQuality={latest.Evidence.DataQualityStatus} reason={latest.Evidence.DataQualityReason}");
            Log(writer, $"Scan-list evidence JSON: {latest.EvidenceJsonPath}");
            Log(writer, $"Scan-list evidence CSV:  {latest.EvidenceCsvPath}");
            if (latest.TradeEligibleSymbols.Count > 0)
            {
                Log(writer, $"retainedTradeEligible={string.Join(", ", latest.TradeEligibleSymbols)}");
            }

            if (latest.WatchCandidateSymbols.Count > 0)
            {
                Log(writer, $"retainedWatchOnly={string.Join(", ", latest.WatchCandidateSymbols)}");
            }

            if (!string.IsNullOrWhiteSpace(latest.ScannerResult.HybridComparisonReportPath))
            {
                Log(writer, $"hybridComparisonReport={latest.ScannerResult.HybridComparisonReportPath}");
            }

            if (!string.IsNullOrWhiteSpace(latest.ScannerResult.HybridComparisonMarkdownReportPath))
            {
                Log(writer, $"hybridComparisonMarkdownReport={latest.ScannerResult.HybridComparisonMarkdownReportPath}");
            }
        }

        foreach (string warning in runResult.Warnings)
        {
            Log(writer, $"WARN: {warning}");
        }

        return runResult;
    }

    private static IReadOnlyList<string> SelectScanListTradeSymbols(
        ScanListRunResult runResult,
        int requestedTopCount,
        bool livePilot)
    {
        int take = livePilot ? 1 : Math.Max(1, requestedTopCount);
        ScanListCycleResult? latest = runResult.Cycles.LastOrDefault();
        if (latest is null)
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string> retained = latest.TradeEligibleSymbols.Count > 0
            ? latest.TradeEligibleSymbols
            : latest.ScannerResult.Options.ScannerMode == PointsScannerMode.LegacyBlocks
                ? latest.ScannerResult.Candidates.Select(candidate => candidate.Symbol).ToArray()
                : Array.Empty<string>();

        return retained
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToArray();
    }

    private static void LogScanListEntryWaitDiagnostics(
        StreamWriter writer,
        ScanListRunResult runResult,
        string context)
    {
        ScanListCycleResult? latest = runResult.Cycles.LastOrDefault();
        if (latest is null)
        {
            Log(writer, $"SAILOR-047 wait diagnostics ({context}): no scan-list cycle is available yet.");
            return;
        }

        PaperScannerRunResult scanner = latest.ScannerResult;
        if (scanner.Options.ScannerMode == PointsScannerMode.LegacyBlocks)
        {
            Log(writer, $"SAILOR-047 wait diagnostics ({context}): legacy-blocks mode is active; waiting still depends on legacy tradeEligible symbols.");
            return;
        }

        var ready = scanner.Candidates.Where(candidate => candidate.PointsCandidate?.Status == PointsScannerStatus.Ready).ToArray();
        var weakReady = scanner.Candidates.Where(candidate => candidate.PointsCandidate?.Status == PointsScannerStatus.WeakReady).ToArray();
        var watchOnly = scanner.Candidates.Where(candidate => candidate.PointsCandidate?.Status == PointsScannerStatus.WatchOnly).ToArray();
        var notReady = scanner.Candidates.Where(candidate => candidate.PointsCandidate?.Status == PointsScannerStatus.NotReady).ToArray();

        Log(writer, $"SAILOR-047 wait diagnostics ({context}): scannerMode={scanner.Options.ScannerMode.ToConfigValue()} tradeEligible={latest.TradeEligibleSymbols.Count} ready={ready.Length} weakReady={weakReady.Length} watchOnly={watchOnly.Length} notReady={notReady.Length} minScore={scanner.Options.PointsMinimumTradeScore:F2} weakEntryAllowed={scanner.Options.PointsAllowWeakEntry}.");

        if (latest.TradeEligibleSymbols.Count > 0)
        {
            Log(writer, $"SAILOR-047 wait diagnostics: Ready trade symbols available: {FormatSymbols(latest.TradeEligibleSymbols, 20)}.");
            return;
        }

        if (ready.Length > 0)
        {
            Log(writer, "SAILOR-047 wait diagnostics: Ready points candidates exist, but scan-list retention/data-quality did not expose a trade-eligible symbol yet. Rechecking on the next cycle.");
        }
        else if (weakReady.Length > 0 && !scanner.Options.PointsAllowWeakEntry)
        {
            Log(writer, "SAILOR-047 wait diagnostics: only WeakReady points candidates are available and weak entry is disabled, so Sailor keeps waiting.");
        }
        else if (watchOnly.Length > 0)
        {
            Log(writer, "SAILOR-047 wait diagnostics: only WatchOnly candidates are available, so Sailor keeps rescanning.");
        }
        else
        {
            Log(writer, "SAILOR-047 wait diagnostics: no points candidates are ready for entry yet.");
        }

        IReadOnlyList<PaperScannerCandidate> displayCandidates = scanner.Candidates
            .Where(candidate => candidate.PointsCandidate is not null)
            .Take(10)
            .ToArray();
        if (displayCandidates.Count == 0)
        {
            return;
        }

        Log(writer, "SAILOR-047 top points candidates while waiting");
        foreach (PaperScannerCandidate candidate in displayCandidates)
        {
            Log(writer, candidate.ToDisplayLine());
        }
    }


    private static async Task<(ScanListRunResult RunResult, IReadOnlyList<string> SelectedSymbols, int RemainingIterations)> WaitForScanListEntrySelectionAsync(
        SailorRuntimeMode mode,
        SailorAppSettings settings,
        IbkrConnectionOptions connectionOptions,
        PaperScannerOptions scannerOptions,
        ScanListWorkbookOptions workbookOptions,
        string[] args,
        StreamWriter writer,
        int requestedTopCount,
        int requestedIterations,
        int cadenceSeconds)
    {
        int safeCadenceSeconds = Math.Max(1, cadenceSeconds);
        int totalWindowSeconds = Math.Max(safeCadenceSeconds, requestedIterations * safeCadenceSeconds);
        int maxWaitSeconds = ReadIntOption(args, "--scan-entry-wait-seconds", totalWindowSeconds);
        int refreshSeconds = Math.Max(1, ReadIntOption(args, "--scan-refresh-seconds", workbookOptions.RefreshSeconds));
        int targetSymbols = Math.Max(1, Math.Min(requestedTopCount, ReadIntOption(args, "--scan-entry-target", requestedTopCount)));
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        ScanListRunResult latestResult = new("n/a", "n/a", Array.Empty<ScanListCycleResult>(), Array.Empty<string>());
        IReadOnlyList<string> selectedSymbols = Array.Empty<string>();

        Log(writer, "");
        Log(writer, "SAILOR-043 wait-for-scan-entry is active.");
        Log(writer, $"The paper run will keep rescanning until at least one trade-eligible symbol appears, up to target={targetSymbols}, or until {maxWaitSeconds}s of the runtime window has elapsed.");
        Log(writer, "Orders remain blocked while no trade-eligible scan-list symbol is available.");

        while (selectedSymbols.Count == 0)
        {
            int elapsedSeconds = (int)Math.Max(0, Math.Round((DateTimeOffset.UtcNow - startedUtc).TotalSeconds, MidpointRounding.AwayFromZero));
            int remainingWaitSeconds = maxWaitSeconds - elapsedSeconds;
            if (remainingWaitSeconds <= 0)
            {
                break;
            }

            int delaySeconds = Math.Min(refreshSeconds, remainingWaitSeconds);
            Log(writer, $"SAILOR-043 waiting {delaySeconds}s before rescanning scan-list; elapsed={elapsedSeconds}s remainingWait={remainingWaitSeconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);

            latestResult = await RunScanListSelectionForConductAsync(
                mode,
                settings,
                connectionOptions,
                scannerOptions,
                workbookOptions,
                args,
                writer,
                "paper run wait-for-scan-entry").ConfigureAwait(false);

            selectedSymbols = SelectScanListTradeSymbols(latestResult, targetSymbols, livePilot: false);
            if (selectedSymbols.Count > 0)
            {
                Log(writer, $"SAILOR-043 found trade-eligible scan-list symbols: {string.Join(", ", selectedSymbols)}");
                break;
            }

            LogScanListEntryWaitDiagnostics(writer, latestResult, "wait-for-scan-entry rescan");
        }

        int totalElapsedSeconds = (int)Math.Max(0, Math.Round((DateTimeOffset.UtcNow - startedUtc).TotalSeconds, MidpointRounding.AwayFromZero));
        int consumedIterations = Math.Min(Math.Max(0, requestedIterations - 1), totalElapsedSeconds / safeCadenceSeconds);
        int remainingIterations = Math.Max(1, requestedIterations - consumedIterations);

        if (selectedSymbols.Count > 0)
        {
            Log(writer, $"SAILOR-043 scan wait consumed approximately {totalElapsedSeconds}s. Remaining conduct iterations={remainingIterations}.");
        }

        return (latestResult, selectedSymbols, remainingIterations);
    }


    private static async Task RunStrategySkeletonAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions runtimeOptions = CreateOptions(mode, args, settings);
        string logFilePath = CreateRuntimeLogFilePath(mode, "run");

        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {runtimeOptions.ModeName} paper conduct runtime started");
        Log(writer, runtimeOptions.ToCompactString());
        Log(writer, "");

        if (mode == SailorRuntimeMode.Live)
        {
            await RunLivePilotAsync(args, settings, runtimeOptions, logFilePath, writer);
            return;
        }

        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        string account = ReadStringOption(args, "--account", modeSettings.Account ?? string.Empty);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        int waitSeconds = ReadIntOption(args, "--wait-seconds", modeSettings.ConnectTimeoutSeconds);
        int cadenceSeconds = ReadIntOption(args, "--cadence-seconds", 1);
        int quantity = ReadIntOption(args, "--quantity", 1);
        int marketDataType = ReadIntOption(args, "--market-data-type", 1);
        int days = ReadIntOption(args, "--days", 5);
        int snapshotSeconds = ReadIntOption(args, "--snapshot-seconds", 2);
        int depthLevels = ReadIntOption(args, "--levels", settings.L1L2.DepthLevels <= 0 ? 5 : settings.L1L2.DepthLevels);
        int seconds = ReadIntOption(args, "--seconds", 10);
        int iterations = ReadIntOption(args, "--iterations", Math.Max(1, seconds / Math.Max(1, cadenceSeconds)));
        int executionRequestId = ReadIntOption(args, "--execution-request-id", 30_000);
        int reconnectAttempts = ReadIntOption(args, "--reconnect-attempts", 3);
        int reconnectBackoffSeconds = ReadIntOption(args, "--reconnect-backoff-seconds", 2);
        int simulateDisconnectAtIteration = ReadIntOption(args, "--simulate-disconnect-at", 0, allowZero: true);

        bool localCache = args.Any(arg => arg.Equals("--local-cache", StringComparison.OrdinalIgnoreCase));
        bool requestIbkrHistory = !localCache && !args.Any(arg => arg.Equals("--no-history-refresh", StringComparison.OrdinalIgnoreCase));
        bool mirrorHistoryToBacktest = !args.Any(arg => arg.Equals("--no-backtest-copy", StringComparison.OrdinalIgnoreCase));
        bool useRth = !args.Any(arg => arg.Equals("--all-hours", StringComparison.OrdinalIgnoreCase));
        bool captureSnapshots = !args.Any(arg => arg.Equals("--no-quotes", StringComparison.OrdinalIgnoreCase));
        bool useL2 = runtimeOptions.UseL2 || args.Any(arg => arg.Equals("--with-depth", StringComparison.OrdinalIgnoreCase));
        if (args.Any(arg => arg.Equals("--no-depth", StringComparison.OrdinalIgnoreCase)))
        {
            useL2 = false;
        }

        bool requestIbkrMarketData = captureSnapshots && !localCache;
        bool smartDepth = args.Any(arg => arg.Equals("--smart-depth", StringComparison.OrdinalIgnoreCase));
        PointsScannerMode scannerMode = ReadScannerMode(args, settings);
        decimal pointsMinimumTradeScore = ReadDecimalOption(args, "--points-min-trade-score", settings.Scanner.PointsMinimumTradeScore);
        bool pointsAllowWeakEntry = ReadBooleanOption(args, "--points-allow-weak-entry", settings.Scanner.PointsAllowWeakEntry);
        bool pointsRetainWatchOnly = ReadBooleanOption(args, "--points-retain-watch-only", settings.Scanner.PointsRetainWatchOnly);
        bool forceFlatNow = args.Any(arg => arg.Equals("--force-flat-now", StringComparison.OrdinalIgnoreCase));

        bool sendOrdersRequested = args.Any(arg => arg.Equals("--send-orders", StringComparison.OrdinalIgnoreCase));
        bool dryRunRequested = args.Any(arg => arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        bool sendOrders = mode == SailorRuntimeMode.Paper && sendOrdersRequested && !dryRunRequested;
        bool dryRun = !sendOrders;

        if (sendOrdersRequested && !sendOrders)
        {
            Log(writer, "WARN: --send-orders was requested but the command is still dry-run. Check mode and --dry-run flag.");
        }

        var connectionOptions = new IbkrConnectionOptions(
            mode,
            runtimeOptions.Host,
            runtimeOptions.Port,
            runtimeOptions.ClientId,
            account,
            modeSettings.ConnectTimeoutSeconds,
            runtimeOptions.UseL1,
            runtimeOptions.UseL2,
            sendOrders,
            runtimeOptions.AllowShort);

        int defaultMaxSymbols = localCache
            ? int.MaxValue
            : Math.Max(runtimeOptions.TopCount, runtimeOptions.TopCount * 3);
        int maxSymbols = ReadIntOption(args, "--max-symbols", defaultMaxSymbols);

        var scannerOptions = new PaperScannerOptions(
            mode,
            runtimeOptions.Timeframe,
            runtimeOptions.ProfileName,
            runtimeOptions.Universe,
            runtimeOptions.TopCount,
            maxSymbols,
            days,
            requestIbkrHistory,
            mirrorHistoryToBacktest,
            useRth,
            captureSnapshots,
            requestIbkrMarketData,
            runtimeOptions.UseL1,
            useL2,
            snapshotSeconds,
            depthLevels,
            marketDataType,
            primaryExchange,
            smartDepth,
            scannerMode,
            pointsMinimumTradeScore,
            pointsAllowWeakEntry,
            pointsRetainWatchOnly);

        PaperScannerOptions replenishmentScannerOptions = scannerOptions;

        if (mode == SailorRuntimeMode.Paper && HasScanListInput(args))
        {
            ScanListWorkbookOptions workbookOptions = CreateScanListWorkbookOptions(args, runtimeOptions.TopCount);
            ScanListRunResult scanListResult = await RunScanListSelectionForConductAsync(
                mode,
                settings,
                connectionOptions,
                scannerOptions,
                workbookOptions,
                args,
                writer,
                "paper run").ConfigureAwait(false);

            IReadOnlyList<string> selectedScanListSymbols = SelectScanListTradeSymbols(scanListResult, runtimeOptions.TopCount, livePilot: false);
            if (selectedScanListSymbols.Count == 0 && HasOption(args, "--wait-for-scan-entry"))
            {
                LogScanListEntryWaitDiagnostics(writer, scanListResult, "initial scan before wait");
                (scanListResult, selectedScanListSymbols, iterations) = await WaitForScanListEntrySelectionAsync(
                    mode,
                    settings,
                    connectionOptions,
                    scannerOptions,
                    workbookOptions,
                    args,
                    writer,
                    runtimeOptions.TopCount,
                    iterations,
                    cadenceSeconds).ConfigureAwait(false);
            }

            if (selectedScanListSymbols.Count == 0)
            {
                Log(writer, "");
                Log(writer, "SAILOR-040 blocked paper dynamic scan-list conduct because no trade-eligible symbols were retained. No orders sent.");
                if (HasOption(args, "--wait-for-scan-entry"))
                {
                    Log(writer, "SAILOR-043 wait-for-scan-entry expired without a trade-eligible symbol. No orders sent.");
                }
                Log(writer, $"Runtime log: {logFilePath}");
                return;
            }

            string selectedUniverse = string.Join(',', selectedScanListSymbols);
            int selectedTopCount = Math.Min(runtimeOptions.TopCount, selectedScanListSymbols.Count);
            runtimeOptions = runtimeOptions with
            {
                Universe = selectedUniverse,
                TopCount = selectedTopCount
            };
            scannerOptions = scannerOptions with
            {
                Universe = selectedUniverse,
                TopCount = selectedTopCount,
                MaxSymbolsToPrepare = selectedScanListSymbols.Count
            };

            Log(writer, $"SAILOR-040 paper dynamic conduct selected symbols: {selectedUniverse}");
        }

        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, scannerOptions.ToDisplayString());
        Log(writer, $"quantity={quantity} cadenceSeconds={cadenceSeconds} iterations={iterations} waitSeconds={waitSeconds} sendOrdersRequested={sendOrdersRequested} sendOrders={sendOrders} dryRun={dryRun} account={(string.IsNullOrWhiteSpace(account) ? "not-configured" : account)}");
        Log(writer, $"reconnectAttempts={reconnectAttempts} reconnectBackoffSeconds={reconnectBackoffSeconds} simulateDisconnectAtIteration={simulateDisconnectAtIteration}");
        Log(writer, "");

        ReconciliationResult reconciliation;
        var reconciliationService = new ReconciliationService(mode);
        if (sendOrders)
        {
            var positionRequest = new PositionRequest(
                mode,
                account,
                TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
                executionRequestId);

            await using (IPositionProvider positionProvider = CreatePositionProvider(localOnly: false, connectionOptions))
            {
                Log(writer, $"Pre-run reconciliation provider: {positionProvider.ProviderName}");
                reconciliation = await reconciliationService.ReconcileAsync(positionProvider, positionRequest, CancellationToken.None);
            }

            Log(writer, reconciliation.ToSummaryString());
            Log(writer, reconciliation.Message);
            foreach (string warning in reconciliation.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }
        else
        {
            reconciliation = reconciliationService.BuildLocalStatus();
            Log(writer, reconciliation.ToSummaryString());
            Log(writer, "Dry-run conduct uses local-only status and assumes fills locally. Broker state is not requested.");
            foreach (string warning in reconciliation.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }

        RuntimeReconciliationDelegate? brokerReconcileAsync = null;
        int nextRecoveryExecutionRequestId = executionRequestId + 1000;
        if (sendOrders)
        {
            brokerReconcileAsync = async cancellationToken =>
            {
                await using IPositionProvider recoveryProvider = CreatePositionProvider(localOnly: false, connectionOptions);
                var recoveryRequest = new PositionRequest(
                    mode,
                    account,
                    TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
                    nextRecoveryExecutionRequestId++);

                return await reconciliationService.ReconcileAsync(recoveryProvider, recoveryRequest, cancellationToken).ConfigureAwait(false);
            };
        }

        bool canOpenEntries = dryRun || reconciliation.CanOpenNewEntries;
        if (sendOrders && !reconciliation.CanOpenNewEntries)
        {
            Log(writer, "");
            Log(writer, "SAILOR-031 blocked before conduct loop because broker reconciliation did not match. No orders sent. Runtime remains close-only until reconcile is clean.");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        Log(writer, "");

        var request = new PaperRuntimeHostRequest(
            runtimeOptions,
            connectionOptions,
            scannerOptions,
            reconciliation,
            sendOrders,
            dryRun,
            canOpenEntries,
            account,
            quantity,
            cadenceSeconds,
            iterations,
            waitSeconds,
            primaryExchange,
            forceFlatNow,
            reconnectAttempts,
            reconnectBackoffSeconds,
            simulateDisconnectAtIteration,
            brokerReconcileAsync,
            ReplenishmentScannerOptions: replenishmentScannerOptions,
            BlockStaleHistoricalReplay: settings.Runtime.Safety.BlockStaleHistoricalReplay,
            LiveBarMaxAgeMinutes: settings.Runtime.Safety.LiveBarMaxAgeMinutes,
            LiveBarFutureToleranceMinutes: settings.Runtime.Safety.LiveBarFutureToleranceMinutes,
            LiveCandleRefreshEnabled: settings.Runtime.Safety.LiveCandleRefreshEnabled,
            LiveCandleRefreshLookbackMinutes: settings.Runtime.Safety.LiveCandleRefreshLookbackMinutes,
            LiveCandleRefreshClientIdOffset: settings.Runtime.Safety.LiveCandleRefreshClientIdOffset,
            LiveCandleRefreshRequestIdBase: settings.Runtime.Safety.LiveCandleRefreshRequestIdBase);

        var host = new PaperRuntimeHost(settings, message => Log(writer, message));
        PaperRuntimeHostResult result = await host.RunAsync(request, CancellationToken.None);

        Log(writer, "");
        Log(writer, result.ToDisplayString());
        if (result.Warnings.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Warnings");
            Log(writer, "--------");
            foreach (string warning in result.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }

        Log(writer, "");
        Log(writer, result.OrderIntentCount > 0 ? "SAILOR-030 conduct loop produced order intents." : "SAILOR-030 conduct loop produced no order intents.");
        Log(writer, "SAILOR-031 degraded-state handling was active for this paper run.");
        Log(writer, sendOrders ? "Paper send-orders mode was active." : "Dry-run mode: no broker orders were sent.");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunLivePilotAsync(
        string[] args,
        SailorAppSettings settings,
        SailorRuntimeOptions initialOptions,
        string logFilePath,
        StreamWriter writer)
    {
        SailorRuntimeModeSettings liveSettings = settings.Runtime.Live;
        string account = ReadStringOption(args, "--account", liveSettings.Account ?? string.Empty);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        int waitSeconds = ReadIntOption(args, "--wait-seconds", liveSettings.ConnectTimeoutSeconds);
        int cadenceSeconds = ReadIntOption(args, "--cadence-seconds", 1);
        int quantity = ReadIntOption(args, "--quantity", 1);
        int marketDataType = ReadIntOption(args, "--market-data-type", 1);
        int days = ReadIntOption(args, "--days", 5);
        int snapshotSeconds = ReadIntOption(args, "--snapshot-seconds", 2);
        int depthLevels = ReadIntOption(args, "--levels", settings.L1L2.DepthLevels <= 0 ? 5 : settings.L1L2.DepthLevels);
        int seconds = ReadIntOption(args, "--seconds", 10);
        int iterations = ReadIntOption(args, "--iterations", Math.Max(1, seconds / Math.Max(1, cadenceSeconds)));
        int executionRequestId = ReadIntOption(args, "--execution-request-id", 34_000);
        int reconnectAttempts = ReadIntOption(args, "--reconnect-attempts", Math.Max(1, settings.Runtime.Safety.MaxReconnectAttempts));
        int reconnectBackoffSeconds = ReadIntOption(args, "--reconnect-backoff-seconds", Math.Max(1, settings.Runtime.Safety.ReconnectDelaySeconds));
        int simulateDisconnectAtIteration = ReadIntOption(args, "--simulate-disconnect-at", 0, allowZero: true);
        int requestedPilotTopCount = Math.Max(1, initialOptions.TopCount);
        decimal requestedPilotMaxNotional = ReadDecimalOption(args, "--max-notional", liveSettings.MaxOrderNotional <= 0m ? 100m : liveSettings.MaxOrderNotional);
        LiveMultiSymbolPilotGateResult multiSymbolGate = LiveMultiSymbolPilotGate.Evaluate(settings, args, requestedPilotTopCount, requestedPilotMaxNotional);

        bool dryRunRequested = args.Any(arg => arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        bool localCache = args.Any(arg => arg.Equals("--local-cache", StringComparison.OrdinalIgnoreCase));
        bool requestIbkrHistory = !localCache && !args.Any(arg => arg.Equals("--no-history-refresh", StringComparison.OrdinalIgnoreCase));
        bool mirrorHistoryToBacktest = !args.Any(arg => arg.Equals("--no-backtest-copy", StringComparison.OrdinalIgnoreCase));
        bool useRth = !args.Any(arg => arg.Equals("--all-hours", StringComparison.OrdinalIgnoreCase));
        bool captureSnapshots = !args.Any(arg => arg.Equals("--no-quotes", StringComparison.OrdinalIgnoreCase));
        bool useL2 = initialOptions.UseL2 || args.Any(arg => arg.Equals("--with-depth", StringComparison.OrdinalIgnoreCase));
        if (args.Any(arg => arg.Equals("--no-depth", StringComparison.OrdinalIgnoreCase)))
        {
            useL2 = false;
        }

        bool requestIbkrMarketData = captureSnapshots && !localCache;
        bool smartDepth = args.Any(arg => arg.Equals("--smart-depth", StringComparison.OrdinalIgnoreCase));
        PointsScannerMode scannerMode = ReadScannerMode(args, settings);
        decimal pointsMinimumTradeScore = ReadDecimalOption(args, "--points-min-trade-score", settings.Scanner.PointsMinimumTradeScore);
        bool pointsAllowWeakEntry = ReadBooleanOption(args, "--points-allow-weak-entry", settings.Scanner.PointsAllowWeakEntry);
        bool pointsRetainWatchOnly = ReadBooleanOption(args, "--points-retain-watch-only", settings.Scanner.PointsRetainWatchOnly);
        bool forceFlatNow = args.Any(arg => arg.Equals("--force-flat-now", StringComparison.OrdinalIgnoreCase));
        ScanListRuntimeEvidence? liveScanListEvidence = null;
        string? liveScanListEvidencePath = null;
        LiveDynamicScanPilotSelection? liveDynamicSelection = null;

        if (HasScanListInput(args))
        {
            ScanListWorkbookOptions workbookOptions = CreateScanListWorkbookOptions(args, initialOptions.TopCount);
            int scanMaxSymbols = ReadIntOption(args, "--max-symbols", Math.Max(1, workbookOptions.HistoryBatchSize));
            var scanListConnectionOptions = new IbkrConnectionOptions(
                SailorRuntimeMode.Live,
                initialOptions.Host,
                initialOptions.Port,
                initialOptions.ClientId,
                account,
                liveSettings.ConnectTimeoutSeconds,
                initialOptions.UseL1,
                initialOptions.UseL2,
                SendOrders: false,
                AllowShort: false);
            var scanListBaseOptions = new PaperScannerOptions(
                SailorRuntimeMode.Live,
                initialOptions.Timeframe,
                initialOptions.ProfileName,
                initialOptions.Universe,
                Math.Max(1, workbookOptions.TradeTop),
                scanMaxSymbols,
                days,
                requestIbkrHistory,
                mirrorHistoryToBacktest,
                useRth,
                captureSnapshots,
                requestIbkrMarketData,
                initialOptions.UseL1,
                useL2,
                snapshotSeconds,
                depthLevels,
                marketDataType,
                primaryExchange,
                smartDepth,
                scannerMode,
                pointsMinimumTradeScore,
                pointsAllowWeakEntry,
                pointsRetainWatchOnly);

            ScanListRunResult scanListResult = await RunScanListSelectionForConductAsync(
                SailorRuntimeMode.Live,
                settings,
                scanListConnectionOptions,
                scanListBaseOptions,
                workbookOptions,
                args,
                writer,
                "live run pilot").ConfigureAwait(false);

            liveDynamicSelection = new LiveDynamicScanPilotHost().SelectBestOne(scanListResult);
            liveScanListEvidence = liveDynamicSelection.Evidence;
            liveScanListEvidencePath = liveDynamicSelection.EvidencePath;
            Log(writer, liveDynamicSelection.ToSummaryString());
            if (!liveDynamicSelection.Passed)
            {
                Log(writer, "");
                Log(writer, liveDynamicSelection.Reason);
                Log(writer, "SAILOR-041 blocked live dynamic scan-list pilot because no trade-eligible symbol was retained. No live order was sent.");
                Log(writer, $"Runtime log: {logFilePath}");
                return;
            }

            string selectedSymbol = liveDynamicSelection.Symbol;
            initialOptions = initialOptions with
            {
                Universe = selectedSymbol,
                TopCount = 1
            };
            Log(writer, $"SAILOR-041 live scan-list pilot selected symbol: {selectedSymbol}");
            Log(writer, "Live trading remains limited to one selected symbol by SAILOR-034 pilot restrictions.");
            Log(writer, "");
        }

        LiveReadinessGateResult gate = EvaluateLiveReadiness(
            settings,
            dryRunRequested ? "live run dry-run" : "live run pilot",
            args,
            requiresTrading: !dryRunRequested,
            readOnly: dryRunRequested,
            account: account);
        LiveReadinessGateOutput gateOutput = new LiveReadinessGate(settings).WriteResult(gate);

        LivePilotRestrictionResult restrictions = EvaluateLivePilotRestrictions(initialOptions, args, settings);
        LivePointsPilotGateResult livePointsGate = LivePointsPilotGate.Evaluate(
            scannerMode,
            requiresTrading: !dryRunRequested,
            selection: liveDynamicSelection,
            minimumTradeScore: pointsMinimumTradeScore);
        bool scanListDataQualityAllowsTrading = liveScanListEvidence is null
            || liveScanListEvidence.DataQualityStatus.Equals("Clean", StringComparison.OrdinalIgnoreCase);
        bool multiSymbolGateAllowsTrading = !multiSymbolGate.Requested || multiSymbolGate.Allowed;
        bool livePointsGateAllowsTrading = !livePointsGate.Required || livePointsGate.Allowed;
        bool sendOrders = !dryRunRequested && gate.LiveTradingAllowed && restrictions.Passed && scanListDataQualityAllowsTrading && multiSymbolGateAllowsTrading && livePointsGateAllowsTrading;
        bool dryRun = !sendOrders;

        var runtimeOptions = initialOptions with
        {
            Universe = restrictions.Symbol,
            TopCount = 1,
            DryRun = dryRun,
            SendOrders = sendOrders,
            AllowShort = restrictions.ShortEnabled
        };

        var connectionOptions = new IbkrConnectionOptions(
            SailorRuntimeMode.Live,
            runtimeOptions.Host,
            runtimeOptions.Port,
            runtimeOptions.ClientId,
            account,
            liveSettings.ConnectTimeoutSeconds,
            runtimeOptions.UseL1,
            runtimeOptions.UseL2,
            sendOrders,
            runtimeOptions.AllowShort);

        var scannerOptions = new PaperScannerOptions(
            SailorRuntimeMode.Live,
            runtimeOptions.Timeframe,
            runtimeOptions.ProfileName,
            restrictions.Symbol,
            1,
            1,
            days,
            requestIbkrHistory,
            mirrorHistoryToBacktest,
            useRth,
            captureSnapshots,
            requestIbkrMarketData,
            runtimeOptions.UseL1,
            useL2,
            snapshotSeconds,
            depthLevels,
            marketDataType,
            primaryExchange,
            smartDepth,
            scannerMode,
            pointsMinimumTradeScore,
            pointsAllowWeakEntry,
            pointsRetainWatchOnly);

        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, scannerOptions.ToDisplayString());
        Log(writer, $"quantity={quantity} cadenceSeconds={cadenceSeconds} iterations={iterations} waitSeconds={waitSeconds} dryRun={dryRun} sendOrders={sendOrders} account={(string.IsNullOrWhiteSpace(account) ? "not-configured" : account)} maxNotional={gate.RequestedMaxNotional:F2}");
        Log(writer, $"reconnectAttempts={reconnectAttempts} reconnectBackoffSeconds={reconnectBackoffSeconds} simulateDisconnectAtIteration={simulateDisconnectAtIteration}");
        Log(writer, "");
        Log(writer, "SAILOR-034 implementation: live pilot.");
        Log(writer, "Restrictions: one explicit symbol, one profile, long-only unless explicitly enabled, small max notional, close-only flatten command, force-flat safety, and operator watching TWS.");
        Log(writer, "Live orders are routed only when Runtime.Live.AllowLiveTrading=true, --confirm-live is supplied, paper certification is promotable, account matches, max notional is small, and live-pilot restrictions pass.");
        Log(writer, "");
        LogLiveReadinessGate(writer, gate);
        Log(writer, $"Readiness JSON: {gateOutput.JsonPath}");
        Log(writer, $"Readiness CSV:  {gateOutput.CsvPath}");
        Log(writer, "");
        LogLiveMultiSymbolPilotGate(writer, multiSymbolGate);
        Log(writer, "");
        LogLivePointsPilotGate(writer, livePointsGate);
        Log(writer, "");
        Log(writer, restrictions.ToSummaryString());
        foreach (string check in restrictions.Checks)
        {
            Log(writer, check);
        }
        foreach (string warning in restrictions.Warnings)
        {
            Log(writer, $"WARN: {warning}");
        }

        if (!gate.LiveTradingAllowed || !restrictions.Passed || !scanListDataQualityAllowsTrading || !multiSymbolGateAllowsTrading || !livePointsGateAllowsTrading)
        {
            var blockedReport = LivePilotReport.From(
                gate,
                restrictions,
                preRunReconciliation: null,
                finalReconciliation: null,
                runtimeResult: null,
                warnings: restrictions.Warnings
                    .Concat(gate.Checks.Where(check => !check.Passed).Select(check => check.ToDisplayLine()))
                    .Concat(multiSymbolGate.Requested ? multiSymbolGate.Checks.Where(check => check.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) : Array.Empty<string>())
                    .Concat(livePointsGate.Required && !livePointsGate.Allowed ? livePointsGate.Checks.Where(check => check.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) : Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                scanListEvidence: liveScanListEvidence,
                scanListEvidencePath: liveScanListEvidencePath,
                livePointsGate: livePointsGate);
            LivePilotReportOutput blockedOutput = new LivePilotReportWriter().Write(blockedReport);
            Log(writer, "");
            string livePilotBlockReason = !gate.LiveTradingAllowed
                ? gate.Reason
                : !restrictions.Passed
                    ? restrictions.BlockReason
                    : !multiSymbolGateAllowsTrading
                        ? multiSymbolGate.Reason
                        : !livePointsGateAllowsTrading
                            ? livePointsGate.Reason
                            : $"Scan-list data quality is {liveScanListEvidence?.DataQualityStatus}: {liveScanListEvidence?.DataQualityReason}";
            Log(writer, livePilotBlockReason);
            Log(writer, multiSymbolGate.Requested
                ? "SAILOR-042 blocked the future multi-symbol live pilot before any broker order route could be created. No live order was sent."
                : "SAILOR-041 blocked live pilot before any broker order route could be created. No live order was sent.");
            Log(writer, blockedReport.ToSummaryString());
            Log(writer, $"Live pilot JSON: {blockedOutput.JsonPath}");
            Log(writer, $"Live pilot CSV:  {blockedOutput.CsvPath}");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        var reconciliationService = new ReconciliationService(SailorRuntimeMode.Live);
        var positionRequest = new PositionRequest(
            SailorRuntimeMode.Live,
            account,
            TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
            executionRequestId);

        Log(writer, "");
        ReconciliationResult preRunReconciliation;
        if (dryRun)
        {
            preRunReconciliation = reconciliationService.BuildLocalStatus();
            Log(writer, "Pre-run reconciliation provider: local-status");
        }
        else
        {
            await using IPositionProvider positionProvider = CreatePositionProvider(localOnly: false, connectionOptions);
            Log(writer, $"Pre-run reconciliation provider: {positionProvider.ProviderName}");
            preRunReconciliation = await reconciliationService.ReconcileAsync(positionProvider, positionRequest, CancellationToken.None).ConfigureAwait(false);
        }

        Log(writer, preRunReconciliation.ToSummaryString());
        Log(writer, preRunReconciliation.Message);
        foreach (string warning in preRunReconciliation.Warnings)
        {
            Log(writer, $"WARN: {warning}");
        }

        if (sendOrders && !preRunReconciliation.CanOpenNewEntries)
        {
            var blockedReport = LivePilotReport.From(
                gate,
                restrictions,
                preRunReconciliation,
                finalReconciliation: preRunReconciliation,
                runtimeResult: null,
                warnings: preRunReconciliation.Warnings,
                scanListEvidence: liveScanListEvidence,
                scanListEvidencePath: liveScanListEvidencePath,
                livePointsGate: livePointsGate);
            LivePilotReportOutput blockedOutput = new LivePilotReportWriter().Write(blockedReport);
            Log(writer, "");
            Log(writer, "SAILOR-034 blocked live pilot because pre-run live reconciliation did not match. No live order was sent.");
            Log(writer, blockedReport.ToSummaryString());
            Log(writer, $"Live pilot JSON: {blockedOutput.JsonPath}");
            Log(writer, $"Live pilot CSV:  {blockedOutput.CsvPath}");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        RuntimeReconciliationDelegate? brokerReconcileAsync = null;
        int nextRecoveryExecutionRequestId = executionRequestId + 1000;
        if (sendOrders)
        {
            brokerReconcileAsync = async cancellationToken =>
            {
                await using IPositionProvider recoveryProvider = CreatePositionProvider(localOnly: false, connectionOptions);
                var recoveryRequest = new PositionRequest(
                    SailorRuntimeMode.Live,
                    account,
                    TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
                    nextRecoveryExecutionRequestId++);

                return await reconciliationService.ReconcileAsync(recoveryProvider, recoveryRequest, cancellationToken).ConfigureAwait(false);
            };
        }

        var request = new PaperRuntimeHostRequest(
            runtimeOptions,
            connectionOptions,
            scannerOptions,
            preRunReconciliation,
            sendOrders,
            dryRun,
            dryRun || preRunReconciliation.CanOpenNewEntries,
            account,
            quantity,
            cadenceSeconds,
            iterations,
            waitSeconds,
            primaryExchange,
            forceFlatNow,
            reconnectAttempts,
            reconnectBackoffSeconds,
            simulateDisconnectAtIteration,
            brokerReconcileAsync,
            EnforceMaxOrderNotional: true,
            MaxOrderNotional: gate.RequestedMaxNotional,
            BlockStaleHistoricalReplay: settings.Runtime.Safety.BlockStaleHistoricalReplay,
            LiveBarMaxAgeMinutes: settings.Runtime.Safety.LiveBarMaxAgeMinutes,
            LiveBarFutureToleranceMinutes: settings.Runtime.Safety.LiveBarFutureToleranceMinutes,
            LiveCandleRefreshEnabled: settings.Runtime.Safety.LiveCandleRefreshEnabled,
            LiveCandleRefreshLookbackMinutes: settings.Runtime.Safety.LiveCandleRefreshLookbackMinutes,
            LiveCandleRefreshClientIdOffset: settings.Runtime.Safety.LiveCandleRefreshClientIdOffset,
            LiveCandleRefreshRequestIdBase: settings.Runtime.Safety.LiveCandleRefreshRequestIdBase);

        var host = new PaperRuntimeHost(settings, message => Log(writer, message));
        PaperRuntimeHostResult runtimeResult = await host.RunAsync(request, CancellationToken.None).ConfigureAwait(false);

        ReconciliationResult finalReconciliation;
        if (sendOrders)
        {
            await using IPositionProvider finalProvider = CreatePositionProvider(localOnly: false, connectionOptions);
            var finalRequest = new PositionRequest(
                SailorRuntimeMode.Live,
                account,
                TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
                executionRequestId + 2000);
            finalReconciliation = await reconciliationService.ReconcileAsync(finalProvider, finalRequest, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            finalReconciliation = reconciliationService.BuildLocalStatus();
        }

        var allWarnings = preRunReconciliation.Warnings
            .Concat(finalReconciliation.Warnings)
            .Concat(runtimeResult.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        LivePilotReport report = LivePilotReport.From(
            gate,
            restrictions,
            preRunReconciliation,
            finalReconciliation,
            runtimeResult,
            allWarnings,
            scanListEvidence: liveScanListEvidence,
            scanListEvidencePath: liveScanListEvidencePath,
            livePointsGate: livePointsGate);
        LivePilotReportOutput output = new LivePilotReportWriter().Write(report);

        Log(writer, "");
        Log(writer, "Final live reconciliation");
        Log(writer, "-------------------------");
        Log(writer, finalReconciliation.ToSummaryString());
        Log(writer, finalReconciliation.Message);
        foreach (string warning in finalReconciliation.Warnings)
        {
            Log(writer, $"WARN: {warning}");
        }

        Log(writer, "");
        Log(writer, report.ToSummaryString());
        Log(writer, $"promotion result: {(report.CanPromote ? "Passed" : "Blocked")} - {report.Reason}");
        Log(writer, $"Live pilot JSON: {output.JsonPath}");
        Log(writer, $"Live pilot CSV:  {output.CsvPath}");
        Log(writer, sendOrders ? "SAILOR-034 live pilot send-orders mode was active." : "SAILOR-034 live pilot dry-run mode: no broker orders were sent.");
        Log(writer, $"Runtime log: {logFilePath}");
    }


    private static async Task RunManualOrderAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        if (args.Length < 4)
        {
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} order TSLA BUY 1 LMT 400.00 --dry-run");
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} order TSLA BUY 1 MKT --dry-run");
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} order TSLA BUY 1 LMT 400.00 --send-orders");
            return;
        }

        string[] positional = args
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        if (positional.Length < 4)
        {
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} order SYMBOL BUY|SELL|SELL_SHORT|BUY_TO_COVER QTY MKT|LMT [LIMIT_PRICE]");
            return;
        }

        string symbol = positional[0].Trim().ToUpperInvariant();
        if (!TryParseOrderSide(positional[1], out SailorOrderSide side))
        {
            Console.WriteLine($"Unsupported side '{positional[1]}'. Use BUY, SELL, SELL_SHORT, or BUY_TO_COVER.");
            return;
        }

        if (!int.TryParse(positional[2], out int quantity) || quantity <= 0)
        {
            Console.WriteLine($"Invalid quantity '{positional[2]}'. Quantity must be a positive whole number.");
            return;
        }

        if (!TryParseOrderType(positional[3], out SailorOrderType orderType))
        {
            Console.WriteLine($"Unsupported order type '{positional[3]}'. Use MKT or LMT for SAILOR-028.");
            return;
        }

        decimal? limitPrice = null;
        if (orderType == SailorOrderType.Limit)
        {
            if (positional.Length < 5 || !decimal.TryParse(positional[4], out decimal parsedLimit) || parsedLimit <= 0m)
            {
                Console.WriteLine("Limit price is required for LMT orders and must be > 0.");
                return;
            }

            limitPrice = parsedLimit;
        }

        SailorRuntimeOptions runtimeOptions = CreateOptions(mode, ["1m", ReadStringOption(args, "--strategy", "manual-paper-order"), "1", symbol], settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        string account = ReadStringOption(args, "--account", modeSettings.Account ?? string.Empty);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        string timeInForce = ReadStringOption(args, "--tif", "DAY");
        string strategyName = ReadStringOption(args, "--strategy", "manual-paper-order");
        string reason = ReadStringOption(args, "--reason", "SAILOR-028 manual paper order command.");
        int waitSeconds = ReadIntOption(args, "--wait-seconds", 10);

        bool sendOrdersRequested = args.Any(arg => arg.Equals("--send-orders", StringComparison.OrdinalIgnoreCase));
        bool dryRunRequested = args.Any(arg => arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        bool sendOrders = mode == SailorRuntimeMode.Paper && sendOrdersRequested && !dryRunRequested;
        bool dryRun = !sendOrders;

        var connectionOptions = new IbkrConnectionOptions(
            mode,
            runtimeOptions.Host,
            runtimeOptions.Port,
            runtimeOptions.ClientId,
            account,
            modeSettings.ConnectTimeoutSeconds,
            runtimeOptions.UseL1,
            runtimeOptions.UseL2,
            sendOrders,
            runtimeOptions.AllowShort);

        SailorOrderIntent intent = SailorOrderIntent.CreateManual(
            mode,
            symbol,
            side,
            orderType,
            quantity,
            limitPrice,
            strategyName,
            reason,
            dryRun,
            account,
            timeInForce);

        string logFilePath = CreateRuntimeLogFilePath(mode, $"order_{symbol}");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {runtimeOptions.ModeName} order router started");
        Log(writer, runtimeOptions.ToCompactString());
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, intent.ToDisplayString());
        Log(writer, $"primaryExchange={primaryExchange} waitSeconds={waitSeconds} sendOrdersRequested={sendOrdersRequested} dryRun={dryRun}");
        Log(writer, "");
        Log(writer, "SAILOR-028 implementation: manual order intent and paper order router.");
        Log(writer, "This command creates a normalized SailorOrderIntent, writes the order ledger, and optionally submits to IBKR paper.");
        Log(writer, "Live order submission is blocked. Strategy conduct loop is still deferred.");
        Log(writer, "");

        if (sendOrdersRequested && !sendOrders)
        {
            Log(writer, "WARN: --send-orders was requested but the command is still dry-run. Check mode and --dry-run flag.");
        }

        if (mode == SailorRuntimeMode.Live)
        {
            decimal fallbackNotional = limitPrice is not null ? limitPrice.Value * quantity : 0m;
            LiveReadinessGateResult gate = EvaluateLiveReadiness(
                settings,
                "live order",
                args,
                requiresTrading: sendOrdersRequested,
                readOnly: !sendOrdersRequested,
                account: account,
                defaultMaxNotional: fallbackNotional);
            LogLiveReadinessGate(writer, gate);
            Log(writer, "");

            if (sendOrdersRequested)
            {
                Log(writer, gate.LiveTradingAllowed
                    ? "SAILOR-034 keeps manual live order routing disabled. Use live run for the one-symbol pilot or live flatten for close-only exit."
                    : "SAILOR-033 blocked live order submission before any broker order route could be created.");
                Log(writer, "No broker order was sent.");
                Log(writer, $"Runtime log: {logFilePath}");
                return;
            }
        }

        await using IOrderRouter router = IbkrOrderRouterFactory.Create(sendOrders, connectionOptions, primaryExchange, waitSeconds);
        Log(writer, $"Order router: {router.RouterName}");
        Log(writer, "");

        SailorOrderReceipt receipt = await router.SubmitAsync(intent, CancellationToken.None);
        var ledger = new OrderLedgerStore(mode);
        string ledgerPath = ledger.Append(intent, receipt);

        Log(writer, receipt.ToDisplayString());
        Log(writer, $"Ledger JSONL: {ledgerPath}");
        Log(writer, $"Ledger CSV:   {ledger.DailyCsvPath}");

        var tradeRegistry = new TradeLifecycleRegistryStore(mode);
        int registryQuantityAfter = EstimateManualCommandPositionAfter(intent, receipt);
        decimal registryAveragePrice = receipt.AverageFillPrice > 0m
            ? receipt.AverageFillPrice
            : limitPrice ?? 0m;
        TradeLifecycle lifecycle = tradeRegistry.ApplyOrderReceipt(
            intent,
            receipt,
            SailorTradeOrigin.SailorManualCommand,
            registryQuantityAfter,
            registryAveragePrice,
            scannerSlotId: null,
            sourceMessage: "SAILOR-051 manual order command lifecycle evidence.");
        Log(writer, $"Trade registry latest JSON: {tradeRegistry.LatestJsonPath}");
        Log(writer, $"Trade registry event JSONL: {tradeRegistry.DailyJsonlPath}");
        Log(writer, $"Trade lifecycle: {lifecycle.ToDisplayString()}");

        if (receipt.Events.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Order events");
            Log(writer, "------------");
            foreach (string row in receipt.Events)
            {
                Log(writer, row);
            }
        }

        if (receipt.Warnings.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Warnings");
            Log(writer, "--------");
            foreach (string warning in receipt.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }

        Log(writer, "");
        Log(writer, receipt.SentToBroker ? "Order was sent to IBKR paper." : "No order was sent to broker.");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunTradesAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        string subcommand = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))?.Trim().ToLowerInvariant() ?? "status";
        if (subcommand is "mirror" or "sync" or "detect")
        {
            await RunBrokerMirrorAsync(mode, args.Skip(1).ToArray(), settings, commandName: "trades mirror").ConfigureAwait(false);
            return;
        }

        if (subcommand is not "status" and not "list")
        {
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} trades status [--all] [--symbol TSLA]");
            Console.WriteLine($"       sailor {mode.ToDisplayName()} trades mirror --account DU123456 --wait-seconds 15");
            return;
        }

        _ = settings;
        bool includeClosed = args.Any(arg => arg.Equals("--all", StringComparison.OrdinalIgnoreCase));
        string symbolFilter = ReadStringOption(args, "--symbol", string.Empty).Trim().ToUpperInvariant();
        var tradeRegistry = new TradeLifecycleRegistryStore(mode);
        TradeLifecycleRegistrySnapshot snapshot = tradeRegistry.LoadSnapshot();
        IReadOnlyList<TradeLifecycle> trades = snapshot.Trades
            .Where(trade => includeClosed || trade.IsActive)
            .Where(trade => string.IsNullOrWhiteSpace(symbolFilter) || trade.NormalizedSymbol.Equals(symbolFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.IsActive)
            .ThenBy(trade => trade.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(trade => trade.UpdatedUtc)
            .ToArray();

        string logFilePath = CreateRuntimeLogFilePath(mode, "trades_status");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {mode.ToDisplayName()} trades status");
        Log(writer, "SAILOR-051 trade lifecycle registry and ownership model.");
        Log(writer, "This command reads local registry evidence only. It does not connect to TWS, reconcile broker state, conduct strategies, or send orders.");
        Log(writer, $"Trade registry latest JSON: {tradeRegistry.LatestJsonPath}");
        Log(writer, $"Trade registry event JSONL: {tradeRegistry.DailyJsonlPath}");
        Log(writer, snapshot.ToSummaryString());
        Log(writer, $"filter activeOnly={!includeClosed} symbol={(string.IsNullOrWhiteSpace(symbolFilter) ? "all" : symbolFilter)} displayed={trades.Count}");
        Log(writer, "");

        if (trades.Count == 0)
        {
            Log(writer, "No trade lifecycles found for this filter.");
        }
        else
        {
            Log(writer, "Trade lifecycles");
            Log(writer, "----------------");
            foreach (TradeLifecycle trade in trades)
            {
                Log(writer, trade.ToDisplayString());
            }
        }

        Log(writer, "");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunBrokerAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        string subcommand = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))?.Trim().ToLowerInvariant() ?? "mirror";
        if (subcommand is "mirror" or "sync" or "detect")
        {
            await RunBrokerMirrorAsync(mode, args.Skip(1).ToArray(), settings, commandName: "broker mirror").ConfigureAwait(false);
            return;
        }

        Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} broker mirror --account DU123456 --wait-seconds 15");
    }

    private static async Task RunBrokerMirrorAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings,
        string commandName)
    {
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        SailorRuntimeOptions options = CreateOptions(mode, Array.Empty<string>(), settings);
        string account = ReadStringOption(args, "--account", modeSettings.Account ?? string.Empty);
        int waitSeconds = ReadIntOption(args, "--wait-seconds", modeSettings.ConnectTimeoutSeconds);
        int executionRequestId = ReadIntOption(args, "--execution-request-id", 52000);
        bool localOnly = HasOption(args, "--local-only");
        bool markIntraday = HasOption(args, "--intraday") || ReadBooleanOption(args, "--manual-intraday", defaultValue: false);
        bool noManualClose = HasOption(args, "--no-manual-close-detect");

        var connectionOptions = new IbkrConnectionOptions(
            mode,
            options.Host,
            options.Port,
            options.ClientId,
            account,
            modeSettings.ConnectTimeoutSeconds,
            options.UseL1,
            options.UseL2,
            SendOrders: false,
            AllowShort: options.AllowShort);

        var request = new PositionRequest(
            mode,
            account,
            TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
            executionRequestId);

        string logFilePath = CreateRuntimeLogFilePath(mode, "broker_mirror");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {mode.ToDisplayName()} {commandName}");
        Log(writer, "SAILOR-052 broker state mirror and manual trade detector.");
        Log(writer, "This command requests broker positions/open orders/executions, persists a broker mirror snapshot, and synchronizes the SAILOR-051 trade lifecycle registry.");
        Log(writer, "It sends no orders and does not conduct strategies.");
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, request.ToDisplayString());
        Log(writer, $"localOnly={localOnly} unknownBrokerPositionsAreIntradayManual={markIntraday} manualCloseDetect={!noManualClose}");
        Log(writer, "");

        await using IPositionProvider provider = CreatePositionProvider(localOnly, connectionOptions);
        Log(writer, $"Position provider: {provider.ProviderName}");

        var reconciliationService = new ReconciliationService(mode);
        ReconciliationResult reconciliation = await reconciliationService.ReconcileAsync(provider, request, CancellationToken.None).ConfigureAwait(false);
        Log(writer, reconciliation.ToSummaryString());
        Log(writer, reconciliation.Message);
        foreach (string warning in reconciliation.Warnings)
        {
            Log(writer, $"WARN: {warning}");
        }

        bool brokerVerified = !localOnly;
        var tradeRegistry = new TradeLifecycleRegistryStore(mode);
        var detector = new BrokerStateManualTradeDetector(mode, tradeRegistry);
        BrokerStateMirrorSnapshot mirror = detector.MirrorAndDetect(
            reconciliation,
            account,
            brokerVerified,
            unknownBrokerPositionsAreIntradayManual: markIntraday,
            markMissingActivePositionsAsManualClosed: !noManualClose,
            source: commandName.Replace(' ', '-'));

        Log(writer, "");
        Log(writer, mirror.ToSummaryString());
        Log(writer, $"Broker mirror latest JSON: {detector.LatestJsonPath}");
        Log(writer, $"Broker mirror event JSONL: {detector.DailyJsonlPath}");
        Log(writer, $"Trade registry latest JSON: {tradeRegistry.LatestJsonPath}");
        Log(writer, $"Trade registry event JSONL: {tradeRegistry.DailyJsonlPath}");

        Log(writer, "");
        if (mirror.Positions.Count == 0)
        {
            Log(writer, "Broker positions: none");
        }
        else
        {
            Log(writer, "Broker positions mirrored");
            Log(writer, "-------------------------");
            foreach (BrokerMirrorPositionRow position in mirror.Positions)
            {
                Log(writer, position.ToDisplayString());
            }
        }

        if (mirror.OpenOrders.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Broker open orders mirrored");
            Log(writer, "---------------------------");
            foreach (BrokerMirrorOpenOrderRow order in mirror.OpenOrders)
            {
                Log(writer, order.ToDisplayString());
            }
        }

        if (mirror.Executions.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Broker executions mirrored");
            Log(writer, "--------------------------");
            foreach (BrokerMirrorExecutionRow execution in mirror.Executions.Take(30))
            {
                Log(writer, execution.ToDisplayString());
            }
        }

        Log(writer, "");
        if (mirror.Detections.Count == 0)
        {
            Log(writer, "Manual/external detections: none");
        }
        else
        {
            Log(writer, "Manual/external detections");
            Log(writer, "--------------------------");
            foreach (BrokerMirrorDetection detection in mirror.Detections)
            {
                Log(writer, detection.ToDisplayString());
            }
        }

        if (mirror.Warnings.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Warnings");
            Log(writer, "--------");
            foreach (string warning in mirror.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }

        Log(writer, "");
        Log(writer, "No orders sent.");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunStatusAsync(
        SailorRuntimeMode mode,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions options = CreateOptions(mode, Array.Empty<string>(), settings);
        var state = new SailorRuntimeState(mode);
        state.SetStatus(SailorRuntimeStatus.Stopped, "Runtime status from local state files. Use reconcile to verify broker state.");

        var service = new ReconciliationService(mode);
        ReconciliationResult localStatus = service.BuildLocalStatus();
        ReconciliationResult? lastReconciliation = service.LoadLastReconciliation();
        var incidentReporter = new RuntimeIncidentReporter(mode);
        RuntimeIncident? latestIncident = incidentReporter.LoadLatestIncident();

        string logFilePath = CreateRuntimeLogFilePath(mode, "status");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} status");
        Log(writer, state.ToDisplayString());
        Log(writer, "");
        Log(writer, "SAILOR-031 status: positions, reconciliation, and latest runtime incident.");
        Log(writer, "This command reads the Sailor order ledger, position store, last reconciliation, and latest degraded-state incident. It does not request broker state; use reconcile for TWS verification.");
        Log(writer, "");
        LogReconciliationResult(writer, localStatus, includeEvents: false);

        if (lastReconciliation is not null)
        {
            Log(writer, "");
            Log(writer, "Last broker reconciliation");
            Log(writer, "--------------------------");
            Log(writer, lastReconciliation.ToSummaryString());
            Log(writer, lastReconciliation.Message);
            Log(writer, $"Last reconciliation JSON: {lastReconciliation.ReconciliationPath}");
        }
        else
        {
            Log(writer, "");
            Log(writer, "No broker reconciliation JSON found yet. Run paper reconcile with the optional IBApi build before allowing strategy entries.");
        }

        Log(writer, "");
        Log(writer, "Runtime incidents");
        Log(writer, "-----------------");
        if (latestIncident is null)
        {
            Log(writer, $"No runtime incident JSON found. Incident directory: {incidentReporter.IncidentDirectory}");
        }
        else
        {
            Log(writer, latestIncident.ToDisplayString());
            Log(writer, latestIncident.SafetyState.ToDisplayString());
            Log(writer, $"Latest incident JSON: {incidentReporter.LatestIncidentPath}");
        }

        Log(writer, "");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunReconcileAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions runtimeOptions = CreateOptions(mode, Array.Empty<string>(), settings);
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);
        string account = ReadStringOption(args, "--account", modeSettings.Account ?? string.Empty);
        int waitSeconds = ReadIntOption(args, "--wait-seconds", modeSettings.ConnectTimeoutSeconds);
        int executionRequestId = ReadIntOption(args, "--execution-request-id", 29_000);
        bool localOnly = args.Any(arg => arg.Equals("--local-only", StringComparison.OrdinalIgnoreCase));

        var connectionOptions = new IbkrConnectionOptions(
            mode,
            runtimeOptions.Host,
            runtimeOptions.Port,
            runtimeOptions.ClientId,
            account,
            modeSettings.ConnectTimeoutSeconds,
            runtimeOptions.UseL1,
            runtimeOptions.UseL2,
            SendOrders: false,
            AllowShort: runtimeOptions.AllowShort);

        var request = new PositionRequest(
            mode,
            account,
            TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
            executionRequestId);

        string logFilePath = CreateRuntimeLogFilePath(mode, "reconcile");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {runtimeOptions.ModeName} positions and reconciliation started");
        Log(writer, runtimeOptions.ToCompactString());
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, request.ToDisplayString());
        Log(writer, $"localOnly={localOnly}");
        Log(writer, "");
        Log(writer, "SAILOR-029 implementation: request broker positions/open orders/executions and compare with the Sailor ledger.");
        Log(writer, "Entries must remain blocked unless reconciliation status is Matched.");
        Log(writer, "");

        await using IPositionProvider provider = CreatePositionProvider(localOnly, connectionOptions);
        Log(writer, $"Position provider: {provider.ProviderName}");
        Log(writer, "");

        var service = new ReconciliationService(mode);
        ReconciliationResult result = await service.ReconcileAsync(provider, request, CancellationToken.None);

        LogReconciliationResult(writer, result, includeEvents: true);
        Log(writer, "");
        Log(writer, result.CanOpenNewEntries
            ? "Reconciliation matched. Later paper runtime entries may proceed after normal safety gates."
            : "Entries are BLOCKED until a broker-verified reconciliation succeeds without critical mismatches.");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunPaperReportAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions options = CreateOptions(mode, Array.Empty<string>(), settings);
        string target = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.Trim().ToLowerInvariant() ?? "latest";
        string logFilePath = CreateRuntimeLogFilePath(mode, "report_latest");

        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} paper certification report");
        Log(writer, options.ToCompactString());
        Log(writer, "");

        if (mode != SailorRuntimeMode.Paper)
        {
            Log(writer, "SAILOR-032 supports paper certification only. Live-readiness gates will consume a paper report in a later milestone.");
            Log(writer, "Usage: sailor paper report latest");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        if (!target.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            Log(writer, $"Unsupported report target '{target}'.");
            Log(writer, "Usage: sailor paper report latest");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        var reportWriter = new PaperSessionReportWriter(mode);
        PaperCertificationReport report = reportWriter.BuildLatestReport();
        PaperCertificationReportOutput output = reportWriter.WriteLatestReport(report);

        Log(writer, "SAILOR-032 implementation: paper certification report.");
        Log(writer, "This report combines the latest paper runtime log, Sailor order ledger, local position store, broker reconciliation JSON, and degraded-state incident files.");
        Log(writer, "A session cannot be promoted when end exposure is non-zero. Reconciliation must also be clean before the later live-readiness gate can consume the report.");
        Log(writer, "");
        Log(writer, report.ToSummaryString());
        Log(writer, $"JSON report:     {output.JsonPath}");
        Log(writer, $"Markdown report: {output.MarkdownPath}");
        Log(writer, $"CSV report:      {output.CsvPath}");
        Log(writer, $"Runtime log:     {logFilePath}");
        Log(writer, "");
        Log(writer, "Certification evidence");
        Log(writer, "----------------------");
        Log(writer, $"session mode: {report.Mode}");
        Log(writer, $"account: {(string.IsNullOrWhiteSpace(report.Account) ? "not-configured" : report.Account)}");
        Log(writer, $"profile: {report.Profile}");
        Log(writer, $"symbols: {(report.Symbols.Count == 0 ? "none" : string.Join(", ", report.Symbols))}");
        Log(writer, $"orders submitted: {report.OrdersSubmitted}");
        Log(writer, $"orders filled: {report.OrdersFilled}");
        Log(writer, $"orders rejected: {report.OrdersRejected}");
        Log(writer, $"positions opened/closed: {report.PositionsOpened}/{report.PositionsClosed}");
        Log(writer, $"force-flat result: {report.ForceFlatResult}");
        Log(writer, $"disconnect incidents: {report.DisconnectIncidentCount}");
        Log(writer, $"reconciliation status: {report.ReconciliationStatus}");
        Log(writer, $"L1/L2 health: {report.L1L2Health}");
        Log(writer, $"P&L: {report.RealizedPnl:F2}");
        Log(writer, $"strategy decisions: {report.StrategyDecisions}");
        if (report.ScanListEvidence is null)
        {
            Log(writer, "scan-list evidence: none");
        }
        else
        {
            Log(writer, $"scan-list evidence: {report.ScanListEvidence.ToSummaryString()}");
            Log(writer, $"scan-list data quality: {report.ScanListEvidence.DataQualityStatus} - {report.ScanListEvidence.DataQualityReason}");
            Log(writer, $"scan-list certification blockers: criticalGaps={report.ScanListEvidence.CriticalDataGaps} mergeConflicts={report.ScanListEvidence.MergeConflictCount} staleSelected={report.ScanListEvidence.StaleSelectedSymbols} notReady={(report.ScanListEvidence.NotReadySelectedSymbols.Count == 0 ? "none" : string.Join(",", report.ScanListEvidence.NotReadySelectedSymbols))}");
            Log(writer, $"scan-list retained symbols: {(report.ScanListEvidence.TradeEligiblePreview.Count == 0 ? "none" : string.Join(", ", report.ScanListEvidence.TradeEligiblePreview))}");
            Log(writer, $"scan-list watch symbols: {(report.ScanListEvidence.SafeWatchCandidatePreview.Count == 0 ? "none" : string.Join(", ", report.ScanListEvidence.SafeWatchCandidatePreview))}");
            Log(writer, $"scanner mode: {report.ScanListEvidence.ScannerMode}");
            Log(writer, $"points candidates: total={report.ScanListEvidence.PointsCandidates} ready={report.ScanListEvidence.ReadyCandidates} weakReady={report.ScanListEvidence.WeakReadyCandidates} watchOnly={report.ScanListEvidence.WatchOnlyCandidates} notReady={report.ScanListEvidence.NotReadyCandidates} minScore={report.ScanListEvidence.MinimumTradeScore:F2}");
            Log(writer, $"points report: {report.ScanListEvidence.PointsReportPath ?? "n/a"}");
            Log(writer, $"legacy comparison report: {report.ScanListEvidence.LegacyComparisonReportPath ?? "n/a"}");
            Log(writer, $"legacy comparison markdown: {report.ScanListEvidence.LegacyComparisonMarkdownReportPath ?? "n/a"}");
        }
        Log(writer, $"all open exposure at end = zero: {report.EndExposureIsZero}");
        Log(writer, $"promotion result: {report.CertificationStatus} - {report.PromotionBlockReason}");

        if (report.EndOpenPositions.Count > 0)
        {
            Log(writer, "");
            Log(writer, "End open positions");
            Log(writer, "------------------");
            foreach (SailorPosition position in report.EndOpenPositions)
            {
                Log(writer, position.ToDisplayLine());
            }
        }

        if (report.Warnings.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Warnings");
            Log(writer, "--------");
            foreach (string warning in report.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }
    }

    private static async Task RunLiveReadinessAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions options = CreateOptions(mode, Array.Empty<string>(), settings);
        string logFilePath = CreateRuntimeLogFilePath(mode, "readiness");

        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} live-readiness gate");
        Log(writer, options.ToCompactString());
        Log(writer, "");

        if (mode != SailorRuntimeMode.Live)
        {
            Log(writer, "SAILOR-033 live-readiness gate is a live-mode command.");
            Log(writer, "Usage: sailor live readiness --account DU123456 --max-notional 100 --confirm-live");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        bool readOnly = args.Any(arg => arg.Equals("--read-only", StringComparison.OrdinalIgnoreCase));
        SailorRuntimeModeSettings liveSettings = GetModeSettings(mode, settings);
        string account = ReadStringOption(args, "--account", liveSettings.Account ?? string.Empty);
        LiveReadinessGateResult gate = EvaluateLiveReadiness(
            settings,
            readOnly ? "live readiness read-only" : "live readiness trading",
            args,
            requiresTrading: !readOnly,
            readOnly: readOnly,
            account: account);
        LiveReadinessGateOutput output = new LiveReadinessGate(settings).WriteResult(gate);

        Log(writer, "SAILOR-033 implementation: live-readiness gate.");
        Log(writer, "Live mode can connect and scan read-only, but live trading remains blocked unless the explicit gate passes.");
        Log(writer, "The gate requires Runtime.Live.AllowLiveTrading=true, --confirm-live, a recent promotable paper certification report, matching account, and small max notional.");
        Log(writer, "");
        LogLiveReadinessGate(writer, gate);
        Log(writer, $"Readiness JSON: {output.JsonPath}");
        Log(writer, $"Readiness CSV:  {output.CsvPath}");
        Log(writer, "");
        Log(writer, gate.LiveTradingAllowed
            ? "Live-readiness gate PASSED. SAILOR-033 still does not send live orders; SAILOR-034 will consume this evidence for the pilot."
            : "Live-readiness gate BLOCKED. No live order can be routed.");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunFlattenSkeletonAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        string symbol = args.Length >= 1 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0].Trim().ToUpperInvariant()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(symbol))
        {
            Console.WriteLine($"Usage: sailor {mode.ToDisplayName()} flatten SYMBOL");
            return;
        }

        SailorRuntimeOptions options = CreateOptions(mode, Array.Empty<string>(), settings);
        string logFilePath = CreateRuntimeLogFilePath(mode, $"flatten_{symbol}");
        await using var writer = CreateWriter(logFilePath);

        if (mode != SailorRuntimeMode.Live)
        {
            SailorOrderIntent paperFlattenIntent = SailorOrderIntent.Flatten(
                mode,
                symbol,
                "manual-runtime-command",
                "Manual flatten command skeleton. SAILOR-034 implements live close-only flatten; paper flatten remains runtime/strategy driven.",
                dryRun: true);

            Log(writer, $"sailor {options.ModeName} flatten skeleton");
            Log(writer, options.ToCompactString());
            Log(writer, paperFlattenIntent.ToDisplayString());
            Log(writer, "No persistent broker session. No order sent. Use paper run --force-flat-now for paper close-only behavior.");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        SailorRuntimeModeSettings liveSettings = settings.Runtime.Live;
        string account = ReadStringOption(args, "--account", liveSettings.Account ?? string.Empty);
        string primaryExchange = ReadStringOption(args, "--primary-exchange", "NASDAQ");
        int waitSeconds = ReadIntOption(args, "--wait-seconds", liveSettings.ConnectTimeoutSeconds);
        int executionRequestId = ReadIntOption(args, "--execution-request-id", 34_500);
        bool confirmLive = args.Any(arg => arg.Equals("--confirm-live", StringComparison.OrdinalIgnoreCase));
        bool sendOrdersRequested = args.Any(arg => arg.Equals("--send-orders", StringComparison.OrdinalIgnoreCase));
        bool dryRunRequested = args.Any(arg => arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        bool localOnly = args.Any(arg => arg.Equals("--local-only", StringComparison.OrdinalIgnoreCase));
        bool sendOrders = sendOrdersRequested && confirmLive && liveSettings.AllowLiveTrading && !dryRunRequested;
        bool dryRun = !sendOrders;

        var connectionOptions = new IbkrConnectionOptions(
            SailorRuntimeMode.Live,
            options.Host,
            options.Port,
            options.ClientId,
            account,
            liveSettings.ConnectTimeoutSeconds,
            options.UseL1,
            options.UseL2,
            sendOrders,
            AllowShort: true);

        Log(writer, "sailor live close-only flatten");
        Log(writer, FormatRuntimeOptionsForDisplay(options));
        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, $"symbol={symbol} waitSeconds={waitSeconds} sendOrdersRequested={sendOrdersRequested} confirmLive={confirmLive} allowLiveTrading={liveSettings.AllowLiveTrading} dryRun={dryRun} localOnly={localOnly}");
        Log(writer, "");
        Log(writer, "SAILOR-034 live close-only flatten command.");
        Log(writer, "This command may only reduce/close an existing broker position. It never opens a new live position and requires --send-orders, --confirm-live, and Runtime.Live.AllowLiveTrading=true before routing.");
        Log(writer, "");

        if (!confirmLive)
        {
            Log(writer, "Blocked: --confirm-live is required even for close-only flatten.");
        }

        if (sendOrdersRequested && !liveSettings.AllowLiveTrading)
        {
            Log(writer, "Blocked: Runtime.Live.AllowLiveTrading=false. No live flatten order will be sent.");
        }

        var positionRequest = new PositionRequest(
            SailorRuntimeMode.Live,
            account,
            TimeSpan.FromSeconds(Math.Max(1, waitSeconds)),
            executionRequestId);
        BrokerStateSnapshot brokerSnapshot;
        await using (IPositionProvider positionProvider = CreatePositionProvider(localOnly: localOnly, connectionOptions))
        {
            brokerSnapshot = await positionProvider.GetBrokerStateAsync(positionRequest, CancellationToken.None).ConfigureAwait(false);
        }

        Log(writer, brokerSnapshot.ToSummaryString());
        foreach (string evt in brokerSnapshot.Events)
        {
            Log(writer, $"broker-event: {evt}");
        }
        foreach (string warning in brokerSnapshot.Warnings)
        {
            Log(writer, $"WARN: {warning}");
        }

        BrokerPositionRow? position = brokerSnapshot.Positions
            .Select(row => row.Normalize())
            .Where(row => row.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && !row.IsFlat)
            .OrderByDescending(row => Math.Abs(row.Quantity))
            .FirstOrDefault();

        if (position is null)
        {
            Log(writer, "");
            Log(writer, $"No non-flat live broker position found for {symbol}. No flatten order was created.");
            Log(writer, $"Runtime log: {logFilePath}");
            return;
        }

        SailorOrderSide side = position.Quantity > 0 ? SailorOrderSide.Sell : SailorOrderSide.BuyToCover;
        int quantity = Math.Abs(position.Quantity);
        SailorOrderIntent intent = SailorOrderIntent.CreateManual(
            SailorRuntimeMode.Live,
            symbol,
            side,
            SailorOrderType.Market,
            quantity,
            limitPrice: null,
            strategyName: "live-close-only-flatten",
            reason: $"SAILOR-034 close-only flatten from broker position qty={position.Quantity} avgCost={position.AverageCost:F4}.",
            dryRun: dryRun,
            account: account,
            timeInForce: "DAY");

        Log(writer, "");
        Log(writer, position.ToDisplayLine());
        Log(writer, intent.ToDisplayString());

        if (sendOrdersRequested && !sendOrders)
        {
            Log(writer, "WARN: --send-orders was requested, but live flatten remains dry-run because one or more live safety switches are missing.");
        }

        await using IOrderRouter router = IbkrOrderRouterFactory.Create(sendOrders, connectionOptions, primaryExchange, waitSeconds);
        Log(writer, $"Order router: {router.RouterName}");
        SailorOrderReceipt receipt = await router.SubmitAsync(intent, CancellationToken.None).ConfigureAwait(false);
        var ledger = new OrderLedgerStore(SailorRuntimeMode.Live);
        string ledgerPath = ledger.Append(intent, receipt);

        Log(writer, receipt.ToDisplayString());
        Log(writer, $"Ledger JSONL: {ledgerPath}");
        Log(writer, $"Ledger CSV:   {ledger.DailyCsvPath}");

        var tradeRegistry = new TradeLifecycleRegistryStore(mode);
        int registryQuantityAfter = EstimateManualCommandPositionAfter(intent, receipt);
        decimal registryAveragePrice = receipt.AverageFillPrice > 0m
            ? receipt.AverageFillPrice
            : position.AverageCost;
        TradeLifecycle lifecycle = tradeRegistry.ApplyOrderReceipt(
            intent,
            receipt,
            SailorTradeOrigin.UnknownBroker,
            registryQuantityAfter,
            registryAveragePrice,
            scannerSlotId: null,
            sourceMessage: "SAILOR-051 live close-only flatten lifecycle evidence.");
        Log(writer, $"Trade registry latest JSON: {tradeRegistry.LatestJsonPath}");
        Log(writer, $"Trade registry event JSONL: {tradeRegistry.DailyJsonlPath}");
        Log(writer, $"Trade lifecycle: {lifecycle.ToDisplayString()}");
        foreach (string evt in receipt.Events)
        {
            Log(writer, $"event: {evt}");
        }
        foreach (string warning in receipt.Warnings)
        {
            Log(writer, $"WARN: {warning}");
        }

        Log(writer, "");
        Log(writer, receipt.SentToBroker ? "Close-only flatten order was sent to IBKR live." : "No live flatten order was sent to broker.");
        Log(writer, $"Runtime log: {logFilePath}");
    }


    private static LivePilotRestrictionResult EvaluateLivePilotRestrictions(
        SailorRuntimeOptions options,
        string[] args,
        SailorAppSettings settings)
    {
        var checks = new List<string>();
        var warnings = new List<string>();

        bool oneTop = options.TopCount == 1;
        checks.Add($"{(oneTop ? "PASS" : "FAIL")} one-symbol-top: live pilot top count must be 1. requestedTop={options.TopCount}.");

        bool singleSymbol = TryGetSinglePilotSymbol(options.Universe, out string symbol, out string symbolReason);
        checks.Add($"{(singleSymbol ? "PASS" : "FAIL")} one-explicit-symbol: {symbolReason}");

        bool oneProfile = !string.IsNullOrWhiteSpace(options.ProfileName);
        checks.Add($"{(oneProfile ? "PASS" : "FAIL")} one-profile: profile={(string.IsNullOrWhiteSpace(options.ProfileName) ? "n/a" : options.ProfileName)}.");

        bool operatorWatchedTws = HasAnyFlag(args, "--operator-watching-tws", "--watching-tws", "--operator-watch-tws");
        checks.Add($"{(operatorWatchedTws ? "PASS" : "FAIL")} operator-watching-tws: operator must watch TWS during the live pilot.");

        bool forceFlatRequired = settings.Runtime.Safety.ForceFlatMinute > 0
                                 && settings.Runtime.Safety.ForceFlatMinute >= settings.Runtime.Safety.LastEntryMinute;
        checks.Add($"{(forceFlatRequired ? "PASS" : "FAIL")} force-flat-required: lastEntryMinute={settings.Runtime.Safety.LastEntryMinute} forceFlatMinute={settings.Runtime.Safety.ForceFlatMinute}.");

        bool requestedShort = HasAnyFlag(args, "--allow-short-live", "--allow-short");
        bool shortEnabled = settings.Runtime.Live.AllowShort && requestedShort;
        if (requestedShort && !settings.Runtime.Live.AllowShort)
        {
            warnings.Add("Short pilot was requested, but Runtime.Live.AllowShort=false. Live pilot remains long-only.");
        }

        checks.Add(shortEnabled
            ? "PASS long-only-first: explicit short pilot enabled by config and command."
            : "PASS long-only-first: live pilot is long-only; short entries are blocked.");

        bool passed = oneTop && singleSymbol && oneProfile && operatorWatchedTws && forceFlatRequired;
        string blockReason = passed
            ? "Live-pilot restrictions passed."
            : checks.FirstOrDefault(row => row.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) ?? "Live-pilot restrictions failed.";

        return new LivePilotRestrictionResult(
            passed,
            symbol,
            options.ProfileName,
            operatorWatchedTws,
            forceFlatRequired,
            shortEnabled,
            checks,
            warnings,
            blockReason);
    }

    private static bool TryGetSinglePilotSymbol(string? universe, out string symbol, out string reason)
    {
        symbol = string.Empty;
        string raw = string.IsNullOrWhiteSpace(universe) ? string.Empty : universe.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            reason = "No symbol was provided. Use: live run 1m v21-15minutes 1 TSLA --confirm-live --operator-watching-tws.";
            return false;
        }

        string[] parts = raw
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length != 1)
        {
            reason = $"Live pilot requires exactly one explicit symbol. Received '{raw}'.";
            return false;
        }

        string candidate = parts[0].Trim().ToUpperInvariant();
        if (candidate.Equals("SMALLCAPS", StringComparison.OrdinalIgnoreCase) ||
            candidate.EndsWith(".CSV", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains('\\', StringComparison.Ordinal) ||
            candidate.Contains('/', StringComparison.Ordinal))
        {
            reason = $"Live pilot requires one explicit symbol, not a universe or file: '{raw}'.";
            return false;
        }

        bool valid = candidate.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-');
        if (!valid)
        {
            reason = $"Symbol '{candidate}' contains unsupported characters for the live pilot.";
            return false;
        }

        symbol = candidate;
        reason = $"explicitSymbol={symbol}.";
        return true;
    }

    private static bool HasAnyFlag(string[] args, params string[] flags)
        => args.Any(arg => flags.Any(flag => arg.Equals(flag, StringComparison.OrdinalIgnoreCase)));

    private static LiveReadinessGateResult EvaluateLiveReadiness(
        SailorAppSettings settings,
        string commandName,
        string[] args,
        bool requiresTrading,
        bool readOnly,
        string account,
        decimal? defaultMaxNotional = null)
    {
        decimal fallback = defaultMaxNotional ?? settings.Runtime.Live.MaxOrderNotional;
        decimal requestedMaxNotional = ReadDecimalOption(args, "--max-notional", fallback);
        bool confirmLive = args.Any(arg => arg.Equals("--confirm-live", StringComparison.OrdinalIgnoreCase));
        var request = new LiveReadinessGateRequest(
            commandName,
            requiresTrading,
            readOnly,
            confirmLive,
            account,
            requestedMaxNotional);

        return new LiveReadinessGate(settings).Evaluate(request);
    }

    private static void LogLiveMultiSymbolPilotGate(StreamWriter writer, LiveMultiSymbolPilotGateResult gate)
    {
        Log(writer, "SAILOR-042 future multi-symbol live-pilot gate");
        Log(writer, gate.ToSummaryString());
        Log(writer, gate.Reason);
        foreach (string check in gate.Checks)
        {
            Log(writer, check);
        }
    }


    private static void LogLivePointsPilotGate(StreamWriter writer, LivePointsPilotGateResult gate)
    {
        Log(writer, "SAILOR-048 live points pilot gate");
        Log(writer, gate.ToSummaryString());
        Log(writer, gate.Reason);
        foreach (string check in gate.Checks)
        {
            Log(writer, check);
        }
    }

    private static void LogLiveReadinessGate(StreamWriter writer, LiveReadinessGateResult gate)
    {
        Log(writer, "SAILOR-033 live-readiness gate");
        Log(writer, gate.ToSummaryString());
        Log(writer, gate.ManualConfirmationRequiredText);
        Log(writer, $"paperCertification={gate.PaperCertificationPath}");
        if (gate.PaperReport is not null)
        {
            Log(writer, $"paperReport={gate.PaperReport.ReportId} status={gate.PaperReport.CertificationStatus} canPromote={gate.PaperReport.CanPromoteToLiveReadiness} account={(string.IsNullOrWhiteSpace(gate.PaperReport.Account) ? "not-configured" : gate.PaperReport.Account)} generatedUtc={gate.PaperReport.GeneratedUtc:O}");
        }

        Log(writer, "Live-readiness checks");
        Log(writer, "---------------------");
        foreach (LiveReadinessCheck check in gate.Checks)
        {
            Log(writer, check.ToDisplayLine());
        }

        Log(writer, $"Result: {gate.Status} - {gate.Reason}");
    }

    private static IPositionProvider CreatePositionProvider(bool localOnly, IbkrConnectionOptions connectionOptions)
    {
        if (localOnly)
        {
            return new DisabledPositionProvider("Local-only reconciliation requested. Broker state was not requested.");
        }

#if SAILOR_IBAPI
        return new IbkrPositionProvider(connectionOptions);
#else
        return new DisabledPositionProvider($"Broker position provider is disabled. Re-run with -p:EnableIbkrApi=true to request TWS {connectionOptions.ModeName} broker positions/open orders/executions.");
#endif
    }

    private static string FormatRuntimeOptionsForDisplay(SailorRuntimeOptions options)
    {
        string orderMode = options.SendOrders && !options.DryRun ? "send-orders" : "dry-run";
        return $"{options.ModeName} {orderMode} host={options.Host} port={options.Port} clientId={options.ClientId} timeframe={options.Timeframe} profile={options.ProfileName} universe={options.Universe} top={options.TopCount} L1={options.UseL1} L2={options.UseL2} allowShort={options.AllowShort}";
    }

    private static void LogReconciliationResult(
        StreamWriter writer,
        ReconciliationResult result,
        bool includeEvents)
    {
        Log(writer, result.ToSummaryString());
        Log(writer, result.Message);
        Log(writer, $"Order ledger:      {result.LedgerPath}");
        Log(writer, $"Position store:    {result.PositionsPath}");
        Log(writer, $"Reconciliation:    {result.ReconciliationPath}");
        Log(writer, "");

        if (result.LocalPositions.Count == 0)
        {
            Log(writer, "Sailor local positions: none");
        }
        else
        {
            Log(writer, "Sailor local positions");
            Log(writer, "----------------------");
            foreach (SailorPosition position in result.LocalPositions)
            {
                Log(writer, position.ToDisplayLine());
            }
        }

        Log(writer, "");
        if (result.BrokerPositions.Count == 0)
        {
            Log(writer, "Broker positions: none or not broker-verified");
        }
        else
        {
            Log(writer, "Broker positions");
            Log(writer, "----------------");
            foreach (BrokerPositionRow position in result.BrokerPositions)
            {
                Log(writer, position.ToDisplayLine());
            }
        }

        Log(writer, "");
        if (result.BrokerOpenOrders.Count == 0)
        {
            Log(writer, "Broker open orders: none or not broker-verified");
        }
        else
        {
            Log(writer, "Broker open orders");
            Log(writer, "------------------");
            foreach (BrokerOpenOrderRow order in result.BrokerOpenOrders)
            {
                Log(writer, order.ToDisplayLine());
            }
        }

        Log(writer, "");
        if (result.Rows.Count == 0)
        {
            Log(writer, "Reconciliation rows: none");
        }
        else
        {
            Log(writer, "Reconciliation rows");
            Log(writer, "-------------------");
            foreach (ReconciliationRow row in result.Rows)
            {
                Log(writer, row.ToDisplayLine());
            }
        }

        if (result.BrokerExecutions.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Recent broker executions");
            Log(writer, "------------------------");
            foreach (BrokerExecutionRow execution in result.BrokerExecutions.Take(30))
            {
                Log(writer, execution.ToDisplayLine());
            }
        }

        if (result.Warnings.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Warnings");
            Log(writer, "--------");
            foreach (string warning in result.Warnings)
            {
                Log(writer, $"WARN: {warning}");
            }
        }

        if (includeEvents && result.Events.Count > 0)
        {
            Log(writer, "");
            Log(writer, "Broker events");
            Log(writer, "-------------");
            foreach (string row in result.Events)
            {
                Log(writer, row);
            }
        }
    }


    private static IReadOnlyList<string> ResolveHistorySymbols(string target, int top)
    {
        var provider = new CsvBacktestDataProvider();
        IReadOnlyList<string> availableSymbols = provider.ListSymbols();
        IReadOnlyList<string> resolved = SailorSymbolUniverses.Resolve(target, availableSymbols);

        int take = Math.Max(1, top);
        return resolved
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToArray();
    }


    private static bool TryParseOrderSide(string value, out SailorOrderSide side)
    {
        string normalized = value.Trim().Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
        side = normalized switch
        {
            "BUY" or "B" => SailorOrderSide.Buy,
            "SELL" or "S" => SailorOrderSide.Sell,
            "SELLSHORT" or "SELL_SHORT" or "SHORT" or "SS" => SailorOrderSide.SellShort,
            "BUYTOCOVER" or "BUY_TO_COVER" or "COVER" or "BTC" => SailorOrderSide.BuyToCover,
            _ => SailorOrderSide.Flatten
        };

        return side != SailorOrderSide.Flatten;
    }

    private static bool TryParseOrderType(string value, out SailorOrderType orderType)
    {
        string normalized = value.Trim().Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
        orderType = normalized switch
        {
            "MKT" or "MARKET" => SailorOrderType.Market,
            "LMT" or "LIMIT" => SailorOrderType.Limit,
            "STP" or "STOP" => SailorOrderType.Stop,
            "STPLMT" or "STP_LMT" or "STOP_LIMIT" => SailorOrderType.StopLimit,
            _ => SailorOrderType.Market
        };

        return normalized is "MKT" or "MARKET" or "LMT" or "LIMIT" or "STP" or "STOP" or "STPLMT" or "STP_LMT" or "STOP_LIMIT";
    }

    private static bool HasOption(string[] args, string optionName)
        => args.Any(arg => arg.Equals(optionName, StringComparison.OrdinalIgnoreCase));

    private static int ReadIntOption(string[] args, string optionName, int defaultValue, bool allowZero = false)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int value) &&
                (value > 0 || (allowZero && value == 0)))
            {
                return value;
            }
        }

        return allowZero ? Math.Max(0, defaultValue) : Math.Max(1, defaultValue);
    }

    private static decimal ReadDecimalOption(string[] args, string optionName, decimal defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args[i + 1]) &&
                !args[i + 1].StartsWith("--", StringComparison.Ordinal) &&
                (decimal.TryParse(args[i + 1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal invariantValue) ||
                 decimal.TryParse(args[i + 1], NumberStyles.Number, CultureInfo.CurrentCulture, out invariantValue)) &&
                invariantValue > 0m)
            {
                return invariantValue;
            }
        }

        return defaultValue > 0m ? defaultValue : 0m;
    }


    private static bool ReadBooleanOption(string[] args, string optionName, bool defaultValue)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i < args.Length - 1 && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                string value = args[i + 1].Trim();
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("on", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        return defaultValue;
    }

    private static string ReadStringOption(string[] args, string optionName, string defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args[i + 1]) &&
                !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[i + 1].Trim();
            }
        }

        return defaultValue;
    }

    private static PointsScannerMode ReadScannerMode(string[] args, SailorAppSettings settings)
    {
        PointsScannerMode configuredFallback = PointsScannerModeExtensions.ParseOrDefault(
            settings.Scanner.DefaultMode,
            PointsScannerMode.LegacyBlocks);
        string rawValue = ReadStringOption(args, "--scanner-mode", configuredFallback.ToConfigValue());
        return PointsScannerModeExtensions.ParseOrDefault(rawValue, configuredFallback);
    }

    private static SailorRuntimeModeSettings GetModeSettings(
        SailorRuntimeMode mode,
        SailorAppSettings settings)
        => mode == SailorRuntimeMode.Live
            ? settings.Runtime.Live
            : settings.Runtime.Paper;

    private static SailorRuntimeOptions CreateOptions(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);

        bool sendOrdersRequested = args.Any(arg => arg.Equals("--send-orders", StringComparison.OrdinalIgnoreCase));
        bool dryRunRequested = args.Any(arg => arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        bool dryRun = dryRunRequested || !sendOrdersRequested || !modeSettings.SendOrders || mode == SailorRuntimeMode.Live;
        bool sendOrders = sendOrdersRequested && modeSettings.SendOrders && mode != SailorRuntimeMode.Live;

        string[] positional = args
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        string timeframe = positional.Length >= 1
            ? positional[0].Trim()
            : settings.DefaultTimeframe;

        string profileName = positional.Length >= 2
            ? positional[1].Trim()
            : settings.DefaultProfile;

        int topCount = modeSettings.DefaultTopCount > 0
            ? modeSettings.DefaultTopCount
            : settings.Scanner.DefaultTopCount;

        if (positional.Length >= 3 && int.TryParse(positional[2], out int parsedTopCount) && parsedTopCount > 0)
        {
            topCount = parsedTopCount;
        }

        string universe = positional.Length >= 4
            ? positional[3].Trim()
            : settings.DefaultUniverse;

        return new SailorRuntimeOptions(
            mode,
            modeSettings.Host,
            modeSettings.Port,
            modeSettings.ClientId,
            timeframe,
            profileName,
            universe,
            topCount,
            dryRun,
            sendOrders,
            modeSettings.UseL1,
            modeSettings.UseL2,
            modeSettings.AllowShort,
            settings.Runtime.Safety.LastEntryMinute,
            settings.Runtime.Safety.ForceFlatMinute);
    }

    private static RuntimeSafetyState BuildScanListSafetyState(
        PaperScannerRunResult result,
        IEnumerable<string> workbookWarnings)
    {
        string[] warnings = workbookWarnings
            .Concat(result.Warnings)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .ToArray();

        bool degraded = warnings.Any(IsScanListDegradedSignal) || result.HistorySuccessCount == 0;
        if (degraded)
        {
            string reason = result.HistorySuccessCount == 0
                ? "Scan-list history scheduler has no clean usable 1m history yet. Entries must stay close-only until history/market data recovers."
                : "Scan-list history/market-data/server state is degraded. Entries must stay close-only while the last clean list remains in memory.";
            return RuntimeSafetyState.CloseOnly(reason);
        }

        return RuntimeSafetyState.Normal("Scan-list one-cycle evidence is clean. Long-running host may evaluate entry eligibility using retained top symbols.");
    }

    private static bool IsScanListDegradedSignal(string warning)
        => warning.Contains("connection", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("timeout", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("timed out", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("socket", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("disabled", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("not available", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("failed", StringComparison.OrdinalIgnoreCase);

    private static string FormatSymbols(IReadOnlyList<string> symbols, int max)
    {
        if (symbols.Count == 0)
        {
            return "none";
        }

        return $"{string.Join(", ", symbols.Take(Math.Max(1, max)))}{(symbols.Count > max ? ", ..." : string.Empty)}";
    }

    private static int EstimateManualCommandPositionAfter(SailorOrderIntent intent, SailorOrderReceipt receipt)
    {
        int fillQuantity = receipt.Status == SailorOrderStatus.DryRun
            ? intent.Quantity
            : receipt.FilledQuantity;

        if (fillQuantity <= 0)
        {
            return 0;
        }

        return intent.Side switch
        {
            SailorOrderSide.Buy => fillQuantity,
            SailorOrderSide.SellShort => -fillQuantity,
            SailorOrderSide.Sell => 0,
            SailorOrderSide.BuyToCover => 0,
            _ => 0
        };
    }

    private static StreamWriter CreateWriter(string logFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        return new StreamWriter(new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read));
    }

    private static void Log(StreamWriter writer, string message)
    {
        Console.WriteLine(message);
        writer.WriteLine(message);
        writer.Flush();
    }

    private static string CreateRuntimeLogFilePath(SailorRuntimeMode mode, string action)
    {
        string root = mode == SailorRuntimeMode.Live
            ? SailorLogPaths.Live
            : SailorLogPaths.Paper;

        string safeAction = string.Join("_", action.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(root, "Runtime", $"{mode.ToDisplayName()}_{safeAction}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    public static void PrintHelp(SailorRuntimeMode mode, SailorAppSettings settings)
    {
        SailorRuntimeModeSettings modeSettings = GetModeSettings(mode, settings);

        string name = mode.ToDisplayName();
        Console.WriteLine($"Sailor {name} runtime commands");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  sailor {name} connect");
        if (mode == SailorRuntimeMode.Live)
        {
            Console.WriteLine($"  sailor {name} connect --read-only");
            Console.WriteLine($"  sailor {name} readiness --account DU123456 --max-notional 100 --confirm-live");
        }
        Console.WriteLine($"  sailor {name} scan");
        Console.WriteLine($"  sailor {name} scan-list 1m v21-15minutes 10 --file scan/data/scan_default.xlsx --sheet Candidates --local-cache --no-quotes");
        Console.WriteLine($"  sailor {name} scan-points 1m v18-silver 10 --file scan/data/scan_default.xlsx --sheet Candidates --scanner-mode points-only --no-depth");
        Console.WriteLine($"  sailor {name} scan-points-test 1m v18-silver 10 --file scan/data/scan_default.xlsx --sheet Candidates --scanner-mode points-only --no-depth --max-symbols 45");
        Console.WriteLine($"  sailor {name} trade-management-test --scenario all");
        Console.WriteLine($"  sailor {name} trade-management-test --scenario severe-disconnect-recovery");
        Console.WriteLine($"  sailor {name} scan 1m sailor-trend-volume 3 smallcaps --local-cache");
        Console.WriteLine($"  sailor {name} scan 1m sailor-trend-volume 3 smallcaps --days 5 --market-data-type 2");
        Console.WriteLine($"  sailor {name} scan 1m v21-15minutes 1 TSLA --local-cache --no-depth");
        Console.WriteLine($"  sailor {name} history 1m TSLA");
        Console.WriteLine($"  sailor {name} history 1m smallcaps --top 10 --days 5");
        Console.WriteLine($"  sailor {name} history 1m TSLA --local-cache");
        Console.WriteLine($"  sailor {name} quotes TSLA");
        Console.WriteLine($"  sailor {name} quotes TSLA --seconds 15 --market-data-type 2");
        Console.WriteLine($"  sailor {name} depth TSLA --levels 5");
        Console.WriteLine($"  sailor {name} quotes TSLA --local-cache");
        Console.WriteLine($"  sailor {name} run --dry-run");
        Console.WriteLine($"  sailor {name} run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 10");
        Console.WriteLine($"  sailor {name} run 1m v21-15minutes 1 TSLA --send-orders --account DU123456 --wait-seconds 15");
        if (mode == SailorRuntimeMode.Live)
        {
            Console.WriteLine($"  sailor {name} run 1m v21-15minutes 1 TSLA --account DU123456 --max-notional 100 --confirm-live --operator-watching-tws");
            Console.WriteLine($"  sailor {name} flatten TSLA --account DU123456 --send-orders --confirm-live");
        }
        Console.WriteLine($"  sailor {name} run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --simulate-disconnect-at 2");
        Console.WriteLine($"  sailor {name} order TSLA BUY 1 LMT 350.00 --dry-run");
        Console.WriteLine($"  sailor {name} order TSLA BUY 1 MKT --dry-run");
        Console.WriteLine($"  sailor {name} order TSLA BUY 1 LMT 350.00 --send-orders");
        Console.WriteLine($"  sailor {name} status");
        Console.WriteLine($"  sailor {name} positions");
        Console.WriteLine($"  sailor {name} trades status");
        Console.WriteLine($"  sailor {name} trades status --all --symbol TSLA");
        Console.WriteLine($"  sailor {name} trades mirror --account DU123456 --wait-seconds 15");
        Console.WriteLine($"  sailor {name} broker mirror --account DU123456 --wait-seconds 15");
        Console.WriteLine($"  sailor {name} broker mirror --account DU123456 --intraday --wait-seconds 15");
        Console.WriteLine($"  sailor {name} run 1m v21-15minutes 10 --scan-file scan/data/scan_default.xlsx --scan-sheet Candidates --scanner-mode points-only --dry-run --iterations 10");
        Console.WriteLine($"  sailor {name} reconcile --account DU123456 --wait-seconds 15");
        Console.WriteLine($"  sailor {name} reconcile --local-only");
        Console.WriteLine($"  sailor {name} report latest");
        Console.WriteLine($"  sailor {name} flatten TSLA");
        Console.WriteLine();
        Console.WriteLine("SAILOR-034 status:");
        Console.WriteLine("  - runtime contracts and command model exist");
        Console.WriteLine("  - paper/live connect performs an IBKR/TWS TCP session probe");
        Console.WriteLine("  - history command can build 1m cache files under cache/history");
        Console.WriteLine("  - quotes/depth commands capture L1/L2 snapshots");
        Console.WriteLine("  - paper/live scan now prepares history, uses the shared Sailor scanner, and enriches candidates with snapshots");
        Console.WriteLine("  - manual paper order command creates a normalized SailorOrderIntent and writes a ledger");
        Console.WriteLine("  - optional IBApi paper order router can be enabled with -p:EnableIbkrApi=true and --send-orders");
        Console.WriteLine("  - status reads the local order ledger and position store");
        Console.WriteLine("  - trades status reads the SAILOR-051 trade lifecycle registry");
        Console.WriteLine("  - broker mirror / trades mirror persist the SAILOR-052 broker-state mirror and classify manual/external trades");
        Console.WriteLine("  - SAILOR-053 dynamic trade session manager merges scanner selections, broker/manual/pre-existing positions, local Sailor positions, and active lifecycle rows into one conduct plan");
        Console.WriteLine("  - reconcile requests broker positions, open orders, and executions when built with -p:EnableIbkrApi=true");
        Console.WriteLine("  - entries are blocked unless broker reconciliation succeeds without critical mismatch");
        Console.WriteLine("  - paper run now starts the scanner-backed conduct loop, builds strategy frames, creates order intents, writes the ledger, and can route through IBKR paper");
        Console.WriteLine("  - dry-run conduct assumes local fills so entry/exit logic can be smoke-tested without broker orders");
        Console.WriteLine("  - disconnect/degraded broker signals move runtime to CloseOnly and block new entries");
        Console.WriteLine("  - paper send-orders mode can attempt reconnect + reconciliation before resuming entries");
        Console.WriteLine("  - runtime incident reports are persisted under logs/Paper/Incidents");
        Console.WriteLine("  - paper report latest generates a JSON/Markdown/CSV paper certification report for the live-readiness gate");
        Console.WriteLine("  - live connect requires --read-only before opening the TCP probe");
        Console.WriteLine("  - live scan remains read-only and never creates an order router");
        Console.WriteLine("  - live readiness/gate evaluates config, --confirm-live, paper certification age/status, account match, and max notional");
        Console.WriteLine("  - SAILOR-037 can read scan/data/scan_default.xlsx and feed workbook symbols into backtest/paper/live scan-list ranking");
        Console.WriteLine("  - SAILOR-038 adds scan-list memory evidence, removed-symbol retention, history batch planning, and the in-memory candle merge foundation");
        Console.WriteLine("  - SAILOR-047 adds paper scan-points diagnostics for points-only scanner audit without starting conduct or routing orders");
        Console.WriteLine("  - SAILOR-048 adds live points-pilot gating, scan-points-test regression diagnostics, and final points-scanner operator commands");
        Console.WriteLine("  - SAILOR-051 records trade lifecycle ownership under state/{paper|live}/trades and exposes trades status");
        Console.WriteLine("  - scan-list inputs support 5-minute refresh, daily list changes, intraday additions, and top-N trade eligibility contracts for the next dynamic runtime milestone");
        Console.WriteLine("  - SAILOR-034 consumes the live-readiness gate for a one-symbol live pilot");
        Console.WriteLine("  - live pilot enforces one explicit symbol, small max notional, operator-watching-TWS acknowledgement, long-only default, pre-run reconciliation, and final zero exposure");
        Console.WriteLine("  - live flatten is available as a close-only command and never opens a new position");
        Console.WriteLine("  - manual live order sending remains blocked; use live run for pilot entries and live flatten for close-only exit");
        Console.WriteLine();
        Console.WriteLine("Configured defaults:");
        Console.WriteLine($"  host:       {modeSettings.Host}");
        Console.WriteLine($"  port:       {modeSettings.Port}");
        Console.WriteLine($"  client id:  {modeSettings.ClientId}");
        Console.WriteLine($"  sendOrders: {modeSettings.SendOrders}");
        if (mode == SailorRuntimeMode.Live)
        {
            Console.WriteLine($"  allow live trading: {modeSettings.AllowLiveTrading}");
            Console.WriteLine($"  max order notional: {modeSettings.MaxOrderNotional:F2}");
            Console.WriteLine($"  paper cert max age: {modeSettings.CertificationMaxAgeHours}h");
        }
        Console.WriteLine($"  timeout:    {modeSettings.ConnectTimeoutSeconds}s");
        Console.WriteLine($"  L1/L2:      {modeSettings.UseL1}/{modeSettings.UseL2}");
        Console.WriteLine($"  last entry: {settings.Runtime.Safety.LastEntryMinute} ET minute");
        Console.WriteLine($"  force flat: {settings.Runtime.Safety.ForceFlatMinute} ET minute");
    }
}
