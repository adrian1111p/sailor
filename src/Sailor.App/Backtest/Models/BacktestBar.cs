namespace Sailor.App.Backtest.Models;

public sealed record BacktestBar(
    DateTimeOffset Time,
    string Symbol,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
