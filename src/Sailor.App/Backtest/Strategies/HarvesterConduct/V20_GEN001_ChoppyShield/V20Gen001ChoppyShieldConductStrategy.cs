namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V20_GEN001_ChoppyShield;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V20Gen001ChoppyShieldConductStrategy : SailorConductProfileStrategyBase
{
    public V20Gen001ChoppyShieldConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v20-gen001-choppyshield",
            strategyName: "V20-GEN001-ChoppyShield",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.ChoppyShield,
            EntryMomentumPercent = 0.08m,
            MinimumVolume = 35000,
            MinimumVolumeRatio = 0.7m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.0m,
            MaximumVwapExtensionPercent = 1.8m,
            PullbackMaximumDistanceFromEmaPercent = 0.65m,
            BreakoutLookbackBars = 8,
            BreakoutBufferPercent = 0.03m,
            VwapReversionMaximumDistancePercent = 1.1m,
            ChoppyMaximumMomentumPercent = 0.45m
        })
    {
    }
}
