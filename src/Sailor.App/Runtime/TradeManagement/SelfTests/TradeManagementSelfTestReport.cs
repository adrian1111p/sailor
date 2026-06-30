namespace Sailor.App.Runtime.TradeManagement.SelfTests;

public sealed record TradeManagementSelfTestReport(
    DateTimeOffset ObservedUtc,
    string Mode,
    string Scenario,
    bool SendOrdersRequested,
    bool Passed,
    IReadOnlyList<TradeManagementSelfTestCaseResult> Cases,
    string? JsonPath = null,
    string? CsvPath = null)
{
    public int PassedCount => Cases.Count(test => test.Passed);

    public int FailedCount => Cases.Count - PassedCount;

    public TradeManagementSelfTestReport WithPaths(string jsonPath, string csvPath)
        => this with { JsonPath = jsonPath, CsvPath = csvPath };

    public string ToSummaryString()
    {
        string paths = string.IsNullOrWhiteSpace(JsonPath)
            ? string.Empty
            : $" json={JsonPath} csv={CsvPath}";
        return $"tradeManagementSelfTest scenario={Scenario} result={(Passed ? "PASS" : "FAIL")} passed={PassedCount}/{Cases.Count} sendOrdersRequested={SendOrdersRequested}{paths}";
    }
}
