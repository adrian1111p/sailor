namespace Sailor.App.Backtest.Models;

public sealed record BacktestOptions(
    string Symbol,
    string Timeframe,
    string ProfileName,
    decimal InitialCash,
    decimal MaxPositionNotional,
    decimal StopLossPercent,
    decimal TakeProfitPercent,
    int MaxHoldBars)
{
    public static BacktestOptions CreateDefault(
        string symbol,
        string timeframe,
        string profileName = "sailor-trend-volume")
    {
        return new BacktestOptions(
            Symbol: symbol.Trim().ToUpperInvariant(),
            Timeframe: string.IsNullOrWhiteSpace(timeframe) ? "1m" : timeframe.Trim(),
            ProfileName: string.IsNullOrWhiteSpace(profileName) ? "sailor-trend-volume" : profileName.Trim(),
            InitialCash: 10_000.00m,
            MaxPositionNotional: 1_000.00m,
            StopLossPercent: 1.00m,
            TakeProfitPercent: 2.00m,
            MaxHoldBars: 30);
    }
}
