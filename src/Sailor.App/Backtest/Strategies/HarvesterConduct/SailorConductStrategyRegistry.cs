using Sailor.App.Backtest.Strategies.HarvesterConduct.ConductV3;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V10_Hybrid;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V12;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V13;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V14_SmallCap;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V15_ShortCap;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V16_SqzBreakout;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V17_HybridFlow;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V18_Silver;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V19_PurpleCloud;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V1_First;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V20_GEN001_ChoppyShield;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V21_15Minutes;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V22_15Minutes;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V23_5Minutes;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V24_5Minutes;
using Sailor.App.Backtest.Strategies.HarvesterConduct.V2_Conduct;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Configuration;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct;

public static class SailorConductStrategyRegistry
{
    public static IReadOnlyList<string> SupportedProfileNames =>
    [
        "v21-15minutes",
        "v23-5minutes",
        "v24-5minutes",
        "v22-15minutes",
        "v16-sqzbreakout",
        "v13",
        "v10-hybrid",
        "v17-hybridflow",
        "v2-conduct",
        "v18-silver",
        "v1-first",
        "conduct-v3",
        "v19-purplecloud",
        "v15-shortcap",
        "v14-smallcap",
        "v20-gen001-choppyshield",
        "v12"
    ];

    public static bool IsSupported(string? profileName)
    {
        return TryNormalize(profileName, out _);
    }

    public static ISailorConductEntryStrategy CreateEntryStrategy(SailorStrategyProfile profile)
    {
        string normalized = NormalizeOrDefault(profile.Name);

        return normalized switch
        {
            "v21-15minutes" => new V21_15MinutesConductStrategy(),
            "v23-5minutes" => new V23_5MinutesConductStrategy(),
            "v24-5minutes" => new V24_5MinutesConductStrategy(),
            "v22-15minutes" => new V22_15MinutesConductStrategy(),
            "v16-sqzbreakout" => new V16SqzBreakoutConductStrategy(),
            "v13" => new V13ConductStrategy(),
            "v10-hybrid" => new V10HybridConductStrategy(),
            "v17-hybridflow" => new V17HybridFlowConductStrategy(),
            "v2-conduct" => new V2ConductFlowStrategy(),
            "v18-silver" => new V18SilverConductStrategy(),
            "v1-first" => new V1FirstConductStrategy(),
            "conduct-v3" => new ConductV3CatamaranStrategy(),
            "sailor-conduct-v3" => new ConductV3CatamaranStrategy(),
            "harvester-conduct-v3" => new ConductV3CatamaranStrategy(),
            "v19-purplecloud" => new V19PurpleCloudConductStrategy(),
            "v15-shortcap" => new V15ShortCapConductStrategy(),
            "v14-smallcap" => new V14SmallCapConductStrategy(),
            "v20-gen001-choppyshield" => new V20Gen001ChoppyShieldConductStrategy(),
            "v12" => new V12ConductStrategy(),
            "harvester-conduct-v9" => new V24_5MinutesConductStrategy(),
            _ => new ConductV3CatamaranStrategy()
        };
    }

    public static bool TryCreateBuiltInProfile(string? profileName, out SailorStrategyProfile profile)
    {
        profile = SailorStrategyProfile.CreateDefault();

        if (!TryNormalize(profileName, out string normalized))
        {
            return false;
        }

        profile = CreateBuiltInProfile(normalized);
        return true;
    }

    public static bool TryCreateDefaultExitSettings(string? profileName, out ConductExitSettings settings)
    {
        settings = new ConductExitSettings();

        if (!TryNormalize(profileName, out string normalized))
        {
            return false;
        }

        settings = normalized switch
        {
            "v21-15minutes" => CreateExit(0.85m, 0.45m, 0.55m, 0.25m, 30.00m, 5, false, 0.40m, 0.20m, 80),
            "v23-5minutes" => CreateExit(0.85m, 0.40m, 0.45m, 0.25m, 30.00m, 4, true, 0.35m, 0.18m, 40),
            "v24-5minutes" => CreateExit(0.90m, 0.45m, 0.50m, 0.28m, 30.00m, 4, true, 0.40m, 0.20m, 55),
            "v22-15minutes" => CreateExit(0.90m, 0.45m, 0.55m, 0.30m, 30.00m, 5, false, 0.40m, 0.20m, 80),
            "v16-sqzbreakout" => CreateExit(0.95m, 0.45m, 0.45m, 0.30m, 30.00m, 4, true, 0.35m, 0.18m, 45),
            "v13" => CreateExit(0.90m, 0.40m, 0.55m, 0.30m, 30.00m, 4, false, 0.40m, 0.20m, 50),
            "v10-hybrid" => CreateExit(0.95m, 0.45m, 0.65m, 0.35m, 30.00m, 3, false, 0.40m, 0.20m, 45),
            "v17-hybridflow" => CreateExit(1.00m, 0.55m, 0.70m, 0.35m, 30.00m, 4, false, 0.40m, 0.20m, 50),
            "v2-conduct" => CreateExit(0.95m, 0.50m, 0.70m, 0.35m, 30.00m, 4, false, 0.40m, 0.20m, 50),
            "v18-silver" => CreateExit(0.80m, 0.35m, 0.45m, 0.25m, 30.00m, 3, true, 0.30m, 0.18m, 35),
            "v1-first" => CreateExit(1.25m, 0.80m, 1.00m, 0.45m, 30.00m, 3, false, 0.40m, 0.20m, 30),
            "conduct-v3" or "sailor-conduct-v3" or "harvester-conduct-v3" => CreateExit(1.00m, 0.55m, 0.70m, 0.35m, 30.00m, 4, false, 0.40m, 0.20m, 50),
            "v19-purplecloud" => CreateExit(0.95m, 0.45m, 0.50m, 0.30m, 30.00m, 4, true, 0.35m, 0.18m, 45),
            "v15-shortcap" => CreateExit(0.85m, 0.35m, 0.45m, 0.25m, 30.00m, 3, true, 0.30m, 0.18m, 35),
            "v14-smallcap" => CreateExit(1.10m, 0.35m, 0.65m, 0.35m, 30.00m, 3, false, 0.40m, 0.20m, 50),
            "v20-gen001-choppyshield" => CreateExit(0.75m, 0.30m, 0.40m, 0.22m, 30.00m, 3, true, 0.25m, 0.15m, 25),
            "v12" => CreateExit(0.90m, 0.40m, 0.55m, 0.30m, 30.00m, 3, false, 0.40m, 0.20m, 45),
            "harvester-conduct-v9" => CreateExit(1.00m, 0.55m, 0.45m, 0.35m, 30.00m, 5, true, 0.40m, 0.20m, 50),
            _ => new ConductExitSettings()
        };

        return true;
    }

    private static SailorStrategyProfile CreateBuiltInProfile(string normalized)
    {
        return normalized switch
        {
            "v21-15minutes" => CreateProfile(normalized, 0.10m, 0.12m, 50_000, 0.70m, false, false, false, 0, 955, 955, 20, 40, 20) with { UseConductExits = false },
            "v23-5minutes" => CreateProfile(normalized, 0.14m, 0.14m, 60_000, 0.80m, false, false, false, 0, 955, 955, 18, 35, 20) with { UseConductExits = false },
            "v24-5minutes" => CreateProfile(normalized, 0.16m, 0.14m, 65_000, 0.85m, false, false, false, 0, 955, 955, 18, 35, 20) with { UseConductExits = false },
            "v22-15minutes" => CreateProfile(normalized, 0.12m, 0.14m, 50_000, 0.75m, false, false, false, 0, 955, 955, 20, 40, 20) with { UseConductExits = false },
            "v16-sqzbreakout" => CreateProfile(normalized, 0.18m, 0.18m, 75_000, 1.00m, true, true, false, 3, 940, 955, 20, 40, 20),
            "v13" => CreateProfile(normalized, 0.15m, 0.16m, 50_000, 0.80m, true, true, false, 2, 940, 955, 20, 40, 20),
            "v10-hybrid" => CreateProfile(normalized, 0.12m, 0.16m, 40_000, 0.70m, false, false, false, 3, 940, 955, 20, 40, 20),
            "v17-hybridflow" => CreateProfile(normalized, 0.12m, 0.18m, 40_000, 0.65m, true, false, false, 3, 940, 955, 20, 40, 20),
            "v2-conduct" => CreateProfile(normalized, 0.12m, 0.15m, 50_000, 0.75m, true, true, false, 2, 945, 955, 20, 40, 20),
            "v18-silver" => CreateProfile(normalized, 0.10m, 0.15m, 35_000, 0.70m, false, false, false, 3, 940, 955, 20, 35, 20),
            "v1-first" => CreateProfile(normalized, 0.20m, 0.20m, 30_000, 0.60m, false, false, false, 2, 930, 955, 20, 30, 20),
            "conduct-v3" or "sailor-conduct-v3" or "harvester-conduct-v3" => CreateProfile(normalized, 0.12m, 0.15m, 50_000, 0.75m, true, true, false, 2, 945, 955, 20, 40, 20) with { ConductProfileName = normalized },
            "v19-purplecloud" => CreateProfile(normalized, 0.18m, 0.18m, 75_000, 1.00m, true, true, false, 3, 940, 955, 20, 40, 20),
            "v15-shortcap" => CreateProfile(normalized, 0.10m, 0.15m, 25_000, 0.80m, false, false, false, 3, 940, 955, 20, 35, 20),
            "v14-smallcap" => CreateProfile(normalized, 0.10m, 0.16m, 20_000, 0.65m, false, false, false, 2, 945, 955, 20, 40, 20),
            "v20-gen001-choppyshield" => CreateProfile(normalized, 0.08m, 0.12m, 35_000, 0.70m, true, false, false, 3, 930, 955, 20, 30, 20),
            "v12" => CreateProfile(normalized, 0.14m, 0.16m, 45_000, 0.75m, true, false, false, 2, 940, 955, 20, 40, 20),
            "harvester-conduct-v9" => CreateProfile(normalized, 0.18m, 0.18m, 100_000, 1.00m, true, true, false, 3, 930, 955, 20, 40, 20),
            _ => CreateProfile(normalized, 0.12m, 0.15m, 50_000, 0.75m, true, true, false, 2, 945, 955, 20, 40, 20)
        };
    }

    private static SailorStrategyProfile CreateProfile(
        string name,
        decimal entryMomentumPercent,
        decimal exitMomentumPercent,
        long minimumVolume,
        decimal minimumVolumeRatio,
        bool requireEma9AboveSma20,
        bool requirePriceAboveVwap,
        bool requirePriceAboveSma200WhenAvailable,
        int minimumBarsBetweenEntries,
        int lastEntryMinute,
        int forceFlatMinute,
        int scannerLookbackBars,
        int scannerMinimumBars,
        int scannerTopCount)
    {
        return SailorStrategyProfile.CreateDefault() with
        {
            Name = name,
            EntryMomentumPercent = entryMomentumPercent,
            ExitMomentumPercent = exitMomentumPercent,
            MinimumPrice = 0.50m,
            MaximumPrice = 1_000.00m,
            MinimumVolume = minimumVolume,
            MinimumVolumeRatio = minimumVolumeRatio,
            RequireEma9AboveSma20 = requireEma9AboveSma20,
            RequirePriceAboveVwap = requirePriceAboveVwap,
            RequirePriceAboveSma200WhenAvailable = requirePriceAboveSma200WhenAvailable,
            UseConductExits = true,
            ConductProfileName = name,
            UseMarketHours = true,
            MarketOpenMinute = 570,
            SkipFirstMinutes = 5,
            LastEntryMinute = lastEntryMinute,
            ForceFlatMinute = forceFlatMinute,
            MinimumBarsBetweenEntries = minimumBarsBetweenEntries,
            UseNextBarOpenEntry = true,
            ScannerLookbackBars = scannerLookbackBars,
            ScannerMinimumBars = scannerMinimumBars,
            ScannerTopCount = scannerTopCount
        };
    }

    private static ConductExitSettings CreateExit(
        decimal hardStopPercent,
        decimal breakevenAfterPercent,
        decimal trailingAfterPercent,
        decimal givebackPercent,
        decimal givebackNotionalCap,
        int minimumBarsBeforeIndicatorExit,
        bool useMicroTrail,
        decimal microTrailActivatePercent,
        decimal microTrailPercent,
        int maxHoldBars)
    {
        return new ConductExitSettings
        {
            HardStopPercent = hardStopPercent,
            UseTakeProfitExit = false,
            TakeProfitPercent = 2.00m,
            MoveStopToBreakevenAfterPercent = breakevenAfterPercent,
            BreakevenBufferPercent = 0.05m,
            StartTrailingAfterPercent = trailingAfterPercent,
            GivebackPercent = givebackPercent,
            GivebackNotionalCap = givebackNotionalCap,
            MinimumBarsBeforeIndicatorExit = minimumBarsBeforeIndicatorExit,
            UseEma9Exit = true,
            UseVwapExit = true,
            UseTrendExit = true,
            UseOppositeMomentumExit = true,
            UseMicroTrail = useMicroTrail,
            MicroTrailActivatePercent = microTrailActivatePercent,
            MicroTrailPercent = microTrailPercent,
            MaxHoldBars = maxHoldBars
        };
    }

    private static bool TryNormalize(string? profileName, out string normalized)
    {
        normalized = Normalize(profileName);
        return normalized.Length > 0 &&
               (SupportedProfileNames.Contains(normalized, StringComparer.OrdinalIgnoreCase) ||
                normalized is "sailor-conduct-v3" or "harvester-conduct-v3" or "harvester-conduct-v9" or "harvester-v3" or "harvester-v9");
    }

    private static string NormalizeOrDefault(string? profileName)
    {
        return TryNormalize(profileName, out string normalized)
            ? normalized
            : "conduct-v3";
    }

    private static string Normalize(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return string.Empty;
        }

        string normalized = profileName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "v21" => "v21-15minutes",
            "v23" => "v23-5minutes",
            "v24" => "v24-5minutes",
            "v22" => "v22-15minutes",
            "v16" => "v16-sqzbreakout",
            "v10" => "v10-hybrid",
            "v17" => "v17-hybridflow",
            "v2" => "v2-conduct",
            "v18" => "v18-silver",
            "v1" => "v1-first",
            "v19" => "v19-purplecloud",
            "v15" => "v15-shortcap",
            "v14" => "v14-smallcap",
            "v20" => "v20-gen001-choppyshield",
            "conduct" => "conduct-v3",
            "catamaran" => "conduct-v3",
            "harvester-v3" => "harvester-conduct-v3",
            "harvester-v9" => "harvester-conduct-v9",
            _ => normalized
        };
    }
}
