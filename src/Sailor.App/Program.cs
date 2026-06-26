using Sailor.App.Backtest;
using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Logging;

Console.WriteLine("sailor - C# day trading application");
Console.WriteLine("Mode: development / paper / backtest only");
Console.WriteLine();

if (args.Length == 0)
{
    PrintHelp();
    return;
}

string command = args[0].Trim().ToLowerInvariant();

switch (command)
{
    case "backtest":
    {
        if (args.Length >= 2 && args[1].Equals("--list", StringComparison.OrdinalIgnoreCase))
        {
            PrintAvailableBacktestData(args);
            break;
        }

        string symbol = args.Length >= 2
            ? args[1].Trim().ToUpperInvariant()
            : "AAPL";

        string timeframe = args.Length >= 3
            ? args[2].Trim()
            : "1m";

        string profileName = args.Length >= 4
            ? args[3].Trim()
            : "sailor-trend-volume";

        await SimpleBacktestRunner.RunAsync(symbol, timeframe, profileName);
        break;
    }

    case "scan":
    {
        string timeframe = args.Length >= 2
            ? args[1].Trim()
            : "1m";

        string profileName = args.Length >= 3
            ? args[2].Trim()
            : "sailor-trend-volume";

        int? topCount = null;
        if (args.Length >= 4 && int.TryParse(args[3], out int parsedTopCount))
        {
            topCount = parsedTopCount;
        }

        await RunScannerAsync(timeframe, profileName, topCount);
        break;
    }

    default:
        Console.WriteLine($"Unknown command: {command}");
        PrintHelp();
        break;
}

static async Task RunScannerAsync(string timeframe, string profileName, int? topCount)
{
    SailorStrategyProfile profile = SailorStrategyProfile.FromName(profileName);
    var provider = new CsvBacktestDataProvider();
    var scanner = new SailorScanner(provider);

    IReadOnlyList<ScannerCandidate> candidates = scanner.Scan(timeframe, profile, topCount);

    string logFilePath = Path.Combine(
        SailorLogPaths.Backtest,
        $"scan_{profile.Name}_{timeframe}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

    await using var fileStream = new FileStream(
        logFilePath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.Read);

    await using var writer = new StreamWriter(fileStream);

    void Log(string message)
    {
        Console.WriteLine(message);
        writer.WriteLine(message);
        writer.Flush();
    }

    Log("sailor scanner started");
    Log($"Timeframe: {timeframe}");
    Log($"Profile: {profile.Name}");
    Log($"Top count: {topCount.GetValueOrDefault(profile.ScannerTopCount)}");
    Log($"Filters: price {profile.MinimumPrice:F2}-{profile.MaximumPrice:F2}, min volume {profile.MinimumVolume}, min volume ratio {profile.MinimumVolumeRatio:F2}");
    Log($"Trend filters: EMA9>SMA20={profile.RequireEma9AboveSma20}, Close>VWAP={profile.RequirePriceAboveVwap}, Close>SMA200 when available={profile.RequirePriceAboveSma200WhenAvailable}");
    Log("");

    if (candidates.Count == 0)
    {
        Log("No scanner candidates found for the selected profile and timeframe.");
    }
    else
    {
        Log("Scanner candidates");
        Log("------------------");

        for (int i = 0; i < candidates.Count; i++)
        {
            Log(candidates[i].ToDisplayLine(i + 1));
        }
    }

    Log("");
    Log("sailor scanner finished");
    Log($"Scanner log: {logFilePath}");
}

static void PrintAvailableBacktestData(string[] args)
{
    var provider = new CsvBacktestDataProvider();

    if (args.Length >= 3)
    {
        string symbol = args[2].Trim().ToUpperInvariant();
        IReadOnlyList<string> timeframes = provider.ListTimeframes(symbol);

        Console.WriteLine($"Available timeframes for {symbol}:");
        foreach (string timeframe in timeframes)
        {
            Console.WriteLine($"  {timeframe}");
        }

        return;
    }

    IReadOnlyList<string> symbols = provider.ListSymbols();

    Console.WriteLine($"Available symbols: {symbols.Count}");
    foreach (string symbol in symbols.Take(80))
    {
        Console.WriteLine($"  {symbol}");
    }

    if (symbols.Count > 80)
    {
        Console.WriteLine($"  ... {symbols.Count - 80} more");
    }
}

static void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  sailor backtest");
    Console.WriteLine("  sailor backtest AAPL");
    Console.WriteLine("  sailor backtest TSLA 1m");
    Console.WriteLine("  sailor backtest TSLA 1m sailor-trend-volume");
    Console.WriteLine("  sailor backtest TSLA 1m simple-momentum");
    Console.WriteLine("  sailor backtest --list");
    Console.WriteLine("  sailor backtest --list AAPL");
    Console.WriteLine("  sailor scan");
    Console.WriteLine("  sailor scan 1m");
    Console.WriteLine("  sailor scan 1m sailor-trend-volume 20");
}
