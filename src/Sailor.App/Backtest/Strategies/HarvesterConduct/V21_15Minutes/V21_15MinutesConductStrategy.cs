namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V21_15Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V21_15MinutesConductStrategy : SailorConductProfileStrategyBase
{
    public V21_15MinutesConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v21-15minutes",
            strategyName: "V21-15Minutes",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback,
            EntryMomentumPercent = 0.1m,
            MinimumVolume = 50000,
            MinimumVolumeRatio = 0.7m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = true,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.02m,
            MaximumVwapExtensionPercent = 2.2m,
            PullbackMaximumDistanceFromEmaPercent = 0.55m,
            BreakoutLookbackBars = 14,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.7m
        })
    {
    }
}
