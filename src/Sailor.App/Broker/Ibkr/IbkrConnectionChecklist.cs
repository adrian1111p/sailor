namespace Sailor.App.Broker.Ibkr;

public static class IbkrConnectionChecklist
{
    public static IReadOnlyList<string> BuildPreflightLines(IbkrConnectionOptions options)
    {
        var lines = new List<string>
        {
            "IBKR/TWS preflight checklist:",
            $"- TWS/Gateway must be running on {options.Host}:{options.Port}.",
            "- API connections must be enabled in TWS/Gateway.",
            $"- Client ID {options.ClientId} must not be used by another application.",
            "- Read-only API mode is OK for SAILOR-024 because no data/orders are requested.",
            "- Orders remain disabled unless a later milestone explicitly enables SendOrders."
        };

        if (string.IsNullOrWhiteSpace(options.Account))
        {
            lines.Add("- Account is not configured in appsettings.json; SAILOR-024 can still probe the socket.");
        }
        else
        {
            lines.Add($"- Configured account: {options.Account}.");
        }

        return lines;
    }

    public static IReadOnlyList<string> BuildPostConnectLines(IbkrConnectionResult result)
    {
        var lines = new List<string>
        {
            "Post-connect status:",
            $"- TCP reachable: {result.Snapshot.IsTcpConnected}.",
            $"- IBApi handshake complete: {result.Snapshot.IsApiHandshakeComplete}.",
            $"- nextValidId: {result.Snapshot.NextValidOrderId?.ToString() ?? "not available in TCP probe"}.",
            $"- managed accounts: {(result.Snapshot.Accounts.Count == 0 ? "not available in TCP probe" : string.Join(',', result.Snapshot.Accounts.Select(account => account.AccountId)))}.",
            "- No market data requested.",
            "- No orders sent."
        };

        return lines;
    }
}
