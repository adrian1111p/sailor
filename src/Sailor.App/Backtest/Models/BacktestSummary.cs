namespace Sailor.App.Backtest.Models;

public sealed record BacktestSummary(
    string Symbol,
    int TotalTrades,
    int Winners,
    int Losers,
    decimal TotalPnl,
    decimal FinalCash)
{
    public decimal WinRatePercent => TotalTrades > 0
        ? (decimal)Winners / TotalTrades * 100m
        : 0m;
}
