namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V15_ShortCap;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V15ShortCapConductStrategy : SailorConductProfileStrategyBase
{
    public V15ShortCapConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v15-shortcap",
            strategyName: "V15-ShortCap",
            variantName: "retained-breakdown") with
        {
            Patterns = SailorConductEntryPattern.VwapReversion | SailorConductEntryPattern.ChoppyShield,
            EntryMomentumPercent = 0.1m,
            MinimumVolume = 25000,
            MinimumVolumeRatio = 0.8m,
            RequireEma9AboveSma20 = false,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.0m,
            MaximumVwapExtensionPercent = 1.8m,
            PullbackMaximumDistanceFromEmaPercent = 0.7m,
            BreakoutLookbackBars = 10,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.0m,
            ChoppyMaximumMomentumPercent = 0.55m
        })
    {
    }
}
