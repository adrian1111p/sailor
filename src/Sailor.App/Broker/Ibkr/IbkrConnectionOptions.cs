using Sailor.App.Runtime.Common;

namespace Sailor.App.Broker.Ibkr;

public sealed record IbkrConnectionOptions(
    SailorRuntimeMode Mode,
    string Host,
    int Port,
    int ClientId,
    string? Account,
    int ConnectTimeoutSeconds,
    bool UseL1,
    bool UseL2,
    bool SendOrders,
    bool AllowShort)
{
    public static IbkrConnectionOptions FromRuntimeOptions(
        SailorRuntimeOptions options,
        string? account,
        int connectTimeoutSeconds)
    {
        return new IbkrConnectionOptions(
            options.Mode,
            options.Host,
            options.Port,
            options.ClientId,
            account,
            Math.Max(1, connectTimeoutSeconds),
            options.UseL1,
            options.UseL2,
            options.SendOrders,
            options.AllowShort);
    }

    public string ModeName => Mode.ToDisplayName();

    public string ToDisplayString()
        => $"mode={ModeName} host={Host} port={Port} clientId={ClientId} account={(string.IsNullOrWhiteSpace(Account) ? "not-configured" : Account)} timeout={ConnectTimeoutSeconds}s L1={UseL1} L2={UseL2} sendOrders={SendOrders} allowShort={AllowShort}";
}
