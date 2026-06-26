namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V18_Silver;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V18SilverConductStrategy : SailorConductProfileStrategyBase
{
    public V18SilverConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v18-silver",
            strategyName: "V18-Silver",
            variantName: "selective-short") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.VwapReversion | SailorConductEntryPattern.ChoppyShield,
            EntryMomentumPercent = 0.1m,
            MinimumVolume = 35000,
            MinimumVolumeRatio = 0.7m,
            RequireEma9AboveSma20 = false,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.0m,
            MaximumVwapExtensionPercent = 2.0m,
            PullbackMaximumDistanceFromEmaPercent = 0.75m,
            BreakoutLookbackBars = 10,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.0m,
            ChoppyMaximumMomentumPercent = 0.65m
        })
    {
    }
}
