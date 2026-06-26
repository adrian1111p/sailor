namespace Sailor.App.Backtest.Strategies.HarvesterConduct.ConductV3;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class ConductV3CatamaranStrategy : SailorConductProfileStrategyBase
{
    public ConductV3CatamaranStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "conduct-v3",
            strategyName: "Conduct-V3",
            variantName: "catamaran") with
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
