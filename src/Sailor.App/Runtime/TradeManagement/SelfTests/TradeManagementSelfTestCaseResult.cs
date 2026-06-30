namespace Sailor.App.Runtime.TradeManagement.SelfTests;

public sealed record TradeManagementSelfTestCaseResult(
    string Scenario,
    bool Passed,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Warnings)
{
    public string Status => Passed ? "PASS" : "FAIL";

    public string ToSummaryString()
        => $"{Scenario}: {Status} checks={Checks.Count} events={Events.Count} warnings={Warnings.Count}";
}
