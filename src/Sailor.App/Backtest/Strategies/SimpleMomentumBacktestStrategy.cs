using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Strategies;

public sealed class SimpleMomentumBacktestStrategy : IBacktestStrategy
{
    public string Name => "SimpleMomentumWithIndicators";

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

        decimal buyThreshold = previousBar.Close * 1.002m;
        decimal sellThreshold = previousBar.Close * 0.998m;

        bool hasTrendData = indicators.Ema9.HasValue && indicators.Sma20.HasValue;
        bool fastTrendOk = !hasTrendData || indicators.Ema9.Value >= indicators.Sma20.Value;
        bool vwapOk = !indicators.Vwap.HasValue || currentBar.Close >= indicators.Vwap.Value;
        bool volumeOk = !indicators.VolumeAverage20.HasValue || currentBar.Volume >= indicators.VolumeAverage20.Value;

        if (!hasOpenPosition && currentBar.Close >= buyThreshold && fastTrendOk && vwapOk && volumeOk)
        {
            return BacktestSignal.Buy(
                $"Momentum + indicators: close {currentBar.Close:F2} >= {buyThreshold:F2}, " +
                $"EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, " +
                $"VWAP={Format(indicators.Vwap)}, VolAvg20={Format(indicators.VolumeAverage20)}");
        }

        if (hasOpenPosition)
        {
            if (currentBar.Close <= sellThreshold)
            {
                return BacktestSignal.Sell(
                    $"Momentum down: close {currentBar.Close:F2} <= {sellThreshold:F2}");
            }

            if (indicators.Ema9.HasValue && currentBar.Close < indicators.Ema9.Value)
            {
                return BacktestSignal.Sell(
                    $"Close below EMA9: close {currentBar.Close:F2} < EMA9 {indicators.Ema9.Value:F2}");
            }
        }

        return BacktestSignal.Hold("No action.");
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }
}
