using Sailor.App.Broker.State;

namespace Sailor.App.Runtime.TradeManagement;

public static class ManualBrokerOrderWorkflow
{
    private static readonly string[] BrokerStateUnavailableTokens =
    [
        "notbrokerverified",
        "not broker verified",
        "broker state was not available",
        "timed out waiting",
        "timed out",
        "timeout",
        "unable to connect",
        "client id is already in use",
        "unable to read",
        "connection refused",
        "connection reset",
        "not connected"
    ];

    public static bool AllowsStrategyEntries(ReconciliationResult reconciliation)
    {
        if (reconciliation.CanOpenNewEntries)
        {
            return true;
        }

        if (!IsCriticalManualBrokerState(reconciliation))
        {
            return false;
        }

        return HasBrokerTruth(reconciliation) && !HasBrokerStateUnavailableWarning(reconciliation);
    }

    public static bool IsCriticalManualBrokerState(ReconciliationResult reconciliation)
        => reconciliation.Status.ToString().Equals("CriticalMismatch", StringComparison.OrdinalIgnoreCase)
            && HasBrokerTruth(reconciliation);

    public static string ToEntryGateReason(ReconciliationResult reconciliation)
    {
        if (reconciliation.CanOpenNewEntries)
        {
            return "Broker reconciliation is matched.";
        }

        if (AllowsStrategyEntries(reconciliation))
        {
            return "SAILOR-062 manual TWS broker state is accepted: scanner entries remain allowed while manual/external broker positions/orders are synchronized into strategy-managed sessions.";
        }

        return $"Broker reconciliation is {reconciliation.Status}; automatic entries remain blocked.";
    }

    private static bool HasBrokerTruth(ReconciliationResult reconciliation)
        => reconciliation.BrokerPositions.Count > 0
            || reconciliation.BrokerOpenOrders.Count > 0
            || reconciliation.BrokerExecutions.Count > 0;

    private static bool HasBrokerStateUnavailableWarning(ReconciliationResult reconciliation)
        => reconciliation.Warnings.Any(IsBrokerStateUnavailable)
            || reconciliation.Events.Any(IsBrokerStateUnavailable);

    private static bool IsBrokerStateUnavailable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return BrokerStateUnavailableTokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
