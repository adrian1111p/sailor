using System.Text.Json;

namespace Sailor.App.Configuration;

public static class SailorSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static SailorAppSettings Load(string? explicitPath = null)
    {
        string? path = ResolveSettingsPath(explicitPath);
        if (path is null)
        {
            return new SailorAppSettings();
        }

        try
        {
            string json = File.ReadAllText(path);
            SailorSettingsRoot? root = JsonSerializer.Deserialize<SailorSettingsRoot>(json, JsonOptions);
            return Normalize(root?.Sailor ?? new SailorAppSettings());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: could not read Sailor settings. Using defaults. Error: {ex.Message}");
            return new SailorAppSettings();
        }
    }

    private static string? ResolveSettingsPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string fullExplicitPath = Path.GetFullPath(explicitPath.Trim());
            return File.Exists(fullExplicitPath) ? fullExplicitPath : null;
        }

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "appsettings.json")),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Sailor.App", "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static SailorAppSettings Normalize(SailorAppSettings settings)
    {
        settings.DefaultTimeframe = string.IsNullOrWhiteSpace(settings.DefaultTimeframe)
            ? "1m"
            : settings.DefaultTimeframe.Trim();

        settings.DefaultProfile = string.IsNullOrWhiteSpace(settings.DefaultProfile)
            ? "sailor-trend-volume"
            : settings.DefaultProfile.Trim();

        settings.DefaultUniverse = string.IsNullOrWhiteSpace(settings.DefaultUniverse)
            ? "all"
            : settings.DefaultUniverse.Trim();

        settings.Risk ??= new BacktestRiskSettings();
        settings.Conduct ??= new ConductExitSettings();
        settings.L1L2 ??= new L1L2SnapshotSettings();
        settings.Runtime ??= new SailorRuntimeSettings();
        settings.ConductProfiles ??= new Dictionary<string, ConductExitSettings>(StringComparer.OrdinalIgnoreCase);
        settings.Scanner ??= new ScannerSettings();
        settings.StrategyLifecyclePolicies = NormalizeStrategyLifecyclePolicies(settings.StrategyLifecyclePolicies);
        settings.Profiles ??= new Dictionary<string, SailorProfileSettings>(StringComparer.OrdinalIgnoreCase);

        NormalizeRisk(settings.Risk);
        NormalizeConduct(settings.Conduct, settings.Risk);

        foreach (ConductExitSettings conductProfile in settings.ConductProfiles.Values)
        {
            NormalizeConduct(conductProfile, settings.Risk);
        }

        if (settings.Scanner.DefaultTopCount <= 0)
        {
            settings.Scanner.DefaultTopCount = 20;
        }

        if (settings.Scanner.PointsMinimumTradeScore <= 0m)
        {
            settings.Scanner.PointsMinimumTradeScore = 45m;
        }

        if (settings.L1L2.DepthLevels <= 0)
        {
            settings.L1L2.DepthLevels = 5;
        }

        if (settings.Runtime.Paper.Port <= 0)
        {
            settings.Runtime.Paper.Port = 7497;
        }

        if (settings.Runtime.Live.Port <= 0)
        {
            settings.Runtime.Live.Port = 7496;
        }

        if (settings.Runtime.Paper.ConnectTimeoutSeconds <= 0)
        {
            settings.Runtime.Paper.ConnectTimeoutSeconds = 10;
        }

        if (settings.Runtime.Live.ConnectTimeoutSeconds <= 0)
        {
            settings.Runtime.Live.ConnectTimeoutSeconds = 10;
        }

        return settings;
    }


    private static Dictionary<string, string> NormalizeStrategyLifecyclePolicies(Dictionary<string, string>? configuredPolicies)
    {
        var policies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = "SingleLifecycleUntilStrategyExit",
            ["v21-15minutes"] = "MultiCycleUntilLastEntryMinute",
            ["v22-15minutes"] = "MultiCycleUntilLastEntryMinute",
            ["v23-5minutes"] = "MultiCycleUntilLastEntryMinute",
            ["v24-5minutes"] = "MultiCycleUntilLastEntryMinute"
        };

        if (configuredPolicies is null)
        {
            return policies;
        }

        foreach (KeyValuePair<string, string> pair in configuredPolicies)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            policies[pair.Key.Trim()] = pair.Value.Trim();
        }

        return policies;
    }

    private static void NormalizeRisk(BacktestRiskSettings risk)
    {
        if (risk.InitialCash <= 0m)
        {
            risk.InitialCash = 10_000.00m;
        }

        if (risk.MaxPositionNotional <= 0m)
        {
            risk.MaxPositionNotional = 1_000.00m;
        }

        if (risk.StopLossPercent <= 0m)
        {
            risk.StopLossPercent = 1.00m;
        }

        if (risk.TakeProfitPercent <= 0m)
        {
            risk.TakeProfitPercent = 2.00m;
        }

        if (risk.MaxHoldBars <= 0)
        {
            risk.MaxHoldBars = 30;
        }
    }

    private static void NormalizeConduct(ConductExitSettings conduct, BacktestRiskSettings risk)
    {
        if (conduct.HardStopPercent <= 0m)
        {
            conduct.HardStopPercent = risk.StopLossPercent;
        }

        if (conduct.TakeProfitPercent <= 0m)
        {
            conduct.TakeProfitPercent = risk.TakeProfitPercent;
        }

        if (conduct.MoveStopToBreakevenAfterPercent < 0m)
        {
            conduct.MoveStopToBreakevenAfterPercent = 0m;
        }

        if (conduct.BreakevenBufferPercent < 0m)
        {
            conduct.BreakevenBufferPercent = 0m;
        }

        if (conduct.StartTrailingAfterPercent < 0m)
        {
            conduct.StartTrailingAfterPercent = 0m;
        }

        if (conduct.GivebackPercent <= 0m)
        {
            conduct.GivebackPercent = 0.35m;
        }

        if (conduct.GivebackNotionalCap < 0m)
        {
            conduct.GivebackNotionalCap = 0m;
        }

        if (conduct.MinimumBarsBeforeIndicatorExit < 0)
        {
            conduct.MinimumBarsBeforeIndicatorExit = 0;
        }

        if (conduct.MicroTrailActivatePercent < 0m)
        {
            conduct.MicroTrailActivatePercent = 0m;
        }

        if (conduct.MicroTrailPercent <= 0m)
        {
            conduct.MicroTrailPercent = 0.20m;
        }

        if (conduct.MaxHoldBars <= 0)
        {
            conduct.MaxHoldBars = risk.MaxHoldBars;
        }
    }

    private sealed class SailorSettingsRoot
    {
        public SailorAppSettings? Sailor { get; set; }
    }
}
