using System.Net;
using System.Text;
using System.Text.Json;
using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Configuration;
using Sailor.App.Logging;

namespace Sailor.App.Backtest.Reports;

public static class SailorHtmlReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

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
                            Side: trade.SideName,
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

        var chartBuilder = new TradeChartBuilder(provider, timeframe);
        IReadOnlyList<TradeChartContext> tradeContexts = tradeRows
            .OrderBy(row => row.Strategy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Sequence)
            .Select((trade, index) => chartBuilder.Build(trade, index + 1))
            .ToArray();

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
            tradeContexts,
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
        IReadOnlyList<TradeChartContext> tradeContexts,
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
        await writer.WriteLineAsync("<title>Sailor Strategy and Trades Report</title>");
        await writer.WriteLineAsync("<style>");
        await writer.WriteLineAsync(GetCss());
        await writer.WriteLineAsync("</style>");
        await writer.WriteLineAsync("<script>");
        await writer.WriteLineAsync(GetEarlyToggleJavaScript());
        await writer.WriteLineAsync("</script>");
        await writer.WriteLineAsync("</head>");
        await writer.WriteLineAsync("<body>");
        await writer.WriteLineAsync("<h1>Strategy Table</h1>");
        await writer.WriteLineAsync($"<p class=\"muted\">Generated {Html(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))} | Timeframe <b>{Html(timeframe)}</b> | Universe <b>{Html(universeNameOrCsv)}</b> | Symbols with data <b>{symbolsWithData.Count}</b> | Trades <b>{tradeContexts.Count}</b></p>");
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
        await writer.WriteLineAsync("<p class=\"muted\">Click a trade row to expand the rebuilt Sailor diagram, indicators, explanations, MFE/MAE, and timeline. V21/V22/V23/V24 trades also include a 15m/5m EMA9 angle diagram.</p>");
        await writer.WriteLineAsync("<div class=\"toolbar\"><input id=\"tradeSearch\" placeholder=\"Filter strategy / symbol / reason\" /><select id=\"strategySelect\"><option value=\"all\">All strategies</option>");
        foreach (string strategy in tradeContexts.Select(row => row.Trade.Strategy).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value))
        {
            await writer.WriteLineAsync($"<option value=\"{Html(strategy)}\">{Html(strategy)}</option>");
        }
        await writer.WriteLineAsync("</select></div>");
        await writer.WriteLineAsync("<table id=\"tradesTable\">");
        await writer.WriteLineAsync("<thead><tr><th></th><th>Strategy</th><th>Variant</th><th>#</th><th>Symbol</th><th>Side</th><th>Qty</th><th>Entry</th><th>Entry$</th><th>Exit</th><th>Exit$</th><th>PnL$</th><th>PnL%</th><th>Exit Reason</th></tr></thead>");
        await writer.WriteLineAsync("<tbody>");

        foreach (TradeChartContext context in tradeContexts)
        {
            TradeReportRow trade = context.Trade;
            string searchable = Html($"{trade.Strategy} {trade.Symbol} {trade.EntryReason} {trade.ExitReason}").ToLowerInvariant();
            await writer.WriteLineAsync(
                $"<tr class=\"trade-summary-row\" data-detail=\"{context.Id}\" data-strategy=\"{Html(trade.Strategy)}\" data-search=\"{searchable}\" onclick=\"return window.sailorToggleTradeDetailsInline(event, '{context.Id}')\"><td><button type=\"button\" class=\"expand-button\" aria-expanded=\"false\">▸</button></td><td>{Html(trade.Strategy)}</td><td>{Html(trade.Variant)}</td><td>{trade.Sequence}</td><td>{Html(trade.Symbol)}</td><td class=\"{SideClass(trade.Side)}\">{Html(trade.Side)}</td><td>{trade.Quantity}</td><td>{Html(trade.EntryTime.ToString("yyyy-MM-dd HH:mm"))}</td><td>{trade.EntryPrice:F4}</td><td>{Html(trade.ExitTime.ToString("yyyy-MM-dd HH:mm"))}</td><td>{trade.ExitPrice:F4}</td><td class=\"{PnlClass(trade.Pnl)}\">{trade.Pnl:F2}</td><td class=\"{PnlClass(trade.Pnl)}\">{trade.PnlPercent:F2}%</td><td>{Html(Shorten(trade.ExitReason, 140))}</td></tr>");
            await writer.WriteLineAsync($"<tr id=\"{context.Id}\" class=\"trade-detail-row\" data-strategy=\"{Html(trade.Strategy)}\" data-search=\"{searchable}\" hidden style=\"display:none\"><td colspan=\"14\"><div class=\"detail-placeholder\">Details are loaded only when this trade is expanded.</div></td></tr>");
        }

        await writer.WriteLineAsync("</tbody></table>");
        await writer.WriteLineAsync("<div id=\"tradeDataStore\" hidden>");
        foreach (TradeChartContext context in tradeContexts)
        {
            await writer.WriteLineAsync($"<script type=\"application/json\" id=\"{context.Id}-json\">{ToJsonScriptContent(context)}</script>");
        }
        await writer.WriteLineAsync("</div>");

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

    private static string ToJsonScriptContent(TradeChartContext context)
    {
        return JsonSerializer.Serialize(context, JsonOptions)
            .Replace("</", "<\\/", StringComparison.Ordinal);
    }

    private static string BuildTradeDetailHtml(TradeChartContext context)
    {
        TradeReportRow trade = context.Trade;
        var builder = new StringBuilder();
        builder.Append("<div class=\"trade-detail-card\">");
        builder.Append($"<h2>Trade diagram - {Html(trade.Strategy)} / {Html(trade.Symbol)} / <span class=\"{SideClass(trade.Side)}\">{Html(trade.Side)}</span> / qty {trade.Quantity} / pnl <span class=\"{PnlClass(trade.Pnl)}\">{trade.Pnl:F2}</span></h2>");
        builder.Append("<div class=\"trade-grid\">");
        builder.Append($"<div><strong>Entry</strong><div>{Html(trade.EntryTime.ToString("yyyy-MM-dd HH:mm:ss"))}</div><div>{trade.EntryPrice:F4}</div><div class=\"muted\">{Html(Shorten(trade.EntryReason, 260))}</div></div>");
        builder.Append($"<div><strong>Exit</strong><div>{Html(trade.ExitTime.ToString("yyyy-MM-dd HH:mm:ss"))}</div><div>{trade.ExitPrice:F4}</div><div class=\"muted\">{Html(Shorten(trade.ExitReason, 260))}</div></div>");
        builder.Append($"<div><strong>MFE / MAE</strong><div><span class=\"positive\">+{context.MfeDollars:F2}</span> / <span class=\"negative\">{context.MaeDollars:F2}</span></div><div class=\"muted\">Move +{context.FavorableMovePercent:F2}% / {context.AdverseMovePercent:F2}%</div></div>");
        builder.Append($"<div><strong>Bars held</strong><div>{context.BarsHeld}</div><div class=\"muted\">from rebuilt CSV bar indices</div></div>");
        builder.Append("</div>");
        builder.Append("<div class=\"indicator-grid\">");
        builder.Append(BuildIndicatorBlock("Entry indicators", context.EntryIndicators));
        builder.Append(BuildIndicatorBlock("Exit indicators", context.ExitIndicators));
        builder.Append("</div>");
        builder.Append("<h3>Primary 1m diagram</h3>");
        builder.Append("<div class=\"chart-legend\"><span><i class=\"legend candle\"></i>Candles</span><span><i class=\"legend ema9\"></i>EMA9</span><span><i class=\"legend sma20\"></i>SMA20</span><span><i class=\"legend sma200\"></i>SMA200</span><span><i class=\"legend vwap\"></i>VWAP</span><span><i class=\"legend entry\"></i>Entry</span><span><i class=\"legend exit\"></i>Exit</span></div>");
        builder.Append($"<canvas class=\"trade-chart\" data-chart-id=\"{context.Id}\" data-chart-kind=\"primary\" width=\"1120\" height=\"360\"></canvas>");

        if (context.HigherTimeframeChart is not null)
        {
            builder.Append($"<h3>{context.HigherTimeframeChart.CandleMinutes}m EMA9 angle conduct diagram</h3>");
            builder.Append($"<p class=\"muted\">{Html(context.HigherTimeframeChart.SignalSummary)}</p>");
            builder.Append("<div class=\"chart-legend\"><span><i class=\"legend candle\"></i>Completed higher-timeframe candles</span><span><i class=\"legend ema9\"></i>EMA9</span><span><i class=\"legend angle\"></i>ATR-normalized angle</span><span><i class=\"legend entry\"></i>Signal candle</span></div>");
            builder.Append($"<canvas class=\"trade-chart\" data-chart-id=\"{context.Id}\" data-chart-kind=\"higher\" width=\"1120\" height=\"360\"></canvas>");
        }

        builder.Append("<h3>Action timeline</h3><ol class=\"timeline\">");
        foreach (string item in context.Timeline)
        {
            builder.Append($"<li>{Html(item)}</li>");
        }
        builder.Append("</ol>");
        builder.Append("<h3>Explanation</h3><ul class=\"explanations\">");
        foreach (string item in context.Explanations)
        {
            builder.Append($"<li>{Html(item)}</li>");
        }
        builder.Append("</ul>");
        builder.Append("</div>");
        return builder.ToString();
    }

    private static string BuildIndicatorBlock(string title, TradeIndicatorContext? indicators)
    {
        if (indicators is null)
        {
            return $"<div class=\"indicator-card\"><strong>{Html(title)}</strong><div class=\"muted\">not available</div></div>";
        }

        decimal volumeRatio = indicators.VolumeAverage20.HasValue && indicators.VolumeAverage20.Value > 0m
            ? indicators.Volume / indicators.VolumeAverage20.Value
            : 0m;

        return $"<div class=\"indicator-card\"><strong>{Html(title)}</strong><div>{Html(indicators.Time)} | Close {indicators.Close:F4}</div><div>EMA9 {FormatNullable(indicators.Ema9)} | SMA20 {FormatNullable(indicators.Sma20)} | SMA200 {FormatNullable(indicators.Sma200)}</div><div>VWAP {FormatNullable(indicators.Vwap)} | Volume {indicators.Volume} | VolAvg20 {FormatNullable(indicators.VolumeAverage20)} | Ratio {volumeRatio:F2}</div></div>";
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

    private static string FormatNullable(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }

    private static string SideClass(string side)
    {
        return side.Equals("SHORT", StringComparison.OrdinalIgnoreCase) ? "side-short" : "side-long";
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
body{font-family:Segoe UI,Arial,sans-serif;background:#f3efe6;color:#1e2430;margin:0;padding:24px;}h1{margin:0 0 18px;}h2{margin:0 0 10px;}h3{margin:18px 0 10px;}table{border-collapse:collapse;width:100%;background:#fff;}th,td{border:1px solid #d8d2c3;padding:8px 10px;font-size:13px;text-align:left;vertical-align:top;}th{background:#e8ddc5;position:sticky;top:0;z-index:1;}tr:nth-child(even){background:#fffdf8;}.toolbar{display:flex;gap:10px;flex-wrap:wrap;margin:12px 0;}select,input{padding:8px;border:1px solid #c9c1b0;border-radius:8px;background:#fff;min-width:140px;}.muted{color:#5f5a51;}.positive{color:#007a3d;font-weight:700;}.negative{color:#c21b10;font-weight:700;}.side-long{color:#007a3d;font-weight:700;}.side-short{color:#b00020;font-weight:700;}.section-title{margin-top:32px;}.card,.trade-detail-card{background:#fff;border:1px solid #d8d2c3;border-radius:12px;padding:16px;margin:18px 0;}.expand-button{border:1px solid #c9c1b0;background:#fff;border-radius:6px;padding:2px 7px;cursor:pointer;}.trade-summary-row{cursor:pointer;}.trade-summary-row:hover{background:#fff7e5;}.trade-detail-row{display:none;background:#fffdf8!important;}.trade-detail-row[hidden]{display:none!important;}.trade-detail-row.open{display:table-row;}.detail-placeholder{padding:14px;color:#5f5a51;}.trade-grid{display:grid;grid-template-columns:repeat(4,minmax(180px,1fr));gap:14px;margin:10px 0 14px;}.indicator-grid{display:grid;grid-template-columns:repeat(2,minmax(280px,1fr));gap:14px;margin:10px 0 14px;}.indicator-card{background:#fffaf0;border:1px solid #e2d5b8;border-radius:10px;padding:10px;}.trade-chart{width:100%;max-width:1120px;height:360px;border:1px solid #d8d2c3;border-radius:8px;background:#fffef9;display:block;margin:8px 0 18px;}.chart-legend{display:flex;gap:14px;flex-wrap:wrap;font-size:12px;margin:6px 0 8px;}.legend{display:inline-block;width:18px;height:3px;margin-right:5px;vertical-align:middle;border-radius:2px;}.legend.candle{background:#2a9d8f;height:8px;width:8px;}.legend.ema9{background:#d6a900;}.legend.sma20{background:#00a8c6;}.legend.sma200{background:#7d2ae8;}.legend.vwap{background:#404f6f;}.legend.entry{background:#12355b;}.legend.exit{background:#d00000;}.legend.angle{background:#ff9900;}.timeline,.explanations{margin:6px 0 0 18px;padding:0;}.timeline li,.explanations li{margin:4px 0;}.trade-detail-card h2{font-size:18px;margin-top:0;}@media(max-width:900px){.trade-grid,.indicator-grid{grid-template-columns:1fr;}.trade-chart{height:300px;}}@media print{th{position:static;}body{background:#fff;}.expand-button{display:none;}.trade-detail-row{display:table-row!important;}}
""";
    }

    private static string GetEarlyToggleJavaScript()
    {
        return """
window.sailorToggleTradeDetailsInline = function(event, id) {
  if (event) {
    event.preventDefault();
    event.stopPropagation();
  }

  var detail = document.getElementById(id);
  if (!detail) {
    return false;
  }

  var source = event ? event.currentTarget : null;
  var row = source && source.classList && source.classList.contains('trade-summary-row')
    ? source
    : source && source.closest
      ? source.closest('.trade-summary-row')
      : document.querySelector('tr[data-detail="' + id + '"]');

  var button = row ? row.querySelector('.expand-button') : null;
  var isOpen = detail.classList.contains('open') || detail.style.display === 'table-row' || detail.hidden === false;
  var open = !isOpen;

  detail.hidden = !open;
  detail.style.display = open ? 'table-row' : 'none';
  detail.classList.toggle('open', open);

  if (button) {
    button.textContent = open ? '▾' : '▸';
    button.setAttribute('aria-expanded', String(open));
  }

  if (open && window.sailorEnsureTradeDetail) {
    window.sailorEnsureTradeDetail(id);
  }

  if (open && window.sailorDrawChartsFor) {
    window.sailorDrawChartsFor(id);
  }

  return false;
};
""";
    }

    private static string GetJavaScript()
    {
        return """
(function(){
const tradeFilter = document.getElementById('tradeFilter');
const strategyTableRows = [...document.querySelectorAll('#strategyTable tbody tr')];
if (tradeFilter) {
  tradeFilter.addEventListener('change', () => {
    const value = tradeFilter.value;
    for (const row of strategyTableRows) {
      row.style.display = value === 'all' || row.dataset.ge50 === value ? '' : 'none';
    }
  });
}

const tradeSearch = document.getElementById('tradeSearch');
const strategySelect = document.getElementById('strategySelect');
const tradeSummaryRows = [...document.querySelectorAll('#tradesTable .trade-summary-row')];
const tradeDetailRows = [...document.querySelectorAll('#tradesTable .trade-detail-row')];
const chartMap = new Map();
const drawnCharts = new Set();

function setDetailVisible(detail, visible){
  if (!detail) return;
  detail.classList.toggle('open', visible);
  detail.hidden = !visible;
  detail.style.display = visible ? 'table-row' : 'none';
}

function filterTrades(){
  const search = tradeSearch ? tradeSearch.value.trim().toLowerCase() : '';
  const strategy = strategySelect ? strategySelect.value : 'all';
  for (const row of tradeSummaryRows) {
    const detail = document.getElementById(row.dataset.detail);
    const matchStrategy = strategy === 'all' || row.dataset.strategy === strategy;
    const matchSearch = !search || (row.dataset.search || '').includes(search);
    const visible = matchStrategy && matchSearch;
    row.style.display = visible ? '' : 'none';
    if (!visible) {
      setDetailVisible(detail, false);
    } else if (detail && detail.classList.contains('open')) {
      setDetailVisible(detail, true);
    }
  }
}

if (tradeSearch) tradeSearch.addEventListener('input', filterTrades);
if (strategySelect) strategySelect.addEventListener('change', filterTrades);

function toggleTradeRow(rowOrButton){
  const row = rowOrButton && rowOrButton.classList && rowOrButton.classList.contains('trade-summary-row')
    ? rowOrButton
    : rowOrButton && rowOrButton.closest
      ? rowOrButton.closest('.trade-summary-row')
      : null;

  if (!row) return;

  const detail = document.getElementById(row.dataset.detail);
  const button = row.querySelector('.expand-button');
  if (!detail) return;

  const opening = !detail.classList.contains('open') || detail.style.display === 'none' || detail.hidden;
  setDetailVisible(detail, opening);

  if (button) {
    button.textContent = opening ? '▾' : '▸';
    button.setAttribute('aria-expanded', String(opening));
  }

  if (opening) {
    drawChartsFor(row.dataset.detail);
  }
}

window.sailorToggleTradeRow = toggleTradeRow;

document.addEventListener('click', event => {
  const button = event.target && event.target.closest ? event.target.closest('.expand-button') : null;
  const row = event.target && event.target.closest ? event.target.closest('.trade-summary-row') : null;

  if (!button && !row) return;

  const targetRow = row || button.closest('.trade-summary-row');
  if (!targetRow) return;

  event.preventDefault();
  toggleTradeRow(targetRow);
});

for (const detail of tradeDetailRows) {
  detail.hidden = true;
  detail.style.display = 'none';
}

function loadChartContext(id){
  if (chartMap.has(id)) return chartMap.get(id);
  const script = document.getElementById(id + '-json');
  if (!script) return null;
  try {
    const context = JSON.parse(script.textContent || '{}');
    chartMap.set(id, context);
    return context;
  } catch (error) {
    console.error('Could not parse trade chart data for ' + id, error);
    return null;
  }
}

function ensureTradeDetail(id){
  const detail = document.getElementById(id);
  if (!detail) return;
  if (detail.dataset.loaded === 'true') return;

  const context = loadChartContext(id);
  const cell = detail.querySelector('td');
  if (!cell) return;

  if (!context) {
    cell.innerHTML = '<div class="trade-detail-card"><b>No chart data available for this trade.</b></div>';
    detail.dataset.loaded = 'true';
    return;
  }

  cell.innerHTML = buildTradeDetailHtml(context);
  detail.dataset.loaded = 'true';
}

function drawChartsFor(id){
  ensureTradeDetail(id);
  if (drawnCharts.has(id)) return;
  const context = loadChartContext(id);
  if (!context) return;
  document.querySelectorAll(`canvas[data-chart-id="${id}"]`).forEach(canvas => {
    const kind = canvas.dataset.chartKind;
    if (kind === 'higher') drawHigherChart(canvas, context);
    else drawPrimaryChart(canvas, context);
  });
  drawnCharts.add(id);
}

window.sailorEnsureTradeDetail = ensureTradeDetail;
window.sailorDrawChartsFor = drawChartsFor;


function buildTradeDetailHtml(context){
  const trade = context.trade || {};
  const side = trade.side || '';
  const pnl = num(trade.pnl);
  const higher = context.higherTimeframeChart;
  const parts = [];

  parts.push('<div class="trade-detail-card">');
  parts.push(`<h2>Trade diagram - ${esc(trade.strategy)} / ${esc(trade.symbol)} / <span class="${sideClass(side)}">${esc(side)}</span> / qty ${trade.quantity || 0} / pnl <span class="${pnlClass(pnl)}">${fmt(pnl,2)}</span></h2>`);
  parts.push('<div class="trade-grid">');
  parts.push(`<div><strong>Entry</strong><div>${esc(formatDateTime(trade.entryTime))}</div><div>${fmt(num(trade.entryPrice),4)}</div><div class="muted">${esc(shorten(trade.entryReason || '',260))}</div></div>`);
  parts.push(`<div><strong>Exit</strong><div>${esc(formatDateTime(trade.exitTime))}</div><div>${fmt(num(trade.exitPrice),4)}</div><div class="muted">${esc(shorten(trade.exitReason || '',260))}</div></div>`);
  parts.push(`<div><strong>MFE / MAE</strong><div><span class="positive">+${fmt(num(context.mfeDollars),2)}</span> / <span class="negative">${fmt(num(context.maeDollars),2)}</span></div><div class="muted">Move +${fmt(num(context.favorableMovePercent),2)}% / ${fmt(num(context.adverseMovePercent),2)}%</div></div>`);
  parts.push(`<div><strong>Bars held</strong><div>${context.barsHeld || 0}</div><div class="muted">from rebuilt CSV bar indices</div></div>`);
  parts.push('</div>');
  parts.push('<div class="indicator-grid">');
  parts.push(buildIndicatorBlock('Entry indicators', context.entryIndicators));
  parts.push(buildIndicatorBlock('Exit indicators', context.exitIndicators));
  parts.push('</div>');
  parts.push('<h3>Primary 1m diagram</h3>');
  parts.push('<div class="chart-legend"><span><i class="legend candle"></i>Candles</span><span><i class="legend ema9"></i>EMA9</span><span><i class="legend sma20"></i>SMA20</span><span><i class="legend sma200"></i>SMA200</span><span><i class="legend vwap"></i>VWAP</span><span><i class="legend entry"></i>Entry</span><span><i class="legend exit"></i>Exit</span></div>');
  parts.push(`<canvas class="trade-chart" data-chart-id="${escAttr(context.id)}" data-chart-kind="primary" width="1120" height="360"></canvas>`);

  if (higher) {
    parts.push(`<h3>${higher.candleMinutes}m EMA9 angle conduct diagram</h3>`);
    parts.push(`<p class="muted">${esc(higher.signalSummary || '')}</p>`);
    parts.push('<div class="chart-legend"><span><i class="legend candle"></i>Completed higher-timeframe candles</span><span><i class="legend ema9"></i>EMA9</span><span><i class="legend angle"></i>ATR-normalized angle</span><span><i class="legend entry"></i>Signal candle</span></div>');
    parts.push(`<canvas class="trade-chart" data-chart-id="${escAttr(context.id)}" data-chart-kind="higher" width="1120" height="360"></canvas>`);
  }

  parts.push('<h3>Action timeline</h3><ol class="timeline">');
  for (const item of (context.timeline || [])) parts.push(`<li>${esc(item)}</li>`);
  parts.push('</ol>');
  parts.push('<h3>Explanation</h3><ul class="explanations">');
  for (const item of (context.explanations || [])) parts.push(`<li>${esc(item)}</li>`);
  parts.push('</ul>');
  parts.push('</div>');
  return parts.join('');
}

function buildIndicatorBlock(title, indicators){
  if (!indicators) return `<div class="indicator-card"><strong>${esc(title)}</strong><div class="muted">not available</div></div>`;
  const volumeRatio = indicators.volumeAverage20 && indicators.volumeAverage20 > 0 ? num(indicators.volume) / num(indicators.volumeAverage20) : 0;
  return `<div class="indicator-card"><strong>${esc(title)}</strong><div>${esc(indicators.time || '')} | Close ${fmt(num(indicators.close),4)}</div><div>EMA9 ${formatNullable(indicators.ema9)} | SMA20 ${formatNullable(indicators.sma20)} | SMA200 ${formatNullable(indicators.sma200)}</div><div>VWAP ${formatNullable(indicators.vwap)} | Volume ${indicators.volume || 0} | VolAvg20 ${formatNullable(indicators.volumeAverage20)} | Ratio ${fmt(volumeRatio,2)}</div></div>`;
}

function esc(value){
  return String(value ?? '').replace(/[&<>"]/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch]));
}

function escAttr(value){ return esc(value).replace(/'/g, '&#39;'); }
function shorten(value, max){ value = String(value || ''); return value.length <= max ? value : value.substring(0, max) + '...'; }
function sideClass(side){ return String(side).toUpperCase() === 'SHORT' ? 'side-short' : 'side-long'; }
function pnlClass(value){ return num(value) >= 0 ? 'positive' : 'negative'; }
function formatNullable(value){ return value === null || value === undefined ? 'n/a' : fmt(num(value),2); }
function formatDateTime(value){ return String(value || '').replace('T',' ').replace(/\.\d+.*$/,'').replace(/Z$/,''); }

function drawPrimaryChart(canvas, context){
  const points = context.primaryPoints || [];
  const ctx = setupCanvas(canvas);
  drawFrame(ctx, canvas, 'Primary 1m trade window');
  if (!points.length) { drawEmpty(ctx, canvas); return; }

  const values = [];
  for (const p of points) {
    values.push(num(p.high), num(p.low));
    for (const key of ['ema9','sma20','sma200','vwap']) if (p[key] !== null && p[key] !== undefined) values.push(num(p[key]));
  }
  const scale = createScale(canvas, values);
  const x = index => scale.left + (points.length === 1 ? scale.width / 2 : index * scale.width / (points.length - 1));

  drawGrid(ctx, canvas, scale);
  drawLine(ctx, points, x, scale, 'ema9', '#d6a900', 2);
  drawLine(ctx, points, x, scale, 'sma20', '#00a8c6', 2);
  drawLine(ctx, points, x, scale, 'sma200', '#7d2ae8', 2);
  drawLine(ctx, points, x, scale, 'vwap', '#404f6f', 1.5, [5,4]);
  drawCandles(ctx, points, x, scale);
  drawMarker(ctx, x(context.entryPointIndex), scale.y(num(context.trade.entryPrice)), 'Entry ' + fmt(num(context.trade.entryPrice), 4), '#12355b');
  drawMarker(ctx, x(context.exitPointIndex), scale.y(num(context.trade.exitPrice)), 'Exit ' + fmt(num(context.trade.exitPrice), 4), '#d00000');
  drawAxisLabels(ctx, canvas, points, scale);
}

function drawHigherChart(canvas, context){
  const higher = context.higherTimeframeChart;
  const points = higher ? higher.points || [] : [];
  const ctx = setupCanvas(canvas);
  drawFrame(ctx, canvas, higher ? `${higher.candleMinutes}m EMA9 angle conduct view` : 'Higher timeframe view');
  if (!points.length) { drawEmpty(ctx, canvas); return; }

  const values = [];
  for (const p of points) {
    values.push(num(p.high), num(p.low));
    if (p.ema9 !== null && p.ema9 !== undefined) values.push(num(p.ema9));
  }
  const scale = createScale(canvas, values);
  const x = index => scale.left + (points.length === 1 ? scale.width / 2 : index * scale.width / (points.length - 1));

  drawGrid(ctx, canvas, scale);
  drawLine(ctx, points, x, scale, 'ema9', '#d6a900', 2.25);
  drawCandles(ctx, points, x, scale);

  const signalIndex = Math.max(0, Math.min(points.length - 1, higher.signalPointIndex || 0));
  const signal = points[signalIndex];
  drawVerticalBand(ctx, x(signalIndex), '#12355b');
  drawMarker(ctx, x(signalIndex), scale.y(num(signal.close)), 'Signal ' + (signal.angleDegrees === null || signal.angleDegrees === undefined ? 'n/a' : fmt(num(signal.angleDegrees), 2) + '°'), '#12355b');
  drawAnglePanel(ctx, canvas, points, x, higher.angleThresholdDegrees || 12);
  drawAxisLabels(ctx, canvas, points, scale);
}

function setupCanvas(canvas){
  const ratio = window.devicePixelRatio || 1;
  const cssWidth = canvas.clientWidth || canvas.width;
  const cssHeight = canvas.clientHeight || 360;
  canvas.width = Math.floor(cssWidth * ratio);
  canvas.height = Math.floor(cssHeight * ratio);
  const ctx = canvas.getContext('2d');
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  canvas._cssWidth = cssWidth;
  canvas._cssHeight = cssHeight;
  return ctx;
}

function drawFrame(ctx, canvas, title){
  const w = canvas._cssWidth, h = canvas._cssHeight;
  ctx.clearRect(0,0,w,h);
  ctx.fillStyle = '#fffef9'; ctx.fillRect(0,0,w,h);
  ctx.strokeStyle = '#d8d2c3'; ctx.strokeRect(0.5,0.5,w-1,h-1);
  ctx.fillStyle = '#1e2430'; ctx.font = '13px Segoe UI, Arial'; ctx.fillText(title, 14, 22);
}

function createScale(canvas, values){
  const w = canvas._cssWidth, h = canvas._cssHeight;
  const left = 50, right = 18, top = 34, bottom = 42;
  let min = Math.min(...values), max = Math.max(...values);
  if (!isFinite(min) || !isFinite(max) || min === max) { min = min || 0; max = max + 1; }
  const pad = (max - min) * 0.08 || 1;
  min -= pad; max += pad;
  return {left, right, top, bottom, width:w-left-right, height:h-top-bottom, min, max, y:v => top + (max-v)/(max-min)*(h-top-bottom)};
}

function drawGrid(ctx, canvas, scale){
  const w = canvas._cssWidth;
  ctx.strokeStyle = '#e8ddc5'; ctx.lineWidth = 1;
  ctx.fillStyle = '#5f5a51'; ctx.font = '11px Segoe UI, Arial';
  for(let i=0;i<=4;i++){
    const y = scale.top + i*scale.height/4;
    const value = scale.max - i*(scale.max-scale.min)/4;
    ctx.beginPath(); ctx.moveTo(scale.left,y); ctx.lineTo(w-scale.right,y); ctx.stroke();
    ctx.fillText(fmt(value,2), 6, y+4);
  }
}

function drawCandles(ctx, points, x, scale){
  const candleWidth = Math.max(3, Math.min(12, scale.width / Math.max(1, points.length) * 0.58));
  for(let i=0;i<points.length;i++){
    const p = points[i];
    const xi = x(i);
    const open = scale.y(num(p.open)), close = scale.y(num(p.close)), high = scale.y(num(p.high)), low = scale.y(num(p.low));
    const green = num(p.close) >= num(p.open);
    ctx.strokeStyle = green ? '#159a82' : '#c23a2b';
    ctx.fillStyle = green ? '#2a9d8f' : '#c23a2b';
    ctx.beginPath(); ctx.moveTo(xi, high); ctx.lineTo(xi, low); ctx.stroke();
    const top = Math.min(open, close), height = Math.max(2, Math.abs(close-open));
    ctx.fillRect(xi - candleWidth/2, top, candleWidth, height);
  }
}

function drawLine(ctx, points, x, scale, key, color, width, dash){
  ctx.save(); ctx.strokeStyle = color; ctx.lineWidth = width || 1.5; ctx.setLineDash(dash || []);
  let started = false;
  ctx.beginPath();
  for(let i=0;i<points.length;i++){
    const value = points[i][key];
    if(value === null || value === undefined) { started = false; continue; }
    const xi = x(i), yi = scale.y(num(value));
    if(!started){ ctx.moveTo(xi, yi); started = true; }
    else ctx.lineTo(xi, yi);
  }
  ctx.stroke(); ctx.restore();
}

function drawMarker(ctx, x, y, label, color){
  ctx.save();
  ctx.strokeStyle = color; ctx.fillStyle = color; ctx.lineWidth = 1.5;
  ctx.beginPath(); ctx.arc(x,y,4,0,Math.PI*2); ctx.fill();
  ctx.beginPath(); ctx.moveTo(x, 34); ctx.lineTo(x, y-6); ctx.stroke();
  ctx.fillStyle = color; ctx.font = '11px Segoe UI, Arial';
  ctx.fillText(label, Math.min(x+6, ctx.canvas.clientWidth-150), Math.max(46, y-8));
  ctx.restore();
}

function drawVerticalBand(ctx, x, color){
  ctx.save(); ctx.strokeStyle = color; ctx.setLineDash([4,3]);
  ctx.beginPath(); ctx.moveTo(x, 34); ctx.lineTo(x, ctx.canvas.clientHeight-42); ctx.stroke(); ctx.restore();
}

function drawAnglePanel(ctx, canvas, points, x, threshold){
  const h = canvas._cssHeight;
  const panelTop = h - 58, panelBottom = h - 22;
  ctx.save();
  ctx.fillStyle = 'rgba(255,153,0,0.07)'; ctx.fillRect(50, panelTop, canvas._cssWidth-68, panelBottom-panelTop);
  ctx.strokeStyle = '#ff9900'; ctx.setLineDash([4,3]);
  ctx.beginPath(); ctx.moveTo(50, (panelTop+panelBottom)/2); ctx.lineTo(canvas._cssWidth-18, (panelTop+panelBottom)/2); ctx.stroke();
  ctx.setLineDash([]); ctx.strokeStyle = '#ff9900'; ctx.lineWidth = 1.4;
  const angleMax = Math.max(threshold*1.5, ...points.map(p => Math.abs(num(p.angleDegrees || 0))));
  let started=false; ctx.beginPath();
  for(let i=0;i<points.length;i++){
    const a = points[i].angleDegrees;
    if(a === null || a === undefined){ started=false; continue; }
    const y = panelBottom - ((num(a)+angleMax)/(angleMax*2))*(panelBottom-panelTop);
    if(!started){ ctx.moveTo(x(i),y); started=true; } else ctx.lineTo(x(i),y);
  }
  ctx.stroke();
  ctx.fillStyle = '#5f5a51'; ctx.font = '10px Segoe UI, Arial';
  ctx.fillText(`Angle ±${fmt(threshold,0)}°`, 54, panelTop-4);
  ctx.restore();
}

function drawAxisLabels(ctx, canvas, points, scale){
  if(!points.length) return;
  ctx.save(); ctx.fillStyle = '#5f5a51'; ctx.font = '10px Segoe UI, Arial';
  ctx.fillText(points[0].time, scale.left, canvas._cssHeight-10);
  const last = points[points.length-1].time;
  ctx.fillText(last, Math.max(scale.left, canvas._cssWidth-18-ctx.measureText(last).width), canvas._cssHeight-10);
  ctx.restore();
}

function drawEmpty(ctx, canvas){
  ctx.fillStyle = '#5f5a51'; ctx.font = '13px Segoe UI, Arial';
  ctx.fillText('No chart data available.', 18, 56);
}

function num(value){ return Number(value || 0); }
function fmt(value, digits){ return Number(value).toFixed(digits); }
})();
""";
    }
}
