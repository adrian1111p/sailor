namespace Sailor.App.Configuration;

public sealed class SailorAppSettings
{
    public string DefaultTimeframe { get; set; } = "1m";

    public string DefaultProfile { get; set; } = "sailor-trend-volume";

    public string DefaultUniverse { get; set; } = "all";

    public BacktestRiskSettings Risk { get; set; } = new();

    public ConductExitSettings Conduct { get; set; } = new();

    public Dictionary<string, ConductExitSettings> ConductProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

public sealed class ConductExitSettings
{
    public decimal HardStopPercent { get; set; } = 1.00m;

    public bool UseTakeProfitExit { get; set; } = false;

    public decimal TakeProfitPercent { get; set; } = 2.00m;

    public decimal MoveStopToBreakevenAfterPercent { get; set; } = 0.60m;

    public decimal BreakevenBufferPercent { get; set; } = 0.05m;

    public decimal StartTrailingAfterPercent { get; set; } = 0.80m;

    public decimal GivebackPercent { get; set; } = 0.35m;

    public decimal GivebackNotionalCap { get; set; } = 30.00m;

    public int MinimumBarsBeforeIndicatorExit { get; set; } = 3;

    public bool UseEma9Exit { get; set; } = true;

    public bool UseVwapExit { get; set; } = true;

    public bool UseTrendExit { get; set; } = true;

    public bool UseOppositeMomentumExit { get; set; } = true;

    public bool UseMicroTrail { get; set; } = false;

    public decimal MicroTrailActivatePercent { get; set; } = 0.40m;

    public decimal MicroTrailPercent { get; set; } = 0.20m;

    public int MaxHoldBars { get; set; } = 45;
}

public sealed class ScannerSettings
{
    public int DefaultTopCount { get; set; } = 20;
}

public sealed class SailorProfileSettings
{
    public string? SideMode { get; set; }

    public decimal? EntryMomentumPercent { get; set; }

    public decimal? ExitMomentumPercent { get; set; }

    public decimal? MinimumPrice { get; set; }

    public decimal? MaximumPrice { get; set; }

    public long? MinimumVolume { get; set; }

    public decimal? MinimumVolumeRatio { get; set; }

    public bool? RequirePriceAboveVwap { get; set; }

    public bool? RequireEma9AboveSma20 { get; set; }

    public bool? RequirePriceAboveSma200WhenAvailable { get; set; }

    public bool? UseConductExits { get; set; }

    public string? ConductProfileName { get; set; }

    public bool? UseMarketHours { get; set; }

    public int? MarketOpenMinute { get; set; }

    public int? SkipFirstMinutes { get; set; }

    public int? LastEntryMinute { get; set; }

    public int? ForceFlatMinute { get; set; }

    public int? MinimumBarsBetweenEntries { get; set; }

    public bool? UseNextBarOpenEntry { get; set; }

    public int? ScannerLookbackBars { get; set; }

    public int? ScannerMinimumBars { get; set; }

    public int? ScannerTopCount { get; set; }
}
