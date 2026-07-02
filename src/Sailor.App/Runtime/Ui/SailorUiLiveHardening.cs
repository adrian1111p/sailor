using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.Ui;

public static class SailorUiLiveHardening
{
    public const string LiveControlsForbiddenReason = "SAILOR-069 live SailorUI desired-state controls are locked read-only. Live UI browser actions cannot create, modify, or route desired-state entries.";
    public const string LiveLoopbackOnlyReason = "SAILOR-069 live SailorUI is loopback-only. Remote/browser-network binding is not allowed for live mode.";

    public static bool IsLoopbackHost(string? host)
    {
        string value = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        return value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("[::1]", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeHost(
        SailorRuntimeMode mode,
        string? requestedHost,
        IList<string>? warnings = null)
    {
        string host = string.IsNullOrWhiteSpace(requestedHost) ? "127.0.0.1" : requestedHost.Trim();
        if (mode == SailorRuntimeMode.Live && !IsLoopbackHost(host))
        {
            warnings?.Add($"SAILOR-069 live SailorUI host '{host}' was replaced with 127.0.0.1. {LiveLoopbackOnlyReason}");
            return "127.0.0.1";
        }

        return host;
    }

    public static bool ResolveControlsEnabled(
        SailorRuntimeMode mode,
        bool requestedControls,
        bool explicitReadOnly)
        => mode == SailorRuntimeMode.Paper && requestedControls && !explicitReadOnly;

    public static string ResolveControlMode(
        SailorRuntimeMode mode,
        bool controlsEnabled)
    {
        if (controlsEnabled)
        {
            return "paper-desired-state";
        }

        return mode == SailorRuntimeMode.Live
            ? "live-read-only-locked"
            : "read-only";
    }

    public static string DescribeStartup(
        SailorRuntimeMode mode,
        bool requestedControls,
        bool controlsEnabled)
    {
        if (mode == SailorRuntimeMode.Live)
        {
            return requestedControls
                ? $"SAILOR-069 live hardening is active. --ui-controls was requested but ignored. {LiveControlsForbiddenReason}"
                : $"SAILOR-069 live hardening is active. {LiveControlsForbiddenReason}";
        }

        return controlsEnabled
            ? "SAILOR-067 paper desired-state controls are enabled. Browser actions persist desired state only; paper runtime safety gates remain server-side."
            : "SAILOR-066 read-only SailorUI is active. Browser actions cannot change desired state.";
    }
}
