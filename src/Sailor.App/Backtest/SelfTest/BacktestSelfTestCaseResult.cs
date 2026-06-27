namespace Sailor.App.Backtest.SelfTest;

public sealed record BacktestSelfTestCaseResult(
    string Name,
    bool Passed,
    string Message,
    TimeSpan Duration)
{
    public static BacktestSelfTestCaseResult Pass(string name, string message, TimeSpan duration)
        => new(name, true, message, duration);

    public static BacktestSelfTestCaseResult Fail(string name, string message, TimeSpan duration)
        => new(name, false, message, duration);
}
