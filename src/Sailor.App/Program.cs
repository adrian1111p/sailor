using System.Globalization;
using Sailor.App.Backtest;
using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Runner;
using Sailor.App.Backtest.Reports;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Backtest.SelfTest;
using Sailor.App.Configuration;
using Sailor.App.Logging;
using Sailor.App.Runtime.Commands;
using Sailor.App.Runtime.Common;
using Sailor.App.Scanner.ScanList;
using Sailor.App.Scanner.Universe;

SailorAppSettings settings = SailorSettingsLoader.Load();

Console.WriteLine("sailor - C# day trading application");
Console.WriteLine("Mode: development / backtest / paper-live skeleton");
Console.WriteLine($"Default timeframe: {settings.DefaultTimeframe}");
Console.WriteLine($"Default profile: {settings.DefaultProfile}");
Console.WriteLine();

if (args.Length == 0)
{
    PrintHelp(settings);
    return;
}

string command = args[0].Trim().ToLowerInvariant();

switch (command)
{

    case "paper":
    {
        await SailorRuntimeCommandRunner.RunAsync(
            SailorRuntimeMode.Paper,
            args.Skip(1).ToArray(),
            settings);
        break;
    }

    case "live":
    {
        await SailorRuntimeCommandRunner.RunAsync(
            SailorRuntimeMode.Live,
            args.Skip(1).ToArray(),
            settings);
        break;
    }

    case "scan-list":
    {
        await RunScanListInspectAsync(args.Skip(1).ToArray());
        break;
    }

    case "backtest":
    {
        if (args.Length >= 2 && args[1].Equals("--list", StringComparison.OrdinalIgnoreCase))
        {
            PrintAvailableBacktestData(args);
            break;
        }

        if (args.Length >= 2 && args[1].Equals("scan-list", StringComparison.OrdinalIgnoreCase))
        {
            string[] scanListArgs = args.Skip(2).ToArray();
            string[] positional = scanListArgs.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
            string scanListTimeframe = positional.Length >= 1 ? positional[0].Trim() : settings.DefaultTimeframe;
            string scanListProfileName = positional.Length >= 2 ? positional[1].Trim() : settings.DefaultProfile;
            int? topCount = positional.Length >= 3 && int.TryParse(positional[2], out int parsedTop) ? parsedTop : null;
            string scanFile = ReadStringOption(scanListArgs, "--file", ReadStringOption(scanListArgs, "--scan-file", ScanListWorkbookOptions.DefaultFilePath));
            string scanSheet = ReadStringOption(scanListArgs, "--sheet", ReadStringOption(scanListArgs, "--scan-sheet", ScanListWorkbookOptions.DefaultSheetName));
            string symbolColumn = ReadStringOption(scanListArgs, "--symbol-column", ScanListWorkbookOptions.DefaultSymbolColumn);
            string universeArgument = SymbolUniverseProviderFactory.BuildXlsxUniverseArgument(scanFile, scanSheet, symbolColumn);

            Console.WriteLine("SAILOR-037 backtest scan-list input enabled.");
            Console.WriteLine($"Scan list: file={scanFile} sheet={scanSheet} symbolColumn={symbolColumn}");
            await SailorBatchBacktestRunner.RunAsync(scanListTimeframe, scanListProfileName, topCount, universeArgument, settings);
            break;
        }

        string symbol = args.Length >= 2
            ? args[1].Trim().ToUpperInvariant()
            : "AAPL";

        string timeframe = args.Length >= 3
            ? args[2].Trim()
            : settings.DefaultTimeframe;

        string profileName = args.Length >= 4
            ? args[3].Trim()
            : settings.DefaultProfile;

        await SimpleBacktestRunner.RunAsync(symbol, timeframe, profileName, echoToConsole: true, settings: settings);
        break;
    }

    case "scan":
    {
        string timeframe = args.Length >= 2
            ? args[1].Trim()
            : settings.DefaultTimeframe;

        string profileName = args.Length >= 3
            ? args[2].Trim()
            : settings.DefaultProfile;

        int? topCount = null;
        if (args.Length >= 4 && int.TryParse(args[3], out int parsedTopCount))
        {
            topCount = parsedTopCount;
        }

        await RunScannerAsync(timeframe, profileName, topCount, settings);
        break;
    }

    case "html-report":
    case "report-html":
    case "strategy-report":
    {
        string timeframe = args.Length >= 2
            ? args[1].Trim()
            : settings.DefaultTimeframe;

        string universeNameOrCsv = args.Length >= 3
            ? args[2].Trim()
            : settings.DefaultUniverse;

        int? symbolLimit = null;
        if (args.Length >= 4 && int.TryParse(args[3], out int parsedSymbolLimit) && parsedSymbolLimit > 0)
        {
            symbolLimit = parsedSymbolLimit;
        }

        string? profilesCsv = args.Length >= 5
            ? args[4].Trim()
            : null;

        await SailorHtmlReportGenerator.RunAsync(
            timeframe,
            universeNameOrCsv,
            settings,
            symbolLimit,
            profilesCsv);

        break;
    }


    case "test-backtest":
    case "backtest-test":
    case "self-test":
    {
        string testMode = args.Length >= 2
            ? args[1].Trim()
            : "quick";

        int exitCode = await BacktestSelfTestRunner.RunAsync(testMode, settings);
        Environment.ExitCode = exitCode;
        break;
    }

    case "rank":
    case "scan-backtest":
    case "batch":
    {
        if (args.Any(arg => arg.Equals("--scan-file", StringComparison.OrdinalIgnoreCase) || arg.Equals("--file", StringComparison.OrdinalIgnoreCase)))
        {
            string[] rankArgs = args.Skip(1).ToArray();
            string[] positional = rankArgs.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
            string timeframeFromScanList = positional.Length >= 1 ? positional[0].Trim() : settings.DefaultTimeframe;
            string profileFromScanList = positional.Length >= 2 ? positional[1].Trim() : settings.DefaultProfile;
            int? topFromScanList = positional.Length >= 3 && int.TryParse(positional[2], out int parsedRankTop) ? parsedRankTop : null;
            string scanFile = ReadStringOption(rankArgs, "--file", ReadStringOption(rankArgs, "--scan-file", ScanListWorkbookOptions.DefaultFilePath));
            string scanSheet = ReadStringOption(rankArgs, "--sheet", ReadStringOption(rankArgs, "--scan-sheet", ScanListWorkbookOptions.DefaultSheetName));
            string symbolColumn = ReadStringOption(rankArgs, "--symbol-column", ScanListWorkbookOptions.DefaultSymbolColumn);
            string universeArgument = SymbolUniverseProviderFactory.BuildXlsxUniverseArgument(scanFile, scanSheet, symbolColumn);

            Console.WriteLine("SAILOR-037 rank/backtest scan-list input enabled.");
            Console.WriteLine($"Scan list: file={scanFile} sheet={scanSheet} symbolColumn={symbolColumn}");
            await SailorBatchBacktestRunner.RunAsync(timeframeFromScanList, profileFromScanList, topFromScanList, universeArgument, settings);
            break;
        }

        string timeframe = args.Length >= 2
            ? args[1].Trim()
            : settings.DefaultTimeframe;

        string profileName = args.Length >= 3
            ? args[2].Trim()
            : settings.DefaultProfile;

        int? topCount = null;
        if (args.Length >= 4 && int.TryParse(args[3], out int parsedTopCount))
        {
            topCount = parsedTopCount;
        }

        string universeNameOrCsv = args.Length >= 5
            ? args[4].Trim()
            : settings.DefaultUniverse;

        await SailorBatchBacktestRunner.RunAsync(
            timeframe,
            profileName,
            topCount,
            universeNameOrCsv,
            settings);

        break;
    }

    default:
        Console.WriteLine($"Unknown command: {command}");
        PrintHelp(settings);
        break;
}


static Task RunScanListInspectAsync(string[] args)
{
    string action = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.Trim().ToLowerInvariant() ?? "inspect";
    if (!action.Equals("inspect", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Unknown scan-list subcommand: {action}");
        Console.WriteLine("Usage: sailor scan-list inspect --file scan/data/scan_default.xlsx --sheet Candidates");
        return Task.CompletedTask;
    }

    string file = ReadStringOption(args, "--file", ReadStringOption(args, "--scan-file", ScanListWorkbookOptions.DefaultFilePath));
    string sheet = ReadStringOption(args, "--sheet", ReadStringOption(args, "--scan-sheet", ScanListWorkbookOptions.DefaultSheetName));
    string symbolColumn = ReadStringOption(args, "--symbol-column", ScanListWorkbookOptions.DefaultSymbolColumn);
    int refreshSeconds = ReadIntOption(args, "--scan-refresh-seconds", ScanListWorkbookOptions.DefaultRefreshSeconds);
    int tradeTop = Math.Max(10, ReadIntOption(args, "--trade-top", ReadIntOption(args, "--keep-trade-top", ScanListWorkbookOptions.DefaultTradeTop)));
    int historyBatchSize = ReadIntOption(args, "--history-batch-size", ScanListWorkbookOptions.DefaultHistoryBatchSize);
    int historyBatchIntervalMinutes = ReadIntOption(args, "--history-batch-interval-minutes", ScanListWorkbookOptions.DefaultHistoryBatchIntervalMinutes);

    var options = new ScanListWorkbookOptions(
        file,
        sheet,
        symbolColumn,
        refreshSeconds,
        tradeTop,
        historyBatchSize,
        historyBatchIntervalMinutes);

    var reader = new ScanListWorkbookReader();
    ScanListWorkbookResult result = reader.Read(options);

    Console.WriteLine("SAILOR-038 scan-list inspect");
    Console.WriteLine(result.ToSummaryString());
    Console.WriteLine(options.ToDisplayString());
    Console.WriteLine($"historyBatches={Math.Max(1, (int)Math.Ceiling(result.SymbolCount / (double)Math.Max(1, historyBatchSize)))}");
    Console.WriteLine($"dailyListRefresh=enabled refreshSeconds={refreshSeconds}");
    Console.WriteLine($"intradaySymbolAdditions=enabled add/drop detection will be evaluated every {refreshSeconds}s by the runtime host");
    Console.WriteLine($"tradeSelection=best {tradeTop} scanner-rated symbols retained for paper/live entry eligibility every {refreshSeconds}s");
    Console.WriteLine("removedSymbolRetention=enabled symbols removed from workbook stay managed when they have open positions or recent top selection");
    Console.WriteLine("historyScheduler=enabled max 45 symbols per batch by default and 10 minutes between batches");
    Console.WriteLine("connectionInterruptionPolicy=runtime must keep the last clean scan-list snapshot in memory and move trading to CloseOnly when broker/server state is degraded");
    Console.WriteLine();

    if (result.Symbols.Count == 0)
    {
        Console.WriteLine("Symbols: none");
    }
    else
    {
        Console.WriteLine("Symbols preview:");
        foreach (string symbol in result.Symbols.Take(80))
        {
            Console.WriteLine($"  {symbol}");
        }

        if (result.Symbols.Count > 80)
        {
            Console.WriteLine($"  ... {result.Symbols.Count - 80} more");
        }
    }

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings");
        Console.WriteLine("--------");
        foreach (string warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }
    }

    if (result.RejectedTokens.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Rejected tokens: {string.Join(", ", result.RejectedTokens.Take(20))}{(result.RejectedTokens.Count > 20 ? ", ..." : string.Empty)}");
    }

    return Task.CompletedTask;
}

static string ReadStringOption(string[] args, string name, string defaultValue)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(args[i + 1]) &&
            !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return args[i + 1].Trim();
        }
    }

    return defaultValue;
}

static int ReadIntOption(string[] args, string name, int defaultValue)
{
    string value = ReadStringOption(args, name, defaultValue.ToString(CultureInfo.InvariantCulture));
    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
        ? parsed
        : defaultValue;
}

static async Task RunScannerAsync(
    string timeframe,
    string profileName,
    int? topCount,
    SailorAppSettings settings)
{
    SailorStrategyProfile profile = SailorStrategyProfile.FromName(profileName, settings);
    var provider = new CsvBacktestDataProvider();
    var scanner = new SailorScanner(provider);

    int effectiveTopCount = Math.Max(
        1,
        topCount.GetValueOrDefault(profile.ScannerTopCount > 0
            ? profile.ScannerTopCount
            : settings.Scanner.DefaultTopCount));

    IReadOnlyList<ScannerCandidate> candidates = scanner.Scan(timeframe, profile, effectiveTopCount);

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
    Log($"Top count: {effectiveTopCount}");
    Log($"Filters: price {profile.MinimumPrice:F2}-{profile.MaximumPrice:F2}, min volume {profile.MinimumVolume}, min volume ratio {profile.MinimumVolumeRatio:F2}");
    Log($"Trend filters: EMA9>SMA20={profile.RequireEma9AboveSma20}, Close>VWAP={profile.RequirePriceAboveVwap}, Close>SMA200 when available={profile.RequirePriceAboveSma200WhenAvailable}");
    Log($"Risk settings: initial cash {settings.Risk.InitialCash:F2}, max position {settings.Risk.MaxPositionNotional:F2}, SL {settings.Risk.StopLossPercent:F2}%, TP {settings.Risk.TakeProfitPercent:F2}%, max hold {settings.Risk.MaxHoldBars}");
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

static void PrintHelp(SailorAppSettings settings)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  sailor backtest");
    Console.WriteLine("  sailor backtest AAPL");
    Console.WriteLine("  sailor backtest TSLA 1m");
    Console.WriteLine("  sailor backtest TSLA 1m sailor-trend-volume");
    Console.WriteLine("  sailor backtest TSLA 1m sailor-conduct-v3");
    Console.WriteLine("  sailor backtest TSLA 1m harvester-conduct-v3");
    Console.WriteLine("  sailor backtest TSLA 1m harvester-conduct-v9");
    Console.WriteLine("  sailor backtest TSLA 1m v21-15minutes");
    Console.WriteLine("  sailor backtest TSLA 1m v24-5minutes");
    Console.WriteLine("  sailor backtest TSLA 1m v16-sqzbreakout");
    Console.WriteLine("  sailor backtest TSLA 1m simple-momentum");
    Console.WriteLine("  sailor backtest --list");
    Console.WriteLine("  sailor scan-list inspect --file scan/data/scan_default.xlsx --sheet Candidates");
    Console.WriteLine("  sailor backtest scan-list 1m v21-15minutes 10 --file scan/data/scan_default.xlsx --sheet Candidates");
    Console.WriteLine("  sailor backtest --list AAPL");
    Console.WriteLine("  sailor scan");
    Console.WriteLine("  sailor scan 1m");
    Console.WriteLine("  sailor scan 1m sailor-trend-volume 20");
    Console.WriteLine("  sailor scan 1m sailor-conduct-v3 20");
    Console.WriteLine("  sailor scan 1m harvester-conduct-v3 20");
    Console.WriteLine("  sailor scan 1m harvester-conduct-v9 20");
    Console.WriteLine("  sailor scan 1m v21-15minutes 20");
    Console.WriteLine("  sailor scan 1m v24-5minutes 20");
    Console.WriteLine("  sailor rank");
    Console.WriteLine("  sailor rank 1m sailor-trend-volume 20 all");
    Console.WriteLine("  sailor rank 1m sailor-trend-volume 20 smallcaps");
    Console.WriteLine("  sailor rank 1m sailor-conduct-v3 20 smallcaps");
    Console.WriteLine("  sailor rank 1m harvester-conduct-v3 20 smallcaps");
    Console.WriteLine("  sailor rank 1m harvester-conduct-v9 20 smallcaps");
    Console.WriteLine("  sailor rank 1m v21-15minutes 20 smallcaps");
    Console.WriteLine("  sailor rank 1m v24-5minutes 20 smallcaps");
    Console.WriteLine("  sailor rank 1m simple-momentum 20 ALIT,BARK,SOFI,PLTR");
    Console.WriteLine("  sailor rank 1m v21-15minutes 10 --scan-file scan/data/scan_default.xlsx --scan-sheet Candidates");
    Console.WriteLine("  sailor html-report 1m smallcaps");
    Console.WriteLine("  sailor html-report 1m smallcaps 20");
    Console.WriteLine("  sailor html-report 1m smallcaps 0 v21-15minutes,v23-5minutes,v24-5minutes,v22-15minutes");
    Console.WriteLine("  sailor test-backtest");
    Console.WriteLine("  sailor test-backtest quick");
    Console.WriteLine("  sailor test-backtest full");
    Console.WriteLine("  sailor paper connect");
    Console.WriteLine("  sailor paper scan 1m sailor-trend-volume 3 smallcaps");
    Console.WriteLine("  sailor paper run 1m v21-15minutes 1 TSLA --dry-run");
    Console.WriteLine("  sailor paper order TSLA BUY 1 LMT 350.00 --dry-run");
    Console.WriteLine("  sailor paper order TSLA BUY 1 LMT 350.00 --send-orders");
    Console.WriteLine("  sailor paper status");
    Console.WriteLine("  sailor paper flatten TSLA");
    Console.WriteLine("  sailor live connect");
    Console.WriteLine("  sailor live status");
    Console.WriteLine();
    Console.WriteLine("Harvester-inspired Sailor-native conduct profiles available now:");
    Console.WriteLine("  v21-15minutes, v23-5minutes, v24-5minutes, v22-15minutes, v16-sqzbreakout, v13,");
    Console.WriteLine("  v10-hybrid, v17-hybridflow, v2-conduct, v18-silver, v1-first, conduct-v3,");
    Console.WriteLine("  v19-purplecloud, v15-shortcap, v14-smallcap, v20-gen001-choppyshield, v12");
    Console.WriteLine("  V11 is intentionally excluded.");
    Console.WriteLine();
    Console.WriteLine("Current appsettings defaults:");
    Console.WriteLine($"  timeframe: {settings.DefaultTimeframe}");
    Console.WriteLine($"  profile:   {settings.DefaultProfile}");
    Console.WriteLine($"  universe:  {settings.DefaultUniverse}");
    Console.WriteLine($"  top count: {settings.Scanner.DefaultTopCount}");
}
