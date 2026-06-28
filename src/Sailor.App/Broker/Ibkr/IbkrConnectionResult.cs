namespace Sailor.App.Broker.Ibkr;

public sealed record IbkrConnectionResult(
    bool Success,
    IbkrConnectionSnapshot Snapshot,
    IReadOnlyList<string> Messages)
{
    public string ToDisplayString()
    {
        string outcome = Success ? "SUCCESS" : "FAILED";
        return $"{outcome}: {Snapshot.ToDisplayString()}";
    }
}
