namespace Sailor.App.Configuration;

public sealed class SailorAppSettings
{
    public string DefaultTimeframe { get; set; } = "1m";

    public string DefaultProfile { get; set; } = "sailor-trend-volume";

    public string DefaultUniverse { get; set; } = "all";

    public BacktestRiskSettings Risk { get; set; } = new();

    public ScannerSettings Scanner { get; set; } = new();

    public Dictionary<string, SailorProfileSettings> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BacktestRiskSettings
{
    public decimal InitialCash { get; set; } = 10_000.00m;

    public decimal MaxPositionNotional { get; set; } = 1_000.00m;

    public decimal StopLossPercent { get; set; } = 1.00m;

    public decimal TakeProfitPercent { get; set; } = 2.00m;

    public int MaxHoldBars { get; set; } = 30;
}

public sealed class ScannerSettings
{
    public int DefaultTopCount { get; set; } = 20;
}

public sealed class SailorProfileSettings
{
    public decimal? EntryMomentumPercent { get; set; }

    public decimal? ExitMomentumPercent { get; set; }

    public decimal? MinimumPrice { get; set; }

    public decimal? MaximumPrice { get; set; }

    public long? MinimumVolume { get; set; }

    public decimal? MinimumVolumeRatio { get; set; }

    public bool? RequirePriceAboveVwap { get; set; }

    public bool? RequireEma9AboveSma20 { get; set; }

    public bool? RequirePriceAboveSma200WhenAvailable { get; set; }

    public int? ScannerLookbackBars { get; set; }

    public int? ScannerMinimumBars { get; set; }

    public int? ScannerTopCount { get; set; }
}
