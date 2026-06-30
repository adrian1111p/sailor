namespace Sailor.App.Configuration;

public sealed class SailorAppSettings
{
    public string DefaultTimeframe { get; set; } = "1m";

    public string DefaultProfile { get; set; } = "sailor-trend-volume";

    public string DefaultUniverse { get; set; } = "all";

    public BacktestRiskSettings Risk { get; set; } = new();

    public ConductExitSettings Conduct { get; set; } = new();

    public L1L2SnapshotSettings L1L2 { get; set; } = new();

    public SailorRuntimeSettings Runtime { get; set; } = new();

    public Dictionary<string, ConductExitSettings> ConductProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ScannerSettings Scanner { get; set; } = new();

    public Dictionary<string, string> StrategyLifecyclePolicies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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


public sealed class L1L2SnapshotSettings
{
    public bool EnableBacktestSyntheticSnapshots { get; set; } = true;

    public bool EnableEntryGuards { get; set; } = true;

    public bool EnableExitGuards { get; set; } = false;

    public bool SyntheticSnapshotsAreAdvisoryOnly { get; set; } = true;

    public bool RequireSnapshotForEntry { get; set; } = false;

    public decimal SyntheticSpreadBps { get; set; } = 6.0m;

    public decimal SyntheticMinimumSpreadCents { get; set; } = 1.0m;

    public decimal MaxSpreadBps { get; set; } = 85.0m;

    public decimal MinimumLiquidityScore { get; set; } = 10.0m;

    public decimal MinimumBookImbalanceForMomentum { get; set; } = 0.02m;

    public decimal MaximumAdverseBookImbalance { get; set; } = 0.60m;

    public int DepthLevels { get; set; } = 5;

    public string[] SuitableProfiles { get; set; } =
    [
        "sailor-trend-volume",
        "sailor-conduct-v3",
        "conduct-v3",
        "harvester-conduct-v3",
        "harvester-conduct-v9",
        "v16-sqzbreakout",
        "v13",
        "v10-hybrid",
        "v17-hybridflow",
        "v2-conduct",
        "v18-silver",
        "v1-first",
        "v19-purplecloud",
        "v15-shortcap",
        "v14-smallcap",
        "v20-gen001-choppyshield",
        "v12"
    ];

    public bool IsProfileSuitable(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        return SuitableProfiles.Any(profile =>
            profileName.Equals(profile, StringComparison.OrdinalIgnoreCase));
    }
}


public sealed class SailorRuntimeSettings
{
    public SailorRuntimeModeSettings Paper { get; set; } = new()
    {
        Port = 7497,
        ClientId = 22,
        SendOrders = false,
        UseL1 = true,
        UseL2 = true,
        AllowShort = true
    };

    public SailorRuntimeModeSettings Live { get; set; } = new()
    {
        Port = 7496,
        ClientId = 21,
        SendOrders = false,
        UseL1 = true,
        UseL2 = true,
        AllowShort = false
    };

    public SailorRuntimeSafetySettings Safety { get; set; } = new();
}

public sealed class SailorRuntimeModeSettings
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; }

    public int ClientId { get; set; }

    public string? Account { get; set; }

    public bool SendOrders { get; set; }

    public bool AllowLiveTrading { get; set; } = false;

    public bool AllowMultiSymbolPilot { get; set; } = false;

    public int MaxConcurrentPositions { get; set; } = 1;

    public decimal MaxTotalPilotNotional { get; set; } = 100.00m;

    public decimal MaxPerSymbolNotional { get; set; } = 100.00m;

    public decimal MaxOrderNotional { get; set; } = 100.00m;

    public int CertificationMaxAgeHours { get; set; } = 24;

    public bool UseL1 { get; set; } = true;

    public bool UseL2 { get; set; } = true;

    public bool AllowShort { get; set; }

    public int DefaultTopCount { get; set; } = 3;

    public int ScannerRefreshSeconds { get; set; } = 60;

    public int ConnectTimeoutSeconds { get; set; } = 10;
}

public sealed class SailorRuntimeSafetySettings
{
    public int LastEntryMinute { get; set; } = 945;

    public int ForceFlatMinute { get; set; } = 955;

    public int MaxActiveSymbols { get; set; } = 3;

    public int MaxReconnectAttempts { get; set; } = 3;

    public int ReconnectDelaySeconds { get; set; } = 5;

    public int HeartbeatSeconds { get; set; } = 10;

    public bool EmergencyFlattenOnDisconnect { get; set; } = true;
}

public sealed class ScannerSettings
{
    public int DefaultTopCount { get; set; } = 20;

    public string DefaultMode { get; set; } = "legacy-blocks";

    public decimal PointsMinimumTradeScore { get; set; } = 45m;

    public bool PointsAllowWeakEntry { get; set; } = false;

    public bool PointsRetainWatchOnly { get; set; } = true;

    public int TargetScannerTrades { get; set; } = 10;

    public int ReplenishmentIntervalSeconds { get; set; } = 300;

    public bool ReplenishmentAllowWeakEntry { get; set; } = false;

    public bool AvoidSameDayStoppedSymbols { get; set; } = true;
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
