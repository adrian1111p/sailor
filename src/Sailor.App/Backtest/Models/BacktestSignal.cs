namespace Sailor.App.Backtest.Models;

public enum BacktestSignalType
{
    Hold = 0,
    Buy = 1,
    Sell = 2
}

public sealed record BacktestSignal(
    BacktestSignalType Type,
    string Reason)
{
    public static BacktestSignal Hold(string reason) => new(BacktestSignalType.Hold, reason);

    public static BacktestSignal Buy(string reason) => new(BacktestSignalType.Buy, reason);

    public static BacktestSignal Sell(string reason) => new(BacktestSignalType.Sell, reason);
}
