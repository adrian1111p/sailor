using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Strategies;

public sealed class SailorTrendVolumeBacktestStrategy : IBacktestStrategy
{
    private readonly SailorStrategyProfile _profile;

    public SailorTrendVolumeBacktestStrategy(SailorStrategyProfile profile)
    {
        _profile = profile;
    }

    public bool AllowsShortEntries => _profile.SideMode.AllowsShort();

    public string Name => _profile.Name;

    public BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        bool hasOpenPosition,
        int positionSide)
    {
        if (previousBar is null)
        {
            return BacktestSignal.Hold("Waiting for previous bar.");
        }

        if (hasOpenPosition)
        {
            return positionSide < 0
                ? EvaluateShortExit(currentBar, previousBar, indicators)
                : EvaluateLongExit(currentBar, previousBar, indicators);
        }

        return EvaluateEntry(currentBar, previousBar, indicators);
    }

    private BacktestSignal EvaluateEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators)
    {
        if (!PassesCommonFilters(currentBar, indicators, out string rejectReason))
        {
            return BacktestSignal.Hold(rejectReason);
        }

        if (_profile.SideMode.AllowsLong())
        {
            BacktestSignal longSignal = EvaluateLongEntry(currentBar, previousBar, indicators);
            if (longSignal.Type == BacktestSignalType.Buy)
            {
                return longSignal;
            }
        }

        if (_profile.SideMode.AllowsShort())
        {
            BacktestSignal shortSignal = EvaluateShortEntry(currentBar, previousBar, indicators);
            if (shortSignal.Type == BacktestSignalType.Sell)
            {
                return shortSignal;
            }
        }

        return BacktestSignal.Hold(
            $"No {_profile.SideMode} entry. Close={currentBar.Close:F2}, EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, VWAP={Format(indicators.Vwap)}.");
    }

    private BacktestSignal EvaluateLongEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators)
    {
        decimal entryThreshold = previousBar.Close * (1m + _profile.EntryMomentumPercent / 100m);
        if (currentBar.Close < entryThreshold)
        {
            return BacktestSignal.Hold($"Long momentum filter failed: close {currentBar.Close:F2} < threshold {entryThreshold:F2}.");
        }

        if (_profile.RequireEma9AboveSma20)
        {
            if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
            {
                return BacktestSignal.Hold("Long fast trend filter waiting for EMA9 and SMA20.");
            }

            if (indicators.Ema9.Value <= indicators.Sma20.Value)
            {
                return BacktestSignal.Hold($"Long fast trend filter failed: EMA9 {indicators.Ema9.Value:F2} <= SMA20 {indicators.Sma20.Value:F2}.");
            }
        }

        if (_profile.RequirePriceAboveVwap)
        {
            if (!indicators.Vwap.HasValue)
            {
                return BacktestSignal.Hold("Long VWAP filter waiting for VWAP.");
            }

            if (currentBar.Close <= indicators.Vwap.Value)
            {
                return BacktestSignal.Hold($"Long VWAP filter failed: close {currentBar.Close:F2} <= VWAP {indicators.Vwap.Value:F2}.");
            }
        }

        if (_profile.RequirePriceAboveSma200WhenAvailable && indicators.Sma200.HasValue && currentBar.Close <= indicators.Sma200.Value)
        {
            return BacktestSignal.Hold($"Long SMA200 filter failed: close {currentBar.Close:F2} <= SMA200 {indicators.Sma200.Value:F2}.");
        }

        return BacktestSignal.Buy(
            $"{_profile.Name} LONG entry: close {currentBar.Close:F2}, EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, " +
            $"SMA200={Format(indicators.Sma200)}, VWAP={Format(indicators.Vwap)}, Volume={currentBar.Volume}, VolAvg20={Format(indicators.VolumeAverage20)}.");
    }

    private BacktestSignal EvaluateShortEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators)
    {
        decimal entryThreshold = previousBar.Close * (1m - _profile.EntryMomentumPercent / 100m);
        if (currentBar.Close > entryThreshold)
        {
            return BacktestSignal.Hold($"Short momentum filter failed: close {currentBar.Close:F2} > threshold {entryThreshold:F2}.");
        }

        if (_profile.RequireEma9AboveSma20)
        {
            if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
            {
                return BacktestSignal.Hold("Short fast trend filter waiting for EMA9 and SMA20.");
            }

            if (indicators.Ema9.Value >= indicators.Sma20.Value)
            {
                return BacktestSignal.Hold($"Short fast trend filter failed: EMA9 {indicators.Ema9.Value:F2} >= SMA20 {indicators.Sma20.Value:F2}.");
            }
        }

        if (_profile.RequirePriceAboveVwap)
        {
            if (!indicators.Vwap.HasValue)
            {
                return BacktestSignal.Hold("Short VWAP filter waiting for VWAP.");
            }

            if (currentBar.Close >= indicators.Vwap.Value)
            {
                return BacktestSignal.Hold($"Short VWAP filter failed: close {currentBar.Close:F2} >= VWAP {indicators.Vwap.Value:F2}.");
            }
        }

        if (_profile.RequirePriceAboveSma200WhenAvailable && indicators.Sma200.HasValue && currentBar.Close >= indicators.Sma200.Value)
        {
            return BacktestSignal.Hold($"Short SMA200 filter failed: close {currentBar.Close:F2} >= SMA200 {indicators.Sma200.Value:F2}.");
        }

        return BacktestSignal.Sell(
            $"{_profile.Name} SHORT entry: close {currentBar.Close:F2}, EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, " +
            $"SMA200={Format(indicators.Sma200)}, VWAP={Format(indicators.Vwap)}, Volume={currentBar.Volume}, VolAvg20={Format(indicators.VolumeAverage20)}.");
    }

    private bool PassesCommonFilters(BacktestBar currentBar, BacktestIndicatorSnapshot indicators, out string rejectReason)
    {
        if (currentBar.Close < _profile.MinimumPrice || currentBar.Close > _profile.MaximumPrice)
        {
            rejectReason = $"Price filter failed: close {currentBar.Close:F2} outside {_profile.MinimumPrice:F2}-{_profile.MaximumPrice:F2}.";
            return false;
        }

        if (currentBar.Volume < _profile.MinimumVolume)
        {
            rejectReason = $"Minimum volume filter failed: volume {currentBar.Volume} < {_profile.MinimumVolume}.";
            return false;
        }

        if (_profile.MinimumVolumeRatio > 0m)
        {
            if (!indicators.VolumeAverage20.HasValue || indicators.VolumeAverage20.Value <= 0m)
            {
                rejectReason = "Volume ratio filter waiting for VolumeAverage20.";
                return false;
            }

            decimal volumeRatio = currentBar.Volume / indicators.VolumeAverage20.Value;
            if (volumeRatio < _profile.MinimumVolumeRatio)
            {
                rejectReason = $"Volume ratio filter failed: ratio {volumeRatio:F2} < {_profile.MinimumVolumeRatio:F2}.";
                return false;
            }
        }

        rejectReason = string.Empty;
        return true;
    }

    private BacktestSignal EvaluateLongExit(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        decimal exitThreshold = previousBar.Close * (1m - _profile.ExitMomentumPercent / 100m);
        if (currentBar.Close <= exitThreshold)
        {
            return BacktestSignal.Sell($"Long momentum exit: close {currentBar.Close:F2} <= threshold {exitThreshold:F2}.");
        }

        if (indicators.Ema9.HasValue && currentBar.Close < indicators.Ema9.Value)
        {
            return BacktestSignal.Sell($"Long EMA9 exit: close {currentBar.Close:F2} < EMA9 {indicators.Ema9.Value:F2}.");
        }

        if (indicators.Vwap.HasValue && currentBar.Close < indicators.Vwap.Value)
        {
            return BacktestSignal.Sell($"Long VWAP exit: close {currentBar.Close:F2} < VWAP {indicators.Vwap.Value:F2}.");
        }

        if (indicators.Ema9.HasValue && indicators.Sma20.HasValue && indicators.Ema9.Value < indicators.Sma20.Value)
        {
            return BacktestSignal.Sell($"Long trend exit: EMA9 {indicators.Ema9.Value:F2} < SMA20 {indicators.Sma20.Value:F2}.");
        }

        return BacktestSignal.Hold("Long position remains valid.");
    }

    private BacktestSignal EvaluateShortExit(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        decimal exitThreshold = previousBar.Close * (1m + _profile.ExitMomentumPercent / 100m);
        if (currentBar.Close >= exitThreshold)
        {
            return BacktestSignal.Buy($"Short momentum exit: close {currentBar.Close:F2} >= threshold {exitThreshold:F2}.");
        }

        if (indicators.Ema9.HasValue && currentBar.Close > indicators.Ema9.Value)
        {
            return BacktestSignal.Buy($"Short EMA9 exit: close {currentBar.Close:F2} > EMA9 {indicators.Ema9.Value:F2}.");
        }

        if (indicators.Vwap.HasValue && currentBar.Close > indicators.Vwap.Value)
        {
            return BacktestSignal.Buy($"Short VWAP exit: close {currentBar.Close:F2} > VWAP {indicators.Vwap.Value:F2}.");
        }

        if (indicators.Ema9.HasValue && indicators.Sma20.HasValue && indicators.Ema9.Value > indicators.Sma20.Value)
        {
            return BacktestSignal.Buy($"Short trend exit: EMA9 {indicators.Ema9.Value:F2} > SMA20 {indicators.Sma20.Value:F2}.");
        }

        return BacktestSignal.Hold("Short position remains valid.");
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }
}
