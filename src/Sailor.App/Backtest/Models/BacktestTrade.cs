namespace Sailor.App.Backtest.Models;

public sealed record BacktestTrade(
    string Symbol,
    DateTimeOffset EntryTime,
    DateTimeOffset ExitTime,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    string EntryReason,
    string ExitReason,
    int PositionSide = 1)
{
    public string SideName => PositionSide < 0 ? "SHORT" : "LONG";

    public decimal Pnl => PositionSide < 0
        ? (EntryPrice - ExitPrice) * Quantity
        : (ExitPrice - EntryPrice) * Quantity;

    public decimal PnlPercent => EntryPrice > 0
        ? (PositionSide < 0
            ? (EntryPrice - ExitPrice) / EntryPrice * 100m
            : (ExitPrice - EntryPrice) / EntryPrice * 100m)
        : 0m;
}
