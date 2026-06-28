namespace Sailor.App.Broker.Ibkr;

public sealed record IbkrConnectionSnapshot(
    IbkrConnectionState State,
    string Host,
    int Port,
    int ClientId,
    DateTimeOffset StartedUtc,
    DateTimeOffset? TcpConnectedUtc,
    DateTimeOffset? DisconnectedUtc,
    TimeSpan Elapsed,
    string? ServerVersion,
    string? ServerTime,
    int? NextValidOrderId,
    IReadOnlyList<IbkrAccountSnapshot> Accounts,
    string? ErrorMessage,
    bool IsApiHandshakeComplete)
{
    public bool IsTcpConnected => State is IbkrConnectionState.TcpConnected or IbkrConnectionState.ApiHandshakePending or IbkrConnectionState.ApiReady;

    public string ToDisplayString()
    {
        string accounts = Accounts.Count == 0
            ? "none"
            : string.Join(",", Accounts.Select(account => account.AccountId));

        return $"state={State} tcpConnected={IsTcpConnected} apiHandshakeComplete={IsApiHandshakeComplete} host={Host} port={Port} clientId={ClientId} elapsedMs={Elapsed.TotalMilliseconds:F0} nextValidId={NextValidOrderId?.ToString() ?? "n/a"} accounts={accounts} error={ErrorMessage ?? "none"}";
    }
}
