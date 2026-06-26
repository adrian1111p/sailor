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
        settings.Scanner ??= new ScannerSettings();
        settings.Profiles ??= new Dictionary<string, SailorProfileSettings>(StringComparer.OrdinalIgnoreCase);

        if (settings.Risk.InitialCash <= 0m)
        {
            settings.Risk.InitialCash = 10_000.00m;
        }

        if (settings.Risk.MaxPositionNotional <= 0m)
        {
            settings.Risk.MaxPositionNotional = 1_000.00m;
        }

        if (settings.Risk.StopLossPercent <= 0m)
        {
            settings.Risk.StopLossPercent = 1.00m;
        }

        if (settings.Risk.TakeProfitPercent <= 0m)
        {
            settings.Risk.TakeProfitPercent = 2.00m;
        }

        if (settings.Risk.MaxHoldBars <= 0)
        {
            settings.Risk.MaxHoldBars = 30;
        }

        if (settings.Scanner.DefaultTopCount <= 0)
        {
            settings.Scanner.DefaultTopCount = 20;
        }

        return settings;
    }

    private sealed class SailorSettingsRoot
    {
        public SailorAppSettings? Sailor { get; set; }
    }
}
