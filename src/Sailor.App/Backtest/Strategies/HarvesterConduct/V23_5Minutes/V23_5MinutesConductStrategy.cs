namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V23_5Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V23_5MinutesConductStrategy : SailorConductProfileStrategyBase
{
    public V23_5MinutesConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v23-5minutes",
            strategyName: "V23-5Minutes",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Breakout,
            EntryMomentumPercent = 0.14m,
            MinimumVolume = 60000,
            MinimumVolumeRatio = 0.8m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = true,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.03m,
            MaximumVwapExtensionPercent = 2.5m,
            PullbackMaximumDistanceFromEmaPercent = 0.65m,
            BreakoutLookbackBars = 10,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.7m
        })
    {
    }
}
