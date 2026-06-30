namespace Sailor.App.Runtime.Paper;

public sealed record DynamicTradeSessionPlan(
    IReadOnlyList<DynamicTradeSessionSeed> Seeds,
    IReadOnlyList<string> Warnings,
    int ScannerOwnedCount,
    int BrokerPositionCount,
    int LocalPositionCount,
    int RegistryRecoveredCount,
    int FallbackCount,
    int StoppedForDaySkippedCount,
    int ExternalOpenOrderCount)
{
    public int TotalCount => Seeds.Count;

    public int ManualOrExternalCount => Seeds.Count(seed => !seed.CountsTowardScannerTarget);

    public string ToSummaryString()
        => $"dynamicSessions={TotalCount} scannerOwned={ScannerOwnedCount} manualOrExternal={ManualOrExternalCount} brokerPositions={BrokerPositionCount} localPositions={LocalPositionCount} registryRecovered={RegistryRecoveredCount} fallback={FallbackCount} stoppedForDaySkipped={StoppedForDaySkippedCount} externalOpenOrders={ExternalOpenOrderCount} warnings={Warnings.Count}";
}
