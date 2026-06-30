using Sailor.App.Backtest;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Broker.Ibkr;
using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed class ScannerSlotManager
{
    private readonly SailorAppSettings _settings;
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly PaperScannerOptions _scannerOptions;
    private readonly TradeLifecycleRegistryStore _tradeRegistry;
    private readonly StrategyLifecyclePolicyResolver _lifecyclePolicyResolver;
    private readonly Action<string> _log;
    private readonly ScannerSlotReplenishmentReportWriter _writer;
    private DateTimeOffset _nextReplenishmentUtc;

    public ScannerSlotManager(
        SailorAppSettings settings,
        IbkrConnectionOptions connectionOptions,
        PaperScannerOptions scannerOptions,
        TradeLifecycleRegistryStore tradeRegistry,
        StrategyLifecyclePolicyResolver lifecyclePolicyResolver,
        Action<string> log,
        SailorRuntimeMode mode)
    {
        _settings = settings;
        _connectionOptions = connectionOptions;
        _scannerOptions = scannerOptions;
        _tradeRegistry = tradeRegistry;
        _lifecyclePolicyResolver = lifecyclePolicyResolver;
        _log = log;
        _writer = new ScannerSlotReplenishmentReportWriter(mode);
        ReplenishIntervalSeconds = Math.Max(60, _settings.Scanner.ReplenishmentIntervalSeconds <= 0 ? 300 : _settings.Scanner.ReplenishmentIntervalSeconds);
        int configuredTarget = Math.Max(0, _settings.Scanner.TargetScannerTrades);
        TargetScannerTrades = mode == SailorRuntimeMode.Live && !_settings.Runtime.Live.AllowMultiSymbolPilot
            ? 0
            : configuredTarget;
        _nextReplenishmentUtc = DateTimeOffset.UtcNow.AddSeconds(ReplenishIntervalSeconds);
    }

    public int TargetScannerTrades { get; }

    public int ReplenishIntervalSeconds { get; }

    public bool AvoidSameDayStoppedSymbols => _settings.Scanner.AvoidSameDayStoppedSymbols;

    public bool ReplenishmentAllowWeakEntry => _settings.Scanner.ReplenishmentAllowWeakEntry;

    public string ToDisplayString()
        => $"targetScannerTrades={TargetScannerTrades} replenishIntervalSeconds={ReplenishIntervalSeconds} allowWeakEntry={ReplenishmentAllowWeakEntry} avoidSameDayStoppedSymbols={AvoidSameDayStoppedSymbols}";

    public void RequestImmediateReplenishment()
        => _nextReplenishmentUtc = DateTimeOffset.MinValue;

    public ScannerSlotReplenishmentReport WriteStatusReport(
        IReadOnlyList<PaperSymbolSession> sessions,
        string reason)
    {
        ScannerSlotReplenishmentReport report = BuildReport(
            sessions,
            newSlotsRequested: 0,
            newSlotsCreated: 0,
            blockedSymbols: Array.Empty<string>(),
            reason: reason,
            referenceTime: ResolveReferenceTime(sessions));
        return _writer.Write(report);
    }

    public async Task<ScannerSlotReplenishmentReport?> TryReplenishIfDueAsync(
        List<PaperSymbolSession> sessions,
        PaperRuntimeHostRequest request,
        RuntimeHealthMonitor healthMonitor,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < _nextReplenishmentUtc)
        {
            return null;
        }

        _nextReplenishmentUtc = now.AddSeconds(ReplenishIntervalSeconds);
        return await ReplenishAsync(sessions, request, healthMonitor, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScannerSlotReplenishmentReport> ReplenishAsync(
        List<PaperSymbolSession> sessions,
        PaperRuntimeHostRequest request,
        RuntimeHealthMonitor healthMonitor,
        CancellationToken cancellationToken)
    {
        DateTimeOffset referenceTime = ResolveReferenceTime(sessions);
        int easternMinute = MarketTime.GetEasternMinuteOfDay(referenceTime);
        int shortfall = Math.Max(0, TargetScannerTrades - CountActiveScannerTrades(sessions));
        var blockedSymbols = new List<string>();

        if (TargetScannerTrades <= 0)
        {
            return _writer.Write(BuildReport(sessions, 0, 0, blockedSymbols, "scanner slot target disabled", referenceTime));
        }

        if (shortfall <= 0)
        {
            return _writer.Write(BuildReport(sessions, 0, 0, blockedSymbols, "scanner target already satisfied", referenceTime));
        }

        if (easternMinute >= request.RuntimeOptions.LastEntryMinute)
        {
            return _writer.Write(BuildReport(sessions, 0, 0, blockedSymbols, $"blocked because ET minute {easternMinute} is at/after LastEntryMinute {request.RuntimeOptions.LastEntryMinute}", referenceTime));
        }

        if (!healthMonitor.CanOpenEntries(request.CanOpenEntries))
        {
            return _writer.Write(BuildReport(sessions, 0, 0, blockedSymbols, $"blocked because runtime safety is {healthMonitor.SafetyState.Mode}: {healthMonitor.SafetyState.Reason}", referenceTime));
        }

        PaperScannerRunResult scannerResult;
        try
        {
            PaperScannerOptions replenishmentOptions = CreateReplenishmentOptions(request);
            using var scannerRunner = new PaperScannerRunner(_settings, _connectionOptions, replenishmentOptions);
            scannerResult = await scannerRunner.RunAsync(replenishmentOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            blockedSymbols.Add($"scanner-failed:{ex.GetType().Name}:{ex.Message}");
            return _writer.Write(BuildReport(sessions, shortfall, 0, blockedSymbols, "scanner replenishment run failed", referenceTime));
        }

        foreach (string warning in scannerResult.Warnings)
        {
            blockedSymbols.Add($"scanner-warning:{warning}");
        }

        var activeSymbols = sessions
            .Select(session => session.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        TradeLifecycleRegistrySnapshot registrySnapshot = _tradeRegistry.LoadSnapshot();
        DateOnly tradeDate = MarketTime.GetEasternDate(referenceTime);
        SailorStrategyProfile profile = SailorStrategyProfile.FromName(request.RuntimeOptions.ProfileName, _settings);
        int newSlotsCreated = 0;

        foreach (PaperScannerCandidate candidate in scannerResult.Candidates.OrderBy(candidate => candidate.Rank))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (newSlotsCreated >= shortfall)
            {
                break;
            }

            string symbol = NormalizeSymbol(candidate.Symbol);
            if (activeSymbols.Contains(symbol))
            {
                blockedSymbols.Add($"{symbol}:already-active");
                continue;
            }

            if (AvoidSameDayStoppedSymbols && WasStoppedForDay(registrySnapshot, symbol, tradeDate))
            {
                blockedSymbols.Add($"{symbol}:stopped-for-day");
                continue;
            }

            string scannerSlotId = CreateScannerSlotId(symbol, candidate.Rank, newSlotsCreated + 1);
            try
            {
                PaperSymbolSession session = PaperSymbolSession.Create(
                    request.RuntimeOptions.Mode,
                    symbol,
                    request.RuntimeOptions.Timeframe,
                    profile,
                    _settings,
                    candidate.Snapshot,
                    localSeed: null,
                    brokerSeed: null,
                    tradeOrigin: SailorTradeOrigin.ScannerOwned,
                    scannerSlotId: scannerSlotId,
                    lifecyclePolicy: _lifecyclePolicyResolver.Resolve(profile.Name, SailorTradeOrigin.ScannerOwned),
                    maxIterations: request.MaxIterations,
                    runtimeLastEntryMinute: request.RuntimeOptions.LastEntryMinute,
                    runtimeForceFlatMinute: request.RuntimeOptions.ForceFlatMinute,
                    requireCurrentLiveBars: request.SendOrders && _settings.Runtime.Safety.RequireCurrentBarsForPaperSendOrders,
                    liveBarMaxAgeMinutes: request.LiveBarMaxAgeMinutes);

                sessions.Add(session);
                activeSymbols.Add(symbol);
                newSlotsCreated++;

                TradeLifecycle lifecycle = _tradeRegistry.RegisterRuntimeSession(
                    symbol,
                    profile.Name,
                    SailorTradeOrigin.ScannerOwned,
                    scannerSlotId,
                    brokerQuantity: 0,
                    brokerAveragePrice: 0m,
                    timeframe: request.RuntimeOptions.Timeframe,
                    account: request.Account,
                    reason: $"SAILOR-055 scanner replenishment created slot from scanner rank {candidate.Rank}.");

                _log($"scanner-slot-created: {symbol} slot={scannerSlotId} tradeLifecycle={lifecycle.TradeId} rank={candidate.Rank}");
            }
            catch (Exception ex)
            {
                blockedSymbols.Add($"{symbol}:session-create-failed:{ex.Message}");
            }
        }

        string reason = newSlotsCreated > 0
            ? "scanner replenishment created new scanner-owned slots"
            : scannerResult.Candidates.Count == 0
                ? "scanner replenishment found no candidates"
                : "scanner replenishment created no slots after candidate filtering";

        return _writer.Write(BuildReport(sessions, shortfall, newSlotsCreated, blockedSymbols, reason, referenceTime));
    }

    private PaperScannerOptions CreateReplenishmentOptions(PaperRuntimeHostRequest request)
    {
        PaperScannerOptions baseOptions = request.ReplenishmentScannerOptions ?? _scannerOptions;
        int target = Math.Max(1, TargetScannerTrades);
        int requestedTop = Math.Max(baseOptions.TopCount, target + 5);
        int requestedMaxSymbols = baseOptions.MaxSymbolsToPrepare <= 0
            ? requestedTop
            : Math.Max(baseOptions.MaxSymbolsToPrepare, requestedTop);

        return baseOptions with
        {
            TopCount = requestedTop,
            MaxSymbolsToPrepare = requestedMaxSymbols,
            PointsAllowWeakEntry = baseOptions.PointsAllowWeakEntry || ReplenishmentAllowWeakEntry
        };
    }

    private ScannerSlotReplenishmentReport BuildReport(
        IReadOnlyList<PaperSymbolSession> sessions,
        int newSlotsRequested,
        int newSlotsCreated,
        IReadOnlyList<string> blockedSymbols,
        string reason,
        DateTimeOffset referenceTime)
    {
        _ = referenceTime;
        int activeScannerTrades = CountActiveScannerTrades(sessions);
        int manualManagedTrades = CountManualManagedTrades(sessions);
        int shortfall = Math.Max(0, TargetScannerTrades - activeScannerTrades);
        return new ScannerSlotReplenishmentReport(
            DateTimeOffset.UtcNow,
            TargetScannerTrades,
            activeScannerTrades,
            manualManagedTrades,
            shortfall,
            newSlotsRequested,
            newSlotsCreated,
            blockedSymbols.ToArray(),
            string.IsNullOrWhiteSpace(reason) ? "n/a" : reason.Trim());
    }

    private static int CountActiveScannerTrades(IReadOnlyList<PaperSymbolSession> sessions)
        => sessions.Count(session => session.TradeOrigin.CountsTowardScannerTarget()
            && session.ScannerSlotActive
            && !session.LifecycleClosedForEntry);

    private static int CountManualManagedTrades(IReadOnlyList<PaperSymbolSession> sessions)
        => sessions.Count(session => !session.TradeOrigin.CountsTowardScannerTarget());

    private static bool WasStoppedForDay(TradeLifecycleRegistrySnapshot snapshot, string symbol, DateOnly tradeDate)
        => snapshot.Trades.Any(trade => trade.TradeDate == tradeDate
            && trade.NormalizedSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
            && (trade.ManualStoppedForDay || trade.Status == TradeLifecycleStatus.StoppedForDay));

    private static DateTimeOffset ResolveReferenceTime(IReadOnlyList<PaperSymbolSession> sessions)
    {
        DateTimeOffset? lastFrameTime = sessions
            .Select(session => session.LastFrameTime)
            .Where(value => value.HasValue)
            .OrderByDescending(value => value!.Value)
            .FirstOrDefault();

        return lastFrameTime ?? DateTimeOffset.UtcNow;
    }

    private static string CreateScannerSlotId(string symbol, int scannerRank, int sequence)
        => $"SCAN-{DateTime.UtcNow:yyyyMMddHHmmss}-{Math.Max(1, scannerRank):000}-{Math.Max(1, sequence):00}-{NormalizeSymbol(symbol)}";

    private static string NormalizeSymbol(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim().ToUpperInvariant();
}
