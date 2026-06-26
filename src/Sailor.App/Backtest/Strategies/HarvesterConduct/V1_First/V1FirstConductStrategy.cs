namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V1_First;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V1FirstConductStrategy : SailorConductProfileStrategyBase
{
    public V1FirstConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v1-first",
            strategyName: "V1-First",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum,
            EntryMomentumPercent = 0.2m,
            MinimumVolume = 30000,
            MinimumVolumeRatio = 0.6m,
            RequireEma9AboveSma20 = false,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = false,
            MinimumEmaSpreadPercent = 0.0m,
            MaximumVwapExtensionPercent = 3.5m,
            PullbackMaximumDistanceFromEmaPercent = 0.9m,
            BreakoutLookbackBars = 10,
            BreakoutBufferPercent = 0.05m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.9m
        })
    {
    }
}
