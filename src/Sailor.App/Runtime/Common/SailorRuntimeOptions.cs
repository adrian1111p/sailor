namespace Sailor.App.Runtime.Common;

public sealed record SailorRuntimeOptions(
    SailorRuntimeMode Mode,
    string Host,
    int Port,
    int ClientId,
    string Timeframe,
    string ProfileName,
    string Universe,
    int TopCount,
    bool DryRun,
    bool SendOrders,
    bool UseL1,
    bool UseL2,
    bool AllowShort,
    int LastEntryMinute,
    int ForceFlatMinute)
{
    public string ModeName => Mode.ToDisplayName();

    public string ToCompactString()
    {
        string orderMode = SendOrders && !DryRun ? "send-orders" : "dry-run";
        return $"{ModeName} {orderMode} host={Host} port={Port} clientId={ClientId} timeframe={Timeframe} profile={ProfileName} universe={Universe} top={TopCount} L1={UseL1} L2={UseL2} allowShort={AllowShort}";
    }
}
