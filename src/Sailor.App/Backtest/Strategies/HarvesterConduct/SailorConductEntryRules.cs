namespace Sailor.App.Backtest.Strategies.HarvesterConduct;

[Flags]
public enum SailorConductEntryPattern
{
    None = 0,
    Momentum = 1,
    Breakout = 2,
    Pullback = 4,
    VwapReversion = 8,
    ChoppyShield = 16
}

public sealed record SailorConductEntryRules(
    string ProfileName,
    string StrategyName,
    string VariantName,
    SailorConductEntryPattern Patterns,
    decimal EntryMomentumPercent,
    decimal MinimumPrice,
    decimal MaximumPrice,
    long MinimumVolume,
    decimal MinimumVolumeRatio,
    bool RequireEma9AboveSma20,
    bool RequireCloseAboveVwap,
    bool RequireCloseAboveSma200WhenAvailable,
    bool RequireGreenBar,
    bool RequireCloseAbovePreviousHigh,
    decimal MinimumEmaSpreadPercent,
    decimal MaximumVwapExtensionPercent,
    decimal PullbackMaximumDistanceFromEmaPercent,
    int BreakoutLookbackBars,
    decimal BreakoutBufferPercent,
    decimal VwapReversionMaximumDistancePercent,
    decimal ChoppyMaximumMomentumPercent)
{
    public static SailorConductEntryRules CreateDefault(
        string profileName,
        string strategyName,
        string variantName)
    {
        return new SailorConductEntryRules(
            ProfileName: profileName,
            StrategyName: strategyName,
            VariantName: variantName,
            Patterns: SailorConductEntryPattern.Momentum,
            EntryMomentumPercent: 0.15m,
            MinimumPrice: 0.50m,
            MaximumPrice: 1_000.00m,
            MinimumVolume: 50_000,
            MinimumVolumeRatio: 0.75m,
            RequireEma9AboveSma20: true,
            RequireCloseAboveVwap: true,
            RequireCloseAboveSma200WhenAvailable: false,
            RequireGreenBar: true,
            RequireCloseAbovePreviousHigh: false,
            MinimumEmaSpreadPercent: 0.00m,
            MaximumVwapExtensionPercent: 3.00m,
            PullbackMaximumDistanceFromEmaPercent: 0.70m,
            BreakoutLookbackBars: 12,
            BreakoutBufferPercent: 0.05m,
            VwapReversionMaximumDistancePercent: 1.25m,
            ChoppyMaximumMomentumPercent: 0.75m);
    }
}
