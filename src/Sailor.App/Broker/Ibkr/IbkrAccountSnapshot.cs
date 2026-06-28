namespace Sailor.App.Broker.Ibkr;

public sealed record IbkrAccountSnapshot(
    string AccountId,
    bool IsConfiguredAccount,
    DateTimeOffset ObservedUtc)
{
    public string ToDisplayString()
        => $"account={AccountId} configuredMatch={IsConfiguredAccount} observedUtc={ObservedUtc:O}";
}
