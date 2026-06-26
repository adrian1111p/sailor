namespace Sailor.App.Backtest.Strategies.HarvesterConduct.V24_5Minutes;

using Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed class V24_5MinutesConductStrategy : SailorConductProfileStrategyBase
{
    public V24_5MinutesConductStrategy()
        : base(SailorConductEntryRules.CreateDefault(
            profileName: "v24-5minutes",
            strategyName: "V24-5Minutes",
            variantName: "default") with
        {
            Patterns = SailorConductEntryPattern.Momentum | SailorConductEntryPattern.Pullback | SailorConductEntryPattern.Breakout,
            EntryMomentumPercent = 0.16m,
            MinimumVolume = 65000,
            MinimumVolumeRatio = 0.85m,
            RequireEma9AboveSma20 = true,
            RequireCloseAboveVwap = true,
            RequireCloseAboveSma200WhenAvailable = false,
            RequireGreenBar = true,
            MinimumEmaSpreadPercent = 0.03m,
            MaximumVwapExtensionPercent = 2.0m,
            PullbackMaximumDistanceFromEmaPercent = 0.55m,
            BreakoutLookbackBars = 10,
            BreakoutBufferPercent = 0.04m,
            VwapReversionMaximumDistancePercent = 1.15m,
            ChoppyMaximumMomentumPercent = 0.65m
        })
    {
    }
}
