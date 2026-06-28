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
    bool ForceFlatNow);

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
