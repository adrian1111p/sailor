using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Scanner.Runtime;
using Sailor.App.Runtime.Ui;

namespace Sailor.App.Runtime.Paper;

public sealed class DynamicTradeSessionManager
{
    private readonly SailorAppSettings _settings;
    private readonly Action<string> _log;

    public DynamicTradeSessionManager(SailorAppSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
    }

    public DynamicTradeSessionPlan BuildPlan(
        PaperRuntimeHostRequest request,
        PaperScannerRunResult scannerResult,
        TradeLifecycleRegistryStore tradeRegistry)
    {
        var warnings = new List<string>();
        var seeds = new List<DynamicTradeSessionSeed>();
        var addedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TradeLifecycleRegistrySnapshot registrySnapshot = tradeRegistry.LoadSnapshot();
        DateOnly tradeDate = DateOnly.FromDateTime(DateTime.UtcNow);
        SailorUiDesiredStateRoutingSnapshot desiredRouting = SailorUiDesiredStateRouter.Load(
            request.UiDesiredStateRoutingEnabled,
            request.RuntimeOptions.Mode,
            request.Account,
            request.UiDesiredStateMaxActiveStrategies);
        foreach (string desiredWarning in desiredRouting.Warnings)
        {
            warnings.Add(desiredWarning);
        }

        int maxScannerOwned = request.HarshConductTestEnabled
            ? Math.Max(1, request.RuntimeOptions.TopCount)
            : Math.Max(1, Math.Min(
                request.RuntimeOptions.TopCount,
                _settings.Runtime.Safety.MaxActiveSymbols <= 0
                    ? request.RuntimeOptions.TopCount
                    : _settings.Runtime.Safety.MaxActiveSymbols));

        int scannerOwnedCount = 0;
        foreach (PaperScannerCandidate candidate in scannerResult.Candidates)
        {
            if (scannerOwnedCount >= maxScannerOwned)
            {
                break;
            }

            string symbol = NormalizeSymbol(candidate.Symbol);
            if (desiredRouting.ShouldSkipFlatScannerEntry(symbol, out string desiredSkipReason))
            {
                warnings.Add(desiredSkipReason);
                continue;
            }

            string strategyProfileName = desiredRouting.ResolveProfileName(symbol, request.RuntimeOptions.ProfileName);
            TradeLifecycle? stopped = FindStoppedForDay(registrySnapshot, symbol, tradeDate);
            string reason = stopped is null
                ? desiredRouting.Enabled && desiredRouting.HasAnyRows
                    ? $"SAILOR-053 scanner-selected symbol. SAILOR-068 strategy={strategyProfileName} from SailorUI desired-state routing."
                    : "SAILOR-053 scanner-selected symbol."
                : $"SAILOR-053 scanner re-selected a symbol that had stop-for-day evidence earlier today ({stopped.TradeId}); preserving scanner decision for the later replenishment policy audit. SAILOR-068 strategy={strategyProfileName}.";

            if (AddSeed(seeds, addedSymbols, new DynamicTradeSessionSeed(
                    symbol,
                    candidate.Snapshot,
                    SailorTradeOrigin.ScannerOwned,
                    CreateScannerSlotId(symbol, candidate.Rank > 0 ? candidate.Rank : scannerOwnedCount + 1),
                    reason,
                    candidate.Candidate.Side,
                    strategyProfileName)))
            {
                scannerOwnedCount++;
            }
        }

        IReadOnlyDictionary<string, BrokerPositionRow> brokerBySymbol = request.Reconciliation.BrokerPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => NormalizeSymbol(position.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(position => Math.Abs(position.Quantity)).First(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, SailorPosition> localBySymbol = request.Reconciliation.LocalPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => NormalizeSymbol(position.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(position => Math.Abs(position.Quantity)).First(), StringComparer.OrdinalIgnoreCase);

        int brokerPositionCount = 0;
        foreach (BrokerPositionRow brokerPosition in brokerBySymbol.Values.OrderBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            string symbol = NormalizeSymbol(brokerPosition.Symbol);
            TradeLifecycle? lifecycle = FindRelevantLifecycle(registrySnapshot, symbol);
            SailorTradeOrigin origin = ResolveBrokerOrigin(lifecycle);
            string reason = lifecycle is null
                ? "SAILOR-053 verified broker position was not selected by scanner; dynamic manager adds it as manual/external managed session."
                : $"SAILOR-053 verified broker position matched lifecycle {lifecycle.TradeId}; dynamic manager keeps it under conduct.";

            if (AddSeed(seeds, addedSymbols, new DynamicTradeSessionSeed(symbol, null, origin, lifecycle?.ScannerSlotId, reason, StrategyProfileName: desiredRouting.ResolveProfileName(symbol, lifecycle?.ProfileName ?? request.RuntimeOptions.ProfileName))))
            {
                brokerPositionCount++;
            }
        }

        int localPositionCount = 0;
        foreach (SailorPosition localPosition in localBySymbol.Values.OrderBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            string symbol = NormalizeSymbol(localPosition.Symbol);
            TradeLifecycle? lifecycle = FindRelevantLifecycle(registrySnapshot, symbol);
            SailorTradeOrigin origin = lifecycle?.Origin ?? SailorTradeOrigin.SailorPreExisting;
            string reason = lifecycle is null
                ? "SAILOR-053 local Sailor position was not selected by scanner; dynamic manager adds it as Sailor pre-existing managed session."
                : $"SAILOR-053 local position matched lifecycle {lifecycle.TradeId}; dynamic manager keeps it under conduct.";

            if (AddSeed(seeds, addedSymbols, new DynamicTradeSessionSeed(symbol, null, origin, lifecycle?.ScannerSlotId, reason, StrategyProfileName: desiredRouting.ResolveProfileName(symbol, lifecycle?.ProfileName ?? request.RuntimeOptions.ProfileName))))
            {
                localPositionCount++;
            }
        }

        int registryRecoveredCount = 0;
        foreach (TradeLifecycle lifecycle in registrySnapshot.Trades
                     .Where(trade => ShouldRecoverFromRegistry(trade, tradeDate))
                     .OrderBy(trade => trade.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            string symbol = NormalizeSymbol(lifecycle.Symbol);
            string reason = $"SAILOR-053 active lifecycle {lifecycle.TradeId} was recovered into the runtime session plan from registry evidence.";
            if (AddSeed(seeds, addedSymbols, new DynamicTradeSessionSeed(symbol, null, lifecycle.Origin, lifecycle.ScannerSlotId, reason, StrategyProfileName: desiredRouting.ResolveProfileName(symbol, lifecycle.ProfileName))))
            {
                registryRecoveredCount++;
            }
        }

        int stoppedForDaySkippedCount = registrySnapshot.Trades
            .Count(trade => trade.TradeDate == tradeDate
                && (trade.ManualStoppedForDay || trade.Status == TradeLifecycleStatus.StoppedForDay)
                && !addedSymbols.Contains(trade.NormalizedSymbol));

        int externalOpenOrderCount = request.Reconciliation.BrokerOpenOrders
            .Count(order => !IsSailorOrderReference(order.OrderRef));
        if (externalOpenOrderCount > 0)
        {
            warnings.Add($"SAILOR-053 observed {externalOpenOrderCount} external broker open order(s). They are mirrored by SAILOR-052 but not yet converted to active sessions until a broker position exists.");
        }

        int fallbackCount = 0;
        if (seeds.Count == 0)
        {
            IReadOnlyList<string> fallbackSymbols = scannerResult.PreparedSymbols.Count > 0
                ? scannerResult.PreparedSymbols
                : scannerResult.ResolvedSymbols;

            foreach (string fallbackSymbol in fallbackSymbols
                         .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (fallbackCount >= maxScannerOwned)
                {
                    break;
                }

                string normalizedFallback = NormalizeSymbol(fallbackSymbol);
                if (desiredRouting.ShouldSkipFlatScannerEntry(normalizedFallback, out string desiredFallbackSkipReason))
                {
                    warnings.Add(desiredFallbackSkipReason);
                    continue;
                }

                if (AddSeed(seeds, addedSymbols, new DynamicTradeSessionSeed(
                        normalizedFallback,
                        null,
                        SailorTradeOrigin.ExplicitRuntime,
                        null,
                        "SAILOR-053 fallback explicit runtime session for smoke testing because no scanner/broker/local/registry session was available.",
                        StrategyProfileName: desiredRouting.ResolveProfileName(fallbackSymbol, request.RuntimeOptions.ProfileName))))
                {
                    fallbackCount++;
                }
            }

            if (fallbackCount > 0)
            {
                warnings.Add("Scanner returned no ranked candidates and no active broker/local/registry positions existed; SAILOR-053 activated prepared fallback symbols so the conduct loop can still be smoke-tested.");
            }
        }

        DynamicTradeSessionPlan plan = new(
            seeds.ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            scannerOwnedCount,
            brokerPositionCount,
            localPositionCount,
            registryRecoveredCount,
            fallbackCount,
            stoppedForDaySkippedCount,
            externalOpenOrderCount);

        return plan;
    }

    private static bool AddSeed(
        List<DynamicTradeSessionSeed> seeds,
        HashSet<string> addedSymbols,
        DynamicTradeSessionSeed seed)
    {
        if (string.IsNullOrWhiteSpace(seed.Symbol) || seed.NormalizedSymbol == "UNKNOWN")
        {
            return false;
        }

        if (!addedSymbols.Add(seed.NormalizedSymbol))
        {
            return false;
        }

        seeds.Add(seed with { Symbol = seed.NormalizedSymbol });
        return true;
    }

    private static TradeLifecycle? FindRelevantLifecycle(TradeLifecycleRegistrySnapshot snapshot, string symbol)
        => snapshot.Trades
            .Where(trade => trade.NormalizedSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.IsActive)
            .ThenByDescending(trade => trade.UpdatedUtc)
            .FirstOrDefault();

    private static TradeLifecycle? FindStoppedForDay(
        TradeLifecycleRegistrySnapshot snapshot,
        string symbol,
        DateOnly tradeDate)
        => snapshot.Trades
            .Where(trade => trade.TradeDate == tradeDate
                && trade.NormalizedSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                && (trade.ManualStoppedForDay || trade.Status == TradeLifecycleStatus.StoppedForDay))
            .OrderByDescending(trade => trade.UpdatedUtc)
            .FirstOrDefault();

    private static bool ShouldRecoverFromRegistry(TradeLifecycle trade, DateOnly tradeDate)
        => trade.TradeDate == tradeDate
            && trade.IsActive
            && !trade.ManualStoppedForDay
            && trade.BrokerQuantity != 0;

    private static SailorTradeOrigin ResolveBrokerOrigin(TradeLifecycle? lifecycle)
    {
        if (lifecycle is null)
        {
            return SailorTradeOrigin.UnknownBroker;
        }

        return lifecycle.Origin is SailorTradeOrigin.UnknownBroker
            ? SailorTradeOrigin.ManualPreStart
            : lifecycle.Origin;
    }

    private static bool IsSailorOrderReference(string? orderRef)
        => !string.IsNullOrWhiteSpace(orderRef)
            && orderRef.Contains("SAILOR", StringComparison.OrdinalIgnoreCase);

    private static string CreateScannerSlotId(string symbol, int rank)
        => $"SCAN-{DateTime.UtcNow:yyyyMMdd}-{Math.Max(1, rank):000}-{NormalizeSymbol(symbol)}";

    private static string NormalizeSymbol(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim().ToUpperInvariant();
}
