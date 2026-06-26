namespace Sailor.App.Backtest.Models;

public sealed record BacktestRunResult(
    string Symbol,
    string Timeframe,
    string ProfileName,
    string StrategyName,
    int Bars,
    int TotalTrades,
    int Winners,
    int Losers,
    decimal TotalPnl,
    decimal FinalCash,
    decimal? LastEma9,
    decimal? LastSma20,
    decimal? LastSma200,
    decimal? LastVwap,
    decimal? LastVolumeAverage20,
    string DataSourcePath,
    string LogFilePath,
    IReadOnlyList<BacktestTrade> Trades)
{
    public decimal WinRatePercent => TotalTrades > 0
        ? (decimal)Winners / TotalTrades * 100m
        : 0m;

    public decimal ReturnPercent => FinalCash > 0m
        ? TotalPnl / (FinalCash - TotalPnl == 0m ? FinalCash : FinalCash - TotalPnl) * 100m
        : 0m;
}
