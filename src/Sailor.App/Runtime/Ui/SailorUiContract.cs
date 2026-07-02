namespace Sailor.App.Runtime.Ui;

public static class SailorUiContract
{
    public const int DefaultPort = 5101;
    public const int DefaultRefreshMilliseconds = 1000;
    public const int DefaultMaxActiveStrategies = 2;
    public const int DefaultScannerRows = 145;
    public const string SnapshotEndpoint = "/api/snapshot";
    public const string DesiredStateEndpoint = "/api/desired-state";
    public const string HealthEndpoint = "/api/health";
    public const string ExportEndpoint = "/api/export";

    public static readonly IReadOnlyList<string> Section2Columns =
    [
        "DLY P&L",
        "Scan Ranking",
        "Symbol",
        "Position",
        "MKT VAL",
        "Buy value",
        "Open",
        "Price",
        "Trade",
        "Strategy",
        "Volume"
    ];

    public static readonly IReadOnlyList<string> Section3Columns =
    [
        "Scan Ranking",
        "Symbol",
        "Trade",
        "Strategy",
        "Volume"
    ];
}
