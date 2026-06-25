using Sailor.App.Backtest.Engine;
using Sailor.App.Backtest.Strategies;
using Harvester.App.Strategy;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sailor.App.Backtest;

/// <summary>Common interface for all backtest strategies.</summary>
public interface IBacktestStrategy
{
    /// <summary>Scan mandatory 1-minute entry bars plus optional higher-timeframe context and produce entry signals.</summary>
    IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null);

    /// <summary>Simulate a single trade from signal to exit.</summary>
    BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars);
}

/// <summary>
/// Optional contract for strategies that expose the accepted-entry handoff and lifecycle result explicitly.
/// </summary>
public interface IBacktestLifecycleStrategy
{
    /// <summary>Build the canonical accepted-entry intent after B-series filtering succeeds.</summary>
    BacktestSelectedEntryIntent CreateSelectedEntryIntent(BacktestSignal signal, string triggerTimeframe);

    /// <summary>Simulate the full lifecycle from an accepted entry intent through finalization.</summary>
    BacktestTradeLifecycleResult SimulateAcceptedEntryIntent(BacktestSelectedEntryIntent selectedEntryIntent, EnrichedBar[] triggerBars);
}

/// <summary>
/// Optional contract for strategies that want to suppress same-symbol retries after a completed trade.
/// </summary>
public interface IBacktestPostTradeSignalGate
{
    /// <summary>Return the next allowed bar index for a signal side after the trade completes, or <c>null</c> for no extra lockout.</summary>
    BacktestSignalRetryLockout? GetRetryLockout(BacktestSignal signal, BacktestTradeResult trade);
}

internal interface IBacktestDiagnosticsProvider
{
    void ResetDiagnostics();

    IReadOnlyList<string> GetDiagnosticsSummaryLines();
}

/// <summary>
/// Base type for concrete strategy versions to enforce a common contract and enable shared evolution.
/// </summary>
public abstract class BacktestStrategyBase : IBacktestStrategy
{
    /// <summary>Optional self-learning adapter injected by the runner.</summary>
    public SelfLearningSignalAdapter? SelfLearning { get; set; }

    /// <summary>Symbol being backtested, set by BacktestEngine before GenerateSignals.</summary>
    public string? Symbol { get; set; }

    public abstract IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null);

    public abstract BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars);

    /// <summary>Apply self-learning stop distance multiplier.</summary>
    protected double ApplySelfLearningStopMultiplier(double stopDist)
    {
        if (SelfLearning is not { IsLoaded: true }) return stopDist;
        return stopDist * SelfLearning.GetEffectiveStopMultiplier();
    }

    /// <summary>Apply self-learning position size multiplier (symbol + setup aware).</summary>
    protected int ApplySelfLearningPositionSize(int positionSize, string? subStrategy = null)
    {
        if (SelfLearning is not { IsLoaded: true }) return positionSize;
        return Math.Max(1, (int)(positionSize * SelfLearning.GetEffectivePositionSizeMultiplier(Symbol ?? "", subStrategy)));
    }

    /// <summary>Check if self-learning recommends blocking a setup.</summary>
    protected bool IsSelfLearningBlocked(string? subStrategy)
    {
        if (SelfLearning is not { IsLoaded: true }) return false;
        return SelfLearning.ShouldBlockSetup(subStrategy, out _);
    }

    /// <summary>
    /// Apply V3 per-setup exit overrides to a base ExitConfig.
    /// Returns the original config if no overrides exist, otherwise a new config with overrides applied.
    /// </summary>
    protected ExitEngine.ExitConfig ApplySelfLearningExitOverrides(ExitEngine.ExitConfig cfg, string? subStrategy = null)
    {
        if (SelfLearning is not { IsLoaded: true, IsV3: true }) return cfg;
        var overrides = SelfLearning.GetExitOverrides(subStrategy);
        if (overrides == null) return cfg;
        return cfg.WithOverrides(overrides);
    }

    /// <summary>Check if the current bar falls into a process-scoped blocked market regime.</summary>
    protected bool IsMarketRegimeBlocked(EnrichedBar row)
    {
        var raw = Environment.GetEnvironmentVariable("HARVESTER_BACKTEST_BLOCK_REGIMES");
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var blockedRegimes = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (blockedRegimes.Length == 0) return false;

        bool isChoppy = !double.IsNaN(row.Adx)
            && row.Adx < 20.0
            && (double.IsNaN(row.BbBandwidth) || row.BbBandwidth < 0.03);

        bool isVolatileAdverse = !double.IsNaN(row.BbBandwidth)
            && row.BbBandwidth > 0.06;

        foreach (var regime in blockedRegimes)
        {
            if (regime.Equals("CHOPPY", StringComparison.OrdinalIgnoreCase)
                || regime.Equals("RANGING", StringComparison.OrdinalIgnoreCase))
            {
                if (isChoppy) return true;
            }

            if (regime.Equals("VOLATILE_ADVERSE", StringComparison.OrdinalIgnoreCase)
                || regime.Equals("VOLATILE", StringComparison.OrdinalIgnoreCase))
            {
                if (isVolatileAdverse) return true;
            }
        }

        return false;
    }

    // â”€â”€ Static self-learning loader shared by all runners â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static SelfLearningSignalAdapter? s_cachedAdapter;
    private static bool s_loadAttempted;

    /// <summary>
    /// Load the latest self-learning recommendations from the default runtime search paths.
    /// Cached across all callers within the process.
    /// </summary>
    public static SelfLearningSignalAdapter? LoadSharedSelfLearning()
    {
        if (s_loadAttempted) return s_cachedAdapter;
        s_loadAttempted = true;

        var adapter = new SelfLearningSignalAdapter();
        if (adapter.TryLoadFromDirectories(SelfLearningSignalAdapter.GetDefaultSearchDirectories(), NullLogger.Instance))
        {
            Console.WriteLine($"[SelfLearning] Loaded: StopMult={adapter.StopDistanceMultiplier:F3} PosSizeMult={adapter.PositionSizeMultiplier:F3}");
            s_cachedAdapter = adapter;
        }
        else
        {
            Console.WriteLine("[SelfLearning] No recommendations found â€” running without adjustments.");
        }

        return s_cachedAdapter;
    }

    /// <summary>
    /// Inject the shared self-learning adapter into a strategy if it inherits from BacktestStrategyBase.
    /// </summary>
    public static void InjectSelfLearning(IBacktestStrategy strategy)
    {
        if (strategy is BacktestStrategyBase bsb)
            bsb.SelfLearning = LoadSharedSelfLearning();
    }

    public static IReadOnlyList<BacktestSignal> FilterExecutionReadySignals(
        IReadOnlyList<BacktestSignal> signals,
        EnrichedBar[] triggerBars,
        int minimumEntryScore = 0)
        => FilterExecutionReadySignals(signals, triggerBars, minimumEntryScore, settings: null);

    public static IReadOnlyList<BacktestSignal> FilterExecutionReadySignals(
        IReadOnlyList<BacktestSignal> signals,
        EnrichedBar[] triggerBars,
        int minimumEntryScore,
        OpportunityScoringSettings? settings)
    {
        if (signals.Count == 0)
            return signals;

        settings ??= OpportunityScoringSettings.Current;

        List<BacktestSignal>? filtered = null;

        for (var i = 0; i < signals.Count; i++)
        {
            var signal = signals[i];
            if (EvaluateEntryGates(signal, triggerBars, minimumEntryScore, settings).Rejected)
            {
                filtered ??= signals.Take(i).ToList();
                continue;
            }

            filtered?.Add(signal);
        }

        return filtered ?? signals;
    }

    public static bool TryGetEntryExecutionRejectReason(
        BacktestSignal signal,
        EnrichedBar[] triggerBars,
        out string reason,
        int minimumEntryScore = 0)
    {
        var evaluation = EvaluateEntryGates(signal, triggerBars, minimumEntryScore);
        reason = evaluation.RejectReason;
        return evaluation.Rejected;
    }

    /// <summary>
    /// Evaluate the shared execution-ready gates for a single signal, classifying each gate as a
    /// genuine hard reject (data integrity / capital protection) or a soft gate.
    /// <para>
    /// In legacy mode (opportunity scoring disabled) the first tripped soft gate is reported as a
    /// hard reject, preserving the historical reject order and reason codes exactly.
    /// </para>
    /// <para>
    /// In opportunity-scoring mode soft gates become score penalties (classifications) and the signal
    /// is only rejected when a hard gate trips or the resulting opportunity score falls below the
    /// configured quality floor.
    /// </para>
    /// </summary>
    public static EntryGateEvaluation EvaluateEntryGates(
        BacktestSignal signal,
        EnrichedBar[] triggerBars,
        int minimumEntryScore = 0,
        OpportunityScoringSettings? settings = null)
    {
        settings ??= OpportunityScoringSettings.Current;

        // HARD gate: the signal must map to a real trigger bar (data integrity, never bypassed).
        if (signal.BarIndex < 0 || signal.BarIndex >= triggerBars.Length)
        {
            return EntryGateEvaluation.HardReject("entry-bar-out-of-range");
        }

        var row = triggerBars[signal.BarIndex];

        // Evaluate every SOFT gate up front so they can be classified as penalties when enabled.
        var softGates = new List<EntrySoftGate>(5);

        if (minimumEntryScore > 0 && signal.EntryScore < minimumEntryScore)
        {
            var shortfall = minimumEntryScore - signal.EntryScore;
            softGates.Add(new EntrySoftGate("score-below-min", shortfall * settings.ScoreShortfallPenaltyPerPoint));
        }

        if (!HasExpectedEntryCandle(signal, triggerBars))
        {
            var reason = signal.Side == TradeSide.Long ? "entry-bar-not-bullish" : "entry-bar-not-bearish";
            softGates.Add(new EntrySoftGate(reason, settings.EntryCandlePenalty));
        }

        if (!HasSecondTrendConfirmation(signal, triggerBars))
        {
            softGates.Add(new EntrySoftGate("second-confirmation-bar-missing", settings.SecondConfirmationPenalty));
        }

        TryGetL1DirectionalConfirmation(row, signal.Side, out var l1Available, out var l1Confirmed);
        if (l1Available && !l1Confirmed)
        {
            softGates.Add(new EntrySoftGate("l1-trend-not-confirmed", settings.L1ConfirmationPenalty));
        }

        TryGetL2DirectionalConfirmation(row, signal.Side, out var l2Available, out var l2Confirmed);
        if (l2Available && !l2Confirmed)
        {
            softGates.Add(new EntrySoftGate("l2-trend-not-confirmed", settings.L2ConfirmationPenalty));
        }

        // Legacy behavior: the first soft gate that trips is a hard reject, preserving the
        // historical reject order and reason codes exactly.
        if (!settings.Enabled)
        {
            return softGates.Count > 0
                ? EntryGateEvaluation.Reject(softGates[0].ReasonCode, signal.EntryScore, softGates)
                : EntryGateEvaluation.Pass(signal.EntryScore, softGates);
        }

        // Opportunity-scoring mode: soft gates become score penalties (classifications), never hard
        // rejects. Only the quality floor can reject the signal at this stage.
        var penaltyTotal = softGates.Sum(gate => gate.Penalty);
        var opportunityScore = signal.EntryScore - penaltyTotal;
        return opportunityScore < settings.QualityFloor
            ? EntryGateEvaluation.Reject("below-quality-floor", opportunityScore, softGates)
            : EntryGateEvaluation.Pass(opportunityScore, softGates);
    }

    private static bool HasExpectedEntryCandle(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        return DirectionalConfirmationEngine.HasExpectedTrendCandle(triggerBars[signal.BarIndex], signal.Side);
    }

    private static bool HasSecondTrendConfirmation(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        return DirectionalConfirmationEngine.HasSecondTrendConfirmation(triggerBars, signal.BarIndex, signal.Side);
    }

    private static bool IsTrendConfirmationBar(EnrichedBar row, TradeSide side)
    {
        return DirectionalConfirmationEngine.HasExpectedTrendCandle(row, side);
    }

    private static void TryGetL1DirectionalConfirmation(
        EnrichedBar row,
        TradeSide side,
        out bool available,
        out bool confirmed)
    {
        DirectionalConfirmationEngine.TryGetL1DirectionalConfirmation(row, side, out available, out confirmed);
    }

    private static void TryGetL2DirectionalConfirmation(
        EnrichedBar row,
        TradeSide side,
        out bool available,
        out bool confirmed)
    {
        DirectionalConfirmationEngine.TryGetL2DirectionalConfirmation(row, side, out available, out confirmed);
    }

    private static bool TryGetMidPrice(double bidPrice, double askPrice, out double midPrice)
    {
        if (!double.IsNaN(bidPrice) && !double.IsNaN(askPrice) && bidPrice > 0 && askPrice > 0)
        {
            midPrice = (bidPrice + askPrice) / 2.0;
            return true;
        }

        midPrice = double.NaN;
        return false;
    }
}

/// <summary>
/// Shared adapter base for strategy versions that delegate to <see cref="ConductStrategyV3"/>.
/// </summary>
public abstract class ConductStrategyAdapterBase : IBacktestStrategy, IBacktestLifecycleStrategy
{
    private readonly ConductStrategyV3 _inner;

    public StrategyConfig Config { get; }

    protected ConductStrategyAdapterBase(StrategyConfig? cfg, Func<StrategyConfig> defaultConfigFactory)
    {
        Config = cfg ?? defaultConfigFactory();
        _inner = new ConductStrategyV3(Config);
    }

    public IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        return _inner.GenerateSignals(bars1m, bars5m, bars15m, bars1h, bars1d);
    }

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        return _inner.SimulateTrade(signal, triggerBars);
    }

    public BacktestSelectedEntryIntent CreateSelectedEntryIntent(BacktestSignal signal, string triggerTimeframe)
    {
        return _inner.CreateSelectedEntryIntent(signal, triggerTimeframe);
    }

    public BacktestTradeLifecycleResult SimulateAcceptedEntryIntent(BacktestSelectedEntryIntent selectedEntryIntent, EnrichedBar[] triggerBars)
    {
        return _inner.SimulateAcceptedEntryIntent(selectedEntryIntent, triggerBars);
    }
}

// â”€â”€ Enriched Bar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>
/// A bar enriched with all computed indicator values.
/// Replaces the Python pattern of adding columns to a pandas DataFrame.
/// All indicator fields default to NaN until computed.
/// </summary>
public sealed class EnrichedBar
{
    public BacktestBar Bar { get; }

    // â”€â”€ Moving Averages â”€â”€
    public double Ema9 { get; set; } = double.NaN;
    public double Ema21 { get; set; } = double.NaN;
    public double Ema50 { get; set; } = double.NaN;
    public double Sma20 { get; set; } = double.NaN;
    public double Sma200 { get; set; } = double.NaN;

    // â”€â”€ ATR â”€â”€
    public double Atr14 { get; set; } = double.NaN;

    // â”€â”€ RSI â”€â”€
    public double Rsi14 { get; set; } = double.NaN;

    // â”€â”€ MACD â”€â”€
    public double Macd { get; set; } = double.NaN;
    public double MacdSignal { get; set; } = double.NaN;
    public double MacdHist { get; set; } = double.NaN;

    // â”€â”€ Bollinger Bands â”€â”€
    public double BbMid { get; set; } = double.NaN;
    public double BbUpper { get; set; } = double.NaN;
    public double BbLower { get; set; } = double.NaN;
    public double BbPctB { get; set; } = double.NaN;
    public double BbBandwidth { get; set; } = double.NaN;

    // â”€â”€ ADX â”€â”€
    public double Adx { get; set; } = double.NaN;
    public double PlusDi { get; set; } = double.NaN;
    public double MinusDi { get; set; } = double.NaN;

    // â”€â”€ Supertrend â”€â”€
    public double Supertrend { get; set; } = double.NaN;
    public int StDirection { get; set; } = 1;

    // â”€â”€ Relative Volume â”€â”€
    public double Rvol { get; set; } = double.NaN;

    // â”€â”€ VWAP â”€â”€
    public double Vwap { get; set; } = double.NaN;

    // â”€â”€ Stochastic â”€â”€
    public double StochK { get; set; } = double.NaN;
    public double StochD { get; set; } = double.NaN;

    // â”€â”€ Keltner Channels â”€â”€
    public double KcMid { get; set; } = double.NaN;
    public double KcUpper { get; set; } = double.NaN;
    public double KcLower { get; set; } = double.NaN;

    // â”€â”€ MFI â”€â”€
    public double Mfi14 { get; set; } = double.NaN;

    // â”€â”€ Order Flow Imbalance â”€â”€
    public double OfiRaw { get; set; } = double.NaN;
    public double OfiCum { get; set; } = double.NaN;
    public double OfiSignal { get; set; } = double.NaN;

    // â”€â”€ Spread Proxy â”€â”€
    public double SpreadRatio { get; set; } = double.NaN;
    public double SpreadZ { get; set; } = double.NaN;

    // â”€â”€ Volume Acceleration â”€â”€
    public double VolAccel { get; set; } = double.NaN;

    // â”€â”€ L2 Liquidity â”€â”€
    public double L2Liquidity { get; set; } = double.NaN;

    // â”€â”€ L1 Quote â”€â”€
    public double BidPrice { get; set; } = double.NaN;
    public double AskPrice { get; set; } = double.NaN;
    public double LastPrice { get; set; } = double.NaN;
    public double BidSize { get; set; } = double.NaN;
    public double AskSize { get; set; } = double.NaN;
    public double SpreadPct { get; set; } = double.NaN;

    // â”€â”€ L2 Book â”€â”€
    public double BidDepthN { get; set; } = double.NaN;
    public double AskDepthN { get; set; } = double.NaN;
    public double ImbalanceRatio { get; set; } = double.NaN;
    public double DepthWeightedMid { get; set; } = double.NaN;
    public double L0ImbalanceRatio { get; set; } = double.NaN;
    public double DeepImbalanceRatio { get; set; } = double.NaN;

    // â”€â”€ Candle Patterns â”€â”€
    public bool IsBullishCandle { get; set; }
    public bool IsBearishCandle { get; set; }
    public bool IsHammer { get; set; }
    public bool IsStar { get; set; }

    // â”€â”€ Lookback â”€â”€
    public double HighestClose10 { get; set; } = double.NaN;
    public double LowestClose10 { get; set; } = double.NaN;

    // â”€â”€ Previous bar OHLCV â”€â”€
    public double PrevOpen { get; set; } = double.NaN;
    public double PrevHigh { get; set; } = double.NaN;
    public double PrevLow { get; set; } = double.NaN;
    public double PrevClose { get; set; } = double.NaN;
    public double PrevVolume { get; set; } = double.NaN;

    // â”€â”€ Williams %R â”€â”€
    public double WillR14 { get; set; } = double.NaN;

    // â”€â”€ Donchian Channels â”€â”€
    public double DcUpper { get; set; } = double.NaN;
    public double DcLower { get; set; } = double.NaN;
    public double DcMid { get; set; } = double.NaN;
    public double DcPct { get; set; } = double.NaN;

    // â”€â”€ DPO â”€â”€
    public double Dpo20 { get; set; } = double.NaN;

    public EnrichedBar(BacktestBar bar)
    {
        Bar = bar;
    }
}

