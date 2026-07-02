using System.Globalization;
using System.Net;
using System.Text;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.Ui;

public sealed record SailorUiReportExportResult(
    DateTimeOffset ExportedUtc,
    string Mode,
    string CsvPath,
    string HtmlPath,
    int ActiveRows,
    int ScannerRows,
    decimal DailyPnl,
    decimal Unrealized,
    decimal Realized)
{
    public string ToSummaryString()
        => $"SAILOR-070 export mode={Mode} activeRows={ActiveRows} scannerRows={ScannerRows} dailyPnl={DailyPnl.ToString("0.00", CultureInfo.InvariantCulture)} csv={CsvPath} html={HtmlPath}";
}

public sealed class SailorUiReportExporter
{
    private readonly SailorRuntimeMode _mode;
    private readonly string _reportDirectory;

    public SailorUiReportExporter(SailorRuntimeMode mode, string? repositoryRoot = null)
    {
        _mode = mode;
        _reportDirectory = string.IsNullOrWhiteSpace(repositoryRoot)
            ? Path.Combine(mode == SailorRuntimeMode.Live ? SailorLogPaths.Live : SailorLogPaths.Paper, "SailorUI")
            : Path.Combine(repositoryRoot.Trim(), "logs", mode.ToDisplayName(), "SailorUI");
    }

    public SailorUiReportExportResult Write(SailorUiSnapshot snapshot)
    {
        Directory.CreateDirectory(_reportDirectory);
        DateTimeOffset exportedUtc = DateTimeOffset.UtcNow;
        string timestamp = exportedUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string prefix = $"sailor_ui_report_{timestamp}";
        string csvPath = Path.Combine(_reportDirectory, prefix + ".csv");
        string htmlPath = Path.Combine(_reportDirectory, prefix + ".html");
        string latestCsvPath = Path.Combine(_reportDirectory, "sailor_ui_report_latest.csv");
        string latestHtmlPath = Path.Combine(_reportDirectory, "sailor_ui_report_latest.html");

        string csv = BuildCsv(snapshot, exportedUtc);
        string html = BuildHtml(snapshot, exportedUtc);
        File.WriteAllText(csvPath, csv, Encoding.UTF8);
        File.WriteAllText(latestCsvPath, csv, Encoding.UTF8);
        File.WriteAllText(htmlPath, html, Encoding.UTF8);
        File.WriteAllText(latestHtmlPath, html, Encoding.UTF8);

        return new SailorUiReportExportResult(
            exportedUtc,
            _mode.ToDisplayName(),
            csvPath,
            htmlPath,
            snapshot.ActiveRows.Count,
            snapshot.ScannerRows.Count,
            snapshot.Pnl.DailyPnl,
            snapshot.Pnl.Unrealized,
            snapshot.Pnl.Realized);
    }

    private static string BuildCsv(SailorUiSnapshot snapshot, DateTimeOffset exportedUtc)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SAILOR-070 SailorUI report export");
        builder.AppendLine("ExportedUtc,Mode,Status,DailyPnL,Unrealized,Realized,ControlsEnabled,ControlMode,SourceSummary");
        builder.AppendCsv(exportedUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendCsv(snapshot.Mode);
        builder.AppendCsv(snapshot.Status);
        builder.AppendCsv(snapshot.Pnl.DailyPnl);
        builder.AppendCsv(snapshot.Pnl.Unrealized);
        builder.AppendCsv(snapshot.Pnl.Realized);
        builder.AppendCsv(snapshot.ControlsEnabled);
        builder.AppendCsv(snapshot.ControlMode);
        builder.AppendCsv(snapshot.SourceSummary, endLine: true);
        builder.AppendLine();
        builder.AppendLine("Section,DailyPnL,Ranking,Symbol,Position,MarketValue,BuyValue,Open,Price,PriceStale,TradeEnabled,Strategy,Volume,Side,Score,Status,Reason");

        foreach (SailorUiTradeRow row in snapshot.ActiveRows)
        {
            builder.AppendCsv("ActiveToday");
            builder.AppendCsv(row.DailyPnl);
            builder.AppendCsv(row.ScanRanking?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            builder.AppendCsv(row.Symbol);
            builder.AppendCsv(row.Position);
            builder.AppendCsv(row.MarketValue);
            builder.AppendCsv(row.BuyValue);
            builder.AppendCsv(row.Open);
            builder.AppendCsv(row.Price);
            builder.AppendCsv(row.PriceStale);
            builder.AppendCsv(row.TradeEnabled);
            builder.AppendCsv(row.Strategy);
            builder.AppendCsv(row.Volume);
            builder.AppendCsv(row.Position < 0 ? "SHORT" : row.Position > 0 ? "LONG" : string.Empty);
            builder.AppendCsv(string.Empty);
            builder.AppendCsv(row.Status);
            builder.AppendCsv(row.Reason, endLine: true);
        }

        foreach (SailorUiScannerRow row in snapshot.ScannerRows)
        {
            builder.AppendCsv("ScannerRest");
            builder.AppendCsv(string.Empty);
            builder.AppendCsv(row.ScanRanking);
            builder.AppendCsv(row.Symbol);
            builder.AppendCsv(string.Empty);
            builder.AppendCsv(string.Empty);
            builder.AppendCsv(string.Empty);
            builder.AppendCsv(string.Empty);
            builder.AppendCsv(row.Price);
            builder.AppendCsv(row.PriceStale);
            builder.AppendCsv(row.TradeEnabled);
            builder.AppendCsv(row.Strategy);
            builder.AppendCsv(row.Volume);
            builder.AppendCsv(row.SelectedSide);
            builder.AppendCsv(row.FinalScore);
            builder.AppendCsv(row.Status);
            builder.AppendCsv(row.Reason, endLine: true);
        }

        return builder.ToString();
    }

    private static string BuildHtml(SailorUiSnapshot snapshot, DateTimeOffset exportedUtc)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\" />");
        builder.AppendLine("<title>SailorUI Report Export</title>");
        builder.AppendLine("<style>body{background:#050505;color:#eee;font-family:Consolas,'Segoe UI Mono',monospace;font-size:12px}table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid #252525;padding:2px 5px;white-space:nowrap}th{background:#111;color:#bbb;text-align:left}.num{text-align:right}.win,.long{background:#04230c}.loss,.short{background:#340707}.zero{background:#080808}.pos{color:#00ff5a}.neg{color:#ff4040}.stale{color:#ffd24a}.section{margin-top:10px;padding:3px;background:#181818;border-top:1px solid #333;border-bottom:1px solid #333;font-weight:bold}.pnl{background:#052d05;border-left:4px solid #0aff0a;padding:6px;margin-bottom:8px}.badge{border:1px solid #555;padding:1px 4px;margin-left:4px}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine($"<div class=\"pnl\"><b>SAILOR-070 SailorUI report export</b><span class=\"badge\">{Html(snapshot.Mode)}</span><span class=\"badge\">{Html(snapshot.Status)}</span><span class=\"badge\">{Html(exportedUtc.ToString("O", CultureInfo.InvariantCulture))}</span><br/>Daily P&amp;L: <span class=\"{PnlClass(snapshot.Pnl.DailyPnl)}\">{Fmt(snapshot.Pnl.DailyPnl)}</span> | Unrealized: <span class=\"{PnlClass(snapshot.Pnl.Unrealized)}\">{Fmt(snapshot.Pnl.Unrealized)}</span> | Realized: <span class=\"{PnlClass(snapshot.Pnl.Realized)}\">{Fmt(snapshot.Pnl.Realized)}</span></div>");
        builder.AppendLine("<div class=\"section\">Section 2 — Active / today trades</div>");
        builder.AppendLine("<table><thead><tr><th>DLY P&amp;L</th><th>Rank</th><th>Symbol</th><th>Position</th><th>MKT VAL</th><th>Buy value</th><th>Open</th><th>Price</th><th>Trade</th><th>Strategy</th><th>Volume</th><th>Reason</th></tr></thead><tbody>");
        foreach (SailorUiTradeRow row in snapshot.ActiveRows)
        {
            string rowClass = ActiveRowClass(row.Position, row.DailyPnl);
            builder.AppendLine($"<tr class=\"{rowClass}\"><td class=\"num {PnlClass(row.DailyPnl)}\">{Fmt(row.DailyPnl)}</td><td class=\"num\">{Html(row.ScanRanking?.ToString(CultureInfo.InvariantCulture) ?? "-")}</td><td>{Html(row.Symbol)}</td><td class=\"num\">{row.Position}</td><td class=\"num {PnlClass(row.MarketValue)}\">{Fmt(row.MarketValue)}</td><td class=\"num\">{Fmt(row.BuyValue)}</td><td class=\"num\">{Fmt(row.Open, 4)}</td><td class=\"num {(row.PriceStale ? "stale" : string.Empty)}\">{Fmt(row.Price, 4)}{(row.PriceStale ? " *" : string.Empty)}</td><td>{(row.TradeEnabled ? "yes" : "no")}</td><td>{Html(row.Strategy)}</td><td class=\"num\">{row.Volume}</td><td>{Html(row.Reason)}</td></tr>");
        }
        builder.AppendLine("</tbody></table>");
        builder.AppendLine("<div class=\"section\">Section 3 — Rest scanner symbols</div>");
        builder.AppendLine("<table><thead><tr><th>Rank</th><th>Symbol</th><th>Trade</th><th>Strategy</th><th>Volume</th><th>Price</th><th>Side</th><th>Score</th><th>Status</th><th>Reason</th></tr></thead><tbody>");
        foreach (SailorUiScannerRow row in snapshot.ScannerRows)
        {
            string rowClass = ScannerRowClass(row.SelectedSide);
            builder.AppendLine($"<tr class=\"{rowClass}\"><td class=\"num\">{row.ScanRanking}</td><td>{Html(row.Symbol)}</td><td>{(row.TradeEnabled ? "yes" : "no")}</td><td>{Html(row.Strategy)}</td><td class=\"num\">{row.Volume}</td><td class=\"num {(row.PriceStale ? "stale" : string.Empty)}\">{Fmt(row.Price, 4)}{(row.PriceStale ? " *" : string.Empty)}</td><td>{Html(row.SelectedSide)}</td><td class=\"num\">{Fmt(row.FinalScore)}</td><td>{Html(row.Status)}</td><td>{Html(row.Reason)}</td></tr>");
        }
        builder.AppendLine("</tbody></table>");
        if (snapshot.Warnings.Count > 0)
        {
            builder.AppendLine("<div class=\"section\">Warnings</div>");
            foreach (string warning in snapshot.Warnings)
            {
                builder.AppendLine($"<div class=\"stale\">WARN: {Html(warning)}</div>");
            }
        }
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string ActiveRowClass(int position, decimal dailyPnl)
    {
        if (dailyPnl < 0m || position < 0)
        {
            return "loss short";
        }

        if (dailyPnl > 0m || position > 0)
        {
            return "win long";
        }

        return "zero";
    }

    private static string ScannerRowClass(string side)
        => side.Equals("SHORT", StringComparison.OrdinalIgnoreCase)
            ? "short"
            : side.Equals("LONG", StringComparison.OrdinalIgnoreCase)
                ? "long"
                : "zero";

    private static string PnlClass(decimal value)
        => value > 0m ? "pos" : value < 0m ? "neg" : "zero";

    private static string Fmt(decimal value, int digits = 2)
        => value.ToString("0." + new string('0', digits), CultureInfo.InvariantCulture);

    private static string Html(string value)
        => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}

internal static class SailorUiReportCsvExtensions
{
    public static void AppendCsv(this StringBuilder builder, object? value, bool endLine = false)
    {
        string text = value switch
        {
            null => string.Empty,
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        bool quote = text.Contains(',', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal) || text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal);
        if (quote)
        {
            builder.Append('"');
            builder.Append(text.Replace("\"", "\"\"", StringComparison.Ordinal));
            builder.Append('"');
        }
        else
        {
            builder.Append(text);
        }

        builder.Append(endLine ? Environment.NewLine : ',');
    }
}
