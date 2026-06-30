using System.Globalization;
using System.Text.Json;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.Paper;

public sealed class SevereDisconnectRecoveryReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SailorRuntimeMode _mode;

    public SevereDisconnectRecoveryReportWriter(SailorRuntimeMode mode)
    {
        _mode = mode;
        ReportDirectory = EnsureDirectory(Path.Combine(RepositoryRoot, "logs", _mode.ToDisplayName(), "Recovery"));
        LatestJsonPath = Path.Combine(ReportDirectory, "severe_recovery_latest.json");
        DailyCsvPath = Path.Combine(ReportDirectory, $"severe_recovery_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public string ReportDirectory { get; }

    public string LatestJsonPath { get; }

    public string DailyCsvPath { get; }

    public SevereDisconnectRecoveryReport Write(SevereDisconnectRecoveryReport report)
    {
        File.WriteAllText(LatestJsonPath, JsonSerializer.Serialize(report, JsonOptions));
        bool writeHeader = !File.Exists(DailyCsvPath);
        using var writer = new StreamWriter(DailyCsvPath, append: true);
        if (writeHeader)
        {
            writer.WriteLine("observedUtc,mode,triggerReason,reconnectRecovered,reconciliationStatus,brokerTruthAvailable,sessionsRebuilt,historyRefreshAttempted,historyRefreshOk,historyRefreshTotal,easternMinuteOfDay,lastEntryMinute,canResumeEntries,scannerReplenishmentAllowed,sessionsBefore,sessionsAfter,activeSymbolsBefore,brokerPositionSymbols,brokerOpenOrderSymbols,rebuiltSymbols,warnings");
        }

        writer.WriteLine(string.Join(',', new[]
        {
            Csv(report.ObservedUtc.ToString("O", CultureInfo.InvariantCulture)),
            Csv(report.Mode),
            Csv(report.TriggerReason),
            report.ReconnectRecovered.ToString(),
            Csv(report.ReconciliationStatus),
            report.BrokerTruthAvailable.ToString(),
            report.SessionsRebuilt.ToString(),
            report.HistoryRefreshAttempted.ToString(),
            report.HistoryRefreshOk.ToString(CultureInfo.InvariantCulture),
            report.HistoryRefreshTotal.ToString(CultureInfo.InvariantCulture),
            report.EasternMinuteOfDay.ToString(CultureInfo.InvariantCulture),
            report.LastEntryMinute.ToString(CultureInfo.InvariantCulture),
            report.CanResumeEntries.ToString(),
            report.ScannerReplenishmentAllowed.ToString(),
            report.SessionsBefore.ToString(CultureInfo.InvariantCulture),
            report.SessionsAfter.ToString(CultureInfo.InvariantCulture),
            Csv(string.Join(';', report.ActiveSymbolsBefore)),
            Csv(string.Join(';', report.BrokerPositionSymbols)),
            Csv(string.Join(';', report.BrokerOpenOrderSymbols)),
            Csv(string.Join(';', report.RebuiltSymbols)),
            Csv(string.Join(" | ", report.Warnings))
        }));

        return report.WithPaths(LatestJsonPath, DailyCsvPath);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
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
