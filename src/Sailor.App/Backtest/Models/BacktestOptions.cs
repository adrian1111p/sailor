using Sailor.App.Configuration;

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
        string? timeframe,
        string? profileName = null,
        SailorAppSettings? settings = null)
    {
        settings ??= new SailorAppSettings();
        BacktestRiskSettings risk = settings.Risk ?? new BacktestRiskSettings();

        return new BacktestOptions(
            Symbol: symbol.Trim().ToUpperInvariant(),
            Timeframe: string.IsNullOrWhiteSpace(timeframe) ? settings.DefaultTimeframe : timeframe.Trim(),
            ProfileName: string.IsNullOrWhiteSpace(profileName) ? settings.DefaultProfile : profileName.Trim(),
            InitialCash: risk.InitialCash,
            MaxPositionNotional: risk.MaxPositionNotional,
            StopLossPercent: risk.StopLossPercent,
            TakeProfitPercent: risk.TakeProfitPercent,
            MaxHoldBars: risk.MaxHoldBars);
    }
}
