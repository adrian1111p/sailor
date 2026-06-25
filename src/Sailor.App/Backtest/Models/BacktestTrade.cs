namespace Sailor.App.Backtest.Models;

public sealed record BacktestTrade(
    string Symbol,
    DateTimeOffset EntryTime,
    DateTimeOffset ExitTime,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    string EntryReason,
    string ExitReason)
{
    public decimal Pnl => (ExitPrice - EntryPrice) * Quantity;

    public decimal PnlPercent => EntryPrice > 0
        ? (ExitPrice - EntryPrice) / EntryPrice * 100m
        : 0m;
}
