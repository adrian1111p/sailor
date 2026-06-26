namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V13;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V13ConductStrategy : SailorConductProfileStrategyBase
{
    public V13ConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v13",
            strategyName: "V13",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback | SailorConductEntryPattern.Breakout,
            EntryMomentumPercent = 0.15m,
            MinimumVolume = 50000,
            MinimumVolumeRatio = 0.8m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = true,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.02m,
            MaximumVwapExtensionPercent = 2.6m,
            PullbackMaximumDistanceFromEmaPercent = 0.6m,
            BreakoutLookbackBars = 12,
            BreakoutBufferPercent = 0.05m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.7m
        })
    {
    }
}
