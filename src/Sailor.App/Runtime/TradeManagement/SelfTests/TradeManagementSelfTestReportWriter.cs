using System.Text.Json;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.TradeManagement.SelfTests;

public sealed class TradeManagementSelfTestReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _latestJsonPath;
    private readonly string _dailyCsvPath;

    public TradeManagementSelfTestReportWriter(SailorRuntimeMode mode)
    {
        string root = mode == SailorRuntimeMode.Live
            ? SailorLogPaths.Live
            : SailorLogPaths.Paper;
        _directory = Path.Combine(root, "SelfTests");
        Directory.CreateDirectory(_directory);
        _latestJsonPath = Path.Combine(_directory, "trade_management_self_test_latest.json");
        _dailyCsvPath = Path.Combine(_directory, $"trade_management_self_test_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public TradeManagementSelfTestReport Write(TradeManagementSelfTestReport report)
    {
        TradeManagementSelfTestReport withPaths = report.WithPaths(_latestJsonPath, _dailyCsvPath);
        File.WriteAllText(_latestJsonPath, JsonSerializer.Serialize(withPaths, JsonOptions));
        AppendCsv(withPaths);
        return withPaths;
    }

    private void AppendCsv(TradeManagementSelfTestReport report)
    {
        bool writeHeader = !File.Exists(_dailyCsvPath);
        using var writer = new StreamWriter(new FileStream(_dailyCsvPath, FileMode.Append, FileAccess.Write, FileShare.Read));
        if (writeHeader)
        {
            writer.WriteLine("observedUtc,mode,scenario,caseScenario,status,checks,events,warnings,sendOrdersRequested");
        }

        foreach (TradeManagementSelfTestCaseResult testCase in report.Cases)
        {
            writer.WriteLine(string.Join(',',
                Csv(report.ObservedUtc.ToString("O")),
                Csv(report.Mode),
                Csv(report.Scenario),
                Csv(testCase.Scenario),
                Csv(testCase.Status),
                testCase.Checks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                testCase.Events.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                testCase.Warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                report.SendOrdersRequested.ToString()));
        }
    }

    private static string Csv(string? value)
    {
        string text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
