using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Broker.Ibkr;
using Sailor.App.Broker.Orders;
using Sailor.App.Configuration;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;

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

            case "run":
                await RunStrategySkeletonAsync(mode, args.Skip(1).ToArray(), settings);
                break;

            case "status":
                await RunStatusSkeletonAsync(mode, settings);
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

    private static async Task RunScanSkeletonAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions options = CreateOptions(mode, args, settings);
        string logFilePath = CreateRuntimeLogFilePath(mode, "scan");

        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} scanner skeleton started");
        Log(writer, options.ToCompactString());
        Log(writer, "Data source: current Sailor CSV backtest cache until SAILOR-024/025 add live history and snapshots.");
        Log(writer, "No market data subscription opened. No orders sent.");
        Log(writer, "");

        SailorStrategyProfile profile = SailorStrategyProfile.FromName(options.ProfileName, settings);
        var provider = new CsvBacktestDataProvider();
        IReadOnlyList<string> availableSymbols = provider.ListSymbols();
        IReadOnlyList<string> universeSymbols = SailorSymbolUniverses.Resolve(options.Universe, availableSymbols);
        IReadOnlyList<string> symbolsWithData = universeSymbols
            .Where(symbol => availableSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Log(writer, $"Universe requested: {options.Universe}");
        Log(writer, $"Symbols with CSV data: {symbolsWithData.Count}");

        var scanner = new SailorScanner(provider);
        IReadOnlyList<ScannerCandidate> candidates = scanner.Scan(
            options.Timeframe,
            profile,
            options.TopCount,
            symbolsWithData);

        if (candidates.Count == 0)
        {
            Log(writer, "No candidates found.");
        }
        else
        {
            Log(writer, "");
            Log(writer, "Scanner candidates");
            Log(writer, "------------------");
            for (int i = 0; i < candidates.Count; i++)
            {
                Log(writer, candidates[i].ToDisplayLine(i + 1));
            }
        }

        Log(writer, "");
        Log(writer, $"sailor {options.ModeName} scanner skeleton finished");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunStrategySkeletonAsync(
        SailorRuntimeMode mode,
        string[] args,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions options = CreateOptions(mode, args, settings);
        string logFilePath = CreateRuntimeLogFilePath(mode, "run");

        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} strategy runtime skeleton started");
        Log(writer, options.ToCompactString());
        Log(writer, "");

        if (mode == SailorRuntimeMode.Live && options.SendOrders)
        {
            Log(writer, "LIVE send-orders is intentionally blocked in SAILOR-022.");
            Log(writer, "The first live command skeleton is dry-run only until broker/session/order safety exists.");
        }

        Log(writer, "Planned runtime loop for later milestones:");
        Log(writer, "1. Connect to broker session.");
        Log(writer, "2. Ensure historical bars exist for selected symbols.");
        Log(writer, "3. Subscribe L1/L2 snapshots.");
        Log(writer, "4. Run Sailor scanner on refresh cadence.");
        Log(writer, "5. Feed SailorStrategyFrame into selected runtime strategy.");
        Log(writer, "6. Convert SailorStrategyDecision into SailorOrderIntent.");
        Log(writer, "7. Route order intent only if send-orders is explicitly enabled and safety checks pass.");
        Log(writer, "8. Force-flat open positions at 15:55 ET.");
        Log(writer, "");
        Log(writer, "No broker connection opened. No orders sent.");
        Log(writer, $"Runtime log: {logFilePath}");
    }

    private static async Task RunStatusSkeletonAsync(
        SailorRuntimeMode mode,
        SailorAppSettings settings)
    {
        SailorRuntimeOptions options = CreateOptions(mode, Array.Empty<string>(), settings);
        var state = new SailorRuntimeState(mode);
        state.SetStatus(SailorRuntimeStatus.Stopped, "Runtime status only; no persistent runtime session exists yet.");

        string logFilePath = CreateRuntimeLogFilePath(mode, "status");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} status");
        Log(writer, state.ToDisplayString());
        Log(writer, "No persistent broker connection. No active subscriptions. No open Sailor-tracked positions in SAILOR-024.");
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
            "Manual flatten command skeleton. SAILOR-024 logs intent only.",
            dryRun: true);

        string logFilePath = CreateRuntimeLogFilePath(mode, $"flatten_{symbol}");
        await using var writer = CreateWriter(logFilePath);
        Log(writer, $"sailor {options.ModeName} flatten skeleton");
        Log(writer, options.ToCompactString());
        Log(writer, intent.ToDisplayString());
        Log(writer, "No persistent broker session. No order sent. Flatten routing starts after the order-router milestone.");
        Log(writer, $"Runtime log: {logFilePath}");
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
        Console.WriteLine($"  sailor {name} scan 1m sailor-trend-volume 3 smallcaps");
        Console.WriteLine($"  sailor {name} run --dry-run");
        Console.WriteLine($"  sailor {name} run 1m v21-15minutes 1 TSLA --dry-run");
        Console.WriteLine($"  sailor {name} status");
        Console.WriteLine($"  sailor {name} flatten TSLA");
        Console.WriteLine();
        Console.WriteLine("SAILOR-024 status:");
        Console.WriteLine("  - runtime contracts and command model exist");
        Console.WriteLine("  - paper/live connect now performs an IBKR/TWS TCP session probe");
        Console.WriteLine("  - no market data subscriptions yet");
        Console.WriteLine("  - no orders sent");
        Console.WriteLine("  - full IBApi nextValidId/account handshake is prepared as the next adapter layer");
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
