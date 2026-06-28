using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Configuration;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Backtest.Conduct;

public sealed class SailorConductExitEngine
{
    private readonly ConductExitSettings _settings;
    private readonly L1L2SnapshotSettings _snapshotSettings;

    public SailorConductExitEngine(
        ConductExitSettings settings,
        L1L2SnapshotSettings? snapshotSettings = null)
    {
        _settings = settings;
        _snapshotSettings = snapshotSettings ?? new L1L2SnapshotSettings();
    }

    public SailorConductExitDecision EvaluateExit(
        int positionSide,
        BacktestBar bar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        BacktestOptions options,
        SailorStrategyProfile profile,
        SailorConductExitState state,
        int currentBarIndex,
        SailorMarketSnapshot? snapshot = null)
    {
        return positionSide < 0
            ? EvaluateShortExit(bar, previousBar, indicators, options, profile, state, currentBarIndex, snapshot)
            : EvaluateLongExit(bar, previousBar, indicators, options, profile, state, currentBarIndex, snapshot);
    }

    public SailorConductExitDecision EvaluateLongExit(
        BacktestBar bar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        BacktestOptions options,
        SailorStrategyProfile profile,
        SailorConductExitState state,
        int currentBarIndex,
        SailorMarketSnapshot? snapshot = null)
    {
        state.ObserveBarHigh(bar.High);

        int barsHeld = currentBarIndex - state.EntryBarIndex;
        decimal hardStopPercent = _settings.HardStopPercent > 0m
            ? _settings.HardStopPercent
            : options.StopLossPercent;

        decimal activeStop = state.EntryPrice * (1m - hardStopPercent / 100m);
        string activeStopMode = "conduct hard stop";

        if (_settings.MoveStopToBreakevenAfterPercent > 0m &&
            state.FavorablePercent >= _settings.MoveStopToBreakevenAfterPercent)
        {
            state.ArmBreakeven();
        }

        if (state.BreakevenArmed)
        {
            decimal breakevenStop = state.EntryPrice * (1m + _settings.BreakevenBufferPercent / 100m);
            if (breakevenStop > activeStop)
            {
                activeStop = breakevenStop;
                activeStopMode = "conduct breakeven stop";
            }
        }

        if (_settings.StartTrailingAfterPercent > 0m &&
            state.FavorablePercent >= _settings.StartTrailingAfterPercent)
        {
            state.ArmTrailing();
        }

        if (state.TrailingArmed)
        {
            decimal givebackPerShare = CalculateGivebackPerShare(state);
            decimal trailingStop = state.PeakPrice - givebackPerShare;

            if (state.BreakevenArmed)
            {
                decimal breakevenStop = state.EntryPrice * (1m + _settings.BreakevenBufferPercent / 100m);
                trailingStop = Math.Max(trailingStop, breakevenStop);
            }

            if (trailingStop > activeStop)
            {
                activeStop = trailingStop;
                activeStopMode = "conduct trailing/giveback stop";
            }
        }

        if (_settings.UseMicroTrail &&
            _settings.MicroTrailActivatePercent > 0m &&
            state.FavorablePercent >= _settings.MicroTrailActivatePercent)
        {
            decimal microTrailStop = state.PeakPrice * (1m - _settings.MicroTrailPercent / 100m);
            if (state.BreakevenArmed)
            {
                decimal breakevenStop = state.EntryPrice * (1m + _settings.BreakevenBufferPercent / 100m);
                microTrailStop = Math.Max(microTrailStop, breakevenStop);
            }

            if (microTrailStop > activeStop)
            {
                activeStop = microTrailStop;
                activeStopMode = "conduct micro trail stop";
            }
        }

        if (bar.Low <= activeStop)
        {
            return SailorConductExitDecision.Exit(
                activeStop,
                $"{activeStopMode}: long low {bar.Low:F2} <= active stop {activeStop:F2}, peak {state.PeakPrice:F2}, held {barsHeld} bars.");
        }

        if (_settings.UseTakeProfitExit)
        {
            decimal takeProfitPercent = _settings.TakeProfitPercent > 0m
                ? _settings.TakeProfitPercent
                : options.TakeProfitPercent;

            decimal takeProfitPrice = state.EntryPrice * (1m + takeProfitPercent / 100m);
            if (bar.High >= takeProfitPrice)
            {
                return SailorConductExitDecision.Exit(
                    takeProfitPrice,
                    $"conduct take profit: long high {bar.High:F2} >= target {takeProfitPrice:F2}, held {barsHeld} bars.");
            }
        }

        int maxHoldBars = _settings.MaxHoldBars > 0
            ? _settings.MaxHoldBars
            : options.MaxHoldBars;

        if (maxHoldBars > 0 && barsHeld >= maxHoldBars)
        {
            return SailorConductExitDecision.Exit(
                bar.Close,
                $"conduct time exit: max hold reached after {barsHeld} bars.");
        }

        if (barsHeld >= _settings.MinimumBarsBeforeIndicatorExit)
        {
            if (_settings.UseOppositeMomentumExit && previousBar is not null)
            {
                decimal exitThreshold = previousBar.Close * (1m - profile.ExitMomentumPercent / 100m);
                if (bar.Close <= exitThreshold)
                {
                    return SailorConductExitDecision.Exit(
                        bar.Close,
                        $"conduct opposite momentum: long close {bar.Close:F2} <= threshold {exitThreshold:F2}, held {barsHeld} bars.");
                }
            }

            if (_settings.UseEma9Exit && indicators.Ema9.HasValue && bar.Close < indicators.Ema9.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct EMA9 reversal: long close {bar.Close:F2} < EMA9 {indicators.Ema9.Value:F2}, held {barsHeld} bars.");
            }

            if (_settings.UseVwapExit && indicators.Vwap.HasValue && bar.Close < indicators.Vwap.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct VWAP reversal: long close {bar.Close:F2} < VWAP {indicators.Vwap.Value:F2}, held {barsHeld} bars.");
            }

            if (_settings.UseTrendExit &&
                indicators.Ema9.HasValue &&
                indicators.Sma20.HasValue &&
                indicators.Ema9.Value < indicators.Sma20.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct trend reversal: long EMA9 {indicators.Ema9.Value:F2} < SMA20 {indicators.Sma20.Value:F2}, held {barsHeld} bars.");
            }
        }

        if (ShouldExitByAdverseSnapshot(profile, snapshot, positionSide: 1, out string l1L2ExitReason))
        {
            return SailorConductExitDecision.Exit(
                bar.Close,
                $"{l1L2ExitReason}, held {barsHeld} bars.");
        }

        return SailorConductExitDecision.Hold(
            $"conduct hold long: peak {state.PeakPrice:F2}, active stop {activeStop:F2}, held {barsHeld} bars.");
    }

    public SailorConductExitDecision EvaluateShortExit(
        BacktestBar bar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        BacktestOptions options,
        SailorStrategyProfile profile,
        SailorConductExitState state,
        int currentBarIndex,
        SailorMarketSnapshot? snapshot = null)
    {
        state.ObserveBarLow(bar.Low);

        int barsHeld = currentBarIndex - state.EntryBarIndex;
        decimal hardStopPercent = _settings.HardStopPercent > 0m
            ? _settings.HardStopPercent
            : options.StopLossPercent;

        decimal activeStop = state.EntryPrice * (1m + hardStopPercent / 100m);
        string activeStopMode = "conduct hard stop";

        if (_settings.MoveStopToBreakevenAfterPercent > 0m &&
            state.FavorablePercent >= _settings.MoveStopToBreakevenAfterPercent)
        {
            state.ArmBreakeven();
        }

        if (state.BreakevenArmed)
        {
            decimal breakevenStop = state.EntryPrice * (1m - _settings.BreakevenBufferPercent / 100m);
            if (breakevenStop < activeStop)
            {
                activeStop = breakevenStop;
                activeStopMode = "conduct breakeven stop";
            }
        }

        if (_settings.StartTrailingAfterPercent > 0m &&
            state.FavorablePercent >= _settings.StartTrailingAfterPercent)
        {
            state.ArmTrailing();
        }

        if (state.TrailingArmed)
        {
            decimal givebackPerShare = CalculateGivebackPerShare(state);
            decimal trailingStop = state.TroughPrice + givebackPerShare;

            if (state.BreakevenArmed)
            {
                decimal breakevenStop = state.EntryPrice * (1m - _settings.BreakevenBufferPercent / 100m);
                trailingStop = Math.Min(trailingStop, breakevenStop);
            }

            if (trailingStop < activeStop)
            {
                activeStop = trailingStop;
                activeStopMode = "conduct trailing/giveback stop";
            }
        }

        if (_settings.UseMicroTrail &&
            _settings.MicroTrailActivatePercent > 0m &&
            state.FavorablePercent >= _settings.MicroTrailActivatePercent)
        {
            decimal microTrailStop = state.TroughPrice * (1m + _settings.MicroTrailPercent / 100m);
            if (state.BreakevenArmed)
            {
                decimal breakevenStop = state.EntryPrice * (1m - _settings.BreakevenBufferPercent / 100m);
                microTrailStop = Math.Min(microTrailStop, breakevenStop);
            }

            if (microTrailStop < activeStop)
            {
                activeStop = microTrailStop;
                activeStopMode = "conduct micro trail stop";
            }
        }

        if (bar.High >= activeStop)
        {
            return SailorConductExitDecision.Exit(
                activeStop,
                $"{activeStopMode}: short high {bar.High:F2} >= active stop {activeStop:F2}, trough {state.TroughPrice:F2}, held {barsHeld} bars.");
        }

        if (_settings.UseTakeProfitExit)
        {
            decimal takeProfitPercent = _settings.TakeProfitPercent > 0m
                ? _settings.TakeProfitPercent
                : options.TakeProfitPercent;

            decimal takeProfitPrice = state.EntryPrice * (1m - takeProfitPercent / 100m);
            if (bar.Low <= takeProfitPrice)
            {
                return SailorConductExitDecision.Exit(
                    takeProfitPrice,
                    $"conduct take profit: short low {bar.Low:F2} <= target {takeProfitPrice:F2}, held {barsHeld} bars.");
            }
        }

        int maxHoldBars = _settings.MaxHoldBars > 0
            ? _settings.MaxHoldBars
            : options.MaxHoldBars;

        if (maxHoldBars > 0 && barsHeld >= maxHoldBars)
        {
            return SailorConductExitDecision.Exit(
                bar.Close,
                $"conduct time exit: max hold reached after {barsHeld} bars.");
        }

        if (barsHeld >= _settings.MinimumBarsBeforeIndicatorExit)
        {
            if (_settings.UseOppositeMomentumExit && previousBar is not null)
            {
                decimal exitThreshold = previousBar.Close * (1m + profile.ExitMomentumPercent / 100m);
                if (bar.Close >= exitThreshold)
                {
                    return SailorConductExitDecision.Exit(
                        bar.Close,
                        $"conduct opposite momentum: short close {bar.Close:F2} >= threshold {exitThreshold:F2}, held {barsHeld} bars.");
                }
            }

            if (_settings.UseEma9Exit && indicators.Ema9.HasValue && bar.Close > indicators.Ema9.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct EMA9 reversal: short close {bar.Close:F2} > EMA9 {indicators.Ema9.Value:F2}, held {barsHeld} bars.");
            }

            if (_settings.UseVwapExit && indicators.Vwap.HasValue && bar.Close > indicators.Vwap.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct VWAP reversal: short close {bar.Close:F2} > VWAP {indicators.Vwap.Value:F2}, held {barsHeld} bars.");
            }

            if (_settings.UseTrendExit &&
                indicators.Ema9.HasValue &&
                indicators.Sma20.HasValue &&
                indicators.Ema9.Value > indicators.Sma20.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct trend reversal: short EMA9 {indicators.Ema9.Value:F2} > SMA20 {indicators.Sma20.Value:F2}, held {barsHeld} bars.");
            }
        }

        if (ShouldExitByAdverseSnapshot(profile, snapshot, positionSide: -1, out string l1L2ExitReason))
        {
            return SailorConductExitDecision.Exit(
                bar.Close,
                $"{l1L2ExitReason}, held {barsHeld} bars.");
        }

        return SailorConductExitDecision.Hold(
            $"conduct hold short: trough {state.TroughPrice:F2}, active stop {activeStop:F2}, held {barsHeld} bars.");
    }

    private bool ShouldExitByAdverseSnapshot(
        SailorStrategyProfile profile,
        SailorMarketSnapshot? snapshot,
        int positionSide,
        out string reason)
    {
        reason = string.Empty;

        if (!_snapshotSettings.EnableExitGuards || !_snapshotSettings.IsProfileSuitable(profile.Name))
        {
            return false;
        }

        if (snapshot is null)
        {
            return false;
        }

        if (snapshot.Quality == SailorMarketSnapshotQuality.SyntheticBacktest &&
            _snapshotSettings.SyntheticSnapshotsAreAdvisoryOnly)
        {
            return false;
        }

        decimal imbalance = snapshot.BookImbalance;

        if (positionSide > 0 && imbalance <= -_snapshotSettings.MaximumAdverseBookImbalance)
        {
            reason = $"conduct L1/L2 adverse exit: long book imbalance {imbalance:F2} <= -{_snapshotSettings.MaximumAdverseBookImbalance:F2}";
            return true;
        }

        if (positionSide < 0 && imbalance >= _snapshotSettings.MaximumAdverseBookImbalance)
        {
            reason = $"conduct L1/L2 adverse exit: short book imbalance {imbalance:F2} >= {_snapshotSettings.MaximumAdverseBookImbalance:F2}";
            return true;
        }

        return false;
    }

    private decimal CalculateGivebackPerShare(SailorConductExitState state)
    {
        decimal referencePrice = state.PositionSide < 0 ? state.TroughPrice : state.PeakPrice;
        decimal priceTrail = referencePrice * Math.Max(0.01m, _settings.GivebackPercent) / 100m;

        if (_settings.GivebackNotionalCap <= 0m || state.Quantity <= 0)
        {
            return priceTrail;
        }

        decimal cappedTrail = _settings.GivebackNotionalCap / state.Quantity;
        return Math.Max(0.01m, Math.Min(priceTrail, cappedTrail));
    }
}
