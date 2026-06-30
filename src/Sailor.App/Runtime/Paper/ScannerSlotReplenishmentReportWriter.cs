using System.Globalization;
using System.Text.Json;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.Paper;

public sealed class ScannerSlotReplenishmentReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SailorRuntimeMode _mode;

    public ScannerSlotReplenishmentReportWriter(SailorRuntimeMode mode)
    {
        _mode = mode;
        ReportDirectory = EnsureDirectory(Path.Combine(RepositoryRoot, "logs", _mode.ToDisplayName(), "ScannerSlots"));
        LatestJsonPath = Path.Combine(ReportDirectory, "scanner_slots_latest.json");
        DailyCsvPath = Path.Combine(ReportDirectory, $"scanner_slots_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public string ReportDirectory { get; }

    public string LatestJsonPath { get; }

    public string DailyCsvPath { get; }

    public ScannerSlotReplenishmentReport Write(ScannerSlotReplenishmentReport report)
    {
        File.WriteAllText(LatestJsonPath, JsonSerializer.Serialize(report, JsonOptions));
        bool writeHeader = !File.Exists(DailyCsvPath);
        using var writer = new StreamWriter(DailyCsvPath, append: true);
        if (writeHeader)
        {
            writer.WriteLine("observedUtc,targetScannerTrades,activeScannerTrades,manualManagedTrades,shortfall,newSlotsRequested,newSlotsCreated,blockedSymbols,reason");
        }

        writer.WriteLine(string.Join(',', new[]
        {
            Escape(report.ObservedUtc.ToString("O", CultureInfo.InvariantCulture)),
            report.TargetScannerTrades.ToString(CultureInfo.InvariantCulture),
            report.ActiveScannerTrades.ToString(CultureInfo.InvariantCulture),
            report.ManualManagedTrades.ToString(CultureInfo.InvariantCulture),
            report.Shortfall.ToString(CultureInfo.InvariantCulture),
            report.NewSlotsRequested.ToString(CultureInfo.InvariantCulture),
            report.NewSlotsCreated.ToString(CultureInfo.InvariantCulture),
            Escape(string.Join(';', report.BlockedSymbols)),
            Escape(report.Reason)
        }));

        return report.WithPaths(LatestJsonPath, DailyCsvPath);
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
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
