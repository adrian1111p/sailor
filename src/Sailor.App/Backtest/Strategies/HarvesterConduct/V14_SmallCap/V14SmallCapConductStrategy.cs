namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V14_SmallCap;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V14SmallCapConductStrategy : SailorConductProfileStrategyBase
{
    public V14SmallCapConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v14-smallcap",
            strategyName: "V14-SmallCap",
            variantName: "baseline") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback | SailorConductEntryPattern.VwapReversion,
            EntryMomentumPercent = 0.1m,
            MinimumVolume = 20000,
            MinimumVolumeRatio = 0.65m,
            RequireEma9AboveSma20 = false,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.0m,
            MaximumVwapExtensionPercent = 2.5m,
            PullbackMaximumDistanceFromEmaPercent = 0.85m,
            BreakoutLookbackBars = 10,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.3m,
            ChoppyMaximumMomentumPercent = 0.75m
        })
    {
    }
}
