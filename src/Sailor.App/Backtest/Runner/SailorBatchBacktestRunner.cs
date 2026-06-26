using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Configuration;
using Sailor.App.Logging;

namespace Sailor.App.Backtest.Runner;

public static class SailorBatchBacktestRunner
{
    public static async Task<string> RunAsync(
        string? timeframe,
        string? profileName,
        int? topCount,
        string? universeNameOrCsv,
        SailorAppSettings? settings = null)
    {
        settings ??= new SailorAppSettings();

        string normalizedTimeframe = string.IsNullOrWhiteSpace(timeframe)
            ? settings.DefaultTimeframe
            : timeframe.Trim();

        SailorStrategyProfile profile = SailorStrategyProfile.FromName(profileName, settings);
        ConductExitSettings conductSettings = ResolveConductSettings(settings, profile);
        int configuredTopCount = profile.ScannerTopCount > 0
            ? profile.ScannerTopCount
            : settings.Scanner.DefaultTopCount;
        int effectiveTopCount = Math.Max(1, topCount.GetValueOrDefault(configuredTopCount));

        string universeName = string.IsNullOrWhiteSpace(universeNameOrCsv)
            ? settings.DefaultUniverse
            : universeNameOrCsv.Trim();

        var provider = new CsvBacktestDataProvider();
        IReadOnlyList<string> availableSymbols = provider.ListSymbols();
        IReadOnlyList<string> requestedUniverse = SailorSymbolUniverses.Resolve(universeName, availableSymbols);
        IReadOnlyList<string> availableUniverse = requestedUniverse
            .Where(symbol => availableSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<string> missingSymbols = requestedUniverse
            .Where(symbol => !availableSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scanner = new SailorScanner(provider);
        IReadOnlyList<ScannerCandidate> candidates = scanner.Scan(
            normalizedTimeframe,
            profile,
            effectiveTopCount,
            availableUniverse);

        string reportPath = Path.Combine(
            SailorLogPaths.Backtest,
            $"ranking_{SanitizeFilePart(universeName)}_{profile.Name}_{normalizedTimeframe}_{DateTime.Now:yyyyMMdd_HHmmss}.md");

        Console.WriteLine("sailor batch backtest started");
        Console.WriteLine($"Timeframe: {normalizedTimeframe}");
        Console.WriteLine($"Profile: {profile.Name}");
        Console.WriteLine($"Universe: {universeName}");
        Console.WriteLine($"Universe symbols with data: {availableUniverse.Count}");
        Console.WriteLine($"Scanner top count: {effectiveTopCount}");
        Console.WriteLine();

        var rows = new List<BacktestRankingRow>();
        var errors = new List<string>();

        for (int i = 0; i < candidates.Count; i++)
        {
            ScannerCandidate candidate = candidates[i];
            Console.WriteLine($"Backtesting {i + 1}/{candidates.Count}: {candidate.Symbol}");

            try
            {
                BacktestRunResult result = await SimpleBacktestRunner.RunAsync(
                    candidate.Symbol,
                    normalizedTimeframe,
                    profile.Name,
                    echoToConsole: false,
                    settings: settings);

                rows.Add(new BacktestRankingRow(
                    ScannerRank: i + 1,
                    Candidate: candidate,
                    Result: result));
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.Symbol}: {ex.Message}");
            }
        }

        IReadOnlyList<BacktestRankingRow> rankedRows = rows
            .OrderByDescending(row => row.Result.TotalPnl)
            .ThenByDescending(row => row.Result.WinRatePercent)
            .ThenByDescending(row => row.Candidate.Score)
            .ThenBy(row => row.Candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await WriteReportAsync(
            reportPath,
            normalizedTimeframe,
            profile,
            settings,
            conductSettings,
            universeName,
            requestedUniverse,
            availableUniverse,
            missingSymbols,
            candidates,
            rankedRows,
            errors);

        Console.WriteLine();
        Console.WriteLine("sailor batch backtest finished");
        Console.WriteLine("Ranking report created:");
        Console.WriteLine(reportPath);

        return reportPath;
    }

    private static async Task WriteReportAsync(
        string reportPath,
        string timeframe,
        SailorStrategyProfile profile,
        SailorAppSettings settings,
        ConductExitSettings conductSettings,
        string universeName,
        IReadOnlyList<string> requestedUniverse,
        IReadOnlyList<string> availableUniverse,
        IReadOnlyList<string> missingSymbols,
        IReadOnlyList<ScannerCandidate> scannerCandidates,
        IReadOnlyList<BacktestRankingRow> rankedRows,
        IReadOnlyList<string> errors)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        await using var fileStream = new FileStream(reportPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(fileStream);

        await writer.WriteLineAsync("# Sailor scanner + backtest ranking report");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync($"Timeframe: `{timeframe}`");
        await writer.WriteLineAsync($"Profile: `{profile.Name}`");
        await writer.WriteLineAsync($"Universe: `{universeName}`");
        await writer.WriteLineAsync($"Configured risk: initial cash `{settings.Risk.InitialCash:F2}`, max position `{settings.Risk.MaxPositionNotional:F2}`, stop loss `{settings.Risk.StopLossPercent:F2}%`, take profit `{settings.Risk.TakeProfitPercent:F2}%`, max hold `{settings.Risk.MaxHoldBars}` bars");
        await writer.WriteLineAsync($"Requested symbols: {requestedUniverse.Count}");
        await writer.WriteLineAsync($"Symbols with data: {availableUniverse.Count}");
        await writer.WriteLineAsync($"Scanner candidates: {scannerCandidates.Count}");
        await writer.WriteLineAsync($"Backtested candidates: {rankedRows.Count}");
        await writer.WriteLineAsync();

        await writer.WriteLineAsync("## Profile filters");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"- Price: {profile.MinimumPrice:F2}-{profile.MaximumPrice:F2}");
        await writer.WriteLineAsync($"- Minimum volume: {profile.MinimumVolume}");
        await writer.WriteLineAsync($"- Minimum volume ratio: {profile.MinimumVolumeRatio:F2}");
        await writer.WriteLineAsync($"- Entry momentum: {profile.EntryMomentumPercent:F2}%");
        await writer.WriteLineAsync($"- Exit momentum: {profile.ExitMomentumPercent:F2}%");
        await writer.WriteLineAsync($"- Require EMA9 > SMA20: {profile.RequireEma9AboveSma20}");
        await writer.WriteLineAsync($"- Require close > VWAP: {profile.RequirePriceAboveVwap}");
        await writer.WriteLineAsync($"- Require close > SMA200 when available: {profile.RequirePriceAboveSma200WhenAvailable}");
        await writer.WriteLineAsync($"- Use conduct exits: {profile.UseConductExits}");
        await writer.WriteLineAsync($"- Conduct profile: {profile.ConductProfileName}");
        await writer.WriteLineAsync($"- Market hours enabled: {profile.UseMarketHours}");
        await writer.WriteLineAsync($"- Market open minute: {profile.MarketOpenMinute}");
        await writer.WriteLineAsync($"- Skip first minutes: {profile.SkipFirstMinutes}");
        await writer.WriteLineAsync($"- Last entry minute: {profile.LastEntryMinute}");
        await writer.WriteLineAsync($"- Force flat minute: {profile.ForceFlatMinute}");
        await writer.WriteLineAsync($"- Minimum bars between entries: {profile.MinimumBarsBetweenEntries}");
        await writer.WriteLineAsync($"- Next-bar-open entry: {profile.UseNextBarOpenEntry}");
        await writer.WriteLineAsync();

        await writer.WriteLineAsync("## Risk settings");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"- Initial cash: {settings.Risk.InitialCash:F2}");
        await writer.WriteLineAsync($"- Max position notional: {settings.Risk.MaxPositionNotional:F2}");
        await writer.WriteLineAsync($"- Stop loss: {settings.Risk.StopLossPercent:F2}%");
        await writer.WriteLineAsync($"- Take profit: {settings.Risk.TakeProfitPercent:F2}%");
        await writer.WriteLineAsync($"- Max hold bars: {settings.Risk.MaxHoldBars}");
        await writer.WriteLineAsync();

        if (profile.UseConductExits)
        {
            await writer.WriteLineAsync("## Conduct exit settings");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync($"- Hard stop: {conductSettings.HardStopPercent:F2}%");
            await writer.WriteLineAsync($"- Fixed take profit enabled: {conductSettings.UseTakeProfitExit}");
            await writer.WriteLineAsync($"- Take profit: {conductSettings.TakeProfitPercent:F2}%");
            await writer.WriteLineAsync($"- Move stop to breakeven after: {conductSettings.MoveStopToBreakevenAfterPercent:F2}%");
            await writer.WriteLineAsync($"- Breakeven buffer: {conductSettings.BreakevenBufferPercent:F2}%");
            await writer.WriteLineAsync($"- Start trailing after: {conductSettings.StartTrailingAfterPercent:F2}%");
            await writer.WriteLineAsync($"- Giveback percent: {conductSettings.GivebackPercent:F2}%");
            await writer.WriteLineAsync($"- Giveback notional cap: {conductSettings.GivebackNotionalCap:F2}");
            await writer.WriteLineAsync($"- Indicator exits after bars: {conductSettings.MinimumBarsBeforeIndicatorExit}");
            await writer.WriteLineAsync($"- EMA9 exit: {conductSettings.UseEma9Exit}");
            await writer.WriteLineAsync($"- VWAP exit: {conductSettings.UseVwapExit}");
            await writer.WriteLineAsync($"- Trend exit: {conductSettings.UseTrendExit}");
            await writer.WriteLineAsync($"- Opposite momentum exit: {conductSettings.UseOppositeMomentumExit}");
            await writer.WriteLineAsync($"- Micro trail: {conductSettings.UseMicroTrail}");
            await writer.WriteLineAsync($"- Micro trail activate: {conductSettings.MicroTrailActivatePercent:F2}%");
            await writer.WriteLineAsync($"- Micro trail percent: {conductSettings.MicroTrailPercent:F2}%");
            await writer.WriteLineAsync($"- Max hold bars: {conductSettings.MaxHoldBars}");
            await writer.WriteLineAsync();
        }

        if (missingSymbols.Count > 0)
        {
            await writer.WriteLineAsync("## Symbols from universe without local CSV data");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(string.Join(", ", missingSymbols));
            await writer.WriteLineAsync();
        }

        await writer.WriteLineAsync("## Final ranking by backtest result");
        await writer.WriteLineAsync();

        if (rankedRows.Count == 0)
        {
            await writer.WriteLineAsync("No candidates could be backtested.");
            await writer.WriteLineAsync();
        }
        else
        {
            await writer.WriteLineAsync("| Rank | Symbol | Scanner rank | PnL | Trades | Win rate | Winners | Losers | Scanner score | Momentum | Vol ratio | Close |");
            await writer.WriteLineAsync("|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            for (int i = 0; i < rankedRows.Count; i++)
            {
                await writer.WriteLineAsync(rankedRows[i].ToMarkdownRow(i + 1));
            }

            await writer.WriteLineAsync();
        }

        await writer.WriteLineAsync("## Scanner order before backtest");
        await writer.WriteLineAsync();

        if (scannerCandidates.Count == 0)
        {
            await writer.WriteLineAsync("No scanner candidates passed the profile filters.");
            await writer.WriteLineAsync();
        }
        else
        {
            await writer.WriteLineAsync("| Scanner rank | Symbol | Score | Momentum | Vol ratio | Close | Reason |");
            await writer.WriteLineAsync("|---:|---|---:|---:|---:|---:|---|");

            for (int i = 0; i < scannerCandidates.Count; i++)
            {
                ScannerCandidate candidate = scannerCandidates[i];
                await writer.WriteLineAsync($"| {i + 1} | {candidate.Symbol} | {candidate.Score:F2} | {candidate.MomentumPercent:F2}% | {candidate.VolumeRatio:F2} | {candidate.Close:F2} | {EscapeMarkdown(candidate.Reason)} |");
            }

            await writer.WriteLineAsync();
        }

        if (rankedRows.Count > 0)
        {
            await writer.WriteLineAsync("## Backtest log files");
            await writer.WriteLineAsync();

            foreach (BacktestRankingRow row in rankedRows)
            {
                await writer.WriteLineAsync($"- {row.Candidate.Symbol}: `{row.Result.LogFilePath}`");
            }

            await writer.WriteLineAsync();
        }

        if (errors.Count > 0)
        {
            await writer.WriteLineAsync("## Errors");
            await writer.WriteLineAsync();

            foreach (string error in errors)
            {
                await writer.WriteLineAsync($"- {EscapeMarkdown(error)}");
            }
        }
    }

    private static ConductExitSettings ResolveConductSettings(
        SailorAppSettings settings,
        SailorStrategyProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ConductProfileName) &&
            settings.ConductProfiles.TryGetValue(profile.ConductProfileName, out ConductExitSettings? conductProfile))
        {
            return conductProfile;
        }

        if (settings.ConductProfiles.TryGetValue(profile.Name, out ConductExitSettings? profileByName))
        {
            return profileByName;
        }

        return settings.Conduct;
    }

    private static string SanitizeFilePart(string value)
    {
        string sanitized = string.Join(
            "_",
            value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        sanitized = sanitized.Replace(',', '_').Replace(' ', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "all" : sanitized;
    }

    private static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|");
    }
}
