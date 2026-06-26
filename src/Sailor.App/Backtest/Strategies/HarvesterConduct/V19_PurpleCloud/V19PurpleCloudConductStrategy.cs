namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V19_PurpleCloud;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V19PurpleCloudConductStrategy : SailorConductProfileStrategyBase
{
    public V19PurpleCloudConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v19-purplecloud",
            strategyName: "V19-PurpleCloud",
            variantName: "retained-breakout") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Breakout,
            EntryMomentumPercent = 0.18m,
            MinimumVolume = 75000,
            MinimumVolumeRatio = 1.0m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = true,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.04m,
            MaximumVwapExtensionPercent = 2.7m,
            PullbackMaximumDistanceFromEmaPercent = 0.7m,
            BreakoutLookbackBars = 14,
            BreakoutBufferPercent = 0.05m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.7m
        })
    {
    }
}
