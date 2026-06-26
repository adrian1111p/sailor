namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V21_15Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct.AngleEma;

public sealed class V21_15MinutesConductStrategy : AngleEmaConductStrategyBase
{
    public V21_15MinutesConductStrategy()
        : base(new AngleEmaConductSettings(
            ProfileName: "v21-15minutes",
            StrategyName: "V21-15Minutes",
            VariantName: "default",
            CandleMinutes: 15,
            AngleThresholdDegrees: 12.00m,
            NeutralAngleDegrees: 12.00m,
            MinimumPrice: 0.50m,
            MaximumPrice: 1_000.00m,
            MinimumVolume: 50000,
            MinimumVolumeRatio: 0.70m,
            AllowShort: true,
            EmaPeriod: 9,
            MaxRecentRawBars: 1800))
    {
    }
}
