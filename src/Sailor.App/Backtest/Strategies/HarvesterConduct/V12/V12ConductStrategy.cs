namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V12;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V12ConductStrategy : SailorConductProfileStrategyBase
{
    public V12ConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v12",
            strategyName: "V12",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback | SailorConductEntryPattern.Breakout | SailorConductEntryPattern.VwapReversion,
            EntryMomentumPercent = 0.14m,
            MinimumVolume = 45000,
            MinimumVolumeRatio = 0.75m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = false,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.01m,
            MaximumVwapExtensionPercent = 2.6m,
            PullbackMaximumDistanceFromEmaPercent = 0.7m,
            BreakoutLookbackBars = 12,
            BreakoutBufferPercent = 0.05m,
            VwapReversionMaximumDistancePercent = 1.25m,
            ChoppyMaximumMomentumPercent = 0.75m
        })
    {
    }
}
