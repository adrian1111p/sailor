namespace Sailor.App.Runtime.Paper;

public sealed record ManualBrokerPositionSyncReport(
    DateTimeOffset ObservedUtc,
    int BrokerPositionCount,
    int ExistingSessionsSynchronized,
    int NewSessionsCreated,
    int ManualFlatSessionsSynchronized,
    int WarningsCount,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Warnings)
{
    public string ToSummaryString()
        => $"manualBrokerSync brokerPositions={BrokerPositionCount} existingSynced={ExistingSessionsSynchronized} newSessions={NewSessionsCreated} manualFlatSynced={ManualFlatSessionsSynchronized} warnings={WarningsCount}";
}
