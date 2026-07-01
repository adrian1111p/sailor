using Sailor.App.Broker.Ibkr;
using Sailor.App.Broker.State;
using Sailor.App.Runtime.Common;
using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed record PaperRuntimeHostRequest(
    SailorRuntimeOptions RuntimeOptions,
    IbkrConnectionOptions ConnectionOptions,
    PaperScannerOptions ScannerOptions,
    ReconciliationResult Reconciliation,
    bool SendOrders,
    bool DryRun,
    bool CanOpenEntries,
    string? Account,
    int Quantity,
    int CadenceSeconds,
    int MaxIterations,
    int WaitSeconds,
    string PrimaryExchange,
    bool ForceFlatNow,
    int ReconnectAttempts,
    int ReconnectBackoffSeconds,
    int SimulateDisconnectAtIteration,
    RuntimeReconciliationDelegate? ReconcileBrokerStateAsync,
    bool EnforceMaxOrderNotional = false,
    decimal MaxOrderNotional = 0m,
    PaperScannerOptions? ReplenishmentScannerOptions = null,
    bool BlockStaleHistoricalReplay = true,
    int LiveBarMaxAgeMinutes = 5,
    int LiveBarFutureToleranceMinutes = 2,
    bool LiveCandleRefreshEnabled = true,
    int LiveCandleRefreshLookbackMinutes = 60,
    int LiveCandleRefreshClientIdOffset = 200,
    int LiveCandleRefreshRequestIdBase = 31_000,
    bool LiveCandleRefreshFallbackEnabled = true,
    bool LiveCandleRefreshDiagnosticsEnabled = true,
    bool LiveRefreshCloseOnlyAfterStale = true,
    bool ManualBrokerPositionsAllowScannerEntries = true,
    bool ManualBrokerPositionsAreStrategyManaged = true,
    bool ManualBrokerPositionMonitorEnabled = true,
    int ManualBrokerPositionMonitorIntervalSeconds = 60,
    int ManualBrokerPositionMonitorClientIdOffset = 300);

public sealed record PaperRuntimeHostResult(
    IReadOnlyList<string> ActiveSymbols,
    int DecisionCount,
    int OrderIntentCount,
    int RoutedOrderCount,
    int FilledOrAssumedFillCount,
    IReadOnlyList<string> Warnings)
{
    public string ToDisplayString()
        => $"activeSymbols={ActiveSymbols.Count} decisions={DecisionCount} intents={OrderIntentCount} routedOrders={RoutedOrderCount} fillsOrAssumedFills={FilledOrAssumedFillCount} warnings={Warnings.Count}";
}
