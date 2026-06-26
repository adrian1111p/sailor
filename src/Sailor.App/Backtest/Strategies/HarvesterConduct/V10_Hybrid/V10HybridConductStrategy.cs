namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V10_Hybrid;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V10HybridConductStrategy : SailorConductProfileStrategyBase
{
    public V10HybridConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v10-hybrid",
            strategyName: "V10-Hybrid",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback | SailorConductEntryPattern.VwapReversion,
            EntryMomentumPercent = 0.12m,
            MinimumVolume = 40000,
            MinimumVolumeRatio = 0.7m,
            RequireEma9AboveSma20 = false,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.0m,
            MaximumVwapExtensionPercent = 3.0m,
            PullbackMaximumDistanceFromEmaPercent = 0.75m,
            BreakoutLookbackBars = 12,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.1m,
            ChoppyMaximumMomentumPercent = 0.8m
        })
    {
    }
}
