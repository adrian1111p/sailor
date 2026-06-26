namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V16_SqzBreakout;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V16SqzBreakoutConductStrategy : SailorConductProfileStrategyBase
{
    public V16SqzBreakoutConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v16-sqzbreakout",
            strategyName: "V16-SqzBreakout",
            variantName: "default") with
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
            MaximumVwapExtensionPercent = 2.8m,
            PullbackMaximumDistanceFromEmaPercent = 0.75m,
            BreakoutLookbackBars = 12,
            BreakoutBufferPercent = 0.06m,
            VwapReversionMaximumDistancePercent = 1.25m,
            ChoppyMaximumMomentumPercent = 0.7m
        })
    {
    }
}
