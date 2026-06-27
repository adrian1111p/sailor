using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Reports;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Configuration;
using Sailor.App.Logging;

namespace Sailor.App.Backtest.SelfTest;

public static class BacktestSelfTestRunner
{
    private static readonly string[] RequiredProfiles =
    [
        "simple-momentum",
        "sailor-trend-volume",
        "sailor-conduct-v3",
        "harvester-conduct-v3",
        "harvester-conduct-v9",
        "v21-15minutes",
        "v23-5minutes",
        "v24-5minutes",
        "v22-15minutes",
        "v16-sqzbreakout",
        "v13",
        "v10-hybrid",
        "v17-hybridflow",
        "v2-conduct",
        "v18-silver",
        "v1-first",
        "conduct-v3",
        "v19-purplecloud",
        "v15-shortcap",
        "v14-smallcap",
        "v20-gen001-choppyshield",
        "v12"
    ];

    private static readonly string[] PreferredSymbols =
    [
        "TSLA", "SOFI", "PLTR", "AIXI", "JOBY", "AFRM", "DKNG", "CIFR"
    ];

    public static async Task<int> RunAsync(string? mode, SailorAppSettings settings)
    {
        string normalizedMode = string.IsNullOrWhiteSpace(mode)
            ? "quick"
            : mode.Trim().ToLowerInvariant();

        bool fullMode = normalizedMode is "full" or "all" or "complete";
        bool reportMode = normalizedMode is "report" or "html" or "html-report";

        var results = new List<BacktestSelfTestCaseResult>();
        string logFilePath = Path.Combine(
            SailorLogPaths.Backtest,
            $"selftest_backtest_{normalizedMode}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

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

        Log("sailor backtest self-test started");
        Log($"Mode: {normalizedMode}");
        Log($"Log file: {logFilePath}");
        Log("");

        await RunCaseAsync(results, Log, "Data provider lists CSV symbols", () => TestDataProviderListsSymbols());
        await RunCaseAsync(results, Log, "Small-cap universe has data", () => TestSmallCapUniverseHasData());
        await RunCaseAsync(results, Log, "Key symbol 1m bars can load", () => TestKeySymbolBarsCanLoad());
        await RunCaseAsync(results, Log, "Indicators are calculated", () => TestIndicatorsAreCalculated());
        await RunCaseAsync(results, Log, "All configured strategy profiles load", () => TestProfilesLoad(settings));
        await RunCaseAsync(results, Log, "V11 remains intentionally excluded", () => TestV11IsExcluded(settings));
        await RunCaseAsync(results, Log, "Market-hours policy is safe", () => TestMarketHoursPolicy(settings));
        await RunCaseAsync(results, Log, "Long/short PnL mirror is correct", () => TestLongShortPnlMirror());
        await RunCaseAsync(results, Log, "Scanner produces ranked candidates", () => TestScannerProducesCandidates(settings));
        await RunCaseAsync(results, Log, "Single-symbol backtest produces multiple intraday trades", () => TestMultipleIntradayTrades(settings));
        await RunCaseAsync(results, Log, "V21/V22/V23/V24 profiles keep long/short conduct enabled", () => TestAngleProfiles(settings));

        if (fullMode || reportMode)
        {
            await RunCaseAsync(results, Log, "All profiles execute on one symbol", () => TestAllProfilesExecuteOnOneSymbol(settings));
            await RunCaseAsync(results, Log, "Mini HTML report can be generated", () => TestMiniHtmlReport(settings));
        }

        int passed = results.Count(r => r.Passed);
        int failed = results.Count - passed;

        Log("");
        Log("Backtest self-test summary");
        Log("--------------------------");
        Log($"Passed: {passed}");
        Log($"Failed: {failed}");
        Log($"Total:  {results.Count}");
        Log($"Log:    {logFilePath}");

        if (failed > 0)
        {
            Log("");
            Log("Failed tests:");
            foreach (BacktestSelfTestCaseResult result in results.Where(r => !r.Passed))
            {
                Log($"  - {result.Name}: {result.Message}");
            }
        }

        Log("");
        Log(failed == 0
            ? "sailor backtest self-test finished: PASS"
            : "sailor backtest self-test finished: FAIL");

        return failed == 0 ? 0 : 1;
    }

    private static async Task RunCaseAsync(
        List<BacktestSelfTestCaseResult> results,
        Action<string> log,
        string name,
        Func<Task<string>> test)
    {
        DateTime started = DateTime.UtcNow;

        try
        {
            string message = await test();
            TimeSpan duration = DateTime.UtcNow - started;
            results.Add(BacktestSelfTestCaseResult.Pass(name, message, duration));
            log($"PASS | {duration.TotalMilliseconds,7:F0} ms | {name} | {message}");
        }
        catch (Exception ex)
        {
            TimeSpan duration = DateTime.UtcNow - started;
            results.Add(BacktestSelfTestCaseResult.Fail(name, ex.Message, duration));
            log($"FAIL | {duration.TotalMilliseconds,7:F0} ms | {name} | {ex.Message}");
        }
    }

    private static Task<string> TestDataProviderListsSymbols()
    {
        var provider = new CsvBacktestDataProvider();
        IReadOnlyList<string> symbols = provider.ListSymbols();

        Require(symbols.Count >= 20, $"Expected at least 20 CSV symbols, found {symbols.Count}.");

        return Task.FromResult($"symbols={symbols.Count}");
    }

    private static Task<string> TestSmallCapUniverseHasData()
    {
        var provider = new CsvBacktestDataProvider();
        IReadOnlyList<string> available = provider.ListSymbols();
        IReadOnlyList<string> smallCaps = SailorSymbolUniverses.Resolve("smallcaps", available);
        int withData = smallCaps.Count(symbol =>
            available.Contains(symbol, StringComparer.OrdinalIgnoreCase) &&
            provider.ListTimeframes(symbol).Contains("1m", StringComparer.OrdinalIgnoreCase));

        Require(withData >= 20, $"Expected at least 20 small-cap symbols with 1m data, found {withData}.");

        return Task.FromResult($"smallcaps-with-1m-data={withData}/{smallCaps.Count}");
    }

    private static Task<string> TestKeySymbolBarsCanLoad()
    {
        var provider = new CsvBacktestDataProvider();
        string symbol = ResolvePreferredSymbol(provider);
        BacktestDataSet dataSet = provider.LoadBars(symbol, "1m");

        Require(dataSet.Bars.Count >= 100, $"Expected at least 100 bars for {symbol}, found {dataSet.Bars.Count}.");
        Require(dataSet.Bars.Zip(dataSet.Bars.Skip(1)).All(pair => pair.First.Time <= pair.Second.Time),
            $"Bars are not sorted by time for {symbol}.");

        return Task.FromResult($"symbol={symbol}, bars={dataSet.Bars.Count}");
    }

    private static Task<string> TestIndicatorsAreCalculated()
    {
        var provider = new CsvBacktestDataProvider();
        string symbol = ResolvePreferredSymbol(provider);
        BacktestDataSet dataSet = provider.LoadBars(symbol, "1m");
        IReadOnlyList<BacktestIndicatorSnapshot> indicators = TechnicalIndicatorCalculator.Calculate(dataSet.Bars);

        Require(indicators.Count == dataSet.Bars.Count,
            $"Indicator count {indicators.Count} does not match bar count {dataSet.Bars.Count}.");
        Require(indicators.Any(i => i.Ema9.HasValue), "EMA9 was never calculated.");
        Require(indicators.Any(i => i.Sma20.HasValue), "SMA20 was never calculated.");
        Require(indicators.Any(i => i.Vwap.HasValue), "VWAP was never calculated.");

        if (dataSet.Bars.Count >= 200)
        {
            Require(indicators.Any(i => i.Sma200.HasValue), "SMA200 was never calculated despite 200+ bars.");
        }

        BacktestIndicatorSnapshot last = indicators[^1];
        return Task.FromResult($"symbol={symbol}, last={last.ToCompactString()}");
    }

    private static Task<string> TestProfilesLoad(SailorAppSettings settings)
    {
        var loaded = new List<string>();

        foreach (string profileName in RequiredProfiles)
        {
            SailorStrategyProfile profile = SailorStrategyProfile.FromName(profileName, settings);
            Require(!string.IsNullOrWhiteSpace(profile.Name), $"Profile {profileName} loaded with empty name.");
            loaded.Add(profile.Name);
        }

        return Task.FromResult($"profiles={loaded.Count}");
    }

    private static Task<string> TestV11IsExcluded(SailorAppSettings settings)
    {
        try
        {
            _ = SailorStrategyProfile.FromName("v11", settings);
        }
        catch (ArgumentException)
        {
            return Task.FromResult("v11 rejected as expected");
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult("v11 rejected as expected");
        }

        throw new InvalidOperationException("Profile v11 loaded successfully, but V11 should remain excluded.");
    }

    private static Task<string> TestMarketHoursPolicy(SailorAppSettings settings)
    {
        var checkedProfiles = new List<string>();

        foreach (string profileName in RequiredProfiles)
        {
            SailorStrategyProfile profile = SailorStrategyProfile.FromName(profileName, settings);
            if (!profile.UseMarketHours)
            {
                continue;
            }

            Require(profile.LastEntryMinute <= 945,
                $"Profile {profile.Name} last entry is {profile.LastEntryMinute}; expected <= 945 ET.");
            Require(profile.ForceFlatMinute == 955,
                $"Profile {profile.Name} force-flat is {profile.ForceFlatMinute}; expected 955 ET.");
            checkedProfiles.Add(profile.Name);
        }

        Require(checkedProfiles.Count > 0, "No market-hours profiles were checked.");

        return Task.FromResult($"market-hour-profiles={checkedProfiles.Count}");
    }

    private static Task<string> TestLongShortPnlMirror()
    {
        var longWin = new BacktestTrade(
            Symbol: "TEST",
            EntryTime: DateTimeOffset.UtcNow,
            ExitTime: DateTimeOffset.UtcNow.AddMinutes(1),
            EntryPrice: 10m,
            ExitPrice: 11m,
            Quantity: 100,
            EntryReason: "test long",
            ExitReason: "test exit",
            PositionSide: 1);

        var shortWin = new BacktestTrade(
            Symbol: "TEST",
            EntryTime: DateTimeOffset.UtcNow,
            ExitTime: DateTimeOffset.UtcNow.AddMinutes(1),
            EntryPrice: 10m,
            ExitPrice: 9m,
            Quantity: 100,
            EntryReason: "test short",
            ExitReason: "test exit",
            PositionSide: -1);

        Require(longWin.Pnl == 100m, $"Long PnL expected 100, got {longWin.Pnl}.");
        Require(shortWin.Pnl == 100m, $"Short PnL expected 100, got {shortWin.Pnl}.");
        Require(longWin.PnlPercent == 10m, $"Long PnL% expected 10, got {longWin.PnlPercent}.");
        Require(shortWin.PnlPercent == 10m, $"Short PnL% expected 10, got {shortWin.PnlPercent}.");

        return Task.FromResult("long and short PnL mirror ok");
    }

    private static Task<string> TestScannerProducesCandidates(SailorAppSettings settings)
    {
        var provider = new CsvBacktestDataProvider();
        var scanner = new SailorScanner(provider);
        SailorStrategyProfile profile = SailorStrategyProfile.FromName("simple-momentum", settings);
        IReadOnlyList<ScannerCandidate> candidates = scanner.Scan("1m", profile, topCount: 5);

        Require(candidates.Count > 0, "Scanner returned zero candidates for simple-momentum.");
        Require(candidates.All(candidate => candidate.Score >= 0), "Scanner returned a candidate with negative score.");

        return Task.FromResult($"candidates={candidates.Count}, top={candidates[0].Symbol}/{candidates[0].Side}");
    }

    private static async Task<string> TestMultipleIntradayTrades(SailorAppSettings settings)
    {
        string[] symbolsToTry = ResolvePreferredSymbolsWithData(new CsvBacktestDataProvider()).Take(6).ToArray();
        string[] profilesToTry = ["simple-momentum", "sailor-trend-volume", "conduct-v3", "v16-sqzbreakout"];
        var attempts = new List<string>();

        foreach (string symbol in symbolsToTry)
        {
            foreach (string profile in profilesToTry)
            {
                BacktestRunResult result = await SimpleBacktestRunner.RunAsync(
                    symbol,
                    "1m",
                    profile,
                    echoToConsole: false,
                    settings: settings);

                attempts.Add($"{symbol}/{profile}={result.TotalTrades}");

                if (result.TotalTrades >= 2)
                {
                    Require(result.Trades.Select(t => t.EntryTime.Date).Distinct().Any(), "Trade dates were not populated.");
                    return $"{symbol}/{profile} trades={result.TotalTrades}, long={result.LongTrades}, short={result.ShortTrades}";
                }
            }
        }

        throw new InvalidOperationException("No tested symbol/profile produced at least 2 trades. Attempts: " + string.Join(", ", attempts));
    }

    private static Task<string> TestAngleProfiles(SailorAppSettings settings)
    {
        string[] angleProfiles = ["v21-15minutes", "v22-15minutes", "v23-5minutes", "v24-5minutes"];

        foreach (string profileName in angleProfiles)
        {
            SailorStrategyProfile profile = SailorStrategyProfile.FromName(profileName, settings);
            Require(profile.SideMode.AllowsLong(), $"{profileName} does not allow long entries.");
            Require(profile.SideMode.AllowsShort(), $"{profileName} does not allow short entries.");
            Require(profile.UseNextBarOpenEntry, $"{profileName} should use next-bar-open entries.");
            Require(profile.UseMarketHours, $"{profileName} should use market-hours control.");
            Require(profile.LastEntryMinute <= 945, $"{profileName} last entry should be <= 945 ET.");
            Require(profile.ForceFlatMinute == 955, $"{profileName} force-flat should be 955 ET.");
        }

        return Task.FromResult("V21/V22/V23/V24 long-short and time controls ok");
    }

    private static async Task<string> TestAllProfilesExecuteOnOneSymbol(SailorAppSettings settings)
    {
        var provider = new CsvBacktestDataProvider();
        string symbol = ResolvePreferredSymbol(provider);
        var summaries = new List<string>();

        foreach (string profileName in RequiredProfiles)
        {
            BacktestRunResult result = await SimpleBacktestRunner.RunAsync(
                symbol,
                "1m",
                profileName,
                echoToConsole: false,
                settings: settings);

            Require(result.Bars > 0, $"{profileName} returned zero bars on {symbol}.");
            Require(result.TotalTrades >= 0, $"{profileName} returned negative trade count.");
            Require(result.Winners + result.Losers <= result.TotalTrades,
                $"{profileName} winners+losers exceeds total trades.");
            summaries.Add($"{profileName}:{result.TotalTrades}");
        }

        return $"symbol={symbol}, " + string.Join(", ", summaries);
    }

    private static async Task<string> TestMiniHtmlReport(SailorAppSettings settings)
    {
        var provider = new CsvBacktestDataProvider();
        string[] symbols = ResolvePreferredSymbolsWithData(provider).Take(2).ToArray();
        Require(symbols.Length >= 1, "No symbols with 1m data for mini HTML report.");

        string universeCsv = string.Join(',', symbols);
        string profilesCsv = "simple-momentum,v21-15minutes";
        string path = await SailorHtmlReportGenerator.RunAsync(
            "1m",
            universeCsv,
            settings,
            symbolLimit: symbols.Length,
            profilesCsv: profilesCsv);

        Require(File.Exists(path), $"HTML report was not created: {path}");
        var info = new FileInfo(path);
        Require(info.Length > 1_000, $"HTML report is unexpectedly small: {info.Length} bytes.");

        return $"report={path}";
    }

    private static string ResolvePreferredSymbol(CsvBacktestDataProvider provider)
    {
        string? symbol = ResolvePreferredSymbolsWithData(provider).FirstOrDefault();
        if (symbol is null)
        {
            throw new InvalidOperationException("No preferred symbols with 1m data were found.");
        }

        return symbol;
    }

    private static IReadOnlyList<string> ResolvePreferredSymbolsWithData(CsvBacktestDataProvider provider)
    {
        IReadOnlyList<string> available = provider.ListSymbols();

        return PreferredSymbols
            .Where(symbol => available.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Where(symbol => provider.ListTimeframes(symbol).Contains("1m", StringComparer.OrdinalIgnoreCase))
            .Concat(available.Where(symbol => provider.ListTimeframes(symbol).Contains("1m", StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
