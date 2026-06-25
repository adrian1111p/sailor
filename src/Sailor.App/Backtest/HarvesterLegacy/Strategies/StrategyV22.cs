using Sailor.App.Backtest.Engine;
using Sailor.App.Backtest.Indicators;

namespace Sailor.App.Backtest.Strategies;

public sealed class V22Config
{
    public double RiskPerTradeDollars { get; set; } = 25.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.20;
    public int MaxShares { get; set; } = 10_000;
    public double CommissionPerShare { get; set; } = 0.005;
    public double AngleThresholdDegrees { get; set; } = 8.5;
    public double ExitReversalAngleDegrees { get; set; } = 6.0;
    public double StopAtrMultiplier { get; set; } = 1.00;
    public double TrailAtrMultiplier { get; set; } = 0.85;
    public double MinimumRiskPerShare { get; set; } = 0.05;
    public double MaxStructuralRiskAtr { get; set; } = 1.20;
    public double PullbackMaxAtr { get; set; } = 0.80;
    public double VwapStretchMaxAtr { get; set; } = 1.45;
    public double BreakoutExtensionMaxAtr { get; set; } = 1.80;
    public double TrendAdxMin { get; set; } = 14.0;
    public double RvolMin { get; set; } = 0.80;
    public double LongRsiMin { get; set; } = 50.0;
    public double LongRsiMax { get; set; } = 78.0;
    public double ShortRsiMin { get; set; } = 22.0;
    public double ShortRsiMax { get; set; } = 50.0;
    public double BreakevenActivationR { get; set; } = 0.90;
    public double ProfitProtectActivationR { get; set; } = 1.60;
    public int CooldownBarsAfterStop { get; set; } = 1;
    public int CooldownBarsAfterWeakness { get; set; } = 1;
    public int MaxHoldBars15m { get; set; } = 16;
    public int MarketOpenMinuteEt { get; set; } = 570;
    public int LastEntryMinuteEt { get; set; } = 945;
    public int EodFlattenMinuteEt { get; set; } = 955;
    public int MinimumBars15mForSignal { get; set; } = 12;
    public int SqueezeLookbackBars { get; set; } = 4;
    public int MinimumScore { get; set; } = 7;
    public string DiagnosticsLabel { get; set; } = "default";
}

public sealed class StrategyV22 : BacktestStrategyBase, IBacktestLifecycleStrategy, IBacktestPostTradeSignalGate
{
    private const string StrategyId = "V22_15MINUTES";
    private const string ContinuationSetupId = "V22_TREND_PULLBACK_15M";
    private const string SqueezeSetupId = "V22_SQUEEZE_RELEASE_15M";
    private const string AngleDriveSetupId = "V22_ANGLE_DRIVE_15M";

    private readonly V22Config _cfg;
    private readonly Dictionary<string, SignalPlan> _signalPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BacktestSignalRetryLockout> _retryLockouts = new(StringComparer.Ordinal);
    private EnrichedBar[] _bars1m = Array.Empty<EnrichedBar>();
    private EnrichedBar[] _bars15m = Array.Empty<EnrichedBar>();

    public StrategyV22(V22Config? cfg = null)
    {
        _cfg = cfg ?? new V22Config();
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
            MaxHoldBars = _cfg.MaxHoldBars15m * 15,
            SlippageCents = 0.0,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
        };

        return new BacktestSelectedEntryIntent(
            IntentId: BuildSignalKey(signal),
            Signal: signal,
            ExitProfile: ExitEngine.ToNormalizedExitProfile(exitConfig),
            LifecycleMetadata: new BacktestStrategyLifecycleMetadata(
                StrategyName: "StrategyV22",
                StrategyVersion: "V22",
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
            TrailingStop: exitResolution.TrailingStop,
            BreakevenActivated: peakR >= _cfg.BreakevenActivationR,
            Tp1Activated: peakR >= 1.0,
            ProfitExtensionArmed: peakR >= _cfg.ProfitProtectActivationR,
            ContinuationTp2ScaleOutTaken: false,
            TrailingTp2Active: peakR >= _cfg.ProfitProtectActivationR,
            TrailingTp2Stop: exitResolution.TrailingStop,
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

        if (!TryBuildCandidate(index, out var candidate))
        {
            return false;
        }

        if (!TryResolveEntryBarIndex(row.Bar.Timestamp, out var entryBarIndex))
        {
            return false;
        }

        var entryRow = _bars1m[entryBarIndex];
        double atr = ResolveAtr(index, entryRow.Atr14);
        if (double.IsNaN(atr) || atr <= 0)
        {
            return false;
        }

        double entryPrice = entryRow.Bar.Open > 0 ? entryRow.Bar.Open : entryRow.Bar.Close;
        if (entryPrice <= 0)
        {
            return false;
        }

        double riskPerShare = ResolveRiskPerShare(candidate.Side, index, entryPrice, atr);
        if (double.IsNaN(riskPerShare) || riskPerShare <= 0)
        {
            return false;
        }

        double stopPrice = candidate.Side == TradeSide.Long
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
            Side: candidate.Side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: positionSize,
            AtrValue: atr,
            HtfTrend: candidate.Bias,
            MtfMomentum: candidate.MomentumLabel,
            SubStrategy: candidate.SetupId,
            EntryScore: candidate.Score);

        plan = new SignalPlan(candidate.SetupId, index, candidate.Score, candidate.AngleDegrees, candidate.MomentumLabel);
        return true;
    }

    private bool TryBuildCandidate(int index, out SignalCandidate candidate)
    {
        candidate = null!;
        if (!TryGetAngleDegrees(index, out var angleDegrees))
        {
            return false;
        }

        if (angleDegrees >= _cfg.AngleThresholdDegrees)
        {
            return TryBuildDirectionalCoreCandidate(index, angleDegrees, TradeSide.Long, out candidate);
        }

        if (angleDegrees <= -_cfg.AngleThresholdDegrees)
        {
            return TryBuildDirectionalCoreCandidate(index, angleDegrees, TradeSide.Short, out candidate);
        }

        return false;
    }

    private bool TryBuildDirectionalCoreCandidate(int index, double angleDegrees, TradeSide side, out SignalCandidate candidate)
    {
        candidate = null!;
        if (!PassesContextGuards(index, side, angleDegrees))
        {
            return false;
        }

        var continuationScore = side == TradeSide.Long
            ? ScoreLongContinuation(index, angleDegrees)
            : ScoreShortContinuation(index, angleDegrees);
        var squeezeScore = side == TradeSide.Long
            ? ScoreLongSqueezeRelease(index, angleDegrees)
            : ScoreShortSqueezeRelease(index, angleDegrees);
        var angleDriveScore = side == TradeSide.Long
            ? ScoreLongAngleDrive(index, angleDegrees)
            : ScoreShortAngleDrive(index, angleDegrees);

        int score = ComputeDirectionalScore(index, side);
        if (continuationScore > 0)
        {
            score += 2;
        }

        if (squeezeScore > 0)
        {
            score += 2;
        }

        if (angleDriveScore > 0)
        {
            score += 1;
        }

        if (score < _cfg.MinimumScore)
        {
            return false;
        }

        string setupId = continuationScore >= squeezeScore && continuationScore > 0
            ? ContinuationSetupId
            : squeezeScore > 0
                ? SqueezeSetupId
                : AngleDriveSetupId;
        string momentumLabel = setupId == ContinuationSetupId
            ? "trend-pullback"
            : setupId == SqueezeSetupId
                ? "squeeze-release"
                : "angle-drive";
        candidate = new SignalCandidate(
            side,
            setupId,
            score,
            angleDegrees,
            side == TradeSide.Long ? HtfBias.Bull : HtfBias.Bear,
            momentumLabel);
        return true;
    }

    private bool PassesContextGuards(int index, TradeSide side, double angleDegrees)
    {
        var row = _bars15m[index];
        double atr = ResolveAtr(index, row.Atr14);
        if (double.IsNaN(atr) || atr <= 0 || row.Bar.Close <= 0)
        {
            return false;
        }

        bool choppy = !double.IsNaN(row.Adx)
            && row.Adx < 12.0
            && (double.IsNaN(row.BbBandwidth) || row.BbBandwidth < 0.03);
        if (choppy)
        {
            return false;
        }

        bool noisyAndWeak = (atr / row.Bar.Close) > 0.12
            && (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin)
            && Math.Abs(angleDegrees) < (_cfg.AngleThresholdDegrees + 2.0);
        if (noisyAndWeak)
        {
            return false;
        }

        if (side == TradeSide.Long)
        {
            bool fullyContradicted = (!double.IsNaN(row.MacdHist) && row.MacdHist < 0)
                && (!double.IsNaN(row.Rsi14) && row.Rsi14 < 48)
                && (!double.IsNaN(row.Vwap) && row.Bar.Close < row.Vwap)
                && (!double.IsNaN(row.Ema9) && row.Bar.Close < row.Ema9);
            return !fullyContradicted;
        }

        bool shortContradicted = (!double.IsNaN(row.MacdHist) && row.MacdHist > 0)
            && (!double.IsNaN(row.Rsi14) && row.Rsi14 > 52)
            && (!double.IsNaN(row.Vwap) && row.Bar.Close > row.Vwap)
            && (!double.IsNaN(row.Ema9) && row.Bar.Close > row.Ema9);
        return !shortContradicted;
    }

    private int ComputeDirectionalScore(int index, TradeSide side)
    {
        var row = _bars15m[index];
        int score = 5;

        bool macdAligned = !double.IsNaN(row.MacdHist)
            && (side == TradeSide.Long ? row.MacdHist > 0 : row.MacdHist < 0);
        if (macdAligned)
        {
            score++;
        }

        bool rsiAligned = !double.IsNaN(row.Rsi14)
            && (side == TradeSide.Long
                ? row.Rsi14 >= _cfg.LongRsiMin && row.Rsi14 <= _cfg.LongRsiMax
                : row.Rsi14 >= _cfg.ShortRsiMin && row.Rsi14 <= _cfg.ShortRsiMax);
        if (rsiAligned)
        {
            score++;
        }

        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.TrendAdxMin)
        {
            score++;
        }

        if ((!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin) || (!double.IsNaN(row.VolAccel) && row.VolAccel > 0))
        {
            score++;
        }

        bool vwapAligned = double.IsNaN(row.Vwap)
            || (side == TradeSide.Long ? row.Bar.Close >= row.Vwap : row.Bar.Close <= row.Vwap);
        if (vwapAligned)
        {
            score++;
        }

        bool supertrendAligned = double.IsNaN(row.Supertrend)
            || (side == TradeSide.Long ? row.Bar.Close >= row.Supertrend : row.Bar.Close <= row.Supertrend);
        if (supertrendAligned)
        {
            score++;
        }

        bool diAligned = !double.IsNaN(row.PlusDi)
            && !double.IsNaN(row.MinusDi)
            && (side == TradeSide.Long ? row.PlusDi > row.MinusDi : row.MinusDi > row.PlusDi);
        if (diAligned)
        {
            score++;
        }

        bool candleAligned = side == TradeSide.Long
            ? row.IsBullishCandle || row.IsHammer
            : row.IsBearishCandle || row.IsStar;
        if (candleAligned)
        {
            score++;
        }

        return score;
    }

    private SignalCandidate? BuildLongCandidate(int index, double angleDegrees)
    {
        var trendScore = ScoreLongContinuation(index, angleDegrees);
        var squeezeScore = ScoreLongSqueezeRelease(index, angleDegrees);
        var angleDriveScore = ScoreLongAngleDrive(index, angleDegrees);
        int score = Math.Max(trendScore, Math.Max(squeezeScore, angleDriveScore));
        if (score < _cfg.MinimumScore)
        {
            return null;
        }

        string setupId = score == trendScore
            ? ContinuationSetupId
            : score == squeezeScore
                ? SqueezeSetupId
                : AngleDriveSetupId;
        string momentumLabel = setupId == ContinuationSetupId
            ? "trend-pullback"
            : setupId == SqueezeSetupId
                ? "squeeze-release"
                : "angle-drive";
        return new SignalCandidate(TradeSide.Long, setupId, score, angleDegrees, HtfBias.Bull, momentumLabel);
    }

    private SignalCandidate? BuildShortCandidate(int index, double angleDegrees)
    {
        var trendScore = ScoreShortContinuation(index, angleDegrees);
        var squeezeScore = ScoreShortSqueezeRelease(index, angleDegrees);
        var angleDriveScore = ScoreShortAngleDrive(index, angleDegrees);
        int score = Math.Max(trendScore, Math.Max(squeezeScore, angleDriveScore));
        if (score < _cfg.MinimumScore)
        {
            return null;
        }

        string setupId = score == trendScore
            ? ContinuationSetupId
            : score == squeezeScore
                ? SqueezeSetupId
                : AngleDriveSetupId;
        string momentumLabel = setupId == ContinuationSetupId
            ? "trend-pullback"
            : setupId == SqueezeSetupId
                ? "squeeze-release"
                : "angle-drive";
        return new SignalCandidate(TradeSide.Short, setupId, score, angleDegrees, HtfBias.Bear, momentumLabel);
    }

    private int ScoreLongAngleDrive(int index, double angleDegrees)
    {
        if (angleDegrees < _cfg.AngleThresholdDegrees)
        {
            return 0;
        }

        var row = _bars15m[index];
        double atr = ResolveAtr(index, row.Atr14);
        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || row.Bar.Close < row.Ema9 || row.Bar.Close < row.Ema21)
        {
            return 0;
        }

        if (!double.IsNaN(row.Vwap) && row.Bar.Close < row.Vwap)
        {
            return 0;
        }

        if (double.IsNaN(atr) || atr <= 0 || row.Bar.Close <= 0 || (atr / row.Bar.Close) > 0.10)
        {
            return 0;
        }

        if (double.IsNaN(row.MacdHist) || row.MacdHist <= 0)
        {
            return 0;
        }

        if (double.IsNaN(row.Adx) || row.Adx < (_cfg.TrendAdxMin + 2.0))
        {
            return 0;
        }

        if (double.IsNaN(row.Rsi14) || row.Rsi14 < _cfg.LongRsiMin || row.Rsi14 > _cfg.LongRsiMax)
        {
            return 0;
        }

        if (double.IsNaN(row.PlusDi) || double.IsNaN(row.MinusDi) || row.PlusDi <= row.MinusDi)
        {
            return 0;
        }

        bool participationConfirmed = (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin)
            || (!double.IsNaN(row.VolAccel) && row.VolAccel > 0);
        if (!participationConfirmed)
        {
            return 0;
        }

        int score = 5;
        if (!double.IsNaN(row.Supertrend) && row.Bar.Close >= row.Supertrend)
        {
            score++;
        }

        if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi) && (row.PlusDi - row.MinusDi) >= 5.0)
        {
            score++;
        }

        if (row.IsBullishCandle || row.IsHammer)
        {
            score++;
        }

        if (!double.IsNaN(row.BbPctB) && row.BbPctB >= 0.60)
        {
            score++;
        }

        if (!double.IsNaN(row.StochK) && !double.IsNaN(row.StochD) && row.StochK >= row.StochD)
        {
            score++;
        }

        if (row.Bar.Close > row.Bar.Open && row.Bar.Close >= row.PrevHigh)
        {
            score++;
        }

        return score;
    }

    private int ScoreShortAngleDrive(int index, double angleDegrees)
    {
        if (angleDegrees > -_cfg.AngleThresholdDegrees)
        {
            return 0;
        }

        var row = _bars15m[index];
        double atr = ResolveAtr(index, row.Atr14);
        if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || row.Bar.Close > row.Ema9 || row.Bar.Close > row.Ema21)
        {
            return 0;
        }

        if (!double.IsNaN(row.Vwap) && row.Bar.Close > row.Vwap)
        {
            return 0;
        }

        if (double.IsNaN(atr) || atr <= 0 || row.Bar.Close <= 0 || (atr / row.Bar.Close) > 0.10)
        {
            return 0;
        }

        if (double.IsNaN(row.MacdHist) || row.MacdHist >= 0)
        {
            return 0;
        }

        if (double.IsNaN(row.Adx) || row.Adx < (_cfg.TrendAdxMin + 2.0))
        {
            return 0;
        }

        if (double.IsNaN(row.Rsi14) || row.Rsi14 < _cfg.ShortRsiMin || row.Rsi14 > _cfg.ShortRsiMax)
        {
            return 0;
        }

        if (double.IsNaN(row.PlusDi) || double.IsNaN(row.MinusDi) || row.MinusDi <= row.PlusDi)
        {
            return 0;
        }

        bool participationConfirmed = (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin)
            || (!double.IsNaN(row.VolAccel) && row.VolAccel > 0);
        if (!participationConfirmed)
        {
            return 0;
        }

        int score = 5;
        if (!double.IsNaN(row.Supertrend) && row.Bar.Close <= row.Supertrend)
        {
            score++;
        }

        if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi) && (row.MinusDi - row.PlusDi) >= 5.0)
        {
            score++;
        }

        if (row.IsBearishCandle || row.IsStar)
        {
            score++;
        }

        if (!double.IsNaN(row.BbPctB) && row.BbPctB <= 0.40)
        {
            score++;
        }

        if (!double.IsNaN(row.StochK) && !double.IsNaN(row.StochD) && row.StochK <= row.StochD)
        {
            score++;
        }

        if (row.Bar.Close < row.Bar.Open && row.Bar.Close <= row.PrevLow)
        {
            score++;
        }

        return score;
    }

    private int ScoreLongContinuation(int index, double angleDegrees)
    {
        if (angleDegrees < _cfg.AngleThresholdDegrees)
        {
            return 0;
        }

        var row = _bars15m[index];
        var prev = _bars15m[index - 1];
        double atr = ResolveAtr(index, prev.Atr14);
        if (double.IsNaN(atr) || atr <= 0)
        {
            return 0;
        }

        bool trendStack = !double.IsNaN(row.Ema9)
            && !double.IsNaN(row.Ema21)
            && !double.IsNaN(row.Ema50)
            && row.Ema9 > row.Ema21
            && row.Ema21 >= row.Ema50;
        if (!trendStack)
        {
            return 0;
        }

        bool pullbackTouched = DistanceAtr(row.Bar.Low, row.Ema9, atr) <= _cfg.PullbackMaxAtr
            || DistanceAtr(row.Bar.Low, row.Vwap, atr) <= _cfg.PullbackMaxAtr;
        bool recovered = row.Bar.Close >= row.Ema9 && (double.IsNaN(row.Vwap) || row.Bar.Close >= row.Vwap);
        if (!pullbackTouched || !recovered)
        {
            return 0;
        }

        if (DistanceAtr(row.Bar.Close, row.Vwap, atr) > _cfg.VwapStretchMaxAtr)
        {
            return 0;
        }

        int score = 4;
        if (row.Bar.Close > prev.Bar.High)
        {
            score++;
        }

        if (!double.IsNaN(row.MacdHist) && row.MacdHist > 0)
        {
            score++;
        }

        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.TrendAdxMin)
        {
            score++;
        }

        if (!double.IsNaN(row.Rsi14) && row.Rsi14 >= _cfg.LongRsiMin && row.Rsi14 <= _cfg.LongRsiMax)
        {
            score++;
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin)
        {
            score++;
        }

        if (!double.IsNaN(row.Supertrend) && row.Bar.Close >= row.Supertrend)
        {
            score++;
        }

        if (row.StDirection >= 0)
        {
            score++;
        }

        if (!double.IsNaN(row.Mfi14) && row.Mfi14 >= 50)
        {
            score++;
        }

        if (!double.IsNaN(row.StochK) && !double.IsNaN(row.StochD) && row.StochK >= row.StochD)
        {
            score++;
        }

        if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi) && row.PlusDi > row.MinusDi)
        {
            score++;
        }

        if (row.IsBullishCandle || row.IsHammer)
        {
            score++;
        }

        if (!double.IsNaN(row.BbPctB) && row.BbPctB >= 0.55 && row.BbPctB <= 0.95)
        {
            score++;
        }

        if (!double.IsNaN(row.VolAccel) && row.VolAccel > 0)
        {
            score++;
        }

        return score;
    }

    private int ScoreShortContinuation(int index, double angleDegrees)
    {
        if (angleDegrees > -_cfg.AngleThresholdDegrees)
        {
            return 0;
        }

        var row = _bars15m[index];
        var prev = _bars15m[index - 1];
        double atr = ResolveAtr(index, prev.Atr14);
        if (double.IsNaN(atr) || atr <= 0)
        {
            return 0;
        }

        bool trendStack = !double.IsNaN(row.Ema9)
            && !double.IsNaN(row.Ema21)
            && !double.IsNaN(row.Ema50)
            && row.Ema9 < row.Ema21
            && row.Ema21 <= row.Ema50;
        if (!trendStack)
        {
            return 0;
        }

        bool pullbackTouched = DistanceAtr(row.Bar.High, row.Ema9, atr) <= _cfg.PullbackMaxAtr
            || DistanceAtr(row.Bar.High, row.Vwap, atr) <= _cfg.PullbackMaxAtr;
        bool recovered = row.Bar.Close <= row.Ema9 && (double.IsNaN(row.Vwap) || row.Bar.Close <= row.Vwap);
        if (!pullbackTouched || !recovered)
        {
            return 0;
        }

        if (DistanceAtr(row.Bar.Close, row.Vwap, atr) > _cfg.VwapStretchMaxAtr)
        {
            return 0;
        }

        int score = 4;
        if (row.Bar.Close < prev.Bar.Low)
        {
            score++;
        }

        if (!double.IsNaN(row.MacdHist) && row.MacdHist < 0)
        {
            score++;
        }

        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.TrendAdxMin)
        {
            score++;
        }

        if (!double.IsNaN(row.Rsi14) && row.Rsi14 >= _cfg.ShortRsiMin && row.Rsi14 <= _cfg.ShortRsiMax)
        {
            score++;
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin)
        {
            score++;
        }

        if (!double.IsNaN(row.Supertrend) && row.Bar.Close <= row.Supertrend)
        {
            score++;
        }

        if (row.StDirection <= 0)
        {
            score++;
        }

        if (!double.IsNaN(row.Mfi14) && row.Mfi14 <= 50)
        {
            score++;
        }

        if (!double.IsNaN(row.StochK) && !double.IsNaN(row.StochD) && row.StochK <= row.StochD)
        {
            score++;
        }

        if (!double.IsNaN(row.PlusDi) && !double.IsNaN(row.MinusDi) && row.MinusDi > row.PlusDi)
        {
            score++;
        }

        if (row.IsBearishCandle || row.IsStar)
        {
            score++;
        }

        if (!double.IsNaN(row.BbPctB) && row.BbPctB >= 0.05 && row.BbPctB <= 0.45)
        {
            score++;
        }

        if (!double.IsNaN(row.VolAccel) && row.VolAccel > 0)
        {
            score++;
        }

        return score;
    }

    private int ScoreLongSqueezeRelease(int index, double angleDegrees)
    {
        if (angleDegrees < _cfg.AngleThresholdDegrees * 0.50)
        {
            return 0;
        }

        var row = _bars15m[index];
        var prev = _bars15m[index - 1];
        double atr = ResolveAtr(index, prev.Atr14);
        if (double.IsNaN(atr) || atr <= 0)
        {
            return 0;
        }

        if (!HadRecentSqueeze(index, _cfg.SqueezeLookbackBars))
        {
            return 0;
        }

        bool breakout = row.Bar.Close > prev.Bar.High;
        if (!breakout && !double.IsNaN(prev.HighestClose10))
        {
            breakout = row.Bar.Close >= prev.HighestClose10;
        }

        if (!breakout)
        {
            return 0;
        }

        if (DistanceAtr(row.Bar.Close, row.Vwap, atr) > _cfg.BreakoutExtensionMaxAtr)
        {
            return 0;
        }

        int score = 5;
        if (!double.IsNaN(row.BbBandwidth) && !double.IsNaN(prev.BbBandwidth) && row.BbBandwidth > prev.BbBandwidth)
        {
            score++;
        }

        if (!double.IsNaN(row.MacdHist) && row.MacdHist > 0)
        {
            score++;
        }

        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.TrendAdxMin)
        {
            score++;
        }

        if (!double.IsNaN(row.Rsi14) && row.Rsi14 >= _cfg.LongRsiMin)
        {
            score++;
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin)
        {
            score++;
        }

        if (!double.IsNaN(row.BbPctB) && row.BbPctB >= 0.70)
        {
            score++;
        }

        if (!double.IsNaN(row.Supertrend) && row.Bar.Close >= row.Supertrend)
        {
            score++;
        }

        if (!double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21) && row.Ema9 > row.Ema21)
        {
            score++;
        }

        if (!double.IsNaN(row.StochK) && row.StochK >= 60)
        {
            score++;
        }

        return score;
    }

    private int ScoreShortSqueezeRelease(int index, double angleDegrees)
    {
        if (angleDegrees > -_cfg.AngleThresholdDegrees * 0.50)
        {
            return 0;
        }

        var row = _bars15m[index];
        var prev = _bars15m[index - 1];
        double atr = ResolveAtr(index, prev.Atr14);
        if (double.IsNaN(atr) || atr <= 0)
        {
            return 0;
        }

        if (!HadRecentSqueeze(index, _cfg.SqueezeLookbackBars))
        {
            return 0;
        }

        bool breakout = row.Bar.Close < prev.Bar.Low;
        if (!breakout && !double.IsNaN(prev.LowestClose10))
        {
            breakout = row.Bar.Close <= prev.LowestClose10;
        }

        if (!breakout)
        {
            return 0;
        }

        if (DistanceAtr(row.Bar.Close, row.Vwap, atr) > _cfg.BreakoutExtensionMaxAtr)
        {
            return 0;
        }

        int score = 5;
        if (!double.IsNaN(row.BbBandwidth) && !double.IsNaN(prev.BbBandwidth) && row.BbBandwidth > prev.BbBandwidth)
        {
            score++;
        }

        if (!double.IsNaN(row.MacdHist) && row.MacdHist < 0)
        {
            score++;
        }

        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.TrendAdxMin)
        {
            score++;
        }

        if (!double.IsNaN(row.Rsi14) && row.Rsi14 <= _cfg.ShortRsiMax)
        {
            score++;
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin)
        {
            score++;
        }

        if (!double.IsNaN(row.BbPctB) && row.BbPctB <= 0.30)
        {
            score++;
        }

        if (!double.IsNaN(row.Supertrend) && row.Bar.Close <= row.Supertrend)
        {
            score++;
        }

        if (!double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21) && row.Ema9 < row.Ema21)
        {
            score++;
        }

        if (!double.IsNaN(row.StochK) && row.StochK <= 40)
        {
            score++;
        }

        return score;
    }

    private ExitResolution ResolveExit(BacktestSignal signal, int source15mIndex)
    {
        var entryDate = TradingTime.GetDateEt(signal.Timestamp);
        int defaultExitBarIndex = ResolveEodExitBarIndex(signal.BarIndex, entryDate);
        var defaultResolution = new ExitResolution(defaultExitBarIndex, ExitReason.Eod, null, "end-of-day-flatten", null, signal.StopPrice);

        double trailingStop = signal.StopPrice;
        double peakR = 0.0;

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

            double atr = ResolveAtr(i, signal.AtrValue);
            if (signal.RiskPerShare > 0)
            {
                peakR = signal.Side == TradeSide.Long
                    ? Math.Max(peakR, (row.Bar.High - signal.EntryPrice) / signal.RiskPerShare)
                    : Math.Max(peakR, (signal.EntryPrice - row.Bar.Low) / signal.RiskPerShare);
            }

            trailingStop = signal.Side == TradeSide.Long
                ? UpdateLongTrailingStop(signal, row, trailingStop, peakR, atr)
                : UpdateShortTrailingStop(signal, row, trailingStop, peakR, atr);

            if (IsStopTouched(signal.Side, row, trailingStop))
            {
                var reason = peakR >= _cfg.BreakevenActivationR ? ExitReason.Trailing : ExitReason.HardStop;
                return new ExitResolution(
                    exitBarIndex,
                    reason,
                    i,
                    $"dynamic-stop:{trailingStop:F4}",
                    BuildCooldownLockout(signal.Side, i, entryDate, reason),
                    trailingStop);
            }

            if (TryGetAngleDegrees(i, out var angleDegrees))
            {
                if (IsReversal(signal.Side, row, angleDegrees))
                {
                    return new ExitResolution(
                        exitBarIndex,
                        ExitReason.SignalReversal,
                        i,
                        $"angle-reversal:{angleDegrees:F2}",
                        BuildCooldownLockout(signal.Side, i, entryDate, ExitReason.SignalReversal),
                        trailingStop);
                }

                if (IsTrendNeutralized(signal.Side, row, angleDegrees))
                {
                    return new ExitResolution(
                        exitBarIndex,
                        ExitReason.TrendChangeFlatten,
                        i,
                        $"angle-neutral:{angleDegrees:F2}",
                        BuildCooldownLockout(signal.Side, i, entryDate, ExitReason.TrendChangeFlatten),
                        trailingStop);
                }
            }

            if (ShouldExitForWeakness(signal, i, peakR))
            {
                var reason = peakR >= _cfg.ProfitProtectActivationR ? ExitReason.PeakGivebackFlatten : ExitReason.TrendChangeFlatten;
                return new ExitResolution(
                    exitBarIndex,
                    reason,
                    i,
                    "trend-weakness",
                    BuildCooldownLockout(signal.Side, i, entryDate, reason),
                    trailingStop);
            }

            if (i - source15mIndex >= _cfg.MaxHoldBars15m && peakR < 0.75)
            {
                return new ExitResolution(
                    exitBarIndex,
                    ExitReason.TimeStop,
                    i,
                    "stalled-intraday",
                    BuildCooldownLockout(signal.Side, i, entryDate, ExitReason.TimeStop),
                    trailingStop);
            }
        }

        return defaultResolution with { TrailingStop = trailingStop };
    }

    private BacktestSignalRetryLockout? BuildCooldownLockout(TradeSide side, int exit15mIndex, DateOnly entryDate, ExitReason reason)
    {
        int barsToSkip = reason is ExitReason.HardStop or ExitReason.Trailing
            ? _cfg.CooldownBarsAfterStop
            : _cfg.CooldownBarsAfterWeakness;
        if (barsToSkip <= 0)
        {
            return null;
        }

        int sameDayBarsSeen = 0;
        for (int i = exit15mIndex + 1; i < _bars15m.Length; i++)
        {
            if (TradingTime.GetDateEt(_bars15m[i].Bar.Timestamp) != entryDate)
            {
                break;
            }

            sameDayBarsSeen++;
            if (sameDayBarsSeen < barsToSkip)
            {
                continue;
            }

            if (TryResolveEntryBarIndex(_bars15m[i].Bar.Timestamp, out var entryBarIndex))
            {
                return new BacktestSignalRetryLockout(side, entryBarIndex);
            }
        }

        return new BacktestSignalRetryLockout(side, ResolveDayEndExclusiveBarIndex(entryDate));
    }

    private double ResolveRiskPerShare(TradeSide side, int index, double entryPrice, double atr)
    {
        double atrRisk = Math.Max(_cfg.MinimumRiskPerShare, atr * _cfg.StopAtrMultiplier);
        return atrRisk;
    }

    private double UpdateLongTrailingStop(BacktestSignal signal, EnrichedBar row, double currentStop, double peakR, double atr)
    {
        double anchor = MaxFinite(row.Ema21, row.Vwap, row.Ema9);
        double candidate = double.IsNaN(anchor)
            ? row.Bar.Close - (atr * _cfg.TrailAtrMultiplier)
            : anchor - (atr * _cfg.TrailAtrMultiplier);
        if (peakR >= _cfg.BreakevenActivationR)
        {
            candidate = Math.Max(candidate, signal.EntryPrice);
        }

        if (peakR >= _cfg.ProfitProtectActivationR)
        {
            candidate = Math.Max(candidate, row.Ema9 - (atr * 0.35));
        }

        return Math.Max(currentStop, candidate);
    }

    private double UpdateShortTrailingStop(BacktestSignal signal, EnrichedBar row, double currentStop, double peakR, double atr)
    {
        double anchor = MinFinite(row.Ema21, row.Vwap, row.Ema9);
        double candidate = double.IsNaN(anchor)
            ? row.Bar.Close + (atr * _cfg.TrailAtrMultiplier)
            : anchor + (atr * _cfg.TrailAtrMultiplier);
        if (peakR >= _cfg.BreakevenActivationR)
        {
            candidate = Math.Min(candidate, signal.EntryPrice);
        }

        if (peakR >= _cfg.ProfitProtectActivationR)
        {
            candidate = Math.Min(candidate, row.Ema9 + (atr * 0.35));
        }

        return Math.Min(currentStop, candidate);
    }

    private bool IsReversal(TradeSide side, EnrichedBar row, double angleDegrees)
    {
        return side == TradeSide.Long
            ? angleDegrees <= -_cfg.ExitReversalAngleDegrees && row.Bar.Close < row.Ema21
            : angleDegrees >= _cfg.ExitReversalAngleDegrees && row.Bar.Close > row.Ema21;
    }

    private bool IsTrendNeutralized(TradeSide side, EnrichedBar row, double angleDegrees)
    {
        if (side == TradeSide.Long)
        {
            return angleDegrees < _cfg.AngleThresholdDegrees
                && (double.IsNaN(row.Ema9) || row.Bar.Close <= row.Ema9);
        }

        return angleDegrees > -_cfg.AngleThresholdDegrees
            && (double.IsNaN(row.Ema9) || row.Bar.Close >= row.Ema9);
    }

    private bool ShouldExitForWeakness(BacktestSignal signal, int index, double peakR)
    {
        var row = _bars15m[index];
        int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);

        if (signal.Side == TradeSide.Long)
        {
            bool contextBroken = row.Bar.Close < row.Ema9
                && row.Bar.Close < row.Ema21
                && row.IsBearishCandle
                && (!double.IsNaN(row.MacdHist) && row.MacdHist <= 0)
                && (double.IsNaN(row.Vwap) || row.Bar.Close < row.Vwap);
            bool giveback = peakR >= _cfg.ProfitProtectActivationR
                && row.Bar.Close < row.Ema9
                && (!double.IsNaN(row.Rsi14) && row.Rsi14 < 55);
            bool lateFade = minuteEt >= _cfg.EodFlattenMinuteEt - 15
                && peakR > 0.25
                && row.Bar.Close < row.Ema9;
            return contextBroken || giveback || lateFade;
        }

        bool shortContextBroken = row.Bar.Close > row.Ema9
            && row.Bar.Close > row.Ema21
            && row.IsBullishCandle
            && (!double.IsNaN(row.MacdHist) && row.MacdHist >= 0)
            && (double.IsNaN(row.Vwap) || row.Bar.Close > row.Vwap);
        bool shortGiveback = peakR >= _cfg.ProfitProtectActivationR
            && row.Bar.Close > row.Ema9
            && (!double.IsNaN(row.Rsi14) && row.Rsi14 > 45);
        bool shortLateFade = minuteEt >= _cfg.EodFlattenMinuteEt - 15
            && peakR > 0.25
            && row.Bar.Close > row.Ema9;
        return shortContextBroken || shortGiveback || shortLateFade;
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

        double normalizedSlope = (row.Ema9 - prev.Ema9) / atr;
        angleDegrees = Math.Atan(normalizedSlope) * (180.0 / Math.PI);
        return true;
    }

    private bool HadRecentSqueeze(int index, int lookbackBars)
    {
        int start = Math.Max(1, index - lookbackBars);
        for (int i = start; i < index; i++)
        {
            var row = _bars15m[i];
            if (!double.IsNaN(row.BbUpper)
                && !double.IsNaN(row.BbLower)
                && !double.IsNaN(row.KcUpper)
                && !double.IsNaN(row.KcLower)
                && row.BbUpper < row.KcUpper
                && row.BbLower > row.KcLower)
            {
                return true;
            }
        }

        return false;
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
        var plan = ResolvePlan(signal);
        var events = new List<BacktestTradeLifecycleEvent>
        {
            new(
                EventType: BacktestTradeLifecycleEventType.EntryAccepted,
                BarIndex: signal.BarIndex,
                Timestamp: signal.Timestamp,
                Price: signal.EntryPrice,
                Quantity: signal.PositionSize,
                Reason: "entry-accepted",
                Detail: $"{plan.SetupId} score {plan.Score} angle {plan.AngleDegrees:F2} triggered {signal.Side} entry."),
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
                Detail: $"15m bar {exit15mBar.Bar.Timestamp:O} triggered {exitResolution.ExitReason} for {plan.SetupId}.",
                ReferencePrice: exitResolution.TrailingStop,
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
            ReferencePrice: exitResolution.TrailingStop,
            RMultiple: pnlR));
        events.Add(new(
            EventType: BacktestTradeLifecycleEventType.Finalized,
            BarIndex: exitBarIndex,
            Timestamp: exitTimestamp,
            Price: exitPrice,
            Quantity: signal.PositionSize,
            Reason: "trade-finalized",
            Detail: $"V22 finalized with {exitResolution.ExitReason}.",
            ReferencePrice: exitResolution.TrailingStop,
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
                Description: signal.SubStrategy,
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

    private SignalPlan ResolvePlan(BacktestSignal signal)
    {
        return _signalPlans.TryGetValue(BuildSignalKey(signal), out var plan)
            ? plan
            : new SignalPlan(signal.SubStrategy, ResolveSource15mIndex(signal), signal.EntryScore, 0.0, signal.MtfMomentum);
    }

    private static string BuildSignalKey(BacktestSignal signal)
        => $"{signal.BarIndex}|{signal.Timestamp:O}|{signal.Side}";

    private double ResolveAtr(int index, double fallbackAtr)
    {
        double atr = _bars15m[index].Atr14;
        if (!double.IsNaN(atr) && atr > 0)
        {
            return atr;
        }

        if (!double.IsNaN(fallbackAtr) && fallbackAtr > 0)
        {
            return fallbackAtr;
        }

        for (int i = index - 1; i >= 0; i--)
        {
            atr = _bars15m[i].Atr14;
            if (!double.IsNaN(atr) && atr > 0)
            {
                return atr;
            }
        }

        return double.NaN;
    }

    private static bool IsStopTouched(TradeSide side, EnrichedBar row, double stopPrice)
    {
        return side == TradeSide.Long
            ? row.Bar.Low <= stopPrice
            : row.Bar.High >= stopPrice;
    }

    private static double DistanceAtr(double price, double reference, double atr)
    {
        if (double.IsNaN(price) || double.IsNaN(reference) || double.IsNaN(atr) || atr <= 0)
        {
            return double.PositiveInfinity;
        }

        return Math.Abs(price - reference) / atr;
    }

    private static double MaxFinite(params double[] values)
    {
        double result = double.NaN;
        foreach (var value in values)
        {
            if (double.IsNaN(value))
            {
                continue;
            }

            result = double.IsNaN(result) ? value : Math.Max(result, value);
        }

        return result;
    }

    private static double MinFinite(params double[] values)
    {
        double result = double.NaN;
        foreach (var value in values)
        {
            if (double.IsNaN(value))
            {
                continue;
            }

            result = double.IsNaN(result) ? value : Math.Min(result, value);
        }

        return result;
    }

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

    private sealed record SignalCandidate(
        TradeSide Side,
        string SetupId,
        int Score,
        double AngleDegrees,
        HtfBias Bias,
        string MomentumLabel);

    private sealed record SignalPlan(
        string SetupId,
        int Source15mIndex,
        int Score,
        double AngleDegrees,
        string MomentumLabel);

    private sealed record ExitResolution(
        int ExitBarIndex,
        ExitReason ExitReason,
        int? Exit15mIndex,
        string Detail,
        BacktestSignalRetryLockout? RetryLockout,
        double TrailingStop);
}
