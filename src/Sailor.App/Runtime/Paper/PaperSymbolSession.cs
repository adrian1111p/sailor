using Sailor.App.Backtest;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Data;
using Sailor.App.Broker.Orders;
using Sailor.App.Broker.State;
using Sailor.App.MarketData.Snapshots;
using Sailor.App.Runtime.Common;
using Sailor.App.Runtime.TradeManagement;
using Sailor.App.Strategy.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed class PaperSymbolSession
{
    private List<BacktestBar> _bars;
    private List<BacktestIndicatorSnapshot> _indicators;
    private readonly SailorRuntimeMode _mode;
    private readonly string _timeframe;
    private readonly SailorStrategyProfile _profile;
    private readonly int _runtimeForceFlatMinute;
    private int _cursor;
    private bool _cursorPositionedOnFrame;

    private PaperSymbolSession(
        SailorRuntimeMode mode,
        string symbol,
        string timeframe,
        string dataSourcePath,
        IReadOnlyList<BacktestBar> bars,
        IReadOnlyList<BacktestIndicatorSnapshot> indicators,
        SailorMarketSnapshot? marketSnapshot,
        SailorStrategyAdapter strategy,
        SailorStrategyProfile profile,
        int runtimeForceFlatMinute,
        SailorTradeOrigin tradeOrigin,
        string? scannerSlotId,
        StrategyLifecyclePolicy lifecyclePolicy,
        int startIndex,
        int quantity,
        decimal averagePrice,
        int entryBarIndex)
    {
        _mode = mode;
        Symbol = symbol;
        _timeframe = timeframe;
        DataSourcePath = dataSourcePath;
        _bars = bars.OrderBy(bar => bar.Time).ToList();
        _indicators = indicators.OrderBy(indicator => indicator.Time).ToList();
        MarketSnapshot = marketSnapshot;
        Strategy = strategy;
        _profile = profile;
        _runtimeForceFlatMinute = runtimeForceFlatMinute;
        TradeOrigin = tradeOrigin;
        ScannerSlotId = string.IsNullOrWhiteSpace(scannerSlotId) ? null : scannerSlotId.Trim();
        LifecyclePolicy = lifecyclePolicy;
        _cursor = Math.Clamp(startIndex - 1, 0, Math.Max(0, _bars.Count - 1));
        _cursorPositionedOnFrame = false;
        PositionQuantity = quantity;
        AveragePrice = averagePrice;
        EntryBarIndex = quantity == 0 ? -1 : entryBarIndex;
    }

    public string Symbol { get; }

    public string DataSourcePath { get; }

    public SailorMarketSnapshot? MarketSnapshot { get; }

    public SailorStrategyAdapter Strategy { get; }

    public SailorTradeOrigin TradeOrigin { get; }

    public string? ScannerSlotId { get; }

    public StrategyLifecyclePolicy LifecyclePolicy { get; }

    public bool LifecycleClosedForEntry { get; private set; }

    public string? LifecycleClosedReason { get; private set; }

    public bool ScannerSlotActive => TradeOrigin == SailorTradeOrigin.ScannerOwned && !string.IsNullOrWhiteSpace(ScannerSlotId);

    public DateTimeOffset? LastFrameTime { get; private set; }

    public int PositionQuantity { get; private set; }

    public decimal AveragePrice { get; private set; }

    public int EntryBarIndex { get; private set; }

    public bool HasOpenPosition => PositionQuantity != 0;

    public int PositionSide => PositionQuantity < 0 ? -1 : PositionQuantity > 0 ? 1 : 0;

    public int AbsoluteQuantity => Math.Abs(PositionQuantity);

    public bool HasMoreBars => _cursor < _bars.Count - 1;

    public DateTimeOffset FirstLoadedBarTime => _bars.Count == 0 ? DateTimeOffset.MinValue : _bars[0].Time;

    public DateTimeOffset LastLoadedBarTime => _bars.Count == 0 ? DateTimeOffset.MinValue : _bars[^1].Time;

    public int LoadedBarCount => _bars.Count;

    public int CurrentBarIndex => _cursor;

    public string StartReason { get; private init; } = string.Empty;

    public static PaperSymbolSession Create(
        SailorRuntimeMode mode,
        string symbol,
        string timeframe,
        SailorStrategyProfile profile,
        Sailor.App.Configuration.SailorAppSettings settings,
        SailorMarketSnapshot? marketSnapshot,
        SailorPosition? localSeed,
        BrokerPositionRow? brokerSeed,
        SailorTradeOrigin tradeOrigin,
        string? scannerSlotId,
        StrategyLifecyclePolicy lifecyclePolicy,
        int maxIterations,
        int runtimeLastEntryMinute,
        int runtimeForceFlatMinute,
        bool requireCurrentLiveBars,
        int liveBarMaxAgeMinutes)
    {
        _ = runtimeLastEntryMinute;
        var provider = new CsvBacktestDataProvider();
        BacktestDataSet dataSet = provider.LoadBars(symbol, timeframe);
        IReadOnlyList<BacktestIndicatorSnapshot> indicators = TechnicalIndicatorCalculator.Calculate(dataSet.Bars);

        int warmup = Math.Max(25, Math.Max(profile.ScannerMinimumBars, profile.ScannerLookbackBars));
        int replayWindow = Math.Max(1, maxIterations) + 5;
        int safeForceFlatMinute = runtimeForceFlatMinute > 0
            ? runtimeForceFlatMinute
            : profile.ForceFlatMinute > 0 ? profile.ForceFlatMinute : 24 * 60;
        int lastUsableIndex = FindLastUsableRegularSessionIndex(dataSet.Bars, profile, safeForceFlatMinute);
        int startIndex;
        string startReason;
        if (requireCurrentLiveBars)
        {
            int latestCurrentIndex = FindLatestCurrentSessionIndex(dataSet.Bars, DateTimeOffset.UtcNow, profile, safeForceFlatMinute, Math.Max(1, liveBarMaxAgeMinutes), futureToleranceMinutes: 2);
            if (latestCurrentIndex >= 0)
            {
                startIndex = latestCurrentIndex;
                startReason = $"live-current-anchor index={latestCurrentIndex} time={dataSet.Bars[latestCurrentIndex].Time:O}";
            }
            else
            {
                startIndex = Math.Min(Math.Max(0, lastUsableIndex), Math.Max(0, dataSet.Bars.Count - 1));
                startReason = $"live-current-anchor unavailable; fallback to last pre-force-flat usable index={startIndex} time={dataSet.Bars[startIndex].Time:O}; stale gate will block entries until fresh bars exist";
            }
        }
        else
        {
            startIndex = Math.Max(warmup, lastUsableIndex - replayWindow);
            startIndex = Math.Min(startIndex, Math.Max(0, lastUsableIndex));
            startReason = $"historical-replay startIndex={startIndex} lastUsableIndex={lastUsableIndex} replayWindow={replayWindow}";
        }

        int quantity = 0;
        decimal averagePrice = 0m;
        if (brokerSeed is not null && !brokerSeed.IsFlat)
        {
            quantity = brokerSeed.Quantity;
            averagePrice = brokerSeed.AverageCost;
        }
        else if (localSeed is not null && !localSeed.IsFlat)
        {
            quantity = localSeed.Quantity;
            averagePrice = localSeed.AveragePrice;
        }

        int entryBarIndex = quantity == 0 ? -1 : startIndex;
        var strategy = new SailorStrategyAdapter(settings, profile, dataSet.Symbol, dataSet.Timeframe);

        return new PaperSymbolSession(
            mode,
            dataSet.Symbol,
            dataSet.Timeframe,
            dataSet.SourcePath,
            dataSet.Bars,
            indicators,
            marketSnapshot,
            strategy,
            profile,
            safeForceFlatMinute,
            tradeOrigin,
            scannerSlotId,
            lifecyclePolicy,
            startIndex,
            quantity,
            averagePrice,
            entryBarIndex)
        {
            StartReason = startReason
        };
    }

    public SailorStrategyFrame NextFrame(SailorRuntimeState runtimeState, bool advanceCursor = true)
    {
        if (!_cursorPositionedOnFrame)
        {
            if (_cursor < _bars.Count - 1)
            {
                _cursor++;
            }

            _cursorPositionedOnFrame = true;
        }
        else if (advanceCursor && _cursor < _bars.Count - 1)
        {
            _cursor++;
        }

        LastFrameTime = _bars[_cursor].Time;

        return new SailorStrategyFrame(
            _mode,
            LastFrameTime.Value,
            Symbol,
            _timeframe,
            _bars.Take(_cursor + 1).ToArray(),
            _indicators.Take(_cursor + 1).ToArray(),
            MarketSnapshot,
            runtimeState);
    }

    public PaperLiveCandleRefreshResult ApplyLiveCandleRefresh(
        IReadOnlyList<BacktestBar> refreshedBars,
        DateTimeOffset observedUtc,
        int maxAgeMinutes,
        int futureToleranceMinutes)
    {
        DateTimeOffset? previousFrameTime = LastFrameTime;
        DateTimeOffset? previousLoadedLastTime = LastLoadedBarTime;

        if (refreshedBars.Count == 0)
        {
            return PaperLiveCandleRefreshResult.Failed(
                Symbol,
                previousFrameTime,
                previousLoadedLastTime,
                "SAILOR-059 live paper candle refresh returned no bars.");
        }

        IReadOnlyList<BacktestBar> normalizedBars = NormalizeBars(refreshedBars);
        int latestCurrentIndex = FindLatestCurrentSessionIndex(
            normalizedBars,
            observedUtc,
            _profile,
            _runtimeForceFlatMinute,
            Math.Max(1, maxAgeMinutes),
            Math.Max(0, futureToleranceMinutes));

        DateTimeOffset refreshedLastTime = normalizedBars[^1].Time;
        if (latestCurrentIndex < 0)
        {
            PaperLiveBarCurrentness latestCurrentness = PaperLiveBarCurrentness.Evaluate(
                refreshedLastTime,
                observedUtc,
                Math.Max(1, maxAgeMinutes),
                Math.Max(0, futureToleranceMinutes));

            return new PaperLiveCandleRefreshResult(
                Symbol,
                Success: true,
                Updated: false,
                Current: false,
                previousFrameTime,
                previousLoadedLastTime,
                refreshedLastTime,
                previousFrameTime,
                normalizedBars.Count,
                AppliedBarIndex: _cursorPositionedOnFrame ? _cursor : -1,
                $"SAILOR-059 refreshed history did not contain a current usable live bar. {latestCurrentness.ToEntryBlockReason(Math.Max(1, maxAgeMinutes))}",
                Warnings: Array.Empty<string>());
        }

        DateTimeOffset appliedTime = normalizedBars[latestCurrentIndex].Time;
        bool updated = !previousFrameTime.HasValue || appliedTime > previousFrameTime.Value || !previousLoadedLastTime.HasValue || refreshedLastTime > previousLoadedLastTime.Value;

        _bars = normalizedBars.ToList();
        _indicators = TechnicalIndicatorCalculator.Calculate(_bars).ToList();
        _cursor = Math.Clamp(latestCurrentIndex, 0, Math.Max(0, _bars.Count - 1));
        _cursorPositionedOnFrame = true;
        LastFrameTime = _bars[_cursor].Time;

        PaperLiveBarCurrentness appliedCurrentness = PaperLiveBarCurrentness.Evaluate(
            LastFrameTime.Value,
            observedUtc,
            Math.Max(1, maxAgeMinutes),
            Math.Max(0, futureToleranceMinutes));

        string message = updated
            ? $"SAILOR-059 live paper candle refresh advanced/anchored {Symbol} to {LastFrameTime:O}."
            : $"SAILOR-059 live paper candle refresh kept {Symbol} on current latest bar {LastFrameTime:O}.";

        return new PaperLiveCandleRefreshResult(
            Symbol,
            Success: true,
            Updated: updated,
            Current: appliedCurrentness.IsCurrent,
            previousFrameTime,
            previousLoadedLastTime,
            refreshedLastTime,
            LastFrameTime,
            _bars.Count,
            _cursor,
            message,
            Warnings: Array.Empty<string>());
    }


    public PaperLiveCandleRefreshResult ApplyLiveCandleRefreshFallback(
        DateTimeOffset observedUtc,
        int maxAgeMinutes,
        int futureToleranceMinutes,
        string failureMessage,
        IEnumerable<string>? warnings = null)
    {
        DateTimeOffset? previousFrameTime = LastFrameTime;
        DateTimeOffset? previousLoadedLastTime = LastLoadedBarTime == DateTimeOffset.MinValue
            ? null
            : LastLoadedBarTime;

        if (_bars.Count == 0)
        {
            return PaperLiveCandleRefreshResult.Failed(
                Symbol,
                previousFrameTime,
                previousLoadedLastTime,
                $"SAILOR-061 live refresh fallback could not run because no in-memory bars exist. {failureMessage}",
                warnings);
        }

        int fallbackIndex = _cursorPositionedOnFrame
            ? Math.Clamp(_cursor, 0, _bars.Count - 1)
            : Math.Clamp(_cursor + 1, 0, _bars.Count - 1);

        DateTimeOffset fallbackTime = _bars[fallbackIndex].Time;
        PaperLiveBarCurrentness currentness = PaperLiveBarCurrentness.Evaluate(
            fallbackTime,
            observedUtc,
            Math.Max(1, maxAgeMinutes),
            Math.Max(0, futureToleranceMinutes));

        if (!currentness.IsCurrent)
        {
            return new PaperLiveCandleRefreshResult(
                Symbol,
                Success: false,
                Updated: false,
                Current: false,
                previousFrameTime,
                previousLoadedLastTime,
                RefreshedLastTime: null,
                AppliedFrameTime: fallbackTime,
                RefreshedBarCount: 0,
                AppliedBarIndex: fallbackIndex,
                $"SAILOR-061 live refresh fallback refused stale in-memory bar after refresh failure. {currentness.ToEntryBlockReason(Math.Max(1, maxAgeMinutes))} Refresh failure: {failureMessage}",
                Warnings: warnings?.ToArray() ?? Array.Empty<string>());
        }

        _cursor = fallbackIndex;
        _cursorPositionedOnFrame = true;
        LastFrameTime = fallbackTime;

        return new PaperLiveCandleRefreshResult(
            Symbol,
            Success: true,
            Updated: false,
            Current: true,
            previousFrameTime,
            previousLoadedLastTime,
            RefreshedLastTime: null,
            AppliedFrameTime: fallbackTime,
            RefreshedBarCount: 0,
            AppliedBarIndex: fallbackIndex,
            $"SAILOR-061 live refresh fallback reused current in-memory bar for {Symbol} at {fallbackTime:O} after refresh failure; {currentness.Reason}. Refresh failure: {failureMessage}",
            Warnings: warnings?.ToArray() ?? Array.Empty<string>());
    }

    public PaperLiveBarCurrentness AssessLiveBarCurrentness(
        DateTimeOffset observedUtc,
        int maxAgeMinutes,
        int futureToleranceMinutes)
    {
        if (LastFrameTime is null)
        {
            return PaperLiveBarCurrentness.Stale(
                observedUtc,
                observedUtc,
                int.MaxValue,
                "no strategy frame has been produced yet");
        }

        return PaperLiveBarCurrentness.Evaluate(
            LastFrameTime.Value,
            observedUtc,
            maxAgeMinutes,
            futureToleranceMinutes);
    }


    private static IReadOnlyList<BacktestBar> NormalizeBars(IReadOnlyList<BacktestBar> bars)
    {
        var byMinute = new SortedDictionary<DateTimeOffset, BacktestBar>();
        foreach (BacktestBar bar in bars)
        {
            byMinute[bar.Time] = bar;
        }

        return byMinute.Values.ToArray();
    }


    private static int FindLastUsableRegularSessionIndex(
        IReadOnlyList<BacktestBar> bars,
        SailorStrategyProfile profile,
        int forceFlatMinute)
    {
        if (bars.Count == 0)
        {
            return 0;
        }

        int firstEntryMinute = profile.UseMarketHours
            ? profile.MarketOpenMinute + Math.Max(0, profile.SkipFirstMinutes)
            : 0;

        int lastEntryOrForceMinute = forceFlatMinute > 0
            ? forceFlatMinute - 1
            : 24 * 60 - 1;

        for (int i = bars.Count - 1; i >= 0; i--)
        {
            int minute = MarketTime.GetEasternMinuteOfDay(bars[i].Time);
            if (minute >= firstEntryMinute && minute <= lastEntryOrForceMinute)
            {
                return i;
            }
        }

        return bars.Count - 1;
    }

    private static int FindLatestCurrentSessionIndex(
        IReadOnlyList<BacktestBar> bars,
        DateTimeOffset observedUtc,
        SailorStrategyProfile profile,
        int forceFlatMinute,
        int maxAgeMinutes,
        int futureToleranceMinutes)
    {
        if (bars.Count == 0)
        {
            return -1;
        }

        DateOnly currentDate = MarketTime.GetEasternDate(observedUtc);
        int firstEntryMinute = profile.UseMarketHours
            ? profile.MarketOpenMinute + Math.Max(0, profile.SkipFirstMinutes)
            : 0;
        int latestAllowedMinute = forceFlatMinute > 0
            ? Math.Min(forceFlatMinute - 1, MarketTime.GetEasternMinuteOfDay(observedUtc))
            : MarketTime.GetEasternMinuteOfDay(observedUtc);
        TimeSpan maxAge = TimeSpan.FromMinutes(Math.Max(1, maxAgeMinutes));

        for (int i = bars.Count - 1; i >= 0; i--)
        {
            BacktestBar bar = bars[i];
            if (MarketTime.GetEasternDate(bar.Time) != currentDate)
            {
                continue;
            }

            int minute = MarketTime.GetEasternMinuteOfDay(bar.Time);
            if (minute < firstEntryMinute || minute > latestAllowedMinute)
            {
                continue;
            }

            TimeSpan age = observedUtc - bar.Time.ToUniversalTime();
            if (age < TimeSpan.FromMinutes(-Math.Max(0, futureToleranceMinutes)) || age > maxAge)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    public SailorStrategyPositionContext ToPositionContext()
        => HasOpenPosition
            ? new SailorStrategyPositionContext(true, PositionQuantity, AveragePrice, EntryBarIndex)
            : SailorStrategyPositionContext.Flat;

    public StrategyLifecycleEntryDecision EvaluateEntryPolicy(int easternMinuteOfDay, int lastEntryMinute)
        => LifecyclePolicy.EvaluateEntry(
            TradeOrigin,
            ScannerSlotActive,
            LifecycleClosedForEntry,
            easternMinuteOfDay,
            lastEntryMinute);

    public void MarkLifecycleClosedAfterStrategyExit(string reason)
    {
        if (LifecycleClosedForEntry)
        {
            return;
        }

        LifecycleClosedForEntry = true;
        LifecycleClosedReason = string.IsNullOrWhiteSpace(reason) ? "strategy-exit" : reason.Trim();
    }

    public string PositionDisplay()
    {
        string side = PositionQuantity > 0 ? "LONG" : PositionQuantity < 0 ? "SHORT" : "FLAT";
        string slot = string.IsNullOrWhiteSpace(ScannerSlotId) ? "slot=n/a" : $"slot={ScannerSlotId}";
        string lifecycleClosed = LifecycleClosedForEntry ? " entryClosed=True" : string.Empty;
        return $"{Symbol} {side} qty={PositionQuantity} avg={AveragePrice:F4} entryBar={EntryBarIndex} origin={TradeOrigin.ToDisplayName()} {slot} lifecycle={LifecyclePolicy.Mode.ToDisplayName()}{lifecycleClosed}";
    }

    public bool SyncBrokerPosition(int brokerQuantity, decimal brokerAveragePrice, out string message)
    {
        if (PositionQuantity == brokerQuantity && AveragePrice == brokerAveragePrice)
        {
            message = $"SAILOR-062 broker position already synchronized for {Symbol}: qty={brokerQuantity} avg={brokerAveragePrice:F4}.";
            return false;
        }

        int previousQuantity = PositionQuantity;
        decimal previousAveragePrice = AveragePrice;
        PositionQuantity = brokerQuantity;
        AveragePrice = brokerQuantity == 0 ? 0m : brokerAveragePrice;

        if (previousQuantity == 0 && brokerQuantity != 0)
        {
            EntryBarIndex = Math.Clamp(_cursor, 0, Math.Max(0, _bars.Count - 1));
            LifecycleClosedForEntry = false;
            LifecycleClosedReason = null;
        }
        else if (brokerQuantity == 0)
        {
            EntryBarIndex = -1;
        }

        message = $"SAILOR-062 synchronized broker/manual position {Symbol}: oldQty={previousQuantity} oldAvg={previousAveragePrice:F4} brokerQty={brokerQuantity} brokerAvg={brokerAveragePrice:F4}.";
        return true;
    }

    public bool ApplyReceipt(
        SailorOrderIntent intent,
        SailorOrderReceipt receipt,
        decimal fallbackFillPrice,
        bool assumeDryRunFill,
        int currentBarIndex,
        out string message)
    {
        int fillQuantity = receipt.FilledQuantity;
        decimal fillPrice = receipt.AverageFillPrice > 0m ? receipt.AverageFillPrice : fallbackFillPrice;

        if (assumeDryRunFill && receipt.Status == SailorOrderStatus.DryRun)
        {
            fillQuantity = intent.Quantity;
            fillPrice = fallbackFillPrice;
        }

        if (fillQuantity <= 0)
        {
            message = $"No position update for {intent.NormalizedSymbol}; receipt status={receipt.Status} filled={receipt.FilledQuantity}.";
            return false;
        }

        int signedFill = intent.Side switch
        {
            SailorOrderSide.Buy => fillQuantity,
            SailorOrderSide.BuyToCover => fillQuantity,
            SailorOrderSide.Sell => -fillQuantity,
            SailorOrderSide.SellShort => -fillQuantity,
            _ => 0
        };

        if (signedFill == 0)
        {
            message = $"No position update for unsupported side {intent.Side}.";
            return false;
        }

        int oldQuantity = PositionQuantity;
        int newQuantity = oldQuantity + signedFill;

        if (oldQuantity == 0 || Math.Sign(oldQuantity) == Math.Sign(signedFill))
        {
            decimal oldNotional = Math.Abs(oldQuantity) * AveragePrice;
            decimal fillNotional = Math.Abs(fillQuantity) * fillPrice;
            AveragePrice = Math.Abs(newQuantity) > 0
                ? (oldNotional + fillNotional) / Math.Abs(newQuantity)
                : 0m;
        }
        else if (newQuantity == 0)
        {
            AveragePrice = 0m;
        }
        else if (Math.Sign(newQuantity) != Math.Sign(oldQuantity))
        {
            AveragePrice = fillPrice;
        }

        PositionQuantity = newQuantity;
        if (oldQuantity == 0 && newQuantity != 0)
        {
            EntryBarIndex = currentBarIndex;
        }
        else if (newQuantity == 0)
        {
            EntryBarIndex = -1;
        }

        message = $"Position update {Symbol}: oldQty={oldQuantity} fill={signedFill} newQty={PositionQuantity} avg={AveragePrice:F4}.";
        return true;
    }
}
