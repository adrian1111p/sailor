using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Strategies;

public sealed class SailorConductBacktestStrategy : IBacktestStrategy
{
    private readonly SailorStrategyProfile _profile;

    public SailorConductBacktestStrategy(SailorStrategyProfile profile)
    {
        _profile = profile;
    }

    public string Name => "SailorConductV3";

    public BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        bool hasOpenPosition)
    {
        if (previousBar is null)
        {
            return BacktestSignal.Hold("Waiting for previous bar.");
        }

        if (hasOpenPosition)
        {
            return BacktestSignal.Hold("Open position is managed by Sailor conduct exit engine.");
        }

        return EvaluateEntry(currentBar, previousBar, indicators);
    }

    private BacktestSignal EvaluateEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators)
    {
        if (currentBar.Close < _profile.MinimumPrice || currentBar.Close > _profile.MaximumPrice)
        {
            return BacktestSignal.Hold(
                $"Price filter failed: close {currentBar.Close:F2} outside {_profile.MinimumPrice:F2}-{_profile.MaximumPrice:F2}.");
        }

        decimal entryThreshold = previousBar.Close * (1m + _profile.EntryMomentumPercent / 100m);
        if (currentBar.Close < entryThreshold)
        {
            return BacktestSignal.Hold(
                $"Conduct entry momentum failed: close {currentBar.Close:F2} < threshold {entryThreshold:F2}.");
        }

        if (_profile.RequireEma9AboveSma20)
        {
            if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
            {
                return BacktestSignal.Hold("Conduct entry waiting for EMA9 and SMA20.");
            }

            if (indicators.Ema9.Value <= indicators.Sma20.Value)
            {
                return BacktestSignal.Hold(
                    $"Conduct fast trend failed: EMA9 {indicators.Ema9.Value:F2} <= SMA20 {indicators.Sma20.Value:F2}.");
            }
        }

        if (_profile.RequirePriceAboveVwap)
        {
            if (!indicators.Vwap.HasValue)
            {
                return BacktestSignal.Hold("Conduct entry waiting for VWAP.");
            }

            if (currentBar.Close <= indicators.Vwap.Value)
            {
                return BacktestSignal.Hold(
                    $"Conduct VWAP filter failed: close {currentBar.Close:F2} <= VWAP {indicators.Vwap.Value:F2}.");
            }
        }

        if (_profile.RequirePriceAboveSma200WhenAvailable &&
            indicators.Sma200.HasValue &&
            currentBar.Close <= indicators.Sma200.Value)
        {
            return BacktestSignal.Hold(
                $"Conduct long trend failed: close {currentBar.Close:F2} <= SMA200 {indicators.Sma200.Value:F2}.");
        }

        if (currentBar.Volume < _profile.MinimumVolume)
        {
            return BacktestSignal.Hold(
                $"Conduct minimum volume failed: volume {currentBar.Volume} < {_profile.MinimumVolume}.");
        }

        if (_profile.MinimumVolumeRatio > 0m)
        {
            if (!indicators.VolumeAverage20.HasValue || indicators.VolumeAverage20.Value <= 0m)
            {
                return BacktestSignal.Hold("Conduct volume ratio waiting for VolumeAverage20.");
            }

            decimal volumeRatio = currentBar.Volume / indicators.VolumeAverage20.Value;
            if (volumeRatio < _profile.MinimumVolumeRatio)
            {
                return BacktestSignal.Hold(
                    $"Conduct volume ratio failed: ratio {volumeRatio:F2} < {_profile.MinimumVolumeRatio:F2}.");
            }
        }

        return BacktestSignal.Buy(
            $"conduct entry: close {currentBar.Close:F2}, threshold {entryThreshold:F2}, " +
            $"EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, " +
            $"SMA200={Format(indicators.Sma200)}, VWAP={Format(indicators.Vwap)}, " +
            $"Volume={currentBar.Volume}, VolAvg20={Format(indicators.VolumeAverage20)}.");
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }
}
