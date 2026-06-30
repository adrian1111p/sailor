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
    private readonly IReadOnlyList<BacktestBar> _bars;
    private readonly IReadOnlyList<BacktestIndicatorSnapshot> _indicators;
    private readonly SailorRuntimeMode _mode;
    private readonly string _timeframe;
    private int _cursor;

    private PaperSymbolSession(
        SailorRuntimeMode mode,
        string symbol,
        string timeframe,
        string dataSourcePath,
        IReadOnlyList<BacktestBar> bars,
        IReadOnlyList<BacktestIndicatorSnapshot> indicators,
        SailorMarketSnapshot? marketSnapshot,
        SailorStrategyAdapter strategy,
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
        _bars = bars;
        _indicators = indicators;
        MarketSnapshot = marketSnapshot;
        Strategy = strategy;
        TradeOrigin = tradeOrigin;
        ScannerSlotId = string.IsNullOrWhiteSpace(scannerSlotId) ? null : scannerSlotId.Trim();
        LifecyclePolicy = lifecyclePolicy;
        _cursor = Math.Clamp(startIndex - 1, 0, Math.Max(0, bars.Count - 1));
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

    public int PositionQuantity { get; private set; }

    public decimal AveragePrice { get; private set; }

    public int EntryBarIndex { get; private set; }

    public bool HasOpenPosition => PositionQuantity != 0;

    public int PositionSide => PositionQuantity < 0 ? -1 : PositionQuantity > 0 ? 1 : 0;

    public int AbsoluteQuantity => Math.Abs(PositionQuantity);

    public bool HasMoreBars => _cursor < _bars.Count - 1;

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
        int maxIterations)
    {
        var provider = new CsvBacktestDataProvider();
        BacktestDataSet dataSet = provider.LoadBars(symbol, timeframe);
        IReadOnlyList<BacktestIndicatorSnapshot> indicators = TechnicalIndicatorCalculator.Calculate(dataSet.Bars);

        int warmup = Math.Max(25, Math.Max(profile.ScannerMinimumBars, profile.ScannerLookbackBars));
        int replayWindow = Math.Max(1, maxIterations) + 5;
        int lastUsableIndex = FindLastUsableRegularSessionIndex(dataSet.Bars, profile);
        int startIndex = Math.Max(warmup, lastUsableIndex - replayWindow);
        startIndex = Math.Min(startIndex, Math.Max(0, lastUsableIndex));

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
            tradeOrigin,
            scannerSlotId,
            lifecyclePolicy,
            startIndex,
            quantity,
            averagePrice,
            entryBarIndex);
    }

    public SailorStrategyFrame NextFrame(SailorRuntimeState runtimeState)
    {
        if (_cursor < _bars.Count - 1)
        {
            _cursor++;
        }

        return new SailorStrategyFrame(
            _mode,
            _bars[_cursor].Time,
            Symbol,
            _timeframe,
            _bars.Take(_cursor + 1).ToArray(),
            _indicators.Take(_cursor + 1).ToArray(),
            MarketSnapshot,
            runtimeState);
    }


    private static int FindLastUsableRegularSessionIndex(
        IReadOnlyList<BacktestBar> bars,
        SailorStrategyProfile profile)
    {
        if (bars.Count == 0)
        {
            return 0;
        }

        int firstEntryMinute = profile.UseMarketHours
            ? profile.MarketOpenMinute + Math.Max(0, profile.SkipFirstMinutes)
            : 0;

        int lastEntryOrForceMinute = profile.ForceFlatMinute > 0
            ? profile.ForceFlatMinute - 1
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
