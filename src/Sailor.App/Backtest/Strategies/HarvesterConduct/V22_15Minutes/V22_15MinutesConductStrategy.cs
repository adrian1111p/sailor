namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V22_15Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V22_15MinutesConductStrategy : SailorConductProfileStrategyBase
{
    public V22_15MinutesConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v22-15minutes",
            strategyName: "V22-15Minutes",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback,
            EntryMomentumPercent = 0.12m,
            MinimumVolume = 50000,
            MinimumVolumeRatio = 0.75m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = true,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.02m,
            MaximumVwapExtensionPercent = 2.25m,
            PullbackMaximumDistanceFromEmaPercent = 0.6m,
            BreakoutLookbackBars = 14,
            BreakoutBufferPercent = 0.05m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.7m
        })
    {
    }
}
