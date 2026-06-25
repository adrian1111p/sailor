using Sailor.App.Backtest.Engine;
using Sailor.App.Backtest.Indicators;

namespace Sailor.App.Backtest.Strategies;

public sealed class V21Config
{
    public double RiskPerTradeDollars { get; set; } = 25.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.20;
    public int MaxShares { get; set; } = 10_000;
    public double CommissionPerShare { get; set; } = 0.005;
    public double AngleThresholdDegrees { get; set; } = 12.0;
    public double StopAtrMultiplier { get; set; } = 1.0;
    public double MinimumRiskPerShare { get; set; } = 0.05;
    public int MarketOpenMinuteEt { get; set; } = 570;
    public int LastEntryMinuteEt { get; set; } = 945;
    public int EodFlattenMinuteEt { get; set; } = 955;
    public int MinimumBars15mForSignal { get; set; } = 9;
    public string DiagnosticsLabel { get; set; } = "default";
}

public sealed class StrategyV21 : BacktestStrategyBase, IBacktestLifecycleStrategy, IBacktestPostTradeSignalGate
{
    private const string StrategyId = "V21_15MINUTES";
    private const string SetupId = "V21_EMA_ANGLE_15M";
    private readonly V21Config _cfg;
    private readonly Dictionary<string, SignalPlan> _signalPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BacktestSignalRetryLockout> _retryLockouts = new(StringComparer.Ordinal);
    private EnrichedBar[] _bars1m = Array.Empty<EnrichedBar>();
    private EnrichedBar[] _bars15m = Array.Empty<EnrichedBar>();

    public StrategyV21(V21Config? cfg = null)
    {
        _cfg = cfg ?? new V21Config();
    }

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        ArgumentNullException.ThrowIfNull(bars1m);

        _bars1m = bars1m;
        _bars15m = Resolve15mBars(bars1m, bars15m);
        _signalPlans.Clear();
        _retryLockouts.Clear();

        if (_bars1m.Length == 0 || _bars15m.Length < 2)
        {
            return Array.Empty<BacktestSignal>();
        }

        var signals = new List<BacktestSignal>();
        for (int i = 1; i < _bars15m.Length; i++)
        {
            if (!TryBuildSignal(i, out var signal, out var plan))
            {
                continue;
            }

            signals.Add(signal);
            _signalPlans[BuildSignalKey(signal)] = plan;
        }

        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => SimulateAcceptedEntryIntent(CreateSelectedEntryIntent(signal, "15m"), triggerBars).Trade;

    public BacktestSelectedEntryIntent CreateSelectedEntryIntent(BacktestSignal signal, string triggerTimeframe)
    {
        var exitConfig = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.StopAtrMultiplier,
            MaxHoldBars = 26,
            SlippageCents = 0.0,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
        };

        return new BacktestSelectedEntryIntent(
            IntentId: BuildSignalKey(signal),
            Signal: signal,
            ExitProfile: ExitEngine.ToNormalizedExitProfile(exitConfig),
            LifecycleMetadata: new BacktestStrategyLifecycleMetadata(
                StrategyName: "StrategyV21",
                StrategyVersion: "V21",
                Symbol: Symbol ?? string.Empty,
                TriggerTimeframe: string.IsNullOrWhiteSpace(triggerTimeframe) ? "15m" : triggerTimeframe,
                SubStrategy: signal.SubStrategy,
                EntryScore: signal.EntryScore));
    }

    public BacktestTradeLifecycleResult SimulateAcceptedEntryIntent(BacktestSelectedEntryIntent selectedEntryIntent, EnrichedBar[] triggerBars)
    {
        ArgumentNullException.ThrowIfNull(selectedEntryIntent);
        ArgumentNullException.ThrowIfNull(triggerBars);

        var signal = selectedEntryIntent.Signal;
        var signalKey = BuildSignalKey(signal);
        var source15mIndex = ResolveSource15mIndex(signal);
        var exitResolution = ResolveExit(signal, source15mIndex);

        if (exitResolution.RetryLockout is not null)
        {
            _retryLockouts[signalKey] = exitResolution.RetryLockout;
        }
        else
        {
            _retryLockouts.Remove(signalKey);
        }

        var exitBarIndex = Math.Clamp(exitResolution.ExitBarIndex, signal.BarIndex, triggerBars.Length - 1);
        var exitBar = triggerBars[exitBarIndex].Bar;
        var exitPrice = ResolveExitPrice(triggerBars, exitBarIndex);

        ComputeExcursionBounds(triggerBars, signal.BarIndex, exitBarIndex, out var peakPrice, out var troughPrice);

        double grossPnl = signal.Side == TradeSide.Long
            ? (exitPrice - signal.EntryPrice) * signal.PositionSize
            : (signal.EntryPrice - exitPrice) * signal.PositionSize;
        double commission = _cfg.CommissionPerShare * signal.PositionSize * 2.0;
        double pnl = grossPnl - commission;
        double pnlR = signal.PositionSize > 0 && signal.RiskPerShare > 0
            ? pnl / (signal.RiskPerShare * signal.PositionSize)
            : 0.0;
        double peakR = signal.RiskPerShare > 0
            ? signal.Side == TradeSide.Long
                ? (peakPrice - signal.EntryPrice) / signal.RiskPerShare
                : (signal.EntryPrice - troughPrice) / signal.RiskPerShare
            : 0.0;

        var lifecycleEvents = BuildLifecycleEvents(signal, selectedEntryIntent, source15mIndex, exitResolution, exitBarIndex, exitBar.Timestamp, exitPrice, pnlR);
        var replayActions = BuildReplayActions(signal, exitBarIndex, exitBar.Timestamp, exitPrice, exitResolution.ExitReason, pnlR);
        var finalState = new BacktestTradeLifecycleState(
            OriginalQuantity: signal.PositionSize,
            OpenQuantity: 0,
            PeakPrice: peakPrice,
            TroughPrice: troughPrice,
            StopPrice: signal.StopPrice,
            TrailingStop: signal.StopPrice,
            BreakevenActivated: false,
            Tp1Activated: false,
            ProfitExtensionArmed: false,
            ContinuationTp2ScaleOutTaken: false,
            TrailingTp2Active: false,
            TrailingTp2Stop: null,
            GrossPnl: grossPnl,
            WeightedExitPrice: exitPrice);

        var trade = new BacktestTradeResult(
            EntryBar: signal.BarIndex,
            EntryTime: signal.Timestamp,
            ExitBar: exitBarIndex,
            ExitTime: exitBar.Timestamp,
            Side: signal.Side,
            EntryPrice: signal.EntryPrice,
            ExitPrice: exitPrice,
            StopPrice: signal.StopPrice,
            PositionSize: signal.PositionSize,
            Pnl: pnl,
            PnlR: pnlR,
            ExitReason: exitResolution.ExitReason,
            PeakR: peakR,
            BarsHeld: Math.Max(1, exitBarIndex - signal.BarIndex),
            SubStrategy: signal.SubStrategy,
            ReplayActions: replayActions,
            SelectedEntryIntent: selectedEntryIntent,
            LifecycleFinalState: finalState,
            LifecycleEvents: lifecycleEvents);

        return new BacktestTradeLifecycleResult(
            signal,
            trade,
            finalState,
            lifecycleEvents);
    }

    public BacktestSignalRetryLockout? GetRetryLockout(BacktestSignal signal, BacktestTradeResult trade)
    {
        var key = BuildSignalKey(signal);
        return _retryLockouts.TryGetValue(key, out var lockout)
            ? lockout
            : null;
    }

    private bool TryBuildSignal(int index, out BacktestSignal signal, out SignalPlan plan)
    {
        signal = null!;
        plan = null!;

        if (index < _cfg.MinimumBars15mForSignal - 1)
        {
            return false;
        }

        var row = _bars15m[index];
        int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
        if (minuteEt < _cfg.MarketOpenMinuteEt || minuteEt > _cfg.LastEntryMinuteEt)
        {
            return false;
        }

        if (!TryGetAngleDegrees(index, out var angleDegrees))
        {
            return false;
        }

        var side = ResolveEntrySide(angleDegrees);
        if (side is null)
        {
            return false;
        }

        if (!TryResolveEntryBarIndex(row.Bar.Timestamp, out var entryBarIndex))
        {
            return false;
        }

        var entryRow = _bars1m[entryBarIndex];
        double atr = !double.IsNaN(row.Atr14) && row.Atr14 > 0
            ? row.Atr14
            : entryRow.Atr14;
        if (double.IsNaN(atr) || atr <= 0)
        {
            return false;
        }

        double entryPrice = entryRow.Bar.Open > 0 ? entryRow.Bar.Open : entryRow.Bar.Close;
        if (entryPrice <= 0)
        {
            return false;
        }

        double riskPerShare = Math.Max(_cfg.MinimumRiskPerShare, atr * _cfg.StopAtrMultiplier);
        double stopPrice = side == TradeSide.Long
            ? entryPrice - riskPerShare
            : entryPrice + riskPerShare;

        int positionSize = BacktestHelpers.ComputePositionSize(
            entryPrice,
            riskPerShare,
            _cfg.RiskPerTradeDollars,
            _cfg.AccountSize,
            _cfg.MaxPositionNotionalPctOfAccount,
            _cfg.MaxShares);
        positionSize = Math.Min(_cfg.MaxShares, ApplySelfLearningPositionSize(positionSize, StrategyId));
        if (positionSize <= 0)
        {
            return false;
        }

        signal = new BacktestSignal(
            BarIndex: entryBarIndex,
            Timestamp: entryRow.Bar.Timestamp,
            Side: side.Value,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: positionSize,
            AtrValue: atr,
            HtfTrend: HtfBias.Neutral,
            MtfMomentum: side == TradeSide.Long ? "EMA15_RISING" : "EMA15_FALLING",
            SubStrategy: SetupId,
            EntryScore: Math.Max(1, (int)Math.Round(Math.Abs(angleDegrees))));

        plan = new SignalPlan(index, angleDegrees);
        return true;
    }

    private ExitResolution ResolveExit(BacktestSignal signal, int source15mIndex)
    {
        var entryDate = TradingTime.GetDateEt(signal.Timestamp);
        int defaultExitBarIndex = ResolveEodExitBarIndex(signal.BarIndex, entryDate);
        var defaultResolution = new ExitResolution(defaultExitBarIndex, ExitReason.Eod, null, "end-of-day-flatten", null);

        for (int i = source15mIndex + 1; i < _bars15m.Length; i++)
        {
            var row = _bars15m[i];
            if (TradingTime.GetDateEt(row.Bar.Timestamp) != entryDate)
            {
                break;
            }

            if (!TryResolveExitBarIndex(row.Bar.Timestamp, entryDate, out var exitBarIndex))
            {
                continue;
            }

            if (!TryGetAngleDegrees(i, out var angleDegrees))
            {
                continue;
            }

            if (signal.Side == TradeSide.Long)
            {
                if (angleDegrees <= -_cfg.AngleThresholdDegrees)
                {
                    return new ExitResolution(exitBarIndex, ExitReason.SignalReversal, i, $"ema-angle-reversal:{angleDegrees:F2}", null);
                }

                if (angleDegrees < _cfg.AngleThresholdDegrees)
                {
                    return new ExitResolution(exitBarIndex, ExitReason.TrendChangeFlatten, i, $"ema-angle-neutral:{angleDegrees:F2}", null);
                }

                if (IsLongFlattenBar(i, out var referenceBarIndex))
                {
                    var lockout = BuildRetryLockout(signal.Side, referenceBarIndex, i, entryDate);
                    return new ExitResolution(exitBarIndex, ExitReason.ReversalFlatten, i, "long-reversal-bar", lockout);
                }
            }
            else
            {
                if (angleDegrees >= _cfg.AngleThresholdDegrees)
                {
                    return new ExitResolution(exitBarIndex, ExitReason.SignalReversal, i, $"ema-angle-reversal:{angleDegrees:F2}", null);
                }

                if (angleDegrees > -_cfg.AngleThresholdDegrees)
                {
                    return new ExitResolution(exitBarIndex, ExitReason.TrendChangeFlatten, i, $"ema-angle-neutral:{angleDegrees:F2}", null);
                }

                if (IsShortFlattenBar(i, out var referenceBarIndex))
                {
                    var lockout = BuildRetryLockout(signal.Side, referenceBarIndex, i, entryDate);
                    return new ExitResolution(exitBarIndex, ExitReason.ReversalFlatten, i, "short-reversal-bar", lockout);
                }
            }
        }

        return defaultResolution;
    }

    private BacktestSignalRetryLockout BuildRetryLockout(TradeSide side, int referenceBarIndex, int exit15mIndex, DateOnly entryDate)
    {
        for (int i = exit15mIndex + 1; i < _bars15m.Length; i++)
        {
            var row = _bars15m[i];
            if (TradingTime.GetDateEt(row.Bar.Timestamp) != entryDate)
            {
                break;
            }

            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt > _cfg.LastEntryMinuteEt)
            {
                break;
            }

            if (!TryGetAngleDegrees(i, out var angleDegrees))
            {
                continue;
            }

            bool reEntryConfirmed = side == TradeSide.Long
                ? angleDegrees >= _cfg.AngleThresholdDegrees && IsLongReEntryBar(i, referenceBarIndex)
                : angleDegrees <= -_cfg.AngleThresholdDegrees && IsShortReEntryBar(i, referenceBarIndex);
            if (!reEntryConfirmed)
            {
                continue;
            }

            if (!TryResolveEntryBarIndex(row.Bar.Timestamp, out var entryBarIndex))
            {
                continue;
            }

            return new BacktestSignalRetryLockout(side, entryBarIndex);
        }

        return new BacktestSignalRetryLockout(side, ResolveDayEndExclusiveBarIndex(entryDate));
    }

    private bool TryGetAngleDegrees(int index, out double angleDegrees)
    {
        angleDegrees = double.NaN;
        if (index < 1 || index >= _bars15m.Length)
        {
            return false;
        }

        var row = _bars15m[index];
        var prev = _bars15m[index - 1];
        if (double.IsNaN(row.Ema9) || double.IsNaN(prev.Ema9))
        {
            return false;
        }

        double atr = !double.IsNaN(row.Atr14) && row.Atr14 > 0
            ? row.Atr14
            : prev.Atr14;
        if (double.IsNaN(atr) || atr <= 0)
        {
            return false;
        }

        // Normalize EMA slope by 15m ATR so the degree threshold stays comparable across symbols.
        double normalizedSlope = (row.Ema9 - prev.Ema9) / atr;
        angleDegrees = Math.Atan(normalizedSlope) * (180.0 / Math.PI);
        return true;
    }

    private TradeSide? ResolveEntrySide(double angleDegrees)
    {
        if (angleDegrees >= _cfg.AngleThresholdDegrees)
        {
            return TradeSide.Long;
        }

        if (angleDegrees <= -_cfg.AngleThresholdDegrees)
        {
            return TradeSide.Short;
        }

        return null;
    }

    private bool IsLongFlattenBar(int index, out int referenceBarIndex)
    {
        referenceBarIndex = index;
        var row = _bars15m[index];
        if (!IsBearish(row))
        {
            return false;
        }

        if (CrossesEma9(row))
        {
            return true;
        }

        int lastGreenIndex = FindPreviousBullishBarIndex(index);
        if (lastGreenIndex < 0)
        {
            return false;
        }

        var lastGreen = _bars15m[lastGreenIndex];
        return row.Bar.Low <= lastGreen.Bar.Low || row.Bar.Close <= lastGreen.Bar.Open;
    }

    private bool IsShortFlattenBar(int index, out int referenceBarIndex)
    {
        referenceBarIndex = index;
        var row = _bars15m[index];
        if (!IsBullish(row))
        {
            return false;
        }

        if (CrossesEma9(row))
        {
            return true;
        }

        int lastRedIndex = FindPreviousBearishBarIndex(index);
        if (lastRedIndex < 0)
        {
            return false;
        }

        var lastRed = _bars15m[lastRedIndex];
        return row.Bar.High >= lastRed.Bar.High || row.Bar.Close >= lastRed.Bar.Open;
    }

    private bool IsLongReEntryBar(int index, int referenceBarIndex)
    {
        var row = _bars15m[index];
        var referenceBar = _bars15m[referenceBarIndex];
        if (!IsBullish(row))
        {
            return false;
        }

        bool crossedOrRecovered = CrossesEma9(row) || row.Bar.Close >= row.Ema9;
        bool exceededReference = row.Bar.High > referenceBar.Bar.High || row.Bar.Close > referenceBar.Bar.Open;
        return crossedOrRecovered && exceededReference;
    }

    private bool IsShortReEntryBar(int index, int referenceBarIndex)
    {
        var row = _bars15m[index];
        var referenceBar = _bars15m[referenceBarIndex];
        if (!IsBearish(row))
        {
            return false;
        }

        bool crossedOrRecovered = CrossesEma9(row) || row.Bar.Close <= row.Ema9;
        bool exceededReference = row.Bar.Low < referenceBar.Bar.Low || row.Bar.Close < referenceBar.Bar.Open;
        return crossedOrRecovered && exceededReference;
    }

    private static bool IsBullish(EnrichedBar row) => row.Bar.Close > row.Bar.Open;

    private static bool IsBearish(EnrichedBar row) => row.Bar.Close < row.Bar.Open;

    private static bool CrossesEma9(EnrichedBar row)
        => !double.IsNaN(row.Ema9) && row.Bar.Low <= row.Ema9 && row.Bar.High >= row.Ema9;

    private int FindPreviousBullishBarIndex(int beforeExclusive)
    {
        var date = TradingTime.GetDateEt(_bars15m[beforeExclusive].Bar.Timestamp);
        for (int i = beforeExclusive - 1; i >= 0; i--)
        {
            if (TradingTime.GetDateEt(_bars15m[i].Bar.Timestamp) != date)
            {
                break;
            }

            if (IsBullish(_bars15m[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindPreviousBearishBarIndex(int beforeExclusive)
    {
        var date = TradingTime.GetDateEt(_bars15m[beforeExclusive].Bar.Timestamp);
        for (int i = beforeExclusive - 1; i >= 0; i--)
        {
            if (TradingTime.GetDateEt(_bars15m[i].Bar.Timestamp) != date)
            {
                break;
            }

            if (IsBearish(_bars15m[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private int ResolveSource15mIndex(BacktestSignal signal)
    {
        var signalKey = BuildSignalKey(signal);
        if (_signalPlans.TryGetValue(signalKey, out var plan))
        {
            return plan.Source15mIndex;
        }

        var entryDate = TradingTime.GetDateEt(signal.Timestamp);
        int candidate = BacktestHelpers.FindBarAtOrBefore(_bars15m, signal.Timestamp.AddTicks(-1));
        for (int i = candidate; i >= 0; i--)
        {
            if (TradingTime.GetDateEt(_bars15m[i].Bar.Timestamp) != entryDate)
            {
                break;
            }

            if (TryResolveEntryBarIndex(_bars15m[i].Bar.Timestamp, out var entryBarIndex) && entryBarIndex == signal.BarIndex)
            {
                return i;
            }
        }

        return Math.Max(0, candidate);
    }

    private bool TryResolveEntryBarIndex(DateTime source15mTimestamp, out int entryBarIndex)
    {
        entryBarIndex = FindFirstBarAfter(_bars1m, source15mTimestamp);
        if (entryBarIndex < 0 || entryBarIndex >= _bars1m.Length)
        {
            return false;
        }

        var entryDate = TradingTime.GetDateEt(source15mTimestamp);
        var entryBarDate = TradingTime.GetDateEt(_bars1m[entryBarIndex].Bar.Timestamp);
        if (entryBarDate != entryDate)
        {
            return false;
        }

        return TradingTime.GetMinuteOfDayEt(_bars1m[entryBarIndex].Bar.Timestamp) < _cfg.EodFlattenMinuteEt;
    }

    private bool TryResolveExitBarIndex(DateTime source15mTimestamp, DateOnly entryDate, out int exitBarIndex)
    {
        exitBarIndex = FindFirstBarAfter(_bars1m, source15mTimestamp);
        if (exitBarIndex < 0 || exitBarIndex >= _bars1m.Length)
        {
            return false;
        }

        return TradingTime.GetDateEt(_bars1m[exitBarIndex].Bar.Timestamp) == entryDate;
    }

    private int ResolveEodExitBarIndex(int entryBarIndex, DateOnly entryDate)
    {
        int lastSameDayIndex = Math.Clamp(entryBarIndex, 0, _bars1m.Length - 1);
        for (int i = lastSameDayIndex; i < _bars1m.Length; i++)
        {
            var row = _bars1m[i];
            var barDate = TradingTime.GetDateEt(row.Bar.Timestamp);
            if (barDate != entryDate)
            {
                break;
            }

            lastSameDayIndex = i;
            if (TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp) >= _cfg.EodFlattenMinuteEt)
            {
                return i;
            }
        }

        return lastSameDayIndex;
    }

    private int ResolveDayEndExclusiveBarIndex(DateOnly date)
    {
        int lastSameDayIndex = -1;
        for (int i = 0; i < _bars1m.Length; i++)
        {
            var barDate = TradingTime.GetDateEt(_bars1m[i].Bar.Timestamp);
            if (barDate < date)
            {
                continue;
            }

            if (barDate > date)
            {
                break;
            }

            lastSameDayIndex = i;
        }

        return lastSameDayIndex >= 0 ? lastSameDayIndex + 1 : _bars1m.Length;
    }

    private static int FindFirstBarAfter(EnrichedBar[] bars, DateTime timestamp)
    {
        int lo = 0;
        int hi = bars.Length - 1;
        int best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (bars[mid].Bar.Timestamp > timestamp)
            {
                best = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return best;
    }

    private static double ResolveExitPrice(EnrichedBar[] bars1m, int exitBarIndex)
    {
        var row = bars1m[exitBarIndex].Bar;
        return row.Open > 0 ? row.Open : row.Close;
    }

    private static void ComputeExcursionBounds(EnrichedBar[] bars, int entryBarIndex, int exitBarIndex, out double peakPrice, out double troughPrice)
    {
        peakPrice = double.MinValue;
        troughPrice = double.MaxValue;

        for (int i = Math.Max(0, entryBarIndex); i <= Math.Clamp(exitBarIndex, entryBarIndex, bars.Length - 1); i++)
        {
            peakPrice = Math.Max(peakPrice, bars[i].Bar.High);
            troughPrice = Math.Min(troughPrice, bars[i].Bar.Low);
        }

        if (peakPrice == double.MinValue)
        {
            peakPrice = bars[Math.Clamp(entryBarIndex, 0, bars.Length - 1)].Bar.High;
        }

        if (troughPrice == double.MaxValue)
        {
            troughPrice = bars[Math.Clamp(entryBarIndex, 0, bars.Length - 1)].Bar.Low;
        }
    }

    private IReadOnlyList<BacktestTradeLifecycleEvent> BuildLifecycleEvents(
        BacktestSignal signal,
        BacktestSelectedEntryIntent selectedEntryIntent,
        int source15mIndex,
        ExitResolution exitResolution,
        int exitBarIndex,
        DateTime exitTimestamp,
        double exitPrice,
        double pnlR)
    {
        var events = new List<BacktestTradeLifecycleEvent>
        {
            new(
                EventType: BacktestTradeLifecycleEventType.EntryAccepted,
                BarIndex: signal.BarIndex,
                Timestamp: signal.Timestamp,
                Price: signal.EntryPrice,
                Quantity: signal.PositionSize,
                Reason: "entry-accepted",
                Detail: $"15m EMA angle {ResolvePlanAngle(source15mIndex):F2} triggered {signal.Side} entry."),
            new(
                EventType: BacktestTradeLifecycleEventType.EntryFilled,
                BarIndex: signal.BarIndex,
                Timestamp: signal.Timestamp,
                Price: signal.EntryPrice,
                Quantity: signal.PositionSize,
                Reason: "entry-filled",
                Detail: $"Filled using the next 1m bar after 15m close for {selectedEntryIntent.LifecycleMetadata.Symbol}.")
        };

        if (exitResolution.Exit15mIndex is int exit15mIndex)
        {
            var exit15mBar = _bars15m[exit15mIndex];
            events.Add(new(
                EventType: BacktestTradeLifecycleEventType.StateTransition,
                BarIndex: exitBarIndex,
                Timestamp: exitTimestamp,
                Price: exitPrice,
                Quantity: signal.PositionSize,
                Reason: exitResolution.Detail,
                Detail: $"15m bar {exit15mBar.Bar.Timestamp:O} triggered {exitResolution.ExitReason}.",
                ReferencePrice: !double.IsNaN(exit15mBar.Ema9) ? exit15mBar.Ema9 : null,
                RMultiple: pnlR));
        }

        events.Add(new(
            EventType: BacktestTradeLifecycleEventType.TradeClosed,
            BarIndex: exitBarIndex,
            Timestamp: exitTimestamp,
            Price: exitPrice,
            Quantity: signal.PositionSize,
            Reason: exitResolution.ExitReason.ToString(),
            Detail: $"Closed {signal.Side} at {exitPrice:F4}.",
            RMultiple: pnlR));
        events.Add(new(
            EventType: BacktestTradeLifecycleEventType.Finalized,
            BarIndex: exitBarIndex,
            Timestamp: exitTimestamp,
            Price: exitPrice,
            Quantity: signal.PositionSize,
            Reason: "trade-finalized",
            Detail: $"V21 finalized with {exitResolution.ExitReason}.",
            RMultiple: pnlR));

        return events
            .OrderBy(evt => evt.BarIndex)
            .ThenBy(evt => evt.Timestamp)
            .ToArray();
    }

    private static IReadOnlyList<BacktestTradeAction> BuildReplayActions(
        BacktestSignal signal,
        int exitBarIndex,
        DateTime exitTimestamp,
        double exitPrice,
        ExitReason exitReason,
        double pnlR)
    {
        return new[]
        {
            new BacktestTradeAction(
                BarIndex: signal.BarIndex,
                Timestamp: signal.Timestamp,
                Price: signal.EntryPrice,
                ActionType: signal.Side == TradeSide.Long ? "BUY" : "SELL_SHORT",
                Description: "15m EMA angle entry",
                ReferencePrice: signal.StopPrice,
                RMultiple: 0.0),
            new BacktestTradeAction(
                BarIndex: exitBarIndex,
                Timestamp: exitTimestamp,
                Price: exitPrice,
                ActionType: signal.Side == TradeSide.Long ? "SELL" : "BUY_TO_COVER",
                Description: exitReason.ToString(),
                ReferencePrice: signal.StopPrice,
                RMultiple: pnlR),
        };
    }

    private double ResolvePlanAngle(int source15mIndex)
    {
        return _signalPlans.Values.FirstOrDefault(plan => plan.Source15mIndex == source15mIndex)?.AngleDegrees
            ?? 0.0;
    }

    private static string BuildSignalKey(BacktestSignal signal)
        => $"{signal.BarIndex}|{signal.Timestamp:O}|{signal.Side}";

    private EnrichedBar[] Resolve15mBars(EnrichedBar[] bars1m, EnrichedBar[]? bars15m)
    {
        if (bars15m is { Length: > 0 })
        {
            return bars15m;
        }

        var resampledBars = Build15mBars(bars1m);
        return resampledBars.Length == 0
            ? Array.Empty<EnrichedBar>()
            : TechnicalIndicators.EnrichWithIndicators(resampledBars);
    }

    private static BacktestBar[] Build15mBars(EnrichedBar[] bars1m)
    {
        var resampled = new List<BacktestBar>();
        if (bars1m.Length == 0)
        {
            return resampled.ToArray();
        }

        DateOnly? bucketDate = null;
        int bucketId = -1;
        double open = 0.0;
        double high = double.MinValue;
        double low = double.MaxValue;
        double close = 0.0;
        double volume = 0.0;
        DateTime bucketTimestamp = default;
        bool bucketStarted = false;

        void Flush()
        {
            if (!bucketStarted)
            {
                return;
            }

            resampled.Add(new BacktestBar(bucketTimestamp, open, high, low, close, volume));
            bucketStarted = false;
            high = double.MinValue;
            low = double.MaxValue;
            volume = 0.0;
        }

        foreach (var row in bars1m)
        {
            var timestamp = row.Bar.Timestamp;
            var dateEt = TradingTime.GetDateEt(timestamp);
            int minuteEt = TradingTime.GetMinuteOfDayEt(timestamp);
            int currentBucketId = minuteEt / 15;

            if (!bucketStarted || bucketDate != dateEt || currentBucketId != bucketId)
            {
                Flush();
                bucketStarted = true;
                bucketDate = dateEt;
                bucketId = currentBucketId;
                open = row.Bar.Open;
                high = row.Bar.High;
                low = row.Bar.Low;
                close = row.Bar.Close;
                volume = row.Bar.Volume;
                bucketTimestamp = timestamp;
                continue;
            }

            high = Math.Max(high, row.Bar.High);
            low = Math.Min(low, row.Bar.Low);
            close = row.Bar.Close;
            volume += row.Bar.Volume;
            bucketTimestamp = timestamp;
        }

        Flush();
        return resampled.ToArray();
    }

    private sealed record SignalPlan(int Source15mIndex, double AngleDegrees);

    private sealed record ExitResolution(
        int ExitBarIndex,
        ExitReason ExitReason,
        int? Exit15mIndex,
        string Detail,
        BacktestSignalRetryLockout? RetryLockout);
}
