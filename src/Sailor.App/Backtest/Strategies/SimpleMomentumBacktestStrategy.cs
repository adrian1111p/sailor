using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Strategies;

public sealed class SimpleMomentumBacktestStrategy : IBacktestStrategy
{
    private readonly SailorStrategyProfile _profile;

    public SimpleMomentumBacktestStrategy(SailorStrategyProfile profile)
    {
        _profile = profile;
    }

    public bool AllowsShortEntries => _profile.SideMode.AllowsShort();

    public string Name => "SimpleMomentumWithIndicators";

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

        decimal buyThreshold = previousBar.Close * 1.002m;
        decimal sellThreshold = previousBar.Close * 0.998m;

        bool hasTrendData = indicators.Ema9.HasValue && indicators.Sma20.HasValue;
        bool fastTrendLongOk = !hasTrendData || indicators.Ema9.GetValueOrDefault() >= indicators.Sma20.GetValueOrDefault();
        bool fastTrendShortOk = !hasTrendData || indicators.Ema9.GetValueOrDefault() <= indicators.Sma20.GetValueOrDefault();
        bool vwapLongOk = !indicators.Vwap.HasValue || currentBar.Close >= indicators.Vwap.Value;
        bool vwapShortOk = !indicators.Vwap.HasValue || currentBar.Close <= indicators.Vwap.Value;
        bool volumeOk = !indicators.VolumeAverage20.HasValue || currentBar.Volume >= indicators.VolumeAverage20.Value;

        if (!hasOpenPosition)
        {
            if (_profile.SideMode.AllowsLong() && currentBar.Close >= buyThreshold && fastTrendLongOk && vwapLongOk && volumeOk)
            {
                return BacktestSignal.Buy(
                    $"LONG momentum + indicators: close {currentBar.Close:F2} >= {buyThreshold:F2}, " +
                    $"EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, " +
                    $"VWAP={Format(indicators.Vwap)}, VolAvg20={Format(indicators.VolumeAverage20)}");
            }

            if (_profile.SideMode.AllowsShort() && currentBar.Close <= sellThreshold && fastTrendShortOk && vwapShortOk && volumeOk)
            {
                return BacktestSignal.Sell(
                    $"SHORT momentum + indicators: close {currentBar.Close:F2} <= {sellThreshold:F2}, " +
                    $"EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, " +
                    $"VWAP={Format(indicators.Vwap)}, VolAvg20={Format(indicators.VolumeAverage20)}");
            }
        }

        if (hasOpenPosition && positionSide > 0)
        {
            if (currentBar.Close <= sellThreshold)
            {
                return BacktestSignal.Sell($"Long momentum down: close {currentBar.Close:F2} <= {sellThreshold:F2}");
            }

            if (indicators.Ema9.HasValue && currentBar.Close < indicators.Ema9.Value)
            {
                return BacktestSignal.Sell($"Long close below EMA9: close {currentBar.Close:F2} < EMA9 {indicators.Ema9.Value:F2}");
            }
        }

        if (hasOpenPosition && positionSide < 0)
        {
            if (currentBar.Close >= buyThreshold)
            {
                return BacktestSignal.Buy($"Short momentum up: close {currentBar.Close:F2} >= {buyThreshold:F2}");
            }

            if (indicators.Ema9.HasValue && currentBar.Close > indicators.Ema9.Value)
            {
                return BacktestSignal.Buy($"Short close above EMA9: close {currentBar.Close:F2} > EMA9 {indicators.Ema9.Value:F2}");
            }
        }

        return BacktestSignal.Hold("No action.");
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }
}
