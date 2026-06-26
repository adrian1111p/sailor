namespace Sailor.App.Backtest.Profiles;

public sealed record SailorStrategyProfile(
    string Name,
    decimal EntryMomentumPercent,
    decimal ExitMomentumPercent,
    decimal MinimumPrice,
    decimal MaximumPrice,
    long MinimumVolume,
    decimal MinimumVolumeRatio,
    bool RequirePriceAboveVwap,
    bool RequireEma9AboveSma20,
    bool RequirePriceAboveSma200WhenAvailable,
    int ScannerLookbackBars,
    int ScannerMinimumBars,
    int ScannerTopCount)
{
    public static SailorStrategyProfile CreateDefault()
    {
        return new SailorStrategyProfile(
            Name: "sailor-trend-volume",
            EntryMomentumPercent: 0.20m,
            ExitMomentumPercent: 0.20m,
            MinimumPrice: 0.50m,
            MaximumPrice: 300.00m,
            MinimumVolume: 100_000,
            MinimumVolumeRatio: 1.00m,
            RequirePriceAboveVwap: true,
            RequireEma9AboveSma20: true,
            RequirePriceAboveSma200WhenAvailable: true,
            ScannerLookbackBars: 20,
            ScannerMinimumBars: 20,
            ScannerTopCount: 20);
    }

    public static SailorStrategyProfile CreateSimpleMomentum()
    {
        SailorStrategyProfile defaultProfile = CreateDefault();

        return defaultProfile with
        {
            Name = "simple-momentum",
            MinimumVolume = 0,
            MinimumVolumeRatio = 0m,
            RequirePriceAboveVwap = false,
            RequireEma9AboveSma20 = false,
            RequirePriceAboveSma200WhenAvailable = false
        };
    }

    public static SailorStrategyProfile FromName(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return CreateDefault();
        }

        string normalized = profileName.Trim().ToLowerInvariant();

        return normalized switch
        {
            "sailor" => CreateDefault(),
            "sailor-trend" => CreateDefault(),
            "sailor-trend-volume" => CreateDefault(),
            "simple" => CreateSimpleMomentum(),
            "simple-momentum" => CreateSimpleMomentum(),
            _ => throw new ArgumentException(
                $"Unknown Sailor strategy profile '{profileName}'. Valid profiles: sailor-trend-volume, simple-momentum.")
        };
    }
}
