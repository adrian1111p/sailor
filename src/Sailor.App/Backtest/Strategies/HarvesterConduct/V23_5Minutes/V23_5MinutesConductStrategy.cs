namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V23_5Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct.AngleEma;

public sealed class V23_5MinutesConductStrategy : AngleEmaConductStrategyBase
{
    public V23_5MinutesConductStrategy()
        : base(new AngleEmaConductSettings(
            ProfileName: "v23-5minutes",
            StrategyName: "V23-5Minutes",
            VariantName: "default",
            CandleMinutes: 5,
            AngleThresholdDegrees: 12.00m,
            NeutralAngleDegrees: 12.00m,
            MinimumPrice: 0.50m,
            MaximumPrice: 1_000.00m,
            MinimumVolume: 60000,
            MinimumVolumeRatio: 0.80m,
            AllowShort: true,
            EmaPeriod: 9,
            MaxRecentRawBars: 600,
            MinimumCompletedCandlesForSignal: 9))
    {
    }
}
