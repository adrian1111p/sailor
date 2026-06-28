using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;

namespace Sailor.App.Runtime.Common;

public sealed class RuntimeHealthMonitor
{
    private static readonly string[] DisconnectTokens =
    [
        "code=1100",
        "code 1100",
        "code=1300",
        "code 1300",
        "socket",
        "disconnect",
        "disconnected",
        "not connected",
        "connection closed",
        "connection reset",
        "connection refused",
        "timed out",
        "timeout",
        "econnect"
    ];

    private static readonly string[] DegradedTokens =
    [
        "code=2103",
        "code 2103",
        "code=2105",
        "code 2105",
        "code=2157",
        "code 2157",
        "farm connection is broken",
        "market data farm connection is broken",
        "hmds data farm connection is broken"
    ];

    private readonly SailorRuntimeMode _mode;
    private readonly RuntimeIncidentReporter _incidentReporter;
    private string? _lastIncidentFingerprint;

    public RuntimeHealthMonitor(SailorRuntimeMode mode, RuntimeIncidentReporter incidentReporter, bool canOpenEntriesInitially)
    {
        _mode = mode;
        _incidentReporter = incidentReporter;
        SafetyState = canOpenEntriesInitially
            ? RuntimeSafetyState.Normal("Initial runtime safety gate is clean.")
            : RuntimeSafetyState.CloseOnly("Initial runtime safety gate blocks entries until broker state is verified.");
    }

    public RuntimeSafetyState SafetyState { get; private set; }

    public RuntimeIncident? LastIncident { get; private set; }

    public bool CanOpenEntries(bool staticEntryGate)
        => staticEntryGate && SafetyState.CanOpenNewEntries;

    public RuntimeIncident? MarkCloseOnly(string kind, string message, IEnumerable<string>? details = null, string? symbol = null)
    {
        SafetyState = RuntimeSafetyState.CloseOnly(message, SafetyState);
        return ReportIfChanged(kind, "warning", message, details, symbol);
    }

    public RuntimeIncident? MarkReconnecting(string message, int attempt, IEnumerable<string>? details = null)
    {
        SafetyState = RuntimeSafetyState.Reconnecting(message, SafetyState, attempt);
        return ReportIfChanged("reconnect", "warning", message, details, symbol: null);
    }

    public RuntimeIncident? MarkKillSwitch(string message, IEnumerable<string>? details = null)
    {
        SafetyState = RuntimeSafetyState.KillSwitch(message, SafetyState);
        return ReportIfChanged("kill-switch", "critical", message, details, symbol: null);
    }

    public RuntimeIncident? MarkRecovered(ReconciliationResult reconciliation, IEnumerable<string>? details = null)
    {
        if (reconciliation.CanOpenNewEntries && reconciliation.Status == ReconciliationStatus.Matched)
        {
            SafetyState = RuntimeSafetyState.Normal("Recovered after reconnect and broker reconciliation matched.", reconciliation.ObservedUtc);
            return ReportIfChanged("recovered", "info", SafetyState.Reason, details ?? reconciliation.Events, symbol: null);
        }

        string message = $"Reconnect completed but reconciliation is not clean: {reconciliation.Status}. Entries remain blocked.";
        SafetyState = RuntimeSafetyState.CloseOnly(message, SafetyState);
        return ReportIfChanged("reconcile-after-reconnect", "critical", message, reconciliation.Warnings.Concat(reconciliation.Events), symbol: null);
    }

    public RuntimeIncident? ObserveBrokerState(BrokerStateSnapshot snapshot, string source)
    {
        var details = snapshot.Warnings.Concat(snapshot.Events).ToArray();
        if (!snapshot.Success)
        {
            return MarkCloseOnly(
                "broker-state-unavailable",
                $"Broker state request failed in {source}. Runtime moved to close-only.",
                details.Length == 0 ? new[] { snapshot.Message } : details);
        }

        RuntimeIncident? incident = ObserveMessages(details, source);
        return incident;
    }

    public RuntimeIncident? ObserveReconciliation(ReconciliationResult reconciliation, string source)
    {
        RuntimeIncident? incident = ObserveMessages(reconciliation.Warnings.Concat(reconciliation.Events), source);
        if (incident is not null)
        {
            return incident;
        }

        if (!reconciliation.CanOpenNewEntries && reconciliation.Status != ReconciliationStatus.LocalOnly)
        {
            return MarkCloseOnly(
                "reconciliation-degraded",
                $"Reconciliation status is {reconciliation.Status}. Runtime entries are blocked.",
                reconciliation.Warnings.Concat(reconciliation.Events));
        }

        return null;
    }

    public RuntimeIncident? ObserveOrderReceipt(SailorOrderReceipt receipt)
    {
        RuntimeIncident? signalIncident = ObserveMessages(receipt.Warnings.Concat(receipt.Events), $"order:{receipt.Symbol}");
        if (signalIncident is not null)
        {
            return signalIncident;
        }

        if (receipt.Status == SailorOrderStatus.Failed)
        {
            return MarkCloseOnly(
                "order-routing-failed",
                $"Order routing failed for {receipt.Symbol}. Runtime moved to close-only.",
                receipt.Warnings.Concat(receipt.Events).Append(receipt.Message),
                receipt.Symbol);
        }

        return null;
    }

    public RuntimeIncident? ObserveMessages(IEnumerable<string> messages, string source)
    {
        string[] rows = messages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.Trim())
            .ToArray();

        string[] disconnectRows = rows
            .Where(IsDisconnectSignal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (disconnectRows.Length > 0)
        {
            return MarkCloseOnly(
                "disconnect",
                $"Broker disconnect/degraded socket signal detected from {source}. Runtime moved to close-only.",
                disconnectRows);
        }

        string[] degradedRows = rows
            .Where(IsDegradedMarketDataSignal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (degradedRows.Length > 0)
        {
            return MarkCloseOnly(
                "market-data-degraded",
                $"Market data/HMDS degraded signal detected from {source}. Runtime moved to close-only.",
                degradedRows);
        }

        return null;
    }

    private RuntimeIncident? ReportIfChanged(
        string kind,
        string severity,
        string message,
        IEnumerable<string>? details,
        string? symbol)
    {
        string fingerprint = $"{SafetyState.Mode}|{kind}|{message}|{symbol}".ToUpperInvariant();
        if (string.Equals(_lastIncidentFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        _lastIncidentFingerprint = fingerprint;
        LastIncident = _incidentReporter.Report(kind, severity, SafetyState, message, details, symbol);
        return LastIncident;
    }

    private static bool IsDisconnectSignal(string message)
    {
        if (ContainsOkConnectionSignal(message))
        {
            return false;
        }

        return DisconnectTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDegradedMarketDataSignal(string message)
    {
        if (ContainsOkConnectionSignal(message))
        {
            return false;
        }

        return DegradedTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsOkConnectionSignal(string message)
        => message.Contains("connection is OK", StringComparison.OrdinalIgnoreCase)
           || message.Contains("connection restored", StringComparison.OrdinalIgnoreCase)
           || message.Contains("code=1101", StringComparison.OrdinalIgnoreCase)
           || message.Contains("code=1102", StringComparison.OrdinalIgnoreCase);
}
