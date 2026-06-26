using System.Net;
using System.Text;
using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Strategies.HarvesterConduct;
using Sailor.App.Configuration;
using Sailor.App.Logging;

namespace Sailor.App.Backtest.Reports;

public static class SailorHtmlReportGenerator
{
    private static readonly IReadOnlyList<StrategyReportDefinition> BuiltInStrategies =
    [
        new("Sailor-TrendVolume", "sailor-trend-volume", "sailor", "Sailor"),
        new("Sailor-ConductV3", "sailor-conduct-v3", "sailor", "Sailor"),
        new("SimpleMomentum", "simple-momentum", "sailor", "Sailor"),
        new("Harvester-ConductV3", "harvester-conduct-v3", "sailor-native", "Harvester conduct"),
        new("Harvester-ConductV9", "harvester-conduct-v9", "sailor-native", "Harvester conduct"),
        new("V21-15Minutes", "v21-15minutes", "default", "Harvester conduct"),
        new("V23-5Minutes", "v23-5minutes", "default", "Harvester conduct"),
        new("V24-5Minutes", "v24-5minutes", "default", "Harvester conduct"),
        new("V22-15Minutes", "v22-15minutes", "default", "Harvester conduct"),
        new("V16-SqzBreakout", "v16-sqzbreakout", "default", "Harvester conduct"),
        new("V13", "v13", "default", "Harvester conduct"),
        new("V10-Hybrid", "v10-hybrid", "default", "Harvester conduct"),
        new("V17-HybridFlow", "v17-hybridflow", "legacy-default", "Harvester conduct"),
        new("V2-Conduct", "v2-conduct", "flow", "Harvester conduct"),
        new("V18-Silver", "v18-silver", "selective-short", "Harvester conduct"),
        new("V1-First", "v1-first", "default", "Harvester conduct"),
        new("Conduct-V3", "conduct-v3", "catamaran", "Harvester conduct"),
        new("V19-PurpleCloud", "v19-purplecloud", "retained-breakout", "Harvester conduct"),
        new("V15-ShortCap", "v15-shortcap", "retained-breakdown", "Harvester conduct"),
        new("V14-SmallCap", "v14-smallcap", "baseline", "Harvester conduct"),
        new("V20-GEN001-ChoppyShield", "v20-gen001-choppyshield", "default", "Harvester conduct"),
        new("V12", "v12", "default", "Harvester conduct")
    ];

    public static async Task<string> RunAsync(
        string timeframe,
        string universeNameOrCsv,
        SailorAppSettings settings,
        int? symbolLimit = null,
        string? profilesCsv = null)
    {
        var provider = new CsvBacktestDataProvider();
        IReadOnlyList<string> availableSymbols = provider.ListSymbols();
        IReadOnlyList<string> requestedSymbols = SailorSymbolUniverses.Resolve(universeNameOrCsv, availableSymbols);
        IReadOnlyList<string> symbolsWithData = requestedSymbols
            .Where(symbol => availableSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Where(symbol => provider.ListTimeframes(symbol).Contains(timeframe, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .Take(symbolLimit.GetValueOrDefault(int.MaxValue))
            .ToArray();

        IReadOnlyList<string> missingSymbols = requestedSymbols
            .Where(symbol => !symbolsWithData.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<StrategyReportDefinition> strategies = ResolveStrategies(profilesCsv);
        var strategyRows = new List<StrategyReportRow>();
        var tradeRows = new List<TradeReportRow>();
        var errors = new List<string>();

        Console.WriteLine("sailor HTML strategy report started");
        Console.WriteLine($"Timeframe: {timeframe}");
        Console.WriteLine($"Universe: {universeNameOrCsv}");
        Console.WriteLine($"Symbols with data: {symbolsWithData.Count}");
        Console.WriteLine($"Strategies: {strategies.Count}");
        Console.WriteLine("Output folder: " + SailorLogPaths.BacktestHtml);
        Console.WriteLine();

        foreach (StrategyReportDefinition strategy in strategies)
        {
            Console.WriteLine($"Running {strategy.Strategy} ({strategy.ProfileName}) on {symbolsWithData.Count} symbols...");

            var runs = new List<BacktestRunResult>();
            int tradeSequence = 0;

            foreach (string symbol in symbolsWithData)
            {
                try
                {
                    BacktestRunResult result = await SimpleBacktestRunner.RunAsync(
                        symbol,
                        timeframe,
                        strategy.ProfileName,
                        echoToConsole: false,
                        settings: settings);

                    runs.Add(result);

                    foreach (BacktestTrade trade in result.Trades)
                    {
                        tradeRows.Add(new TradeReportRow(
                            Strategy: strategy.Strategy,
                            ProfileName: strategy.ProfileName,
                            Variant: strategy.Variant,
                            Symbol: trade.Symbol,
                            Sequence: ++tradeSequence,
                            Side: "Long",
                            Quantity: trade.Quantity,
                            EntryTime: trade.EntryTime,
                            ExitTime: trade.ExitTime,
                            EntryPrice: trade.EntryPrice,
                            ExitPrice: trade.ExitPrice,
                            Pnl: trade.Pnl,
                            PnlPercent: trade.PnlPercent,
                            EntryReason: trade.EntryReason,
                            ExitReason: trade.ExitReason));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{strategy.ProfileName} {symbol}: {ex.Message}");
                }
            }

            strategyRows.Add(BuildStrategyRow(strategy, symbolsWithData.Count, runs));
        }

        strategyRows = strategyRows
            .OrderByDescending(row => row.TotalPnl)
            .ThenByDescending(row => row.Trades)
            .ThenBy(row => row.Definition.Strategy, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string safeUniverse = SanitizeFilePart(universeNameOrCsv);
        string safeTimeframe = SanitizeFilePart(timeframe);
        string reportPath = Path.Combine(
            SailorLogPaths.BacktestHtml,
            $"strategy_trades_report_{safeUniverse}_{safeTimeframe}_{DateTime.Now:yyyyMMdd_HHmmss}.html");

        await WriteHtmlAsync(
            reportPath,
            timeframe,
            universeNameOrCsv,
            symbolsWithData,
            missingSymbols,
            strategyRows,
            tradeRows,
            errors);

        Console.WriteLine();
        Console.WriteLine("sailor HTML strategy report finished");
        Console.WriteLine("HTML report created:");
        Console.WriteLine(reportPath);

        return reportPath;
    }

    private static IReadOnlyList<StrategyReportDefinition> ResolveStrategies(string? profilesCsv)
    {
        if (string.IsNullOrWhiteSpace(profilesCsv) || profilesCsv.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return BuiltInStrategies;
        }

        var requested = profilesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(profile => profile.Trim())
            .Where(profile => profile.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return requested
            .Select(profile => BuiltInStrategies.FirstOrDefault(
                    item => item.ProfileName.Equals(profile, StringComparison.OrdinalIgnoreCase) ||
                            item.Strategy.Equals(profile, StringComparison.OrdinalIgnoreCase))
                ?? new StrategyReportDefinition(profile, profile, "custom", "Custom"))
            .ToArray();
    }

    private static StrategyReportRow BuildStrategyRow(
        StrategyReportDefinition definition,
        int symbols,
        IReadOnlyList<BacktestRunResult> runs)
    {
        IReadOnlyList<BacktestTrade> trades = runs.SelectMany(run => run.Trades).ToArray();
        int tradeCount = trades.Count;
        int winners = trades.Count(trade => trade.Pnl > 0m);
        int losers = trades.Count(trade => trade.Pnl < 0m);
        decimal totalPnl = trades.Sum(trade => trade.Pnl);
        decimal grossWin = trades.Where(trade => trade.Pnl > 0m).Sum(trade => trade.Pnl);
        decimal grossLoss = trades.Where(trade => trade.Pnl < 0m).Sum(trade => trade.Pnl);
        decimal avgWin = winners > 0 ? grossWin / winners : 0m;
        decimal avgLoss = losers > 0 ? grossLoss / losers : 0m;
        decimal expectancy = tradeCount > 0 ? totalPnl / tradeCount : 0m;
        decimal winRate = tradeCount > 0 ? (decimal)winners / tradeCount * 100m : 0m;
        decimal profitFactor = grossLoss < 0m
            ? grossWin / Math.Abs(grossLoss)
            : grossWin > 0m ? 999.99m : 0m;

        decimal sharpe = CalculateSharpe(trades.Select(trade => trade.Pnl).ToArray());
        decimal equitySharpe = CalculateEquitySharpe(trades.Select(trade => trade.Pnl).ToArray());
        decimal sortino = CalculateSortino(trades.Select(trade => trade.Pnl).ToArray());
        decimal maxDrawdown = CalculateMaxDrawdown(trades.Select(trade => trade.Pnl).ToArray());
        decimal equityDownDeviation = CalculateEquityDownDeviationPercent(trades.Select(trade => trade.Pnl).ToArray(), startingEquity: 10_000m);

        return new StrategyReportRow(
            Definition: definition,
            Symbols: symbols,
            Trades: tradeCount,
            Winners: winners,
            Losers: losers,
            WinRatePercent: winRate,
            ProfitFactor: profitFactor,
            Sharpe: sharpe,
            EquitySharpe: equitySharpe,
            EquitySortino: sortino,
            EquityDownDeviationPercent: equityDownDeviation,
            TotalPnl: totalPnl,
            MaxDrawdownDollars: maxDrawdown,
            AverageWinDollars: avgWin,
            AverageLossDollars: avgLoss,
            Expectancy: expectancy,
            GovernorStops: 0,
            GovernorReason: "-");
    }

    private static async Task WriteHtmlAsync(
        string reportPath,
        string timeframe,
        string universeNameOrCsv,
        IReadOnlyList<string> symbolsWithData,
        IReadOnlyList<string> missingSymbols,
        IReadOnlyList<StrategyReportRow> strategyRows,
        IReadOnlyList<TradeReportRow> tradeRows,
        IReadOnlyList<string> errors)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        await using var fileStream = new FileStream(reportPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await writer.WriteLineAsync("<!DOCTYPE html>");
        await writer.WriteLineAsync("<html lang=\"en\">");
        await writer.WriteLineAsync("<head>");
        await writer.WriteLineAsync("<meta charset=\"utf-8\" />");
        await writer.WriteLineAsync("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        await writer.WriteLineAsync("<title>Sailor Strategy Report</title>");
        await writer.WriteLineAsync("<style>");
        await writer.WriteLineAsync(GetCss());
        await writer.WriteLineAsync("</style>");
        await writer.WriteLineAsync("</head>");
        await writer.WriteLineAsync("<body>");
        await writer.WriteLineAsync("<h1>Strategy Table</h1>");
        await writer.WriteLineAsync($"<p class=\"muted\">Generated {Html(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))} | Timeframe <b>{Html(timeframe)}</b> | Universe <b>{Html(universeNameOrCsv)}</b> | Symbols with data <b>{symbolsWithData.Count}</b></p>");
        await writer.WriteLineAsync("<div class=\"toolbar\"><label>&gt;=50 filter <select id=\"tradeFilter\"><option value=\"all\">All</option><option value=\"yes\">YES</option><option value=\"no\">NO</option></select></label></div>");
        await writer.WriteLineAsync("<table id=\"strategyTable\">");
        await writer.WriteLineAsync("<thead><tr><th>Strategy</th><th>Variant</th><th>Style</th><th>Symbols</th><th>Trades</th><th>&gt;=50</th><th>WinRate</th><th>PF</th><th>Sharpe</th><th>EqSharpe</th><th>EqSortino</th><th>EqDownDev</th><th>TotalPnL$</th><th>MaxDD$</th><th>AvgWin$</th><th>AvgLoss$</th><th>Expectancy</th><th>GovStops</th><th>GovReason</th></tr></thead>");
        await writer.WriteLineAsync("<tbody>");

        foreach (StrategyReportRow row in strategyRows)
        {
            string yesNo = row.Trades >= 50 ? "YES" : "NO";
            await writer.WriteLineAsync(
                $"<tr data-ge50=\"{yesNo.ToLowerInvariant()}\"><td>{Html(row.Definition.Strategy)}</td><td>{Html(row.Definition.Variant)}</td><td>{Html(row.Definition.Style)}</td><td>{row.Symbols}</td><td>{row.Trades}</td><td>{yesNo}</td><td>{row.WinRatePercent:F1}%</td><td>{Format(row.ProfitFactor)}</td><td>{row.Sharpe:F2}</td><td>{row.EquitySharpe:F2}</td><td>{row.EquitySortino:F2}</td><td>{row.EquityDownDeviationPercent:F2}%</td><td class=\"{PnlClass(row.TotalPnl)}\">{row.TotalPnl:F2}</td><td>{row.MaxDrawdownDollars:F2}</td><td>{row.AverageWinDollars:F2}</td><td>{row.AverageLossDollars:F2}</td><td>{row.Expectancy:F2}</td><td>{row.GovernorStops}</td><td>{Html(row.GovernorReason)}</td></tr>");
        }

        await writer.WriteLineAsync("</tbody></table>");

        await writer.WriteLineAsync("<h1 class=\"section-title\">Trades</h1>");
        await writer.WriteLineAsync("<div class=\"toolbar\"><input id=\"tradeSearch\" placeholder=\"Filter strategy / symbol / reason\" /><select id=\"strategySelect\"><option value=\"all\">All strategies</option>");
        foreach (string strategy in tradeRows.Select(row => row.Strategy).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value))
        {
            await writer.WriteLineAsync($"<option value=\"{Html(strategy)}\">{Html(strategy)}</option>");
        }
        await writer.WriteLineAsync("</select></div>");
        await writer.WriteLineAsync("<table id=\"tradesTable\">");
        await writer.WriteLineAsync("<thead><tr><th>Strategy</th><th>Variant</th><th>#</th><th>Symbol</th><th>Side</th><th>Qty</th><th>Entry</th><th>Entry$</th><th>Exit</th><th>Exit$</th><th>PnL$</th><th>PnL%</th><th>Entry Reason</th><th>Exit Reason</th></tr></thead>");
        await writer.WriteLineAsync("<tbody>");

        foreach (TradeReportRow trade in tradeRows.OrderBy(row => row.Strategy).ThenBy(row => row.Sequence))
        {
            string searchable = Html($"{trade.Strategy} {trade.Symbol} {trade.EntryReason} {trade.ExitReason}");
            await writer.WriteLineAsync(
                $"<tr data-strategy=\"{Html(trade.Strategy)}\" data-search=\"{searchable.ToLowerInvariant()}\"><td>{Html(trade.Strategy)}</td><td>{Html(trade.Variant)}</td><td>{trade.Sequence}</td><td>{Html(trade.Symbol)}</td><td class=\"side-long\">{Html(trade.Side)}</td><td>{trade.Quantity}</td><td>{Html(trade.EntryTime.ToString("yyyy-MM-dd HH:mm"))}</td><td>{trade.EntryPrice:F4}</td><td>{Html(trade.ExitTime.ToString("yyyy-MM-dd HH:mm"))}</td><td>{trade.ExitPrice:F4}</td><td class=\"{PnlClass(trade.Pnl)}\">{trade.Pnl:F2}</td><td class=\"{PnlClass(trade.Pnl)}\">{trade.PnlPercent:F2}%</td><td>{Html(Shorten(trade.EntryReason, 180))}</td><td>{Html(Shorten(trade.ExitReason, 180))}</td></tr>");
        }

        await writer.WriteLineAsync("</tbody></table>");

        if (missingSymbols.Count > 0 || errors.Count > 0)
        {
            await writer.WriteLineAsync("<section class=\"card\"><h2>Run Notes</h2>");
            if (missingSymbols.Count > 0)
            {
                await writer.WriteLineAsync($"<p><b>Symbols without data for this timeframe:</b> {Html(string.Join(", ", missingSymbols))}</p>");
            }
            if (errors.Count > 0)
            {
                await writer.WriteLineAsync("<p><b>Errors:</b></p><ul>");
                foreach (string error in errors.Take(100))
                {
                    await writer.WriteLineAsync($"<li>{Html(error)}</li>");
                }
                await writer.WriteLineAsync("</ul>");
            }
            await writer.WriteLineAsync("</section>");
        }

        await writer.WriteLineAsync("<script>");
        await writer.WriteLineAsync(GetJavaScript());
        await writer.WriteLineAsync("</script>");
        await writer.WriteLineAsync("</body></html>");
    }

    private static decimal CalculateSharpe(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        decimal mean = values.Average();
        decimal std = StandardDeviation(values, mean);
        return std == 0m ? 0m : mean / std * (decimal)Math.Sqrt(values.Count);
    }

    private static decimal CalculateEquitySharpe(IReadOnlyList<decimal> values)
    {
        return CalculateSharpe(values);
    }

    private static decimal CalculateSortino(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        decimal mean = values.Average();
        decimal downsideVariance = values
            .Where(value => value < 0m)
            .Select(value => (value - 0m) * (value - 0m))
            .DefaultIfEmpty(0m)
            .Average();

        decimal downsideStd = (decimal)Math.Sqrt((double)downsideVariance);
        return downsideStd == 0m ? 0m : mean / downsideStd * (decimal)Math.Sqrt(values.Count);
    }

    private static decimal CalculateMaxDrawdown(IReadOnlyList<decimal> values)
    {
        decimal equity = 0m;
        decimal peak = 0m;
        decimal maxDrawdown = 0m;

        foreach (decimal value in values)
        {
            equity += value;
            peak = Math.Max(peak, equity);
            maxDrawdown = Math.Max(maxDrawdown, peak - equity);
        }

        return maxDrawdown;
    }

    private static decimal CalculateEquityDownDeviationPercent(IReadOnlyList<decimal> values, decimal startingEquity)
    {
        if (values.Count == 0 || startingEquity <= 0m)
        {
            return 0m;
        }

        decimal equity = startingEquity;
        decimal peak = startingEquity;
        var drawdowns = new List<decimal>();

        foreach (decimal value in values)
        {
            equity += value;
            peak = Math.Max(peak, equity);
            decimal drawdownPercent = peak > 0m ? (peak - equity) / peak * 100m : 0m;
            drawdowns.Add(drawdownPercent);
        }

        decimal mean = drawdowns.Average();
        return StandardDeviation(drawdowns, mean);
    }

    private static decimal StandardDeviation(IReadOnlyList<decimal> values, decimal mean)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        decimal variance = values
            .Select(value => (value - mean) * (value - mean))
            .Sum() / (values.Count - 1);

        return (decimal)Math.Sqrt((double)variance);
    }

    private static string Format(decimal value)
    {
        return value >= 999.99m ? "999.99" : value.ToString("F2");
    }

    private static string PnlClass(decimal value)
    {
        return value >= 0m ? "positive" : "negative";
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value) ?? string.Empty;
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    private static string SanitizeFilePart(string value)
    {
        string sanitized = string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        sanitized = sanitized.Replace(',', '_').Replace(' ', '_').Replace('-', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "all" : sanitized;
    }

    private static string GetCss()
    {
        return """
body{font-family:Segoe UI,Arial,sans-serif;background:#f3efe6;color:#1e2430;margin:0;padding:24px;}h1{margin:0 0 18px;}h2{margin:0 0 10px;}table{border-collapse:collapse;width:100%;background:#fff;}th,td{border:1px solid #d8d2c3;padding:8px 10px;font-size:13px;text-align:left;vertical-align:top;}th{background:#e8ddc5;position:sticky;top:0;z-index:1;}tr:nth-child(even){background:#fffdf8;}.toolbar{display:flex;gap:10px;flex-wrap:wrap;margin:12px 0;}select,input{padding:8px;border:1px solid #c9c1b0;border-radius:8px;background:#fff;min-width:140px;}.muted{color:#5f5a51;}.positive{color:#007a3d;font-weight:700;}.negative{color:#c21b10;font-weight:700;}.side-long{color:#007a3d;font-weight:700;}.section-title{margin-top:32px;}.card{background:#fff;border:1px solid #d8d2c3;border-radius:12px;padding:16px;margin:18px 0;}@media print{th{position:static;}body{background:#fff;}}
""";
    }

    private static string GetJavaScript()
    {
        return """
const tradeFilter = document.getElementById('tradeFilter');
const strategyTableRows = [...document.querySelectorAll('#strategyTable tbody tr')];
tradeFilter.addEventListener('change', () => {
  const value = tradeFilter.value;
  for (const row of strategyTableRows) {
    row.style.display = value === 'all' || row.dataset.ge50 === value ? '' : 'none';
  }
});

const tradeSearch = document.getElementById('tradeSearch');
const strategySelect = document.getElementById('strategySelect');
const tradeRows = [...document.querySelectorAll('#tradesTable tbody tr')];
function filterTrades(){
  const search = tradeSearch.value.trim().toLowerCase();
  const strategy = strategySelect.value;
  for (const row of tradeRows) {
    const matchStrategy = strategy === 'all' || row.dataset.strategy === strategy;
    const matchSearch = !search || row.dataset.search.includes(search);
    row.style.display = matchStrategy && matchSearch ? '' : 'none';
  }
}
tradeSearch.addEventListener('input', filterTrades);
strategySelect.addEventListener('change', filterTrades);
""";
    }
}
