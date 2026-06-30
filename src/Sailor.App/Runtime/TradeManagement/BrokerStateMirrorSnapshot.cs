namespace Sailor.App.Runtime.TradeManagement;

public sealed record BrokerStateMirrorSnapshot(
    string Mode,
    string Account,
    string Source,
    string ReconciliationStatus,
    bool BrokerVerified,
    bool CanOpenNewEntries,
    DateTimeOffset ObservedUtc,
    IReadOnlyList<BrokerMirrorPositionRow> Positions,
    IReadOnlyList<BrokerMirrorOpenOrderRow> OpenOrders,
    IReadOnlyList<BrokerMirrorExecutionRow> Executions,
    IReadOnlyList<BrokerMirrorDetection> Detections,
    IReadOnlyList<string> Warnings,
    string RegistryLatestPath)
{
    public int PositionCount => Positions.Count;

    public int NonFlatPositionCount => Positions.Count(position => position.Quantity != 0);

    public int OpenOrderCount => OpenOrders.Count;

    public int ExecutionCount => Executions.Count;

    public int ManualOrExternalDetectionCount => Detections.Count(detection => detection.Type is BrokerMirrorDetectionType.ManualPreStartPositionRegistered
        or BrokerMirrorDetectionType.ManualIntradayPositionRegistered
        or BrokerMirrorDetectionType.ManualCloseDetected
        or BrokerMirrorDetectionType.ExternalOpenOrderDetected
        or BrokerMirrorDetectionType.ExternalExecutionDetected);

    public string ToSummaryString()
        => $"brokerMirror mode={Mode} account={(string.IsNullOrWhiteSpace(Account) ? "not-configured" : Account)} brokerVerified={BrokerVerified} reconciliation={ReconciliationStatus} positions={PositionCount} nonFlat={NonFlatPositionCount} openOrders={OpenOrderCount} executions={ExecutionCount} detections={Detections.Count} manualOrExternal={ManualOrExternalDetectionCount} canOpenEntries={CanOpenNewEntries} observedUtc={ObservedUtc:O}";
}
