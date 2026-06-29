namespace Sailor.App.Backtest.Scanner.Points;

public enum PointsScannerStatus
{
    Ready = 0,
    WeakReady = 1,
    WatchOnly = 2,
    NotReady = 3
}

public static class PointsScannerStatusExtensions
{
    public static string ToDisplayName(this PointsScannerStatus status)
        => status switch
        {
            PointsScannerStatus.Ready => "Ready",
            PointsScannerStatus.WeakReady => "WeakReady",
            PointsScannerStatus.WatchOnly => "WatchOnly",
            PointsScannerStatus.NotReady => "NotReady",
            _ => status.ToString()
        };
}
