using Sailor.App.MarketData.Snapshots;
using Sailor.App.Runtime.TradeManagement;

namespace Sailor.App.Runtime.Paper;

public sealed record DynamicTradeSessionSeed(
    string Symbol,
    SailorMarketSnapshot? Snapshot,
    SailorTradeOrigin Origin,
    string? ScannerSlotId,
    string Reason,
    string? ScannerSide = null)
{
    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? "UNKNOWN" : Symbol.Trim().ToUpperInvariant();

    public bool CountsTowardScannerTarget => Origin.CountsTowardScannerTarget();

    public string ToDisplayString()
    {
        string slot = string.IsNullOrWhiteSpace(ScannerSlotId) ? "slot=n/a" : $"slot={ScannerSlotId}";
        string snapshot = Snapshot is null
            ? "snapshot=n/a"
            : $"snapshotL1={Snapshot.HasL1} snapshotL2={Snapshot.HasL2}";
        string scannerSide = string.IsNullOrWhiteSpace(ScannerSide) ? "scannerSide=n/a" : $"scannerSide={ScannerSide.Trim().ToUpperInvariant()}";
        return $"{NormalizedSymbol}: origin={Origin.ToDisplayName()} {slot} scannerTarget={CountsTowardScannerTarget} {scannerSide} {snapshot} reason={Reason}";
    }
}
