using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.MarketData.Live;

public sealed record LiveMarketDataSnapshotResult(
    string Symbol,
    bool Success,
    bool RemoteRequested,
    bool RemoteProviderAvailable,
    SailorMarketSnapshot? Snapshot,
    string Message,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Events,
    string? SnapshotLogPath)
{
    public string ToDisplayString()
    {
        string snapshot = Snapshot is null
            ? "snapshot=n/a"
            : Snapshot.ToCompactString();

        return $"symbol={Symbol} success={Success} remoteRequested={RemoteRequested} remoteProviderAvailable={RemoteProviderAvailable} {snapshot} message={Message}";
    }

    public static LiveMarketDataSnapshotResult Failed(
        LiveMarketDataRequest request,
        bool remoteRequested,
        bool remoteProviderAvailable,
        string message,
        IEnumerable<string>? warnings = null,
        IEnumerable<string>? events = null)
        => new(
            request.NormalizedSymbol,
            Success: false,
            remoteRequested,
            remoteProviderAvailable,
            Snapshot: null,
            message,
            (warnings ?? Array.Empty<string>()).ToArray(),
            (events ?? Array.Empty<string>()).ToArray(),
            SnapshotLogPath: null);
}
