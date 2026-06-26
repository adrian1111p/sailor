namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V24_5Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct.AngleEma;

public sealed class V24_5MinutesConductStrategy : AngleEmaConductStrategyBase
{
    public V24_5MinutesConductStrategy()
        : base(new AngleEmaConductSettings(
            ProfileName: "v24-5minutes",
            StrategyName: "V24-5Minutes",
            VariantName: "default",
            CandleMinutes: 5,
            AngleThresholdDegrees: 12.00m,
            NeutralAngleDegrees: 12.00m,
            MinimumPrice: 0.50m,
            MaximumPrice: 1_000.00m,
            MinimumVolume: 65000,
            MinimumVolumeRatio: 0.85m,
            AllowShort: true,
            EmaPeriod: 9,
            MaxRecentRawBars: 600))
    {
    }
}
