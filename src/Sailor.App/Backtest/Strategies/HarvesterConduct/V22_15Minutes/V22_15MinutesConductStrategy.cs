namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V22_15Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct.AngleEma;

public sealed class V22_15MinutesConductStrategy : AngleEmaConductStrategyBase
{
    public V22_15MinutesConductStrategy()
        : base(new AngleEmaConductSettings(
            ProfileName: "v22-15minutes",
            StrategyName: "V22-15Minutes",
            VariantName: "default",
            CandleMinutes: 15,
            AngleThresholdDegrees: 8.50m,
            NeutralAngleDegrees: 8.50m,
            MinimumPrice: 0.50m,
            MaximumPrice: 1_000.00m,
            MinimumVolume: 50000,
            MinimumVolumeRatio: 0.75m,
            AllowShort: true,
            EmaPeriod: 9,
            MaxRecentRawBars: 1800,
            MinimumCompletedCandlesForSignal: 12))
    {
    }
}
