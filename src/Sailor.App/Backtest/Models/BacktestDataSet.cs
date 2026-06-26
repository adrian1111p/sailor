namespace Sailor.App.Backtest.Models;

public sealed record BacktestDataSet(
    string Symbol,
    string Timeframe,
    string SourcePath,
    IReadOnlyList<BacktestBar> Bars);
