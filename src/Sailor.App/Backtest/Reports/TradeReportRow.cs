namespace Sailor.App.Backtest.Reports;

public sealed record TradeReportRow(
    string Strategy,
    string ProfileName,
    string Variant,
    string Symbol,
    int Sequence,
    string Side,
    int Quantity,
    DateTimeOffset EntryTime,
    DateTimeOffset ExitTime,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal Pnl,
    decimal PnlPercent,
    string EntryReason,
    string ExitReason);
