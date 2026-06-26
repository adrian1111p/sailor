using Sailor.App.Configuration;

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
    bool UseConductExits,
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
            UseConductExits: false,
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
            RequirePriceAboveSma200WhenAvailable = false,
            UseConductExits = false
        };
    }

    public static SailorStrategyProfile CreateConductV3()
    {
        SailorStrategyProfile defaultProfile = CreateDefault();

        return defaultProfile with
        {
            Name = "sailor-conduct-v3",
            EntryMomentumPercent = 0.15m,
            ExitMomentumPercent = 0.15m,
            MinimumVolume = 50_000,
            MinimumVolumeRatio = 0.80m,
            RequirePriceAboveVwap = true,
            RequireEma9AboveSma20 = true,
            RequirePriceAboveSma200WhenAvailable = false,
            UseConductExits = true,
            ScannerLookbackBars = 20,
            ScannerMinimumBars = 20,
            ScannerTopCount = 20
        };
    }

    public static SailorStrategyProfile FromName(string? profileName)
    {
        return FromName(profileName, null);
    }

    public static SailorStrategyProfile FromName(string? profileName, SailorAppSettings? settings)
    {
        string requestedName = string.IsNullOrWhiteSpace(profileName)
            ? settings?.DefaultProfile ?? "sailor-trend-volume"
            : profileName.Trim();

        string normalized = requestedName.Trim().ToLowerInvariant();

        SailorStrategyProfile builtInProfile = normalized switch
        {
            "sailor" => CreateDefault(),
            "sailor-trend" => CreateDefault(),
            "sailor-trend-volume" => CreateDefault(),
            "conduct" => CreateConductV3(),
            "conduct-v3" => CreateConductV3(),
            "sailor-conduct" => CreateConductV3(),
            "sailor-conduct-v3" => CreateConductV3(),
            "simple" => CreateSimpleMomentum(),
            "simple-momentum" => CreateSimpleMomentum(),
            _ when TryGetConfiguredProfile(normalized, settings, out SailorStrategyProfile configuredOnlyProfile) => configuredOnlyProfile,
            _ => throw new ArgumentException(
                $"Unknown Sailor strategy profile '{profileName}'. Valid profiles: sailor-trend-volume, sailor-conduct-v3, simple-momentum, or a profile configured in appsettings.json.")
        };

        return ApplyConfiguredOverrides(builtInProfile, settings);
    }

    private static bool TryGetConfiguredProfile(
        string normalizedName,
        SailorAppSettings? settings,
        out SailorStrategyProfile profile)
    {
        profile = CreateDefault() with { Name = normalizedName };

        if (settings?.Profiles is null || settings.Profiles.Count == 0)
        {
            return false;
        }

        KeyValuePair<string, SailorProfileSettings> configuredProfile = settings.Profiles
            .FirstOrDefault(item => item.Key.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(configuredProfile.Key))
        {
            return false;
        }

        profile = ApplyConfiguredOverrides(
            CreateDefault() with { Name = configuredProfile.Key.Trim() },
            configuredProfile.Value);

        return true;
    }

    private static SailorStrategyProfile ApplyConfiguredOverrides(
        SailorStrategyProfile profile,
        SailorAppSettings? settings)
    {
        if (settings?.Profiles is null || settings.Profiles.Count == 0)
        {
            return profile;
        }

        KeyValuePair<string, SailorProfileSettings> configuredProfile = settings.Profiles
            .FirstOrDefault(item => item.Key.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(configuredProfile.Key)
            ? profile
            : ApplyConfiguredOverrides(profile, configuredProfile.Value);
    }

    private static SailorStrategyProfile ApplyConfiguredOverrides(
        SailorStrategyProfile profile,
        SailorProfileSettings? settings)
    {
        if (settings is null)
        {
            return profile;
        }

        return profile with
        {
            EntryMomentumPercent = settings.EntryMomentumPercent ?? profile.EntryMomentumPercent,
            ExitMomentumPercent = settings.ExitMomentumPercent ?? profile.ExitMomentumPercent,
            MinimumPrice = settings.MinimumPrice ?? profile.MinimumPrice,
            MaximumPrice = settings.MaximumPrice ?? profile.MaximumPrice,
            MinimumVolume = settings.MinimumVolume ?? profile.MinimumVolume,
            MinimumVolumeRatio = settings.MinimumVolumeRatio ?? profile.MinimumVolumeRatio,
            RequirePriceAboveVwap = settings.RequirePriceAboveVwap ?? profile.RequirePriceAboveVwap,
            RequireEma9AboveSma20 = settings.RequireEma9AboveSma20 ?? profile.RequireEma9AboveSma20,
            RequirePriceAboveSma200WhenAvailable = settings.RequirePriceAboveSma200WhenAvailable ?? profile.RequirePriceAboveSma200WhenAvailable,
            UseConductExits = settings.UseConductExits ?? profile.UseConductExits,
            ScannerLookbackBars = settings.ScannerLookbackBars ?? profile.ScannerLookbackBars,
            ScannerMinimumBars = settings.ScannerMinimumBars ?? profile.ScannerMinimumBars,
            ScannerTopCount = settings.ScannerTopCount ?? profile.ScannerTopCount
        };
    }
}
