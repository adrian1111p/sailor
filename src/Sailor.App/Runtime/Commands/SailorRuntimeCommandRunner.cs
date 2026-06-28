using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Broker.Ibkr;
using Sailor.App.Broker.Ibkr.Orders;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.Logging;
using Sailor.App.MarketData.History;
using Sailor.App.MarketData.Live;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.Paper;
using Sailor.App.Scanner.Runtime;

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
                await RunConnectSkeletonAsync(mode, settings);
                break;

            case "scan":
                await RunScanSkeletonAsync(mode, args.Skip(1).ToArray(), settings);
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

            case "reconcile":
                await RunReconcileAsync(mode, args.Skip(1).ToArray(), settings);
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
            smartDepth);

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
            Log(writer, "LIVE runtime remains blocked. SAILOR-030 implements paper conduct only.");
            Log(writer, "No broker connection opened. No orders sent.");
            Log(writer, $"Runtime log: {logFilePath}");
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
            smartDepth);

        Log(writer, connectionOptions.ToDisplayString());
        Log(writer, scannerOptions.ToDisplayString());
        Log(writer, $"quantity={quantity} cadenceSeconds={cadenceSeconds} iterations={iterations} waitSeconds={waitSeconds} sendOrdersRequested={sendOrdersRequested} sendOrders={sendOrders} dryRun={dryRun} account={(string.IsNullOrWhiteSpace(account) ? "not-configured" : account)}");
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

            await using IPositionProvider positionProvider = CreatePositionProvider(localOnly: false, connectionOptions);
            Log(writer, $"Pre-run reconciliation provider: {positionProvider.ProviderName}");
            reconciliation = await reconciliationService.ReconcileAsync(positionProvider, positionRequest, CancellationToken.None);
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

        bool canOpenEntries = dryRun || reconciliation.CanOpenNewEntries;
        if (sendOrders && !reconciliation.CanOpenNewEntries)
        {
            Log(writer, "");
            Log(writer, "SAILOR-030 blocked before conduct loop because broker reconciliation did not match. No orders sent.");
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
            forceFlatNow);

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
        Log(writer, sendOrders ? "Paper send-orders mode was active." : "Dry-run mode: no broker orders were sent.");
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

        if (mode == SailorRuntimeMode.Live && args.Any(arg => arg.Equals("--send-orders", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("SAILOR-028 blocks live order submission. Use paper mode only.");
            return;
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

        await using IOrderRouter router = IbkrOrderRouterFactory.Create(sendOrders, connectionOptions, primaryExchange, waitSeconds);
        Log(writer, $"Order router: {router.RouterName}");
        Log(writer, "");

        SailorOrderReceipt receipt = await router.SubmitAsync(intent, CancellationToken.None);
        var ledger = new OrderLedgerStore(mode);
        string ledgerPath = ledger.Append(intent, receipt);

        Log(writer, receipt.ToDisplayString());
        Log(writer, $"Ledger JSONL: {ledgerPath}");
        Log(writer, $"Ledger CSV:   {ledger.DailyCsvPath}");

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

        string logFilePath = CreateRuntimeLogFilePath(mode, "status");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} status");
        Log(writer, state.ToDisplayString());
        Log(writer, "");
        Log(writer, "SAILOR-029 implementation: positions and reconciliation status.");
        Log(writer, "This command reads the Sailor order ledger and position store. It does not request broker state; use reconcile for TWS verification.");
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

        SailorRuntimeOptions options = CreateOptions(mode, args.Skip(1).ToArray(), settings);
        SailorOrderIntent intent = SailorOrderIntent.Flatten(
            mode,
            symbol,
            "manual-runtime-command",
            "Manual flatten command skeleton. SAILOR-029 can reconcile positions, but real flatten order generation waits for the paper conduct/close-only milestone.",
            dryRun: true);

        string logFilePath = CreateRuntimeLogFilePath(mode, $"flatten_{symbol}");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} flatten skeleton");
        Log(writer, options.ToCompactString());
        Log(writer, intent.ToDisplayString());
        Log(writer, "No persistent broker session. No order sent. Run reconcile first; real flatten routing starts in the paper conduct/close-only milestone.");
        Log(writer, $"Runtime log: {logFilePath}");
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
        return new DisabledPositionProvider();
#endif
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

    private static int ReadIntOption(string[] args, string optionName, int defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int value) &&
                value > 0)
            {
                return value;
            }
        }

        return Math.Max(1, defaultValue);
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
        Console.WriteLine($"  sailor {name} scan");
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
        Console.WriteLine($"  sailor {name} order TSLA BUY 1 LMT 350.00 --dry-run");
        Console.WriteLine($"  sailor {name} order TSLA BUY 1 MKT --dry-run");
        Console.WriteLine($"  sailor {name} order TSLA BUY 1 LMT 350.00 --send-orders");
        Console.WriteLine($"  sailor {name} status");
        Console.WriteLine($"  sailor {name} positions");
        Console.WriteLine($"  sailor {name} reconcile --account DU123456 --wait-seconds 15");
        Console.WriteLine($"  sailor {name} reconcile --local-only");
        Console.WriteLine($"  sailor {name} flatten TSLA");
        Console.WriteLine();
        Console.WriteLine("SAILOR-030 status:");
        Console.WriteLine("  - runtime contracts and command model exist");
        Console.WriteLine("  - paper/live connect performs an IBKR/TWS TCP session probe");
        Console.WriteLine("  - history command can build 1m cache files under cache/history");
        Console.WriteLine("  - quotes/depth commands capture L1/L2 snapshots");
        Console.WriteLine("  - paper/live scan now prepares history, uses the shared Sailor scanner, and enriches candidates with snapshots");
        Console.WriteLine("  - manual paper order command creates a normalized SailorOrderIntent and writes a ledger");
        Console.WriteLine("  - optional IBApi paper order router can be enabled with -p:EnableIbkrApi=true and --send-orders");
        Console.WriteLine("  - status reads the local order ledger and position store");
        Console.WriteLine("  - reconcile requests broker positions, open orders, and executions when built with -p:EnableIbkrApi=true");
        Console.WriteLine("  - entries are blocked unless broker reconciliation succeeds without critical mismatch");
        Console.WriteLine("  - paper run now starts the scanner-backed conduct loop, builds strategy frames, creates order intents, writes the ledger, and can route through IBKR paper");
        Console.WriteLine("  - dry-run conduct assumes local fills so entry/exit logic can be smoke-tested without broker orders");
        Console.WriteLine("  - live order sending is blocked");
        Console.WriteLine();
        Console.WriteLine("Configured defaults:");
        Console.WriteLine($"  host:       {modeSettings.Host}");
        Console.WriteLine($"  port:       {modeSettings.Port}");
        Console.WriteLine($"  client id:  {modeSettings.ClientId}");
        Console.WriteLine($"  sendOrders: {modeSettings.SendOrders}");
        Console.WriteLine($"  timeout:    {modeSettings.ConnectTimeoutSeconds}s");
        Console.WriteLine($"  L1/L2:      {modeSettings.UseL1}/{modeSettings.UseL2}");
        Console.WriteLine($"  last entry: {settings.Runtime.Safety.LastEntryMinute} ET minute");
        Console.WriteLine($"  force flat: {settings.Runtime.Safety.ForceFlatMinute} ET minute");
    }
}
