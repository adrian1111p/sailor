namespace Sailor.App.Backtest.Engine;

// â”€â”€ Enums â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public enum TradeSide
{
    Long,
    Short,
}

public enum ExitReason
{
    HardStop,
    BreakEven,
    Trailing,
    Tp1,
    Tp2,
    Tp3,
    TimeStop,
    Eod,
    SignalReversal,
    MicroTrail,
    ReversalFlatten,
    Giveback,
    EmaTrail,
    EntryLossFlatten,
    PeakGivebackFlatten,
    StagnationFlatten,
    MaExtensionL2Flip,
    PriceTierStop,
    OppositeBarsFlatten,
    TrendChangeFlatten,
    L1MissingExit,
    SymbolLossFlatten,
}

public enum HtfBias
{
    Bull,
    Bear,
    Neutral,
}

// â”€â”€ Bar Data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>OHLCV bar â€” replaces a single row of a pandas DataFrame.</summary>
public sealed record BacktestBar(
    DateTime Timestamp,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume
);

// â”€â”€ Signal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>Entry signal produced by a strategy's signal generator.</summary>
public sealed record BacktestSignal
{
    public int BarIndex { get; init; }
    public DateTime Timestamp { get; init; }
    public TradeSide Side { get; init; }
    public double EntryPrice { get; init; }
    public double StopPrice { get; init; }
    public double RiskPerShare { get; init; }
    public int PositionSize { get; init; }
    public double AtrValue { get; init; }
    public HtfBias HtfTrend { get; init; }
    public string MtfMomentum { get; init; }
    public string SubStrategy { get; init; }
    public int EntryScore { get; init; }

    public BacktestSignal(
        int BarIndex, DateTime Timestamp, TradeSide Side,
        double EntryPrice, double StopPrice, double RiskPerShare,
        int PositionSize, double AtrValue, HtfBias HtfTrend,
        string MtfMomentum, string SubStrategy = "", int EntryScore = 0)
    {
        // Guard: reject directional SubStrategy names â€” all setups must be direction-neutral
        if (SubStrategy.Contains("_LONG", StringComparison.OrdinalIgnoreCase)
            || SubStrategy.Contains("_SHORT", StringComparison.OrdinalIgnoreCase)
            || SubStrategy.EndsWith("LONG", StringComparison.OrdinalIgnoreCase)
            || SubStrategy.EndsWith("SHORT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SubStrategy '{SubStrategy}' contains a directional suffix (_LONG/_SHORT). "
                + "All setups must be direction-neutral. Remove the directional suffix.");
        }

        this.BarIndex = BarIndex;
        this.Timestamp = Timestamp;
        this.Side = Side;
        this.EntryPrice = EntryPrice;
        this.StopPrice = StopPrice;
        this.RiskPerShare = RiskPerShare;
        this.PositionSize = PositionSize;
        this.AtrValue = AtrValue;
        this.HtfTrend = HtfTrend;
        this.MtfMomentum = MtfMomentum;
        this.SubStrategy = SubStrategy;
        this.EntryScore = EntryScore;
    }
}

/// <summary>Strategy-directed lockout for future signals on one side after a completed trade.</summary>
public sealed record BacktestSignalRetryLockout(
    TradeSide Side,
    int NextAllowedBarIndex
);

// â”€â”€ Trade Result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>Outcome of a completed simulated trade.</summary>
public sealed record BacktestTradeAction(
    int BarIndex,
    DateTime Timestamp,
    double Price,
    string ActionType,
    string Description,
    double? ReferencePrice = null,
    double? RMultiple = null
);

public enum BacktestTradeLifecycleEventType
{
    EntryAccepted,
    EntryFilled,
    MonitoringStep,
    StateTransition,
    PartialExit,
    TradeClosed,
    Finalized,
}

public sealed record BacktestTradeLifecycleEvent(
    BacktestTradeLifecycleEventType EventType,
    int BarIndex,
    DateTime Timestamp,
    double Price,
    int? Quantity = null,
    string Reason = "",
    string Detail = "",
    double? ReferencePrice = null,
    double? RMultiple = null
);

public sealed record BacktestStrategyLifecycleMetadata(
    string StrategyName,
    string StrategyVersion,
    string Symbol,
    string TriggerTimeframe,
    string SubStrategy,
    int EntryScore
);

public sealed record BacktestNormalizedExitProfile(
    double HardStopR,
    double BreakevenR,
    double TrailR,
    double GivebackPct,
    double Tp1R,
    double Tp2R,
    bool UseContinuationTp2ScaleOut,
    double ContinuationTp2ScalePct,
    bool UseTrailingTp2,
    double TrailingTp2AtrMultiplier,
    int MaxHoldBars,
    double GivebackMinPeakR,
    bool UseFixedGivebackUsdCap,
    bool UseNotionalGivebackCap,
    double GivebackPctOfNotional,
    double GivebackUsdCap,
    bool UseVariableGivebackUsdCap,
    double GivebackCapAnchorLowPrice,
    double GivebackCapAnchorHighPrice,
    double GivebackCapAtLowPrice,
    double GivebackCapAtHighPrice,
    double GivebackCapMinUsd,
    double GivebackCapMaxUsd,
    bool UseTightTrailOnFixedGiveback,
    double TightTrailAnchorLowPrice,
    double TightTrailAnchorHighPrice,
    double TightTrailAtLowPrice,
    double TightTrailAtHighPrice,
    double SlippageCents,
    double CommissionPerShare,
    bool DeductCommission,
    double Tp1PartialClosePct,
    bool Tp1TightenToBe,
    double Tp1BreakevenBufferAtr,
    bool ReversalFlatten,
    bool MicroTrail,
    double MicroTrailCents,
    double MicroTrailActivateCents,
    bool EmaTrail,
    double EmaTrailBufferAtr,
    bool FlattenOnEntryLossCross,
    double EntryLossBufferCents,
    bool EntryLossFlattenUseMarketPrice,
    bool FlattenOnPeakGiveback,
    double PeakGivebackKeepFraction,
    double PeakGivebackActivateR,
    bool FlattenOnStagnation,
    int StagnationBars,
    double StagnationMinPeakR,
    double StagnationMaxAdverseR,
    bool UsePriceTierMicroTrail,
    bool UsePriceTierStopFloor,
    bool UseMaExtensionL2Flip,
    double MaExtensionMinR,
    double MaExtensionAtrThreshold,
    bool UseL1L2DecisionOnOppositeBarsFlatten,
    double SymbolLossFlattenUsd
);

public sealed record BacktestSelectedEntryIntent(
    string IntentId,
    BacktestSignal Signal,
    BacktestNormalizedExitProfile ExitProfile,
    BacktestStrategyLifecycleMetadata LifecycleMetadata
);

/// <summary>Outcome of a completed simulated trade.</summary>
public sealed record BacktestTradeResult(
    int EntryBar,
    DateTime EntryTime,
    int ExitBar,
    DateTime ExitTime,
    TradeSide Side,
    double EntryPrice,
    double ExitPrice,
    double StopPrice,
    int PositionSize,
    double Pnl,
    double PnlR,
    ExitReason ExitReason,
    double PeakR,
    int BarsHeld,
    string SubStrategy = "",
    string GovernorBucket = "",
    bool GovernorTriggeredStop = false,
    string GovernorStopReason = "",
    IReadOnlyList<BacktestTradeAction>? ReplayActions = null,
    BacktestSelectedEntryIntent? SelectedEntryIntent = null,
    BacktestTradeLifecycleState? LifecycleFinalState = null,
    IReadOnlyList<BacktestTradeLifecycleEvent>? LifecycleEvents = null,
    DateTime? OriginalEntryTime = null,
    int? OriginalEntryBar = null,
    DateTime? ShuffledEntryTime = null,
    int? ShuffledEntryBar = null,
    string ShuffleReason = "")
{
    public DateTime EffectiveEntryTime => ShuffledEntryTime ?? EntryTime;

    public int EffectiveEntryBar => ShuffledEntryBar ?? EntryBar;

    public DateTime OriginalEntryTimeResolved => OriginalEntryTime ?? EntryTime;

    public int OriginalEntryBarResolved => OriginalEntryBar ?? EntryBar;
}

public sealed record BacktestTradeLifecycleState(
    int OriginalQuantity,
    int OpenQuantity,
    double PeakPrice,
    double TroughPrice,
    double StopPrice,
    double TrailingStop,
    bool BreakevenActivated,
    bool Tp1Activated,
    bool ProfitExtensionArmed,
    bool ContinuationTp2ScaleOutTaken,
    bool TrailingTp2Active,
    double? TrailingTp2Stop,
    double GrossPnl,
    double WeightedExitPrice
);

public sealed record BacktestTradeLifecycleResult(
    BacktestSignal Signal,
    BacktestTradeResult Trade,
    BacktestTradeLifecycleState FinalState,
    IReadOnlyList<BacktestTradeLifecycleEvent> Events
);

public sealed record BacktestGovernorReport(
    bool SessionStopped,
    string StopReason,
    int HaltedBucketCount,
    string HaltedBucketSummary,
    IReadOnlyDictionary<string, int> StopReasonCounts)
{
    public static BacktestGovernorReport None { get; } = new(
        SessionStopped: false,
        StopReason: string.Empty,
        HaltedBucketCount: 0,
        HaltedBucketSummary: string.Empty,
        StopReasonCounts: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
}

// â”€â”€ Statistics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>Performance statistics computed from a list of trades.</summary>
public sealed record BacktestStatistics(
    int TotalTrades,
    int Winners,
    int Losers,
    double WinRate,
    double AvgWin,
    double AvgLoss,
    double ProfitFactor,
    double ExpectancyR,
    double TotalPnl,
    double MaxDrawdown,
    double MaxDrawdownPct,
    double Sharpe,
    double AvgBarsHeld,
    int LongTrades,
    int ShortTrades,
    double LongWinRate,
    double ShortWinRate,
    IReadOnlyDictionary<ExitReason, int> ExitReasons,
    BacktestGovernorReport Governor,
    double EquityCurveSharpe = 0.0,
    double DownsideDeviation = 0.0,
    double EquityCurveDownsideDeviation = 0.0,
    double Sortino = 0.0,
    double EquityCurveSortino = 0.0
);

// â”€â”€ Backtest Result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>Complete result of a single backtest run.</summary>
public sealed record BacktestResult(
    string Symbol,
    string TriggerTf,
    IReadOnlyList<BacktestTradeResult> Trades,
    IReadOnlyList<(DateTime Time, double Equity)> EquityCurve,
    BacktestStatistics Stats,
    IReadOnlyList<BacktestSelectedEntryIntent>? AcceptedEntryIntents = null,
    IReadOnlyList<BacktestTradeLifecycleResult>? LifecycleTrades = null
)
{
    public string SummaryTable()
    {
        var lines = new List<string>
        {
            $"{"Metric",-25} {"Value",15}",
            new string('-', 42),
            $"{"Symbol",-25} {Symbol,15}",
            $"{"Trigger TF",-25} {TriggerTf,15}",
            $"{"Total Trades",-25} {Stats.TotalTrades,15}",
            $"{"Winners",-25} {Stats.Winners,15}",
            $"{"Losers",-25} {Stats.Losers,15}",
            $"{"Win Rate",-25} {Stats.WinRate,14:P1}",
            $"{"Avg Win ($)",-25} {Stats.AvgWin,15:F2}",
            $"{"Avg Loss ($)",-25} {Stats.AvgLoss,15:F2}",
            $"{"Profit Factor",-25} {Stats.ProfitFactor,15:F2}",
            $"{"Expectancy (R)",-25} {Stats.ExpectancyR,14:F2}R",
            $"{"Total PnL ($)",-25} {Stats.TotalPnl,15:F2}",
            $"{"Max Drawdown ($)",-25} {Stats.MaxDrawdown,15:F2}",
            $"{"Max Drawdown (%)",-25} {Stats.MaxDrawdownPct,14:P1}",
            $"{"Sharpe Ratio",-25} {Stats.Sharpe,15:F2}",
            $"{"Sortino Ratio",-25} {Stats.Sortino,15:F2}",
            $"{"Downside Deviation",-25} {Stats.DownsideDeviation,14:P2}",
            $"{"Equity Curve Sharpe",-25} {Stats.EquityCurveSharpe,15:F2}",
            $"{"Equity Curve Sortino",-25} {Stats.EquityCurveSortino,15:F2}",
            $"{"Equity Curve Downside",-25} {Stats.EquityCurveDownsideDeviation,14:P2}",
            $"{"Avg Bars Held",-25} {Stats.AvgBarsHeld,15:F0}",
            $"{"Long Trades",-25} {Stats.LongTrades,15}",
            $"{"Short Trades",-25} {Stats.ShortTrades,15}",
            $"{"Long Win Rate",-25} {Stats.LongWinRate,14:P1}",
            $"{"Short Win Rate",-25} {Stats.ShortWinRate,14:P1}",
        };

        if (Stats.Governor.SessionStopped || Stats.Governor.HaltedBucketCount > 0)
        {
            lines.Add($"{"Governor Stopped",-25} {(Stats.Governor.SessionStopped ? "YES" : "NO"),15}");
            lines.Add($"{"Governor Reason",-25} {Stats.Governor.StopReason,15}");
            lines.Add($"{"Governor Buckets",-25} {Stats.Governor.HaltedBucketSummary,15}");
        }

        foreach (var (reason, count) in Stats.ExitReasons.OrderBy(x => x.Key.ToString()))
        {
            lines.Add($"{"  Exit: " + reason,-25} {count,15}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string TradesTable(int n = 20)
    {
        if (Trades.Count == 0) return "No trades.";

        var header = $"{"Entry Time",-18} {"Side",-6} {"Entry$",8} {"Exit$",8} {"PnL$",10} {"PnL(R)",8} {"Exit Reason",-18} {"Bars",5}";
        var lines = new List<string> { header, new string('-', header.Length) };

        foreach (var t in Trades.TakeLast(n))
        {
            lines.Add($"{t.EntryTime:yyyy-MM-dd HH:mm} {t.Side,-6} {t.EntryPrice,8:F2} {t.ExitPrice,8:F2} {t.Pnl,10:F2} {t.PnlR,7:F2}R {t.ExitReason,-18} {t.BarsHeld,5}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

