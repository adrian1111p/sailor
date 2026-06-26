namespace Sailor.App.Backtest.Reports;

public sealed record StrategyReportRow(
    StrategyReportDefinition Definition,
    int Symbols,
    int Trades,
    int Winners,
    int Losers,
    decimal WinRatePercent,
    decimal ProfitFactor,
    decimal Sharpe,
    decimal EquitySharpe,
    decimal EquitySortino,
    decimal EquityDownDeviationPercent,
    decimal TotalPnl,
    decimal MaxDrawdownDollars,
    decimal AverageWinDollars,
    decimal AverageLossDollars,
    decimal Expectancy,
    int GovernorStops,
    string GovernorReason);
