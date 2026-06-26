namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V17_HybridFlow;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V17HybridFlowConductStrategy : SailorConductProfileStrategyBase
{
    public V17HybridFlowConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v17-hybridflow",
            strategyName: "V17-HybridFlow",
            variantName: "legacy-default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback | SailorConductEntryPattern.ChoppyShield,
            EntryMomentumPercent = 0.12m,
            MinimumVolume = 40000,
            MinimumVolumeRatio = 0.65m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.0m,
            MaximumVwapExtensionPercent = 2.8m,
            PullbackMaximumDistanceFromEmaPercent = 0.7m,
            BreakoutLookbackBars = 12,
            BreakoutBufferPercent = 0.05m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.75m
        })
    {
    }
}
