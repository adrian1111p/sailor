using Sailor.App.Backtest.Profiles;
using Sailor.App.Broker.State;
using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Runtime.Ui;

namespace Sailor.App.Runtime.Paper;

public sealed class ManualBrokerPositionRuntimeSync
{
    private readonly SailorAppSettings _settings;
    private readonly TradeLifecycleRegistryStore _tradeRegistry;
    private readonly StrategyLifecyclePolicyResolver _lifecyclePolicyResolver;
    private readonly Action<string> _log;

    public ManualBrokerPositionRuntimeSync(
        SailorAppSettings settings,
        TradeLifecycleRegistryStore tradeRegistry,
        StrategyLifecyclePolicyResolver lifecyclePolicyResolver,
        Action<string> log)
    {
        _settings = settings;
        _tradeRegistry = tradeRegistry;
        _lifecyclePolicyResolver = lifecyclePolicyResolver;
        _log = log;
    }

    public ManualBrokerPositionSyncReport Synchronize(
        List<PaperSymbolSession> sessions,
        PaperRuntimeHostRequest request,
        ReconciliationResult reconciliation,
        SailorRuntimeState runtimeState)
    {
        DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
        var events = new List<string>();
        var warnings = new List<string>();

        IReadOnlyDictionary<string, BrokerPositionRow> brokerBySymbol = reconciliation.BrokerPositions
            .Where(position => !position.IsFlat)
            .GroupBy(position => NormalizeSymbol(position.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(position => Math.Abs(position.Quantity)).First(), StringComparer.OrdinalIgnoreCase);

        int existingSynced = 0;
        int newSessionsCreated = 0;
        int manualFlatSynced = 0;
        SailorUiDesiredStateRoutingSnapshot desiredRouting = SailorUiDesiredStateRouter.Load(
            request.UiDesiredStateRoutingEnabled,
            request.RuntimeOptions.Mode,
            request.Account,
            request.UiDesiredStateMaxActiveStrategies);
        foreach (string desiredWarning in desiredRouting.Warnings)
        {
            warnings.Add(desiredWarning);
        }

        foreach (BrokerPositionRow brokerPosition in brokerBySymbol.Values.OrderBy(position => position.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            string symbol = NormalizeSymbol(brokerPosition.Symbol);
            PaperSymbolSession? existingSession = sessions.FirstOrDefault(session => session.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            if (existingSession is not null)
            {
                if (existingSession.SyncBrokerPosition(brokerPosition.Quantity, brokerPosition.AverageCost, out string syncMessage))
                {
                    existingSynced++;
                    events.Add($"{symbol}: {syncMessage}");
                }

                continue;
            }

            try
            {
                TradeLifecycle? lifecycle = FindRelevantLifecycle(_tradeRegistry.LoadSnapshot(), symbol);
                SailorTradeOrigin origin = ResolveManualBrokerOrigin(lifecycle);
                string strategyProfileName = desiredRouting.ResolveProfileName(symbol, lifecycle?.ProfileName ?? request.RuntimeOptions.ProfileName);
                SailorStrategyProfile profile = SailorStrategyProfile.FromName(strategyProfileName, _settings);
                StrategyLifecyclePolicy lifecyclePolicy = _lifecyclePolicyResolver.Resolve(profile.Name, origin);
                PaperSymbolSession session = PaperSymbolSession.Create(
                    request.RuntimeOptions.Mode,
                    symbol,
                    request.RuntimeOptions.Timeframe,
                    profile,
                    _settings,
                    marketSnapshot: null,
                    localSeed: null,
                    brokerSeed: brokerPosition,
                    origin,
                    scannerSlotId: lifecycle?.ScannerSlotId,
                    lifecyclePolicy,
                    request.MaxIterations,
                    request.RuntimeOptions.LastEntryMinute,
                    request.RuntimeOptions.ForceFlatMinute,
                    request.SendOrders && _settings.Runtime.Safety.RequireCurrentBarsForPaperSendOrders,
                    _settings.Runtime.Safety.LiveBarMaxAgeMinutes);

                TradeLifecycle registered = _tradeRegistry.RegisterRuntimeSession(
                    symbol,
                    profile.Name,
                    origin,
                    lifecycle?.ScannerSlotId,
                    session.PositionQuantity,
                    session.AveragePrice,
                    request.RuntimeOptions.Timeframe,
                    request.Account,
                    $"SAILOR-062 manual TWS broker position was promoted to a strategy-managed runtime session. SAILOR-068 strategy={profile.Name}.");

                sessions.Add(session);
                newSessionsCreated++;
                events.Add($"{symbol}: SAILOR-062 created strategy-managed manual broker session tradeLifecycle={registered.TradeId} qty={session.PositionQuantity} avg={session.AveragePrice:F4} origin={origin.ToDisplayName()} strategy={profile.Name} policy={lifecyclePolicy.Mode.ToDisplayName()}.");
                _log($"manual-broker-session-created: {symbol} tradeLifecycle={registered.TradeId} qty={session.PositionQuantity} avg={session.AveragePrice:F4} origin={origin.ToDisplayName()} strategy={profile.Name} policy={lifecyclePolicy.Mode.ToDisplayName()}");
            }
            catch (Exception ex)
            {
                string warning = $"{symbol}: SAILOR-062 could not create strategy-managed manual broker session: {ex.Message}";
                warnings.Add(warning);
                _log($"WARN: {warning}");
            }
        }

        foreach (PaperSymbolSession session in sessions.Where(IsManualBrokerManagedSession).ToArray())
        {
            if (brokerBySymbol.ContainsKey(session.Symbol) || !session.HasOpenPosition)
            {
                continue;
            }

            if (session.SyncBrokerPosition(0, 0m, out string flatMessage))
            {
                manualFlatSynced++;
                events.Add($"{session.Symbol}: {flatMessage}");
            }
        }

        if (newSessionsCreated > 0 || existingSynced > 0 || manualFlatSynced > 0)
        {
            runtimeState.SetActiveSymbols(sessions.Select(session => session.Symbol));
        }

        return new ManualBrokerPositionSyncReport(
            observedUtc,
            brokerBySymbol.Count,
            existingSynced,
            newSessionsCreated,
            manualFlatSynced,
            warnings.Count,
            events.ToArray(),
            warnings.ToArray());
    }

    private static bool IsManualBrokerManagedSession(PaperSymbolSession session)
        => session.TradeOrigin is SailorTradeOrigin.ManualPreStart
            or SailorTradeOrigin.ManualIntraday
            or SailorTradeOrigin.UnknownBroker
            or SailorTradeOrigin.SailorManualCommand;

    private static TradeLifecycle? FindRelevantLifecycle(TradeLifecycleRegistrySnapshot snapshot, string symbol)
        => snapshot.Trades
            .Where(trade => trade.NormalizedSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.IsActive)
            .ThenByDescending(trade => trade.UpdatedUtc)
            .FirstOrDefault();

    private static SailorTradeOrigin ResolveManualBrokerOrigin(TradeLifecycle? lifecycle)
    {
        if (lifecycle is null)
        {
            return SailorTradeOrigin.ManualIntraday;
        }

        return lifecycle.Origin is SailorTradeOrigin.UnknownBroker
            ? SailorTradeOrigin.ManualIntraday
            : lifecycle.Origin;
    }

    private static string NormalizeSymbol(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim().ToUpperInvariant();
}
