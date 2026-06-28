using Sailor.App.Broker.State;

namespace Sailor.App.Runtime.Common;

public delegate Task<ReconciliationResult> RuntimeReconciliationDelegate(CancellationToken cancellationToken);

public sealed class ConnectionRecoveryService
{
    private readonly RuntimeHealthMonitor _healthMonitor;
    private readonly Action<string> _log;

    public ConnectionRecoveryService(RuntimeHealthMonitor healthMonitor, Action<string> log)
    {
        _healthMonitor = healthMonitor;
        _log = log;
    }

    public async Task<ConnectionRecoveryResult> TryRecoverAsync(
        RuntimeReconciliationDelegate? reconcileBrokerStateAsync,
        IReadOnlyList<string> activeSymbols,
        int maxAttempts,
        TimeSpan initialBackoff,
        CancellationToken cancellationToken)
    {
        var events = new List<string>();
        var warnings = new List<string>();

        if (reconcileBrokerStateAsync is null)
        {
            string warning = "No broker reconciliation delegate is available. Runtime remains close-only.";
            warnings.Add(warning);
            _log($"WARN: {warning}");
            return new ConnectionRecoveryResult(false, null, _healthMonitor.SafetyState, events, warnings);
        }

        int attempts = Math.Max(0, maxAttempts);
        if (attempts == 0)
        {
            string warning = "Reconnect attempts are disabled. Runtime remains close-only.";
            warnings.Add(warning);
            _log($"WARN: {warning}");
            return new ConnectionRecoveryResult(false, null, _healthMonitor.SafetyState, events, warnings);
        }

        TimeSpan backoff = initialBackoff <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : initialBackoff;
        ReconciliationResult? lastReconciliation = null;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RuntimeIncident? reconnectIncident = _healthMonitor.MarkReconnecting(
                $"Reconnect attempt {attempt}/{attempts} started after degraded runtime state.",
                attempt,
                activeSymbols.Count == 0 ? Array.Empty<string>() : new[] { $"activeSymbols={string.Join(',', activeSymbols)}" });

            if (reconnectIncident is not null)
            {
                _log(reconnectIncident.ToDisplayString());
            }

            if (attempt > 1)
            {
                TimeSpan delay = TimeSpan.FromTicks(backoff.Ticks * Math.Min(attempt - 1, 5));
                string delayMessage = $"reconnect-backoff attempt={attempt} delaySeconds={delay.TotalSeconds:F0}";
                events.Add(delayMessage);
                _log(delayMessage);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                ReconciliationResult reconciliation = await reconcileBrokerStateAsync(cancellationToken).ConfigureAwait(false);
                lastReconciliation = reconciliation;
                events.Add($"reconnect-reconcile attempt={attempt} status={reconciliation.Status} canOpenEntries={reconciliation.CanOpenNewEntries}");
                foreach (string warning in reconciliation.Warnings)
                {
                    warnings.Add(warning);
                }

                if (reconciliation.CanOpenNewEntries && reconciliation.Status == ReconciliationStatus.Matched)
                {
                    RuntimeIncident? recoveredIncident = _healthMonitor.MarkRecovered(
                        reconciliation,
                        reconciliation.Events.Concat(new[]
                        {
                            $"Replay market data subscriptions requested for: {(activeSymbols.Count == 0 ? "none" : string.Join(',', activeSymbols))}",
                            "Broker state was reconciled after reconnect."
                        }));

                    if (recoveredIncident is not null)
                    {
                        _log(recoveredIncident.ToDisplayString());
                    }

                    events.Add(activeSymbols.Count == 0
                        ? "market-data-replay skipped because no active symbols were registered."
                        : $"market-data-replay queued symbols={string.Join(',', activeSymbols)}");

                    return new ConnectionRecoveryResult(true, reconciliation, _healthMonitor.SafetyState, events, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                }

                RuntimeIncident? dirtyIncident = _healthMonitor.MarkRecovered(reconciliation);
                if (dirtyIncident is not null)
                {
                    _log(dirtyIncident.ToDisplayString());
                }
            }
            catch (Exception ex)
            {
                string warning = $"Reconnect attempt {attempt}/{attempts} failed: {ex.GetType().Name}: {ex.Message}";
                warnings.Add(warning);
                events.Add(warning);
                _log($"WARN: {warning}");
                _healthMonitor.MarkCloseOnly("reconnect-failed", warning, new[] { warning });
            }
        }

        string finalWarning = $"Reconnect did not restore a clean broker state after {attempts} attempt(s). Runtime remains close-only.";
        warnings.Add(finalWarning);
        _log($"WARN: {finalWarning}");
        return new ConnectionRecoveryResult(false, lastReconciliation, _healthMonitor.SafetyState, events, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

public sealed record ConnectionRecoveryResult(
    bool Recovered,
    ReconciliationResult? Reconciliation,
    RuntimeSafetyState SafetyState,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Warnings)
{
    public string ToDisplayString()
        => $"recovered={Recovered} reconciliationStatus={(Reconciliation?.Status.ToString() ?? "n/a")} {SafetyState.ToDisplayString()} warnings={Warnings.Count}";
}
