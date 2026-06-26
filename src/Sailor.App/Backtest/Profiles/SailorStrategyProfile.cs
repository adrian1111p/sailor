using Sailor.App.Backtest.Strategies.HarvesterConduct;
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
    string ConductProfileName,
    bool UseMarketHours,
    int MarketOpenMinute,
    int SkipFirstMinutes,
    int LastEntryMinute,
    int ForceFlatMinute,
    int MinimumBarsBetweenEntries,
    bool UseNextBarOpenEntry,
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
            ConductProfileName: "default",
            UseMarketHours: false,
            MarketOpenMinute: 570,
            SkipFirstMinutes: 0,
            LastEntryMinute: 960,
            ForceFlatMinute: 960,
            MinimumBarsBetweenEntries: 0,
            UseNextBarOpenEntry: false,
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
            UseConductExits = false,
            ConductProfileName = "default"
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
            ConductProfileName = "sailor-conduct-v3",
            UseMarketHours = true,
            MarketOpenMinute = 570,
            SkipFirstMinutes = 5,
            LastEntryMinute = 930,
            ForceFlatMinute = 955,
            MinimumBarsBetweenEntries = 2,
            UseNextBarOpenEntry = true,
            ScannerLookbackBars = 20,
            ScannerMinimumBars = 20,
            ScannerTopCount = 20
        };
    }

    public static SailorStrategyProfile CreateHarvesterConductV3()
    {
        SailorStrategyProfile conduct = CreateConductV3();

        return conduct with
        {
            Name = "harvester-conduct-v3",
            EntryMomentumPercent = 0.12m,
            ExitMomentumPercent = 0.15m,
            MinimumVolume = 50_000,
            MinimumVolumeRatio = 0.75m,
            MaximumPrice = 1_000.00m,
            RequirePriceAboveVwap = true,
            RequireEma9AboveSma20 = true,
            RequirePriceAboveSma200WhenAvailable = false,
            ConductProfileName = "harvester-conduct-v3",
            MinimumBarsBetweenEntries = 2,
            ScannerTopCount = 20
        };
    }

    public static SailorStrategyProfile CreateHarvesterConductV9()
    {
        SailorStrategyProfile conduct = CreateConductV3();

        return conduct with
        {
            Name = "harvester-conduct-v9",
            EntryMomentumPercent = 0.18m,
            ExitMomentumPercent = 0.18m,
            MinimumVolume = 100_000,
            MinimumVolumeRatio = 1.00m,
            MaximumPrice = 1_000.00m,
            RequirePriceAboveVwap = true,
            RequireEma9AboveSma20 = true,
            RequirePriceAboveSma200WhenAvailable = false,
            ConductProfileName = "harvester-conduct-v9",
            SkipFirstMinutes = 5,
            LastEntryMinute = 930,
            ForceFlatMinute = 955,
            MinimumBarsBetweenEntries = 3,
            UseNextBarOpenEntry = true,
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
            "conduct" => CreateConductV3() with { Name = "conduct-v3", ConductProfileName = "conduct-v3" },
            "conduct-v3" => CreateConductV3() with { Name = "conduct-v3", ConductProfileName = "conduct-v3" },
            "sailor-conduct" => CreateConductV3(),
            "sailor-conduct-v3" => CreateConductV3(),
            "harvester-conduct" => CreateHarvesterConductV3(),
            "harvester-conduct-v3" => CreateHarvesterConductV3(),
            "harvester-v3" => CreateHarvesterConductV3(),
            "harvester-conduct-v9" => CreateHarvesterConductV9(),
            "harvester-v9" => CreateHarvesterConductV9(),
            _ when SailorConductStrategyRegistry.TryCreateBuiltInProfile(normalized, out SailorStrategyProfile conductProfile) => conductProfile,
            "simple" => CreateSimpleMomentum(),
            "simple-momentum" => CreateSimpleMomentum(),
            _ when TryGetConfiguredProfile(normalized, settings, out SailorStrategyProfile configuredOnlyProfile) => configuredOnlyProfile,
            _ => throw new ArgumentException(
                $"Unknown Sailor strategy profile '{profileName}'. Valid profiles include sailor-trend-volume, simple-momentum, conduct-v3, harvester-conduct-v3, harvester-conduct-v9, v21-15minutes, v23-5minutes, v24-5minutes, v22-15minutes, v16-sqzbreakout, v13, v10-hybrid, v17-hybridflow, v2-conduct, v18-silver, v1-first, v19-purplecloud, v15-shortcap, v14-smallcap, v20-gen001-choppyshield, v12, or a profile configured in appsettings.json. V11 is intentionally excluded.")
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
            ConductProfileName = string.IsNullOrWhiteSpace(settings.ConductProfileName) ? profile.ConductProfileName : settings.ConductProfileName.Trim(),
            UseMarketHours = settings.UseMarketHours ?? profile.UseMarketHours,
            MarketOpenMinute = settings.MarketOpenMinute ?? profile.MarketOpenMinute,
            SkipFirstMinutes = settings.SkipFirstMinutes ?? profile.SkipFirstMinutes,
            LastEntryMinute = settings.LastEntryMinute ?? profile.LastEntryMinute,
            ForceFlatMinute = settings.ForceFlatMinute ?? profile.ForceFlatMinute,
            MinimumBarsBetweenEntries = settings.MinimumBarsBetweenEntries ?? profile.MinimumBarsBetweenEntries,
            UseNextBarOpenEntry = settings.UseNextBarOpenEntry ?? profile.UseNextBarOpenEntry,
            ScannerLookbackBars = settings.ScannerLookbackBars ?? profile.ScannerLookbackBars,
            ScannerMinimumBars = settings.ScannerMinimumBars ?? profile.ScannerMinimumBars,
            ScannerTopCount = settings.ScannerTopCount ?? profile.ScannerTopCount
        };
    }
}
