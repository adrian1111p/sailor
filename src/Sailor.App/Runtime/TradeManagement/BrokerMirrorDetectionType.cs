namespace Sailor.App.Runtime.TradeManagement;

public enum BrokerMirrorDetectionType
{
    SailorOwnedPositionSynchronized = 0,
    ManualPreStartPositionRegistered = 1,
    ManualIntradayPositionRegistered = 2,
    ManualCloseDetected = 3,
    ExternalOpenOrderDetected = 4,
    ExternalExecutionDetected = 5,
    BrokerFlatConfirmed = 6,
    BrokerMirrorWarning = 7
}

public static class BrokerMirrorDetectionTypeExtensions
{
    public static string ToDisplayName(this BrokerMirrorDetectionType type)
        => type switch
        {
            BrokerMirrorDetectionType.SailorOwnedPositionSynchronized => "sailor-owned-position-synchronized",
            BrokerMirrorDetectionType.ManualPreStartPositionRegistered => "manual-pre-start-position-registered",
            BrokerMirrorDetectionType.ManualIntradayPositionRegistered => "manual-intraday-position-registered",
            BrokerMirrorDetectionType.ManualCloseDetected => "manual-close-detected",
            BrokerMirrorDetectionType.ExternalOpenOrderDetected => "external-open-order-detected",
            BrokerMirrorDetectionType.ExternalExecutionDetected => "external-execution-detected",
            BrokerMirrorDetectionType.BrokerFlatConfirmed => "broker-flat-confirmed",
            BrokerMirrorDetectionType.BrokerMirrorWarning => "broker-mirror-warning",
            _ => type.ToString().ToLowerInvariant()
        };
}
