namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V2_Conduct;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V2ConductFlowStrategy : SailorConductProfileStrategyBase
{
    public V2ConductFlowStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v2-conduct",
            strategyName: "V2-Conduct",
            variantName: "flow") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback,
            EntryMomentumPercent = 0.12m,
            MinimumVolume = 50000,
            MinimumVolumeRatio = 0.75m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = true,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.01m,
            MaximumVwapExtensionPercent = 2.4m,
            PullbackMaximumDistanceFromEmaPercent = 0.65m,
            BreakoutLookbackBars = 12,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.2m,
            ChoppyMaximumMomentumPercent = 0.7m
        })
    {
    }
}
