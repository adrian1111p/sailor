namespace Sailor.App.Backtest.Reports;

public sealed record StrategyReportDefinition(
    string Strategy,
    string ProfileName,
    string Variant,
    string Style);
