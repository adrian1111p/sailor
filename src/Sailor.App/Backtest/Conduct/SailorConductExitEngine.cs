using Sailor.App.Backtest.Models;
using Sailor.App.Configuration;

namespace Sailor.App.Backtest.Conduct;

public sealed class SailorConductExitEngine
{
    private readonly ConductExitSettings _settings;

    public SailorConductExitEngine(ConductExitSettings settings)
    {
        _settings = settings;
    }

    public SailorConductExitDecision EvaluateLongExit(
        BacktestBar bar,
        BacktestIndicatorSnapshot indicators,
        BacktestOptions options,
        SailorConductExitState state,
        int currentBarIndex)
    {
        state.ObserveBarHigh(bar.High);

        int barsHeld = currentBarIndex - state.EntryBarIndex;
        decimal hardStopPercent = _settings.HardStopPercent > 0m
            ? _settings.HardStopPercent
            : options.StopLossPercent;

        decimal activeStop = state.EntryPrice * (1m - hardStopPercent / 100m);

        if (_settings.MoveStopToBreakevenAfterPercent > 0m &&
            state.PeakPercent >= _settings.MoveStopToBreakevenAfterPercent)
        {
            state.ArmBreakeven();
        }

        if (state.BreakevenArmed)
        {
            decimal breakevenStop = state.EntryPrice * (1m + _settings.BreakevenBufferPercent / 100m);
            activeStop = Math.Max(activeStop, breakevenStop);
        }

        if (_settings.StartTrailingAfterPercent > 0m &&
            state.PeakPercent >= _settings.StartTrailingAfterPercent)
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

            activeStop = Math.Max(activeStop, trailingStop);
        }

        if (bar.Low <= activeStop)
        {
            string stopMode = state.TrailingArmed
                ? "conduct trailing/giveback stop"
                : state.BreakevenArmed
                    ? "conduct breakeven stop"
                    : "conduct hard stop";

            return SailorConductExitDecision.Exit(
                activeStop,
                $"{stopMode}: low {bar.Low:F2} <= active stop {activeStop:F2}, peak {state.PeakPrice:F2}, held {barsHeld} bars.");
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
                    $"conduct take profit: high {bar.High:F2} >= target {takeProfitPrice:F2}, held {barsHeld} bars.");
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
            if (_settings.UseEma9Exit && indicators.Ema9.HasValue && bar.Close < indicators.Ema9.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct EMA9 reversal: close {bar.Close:F2} < EMA9 {indicators.Ema9.Value:F2}, held {barsHeld} bars.");
            }

            if (_settings.UseVwapExit && indicators.Vwap.HasValue && bar.Close < indicators.Vwap.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct VWAP reversal: close {bar.Close:F2} < VWAP {indicators.Vwap.Value:F2}, held {barsHeld} bars.");
            }

            if (_settings.UseTrendExit &&
                indicators.Ema9.HasValue &&
                indicators.Sma20.HasValue &&
                indicators.Ema9.Value < indicators.Sma20.Value)
            {
                return SailorConductExitDecision.Exit(
                    bar.Close,
                    $"conduct trend reversal: EMA9 {indicators.Ema9.Value:F2} < SMA20 {indicators.Sma20.Value:F2}, held {barsHeld} bars.");
            }
        }

        return SailorConductExitDecision.Hold(
            $"conduct hold: peak {state.PeakPrice:F2}, active stop {activeStop:F2}, held {barsHeld} bars.");
    }

    private decimal CalculateGivebackPerShare(SailorConductExitState state)
    {
        decimal priceTrail = state.PeakPrice * Math.Max(0.01m, _settings.GivebackPercent) / 100m;

        if (_settings.GivebackNotionalCap <= 0m || state.Quantity <= 0)
        {
            return priceTrail;
        }

        decimal cappedTrail = _settings.GivebackNotionalCap / state.Quantity;
        return Math.Max(0.01m, Math.Min(priceTrail, cappedTrail));
    }
}
