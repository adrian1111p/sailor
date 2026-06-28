namespace Sailor.App.Runtime.Common;

public enum RuntimeSafetyMode
{
    Normal = 0,
    CloseOnly = 1,
    Reconnecting = 2,
    KillSwitch = 3
}

public sealed record RuntimeSafetyState(
    RuntimeSafetyMode Mode,
    bool CanOpenNewEntries,
    bool CanRouteExits,
    bool CanRequestMarketData,
    string Reason,
    DateTimeOffset ObservedUtc,
    int ReconnectAttempts,
    DateTimeOffset? LastCleanReconciliationUtc)
{
    public bool IsDegraded => Mode is RuntimeSafetyMode.CloseOnly or RuntimeSafetyMode.Reconnecting or RuntimeSafetyMode.KillSwitch;

    public static RuntimeSafetyState Normal(string reason, DateTimeOffset? cleanReconciliationUtc = null)
        => new(
            RuntimeSafetyMode.Normal,
            CanOpenNewEntries: true,
            CanRouteExits: true,
            CanRequestMarketData: true,
            NormalizeReason(reason, "Runtime is broker-verified and normal entries are allowed."),
            DateTimeOffset.UtcNow,
            ReconnectAttempts: 0,
            cleanReconciliationUtc);

    public static RuntimeSafetyState CloseOnly(string reason, RuntimeSafetyState? previous = null)
        => new(
            RuntimeSafetyMode.CloseOnly,
            CanOpenNewEntries: false,
            CanRouteExits: true,
            CanRequestMarketData: false,
            NormalizeReason(reason, "Runtime is degraded. New entries are blocked; exits/flatten remain allowed once broker connectivity is available."),
            DateTimeOffset.UtcNow,
            previous?.ReconnectAttempts ?? 0,
            previous?.LastCleanReconciliationUtc);

    public static RuntimeSafetyState Reconnecting(string reason, RuntimeSafetyState? previous = null, int reconnectAttempts = 0)
        => new(
            RuntimeSafetyMode.Reconnecting,
            CanOpenNewEntries: false,
            CanRouteExits: false,
            CanRequestMarketData: false,
            NormalizeReason(reason, "Runtime is reconnecting. New entries and broker-routed exits are paused until reconciliation succeeds."),
            DateTimeOffset.UtcNow,
            reconnectAttempts,
            previous?.LastCleanReconciliationUtc);

    public static RuntimeSafetyState KillSwitch(string reason, RuntimeSafetyState? previous = null)
        => new(
            RuntimeSafetyMode.KillSwitch,
            CanOpenNewEntries: false,
            CanRouteExits: false,
            CanRequestMarketData: false,
            NormalizeReason(reason, "Runtime kill-switch is active. No broker orders may be routed."),
            DateTimeOffset.UtcNow,
            previous?.ReconnectAttempts ?? 0,
            previous?.LastCleanReconciliationUtc);

    public string ToDisplayString()
        => $"safety={Mode} canOpenEntries={CanOpenNewEntries} canRouteExits={CanRouteExits} canRequestMarketData={CanRequestMarketData} reconnectAttempts={ReconnectAttempts} observedUtc={ObservedUtc:O} lastCleanReconciliationUtc={(LastCleanReconciliationUtc?.ToString("O") ?? "n/a")} reason={Reason}";

    private static string NormalizeReason(string? reason, string fallback)
        => string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim();
}
