namespace Sailor.App.Runtime.TradeManagement;

public enum TradeLifecycleStatus
{
    PendingEntry = 0,
    EntrySubmitted = 1,
    Open = 2,
    ExitSubmitted = 3,
    ClosedByStrategy = 4,
    ClosedManually = 5,
    StoppedForDay = 6,
    Recovered = 7,
    UnknownBroker = 8,
    Error = 9
}

public static class TradeLifecycleStatusExtensions
{
    public static bool IsActive(this TradeLifecycleStatus status)
        => status is TradeLifecycleStatus.PendingEntry
            or TradeLifecycleStatus.EntrySubmitted
            or TradeLifecycleStatus.Open
            or TradeLifecycleStatus.ExitSubmitted
            or TradeLifecycleStatus.Recovered
            or TradeLifecycleStatus.UnknownBroker;

    public static bool IsClosed(this TradeLifecycleStatus status)
        => !status.IsActive();

    public static string ToDisplayName(this TradeLifecycleStatus status)
        => status switch
        {
            TradeLifecycleStatus.PendingEntry => "pending-entry",
            TradeLifecycleStatus.EntrySubmitted => "entry-submitted",
            TradeLifecycleStatus.Open => "open",
            TradeLifecycleStatus.ExitSubmitted => "exit-submitted",
            TradeLifecycleStatus.ClosedByStrategy => "closed-by-strategy",
            TradeLifecycleStatus.ClosedManually => "closed-manually",
            TradeLifecycleStatus.StoppedForDay => "stopped-for-day",
            TradeLifecycleStatus.Recovered => "recovered",
            TradeLifecycleStatus.UnknownBroker => "unknown-broker",
            TradeLifecycleStatus.Error => "error",
            _ => status.ToString().ToLowerInvariant()
        };
}
