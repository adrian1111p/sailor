using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Indicators;

public static class TechnicalIndicatorCalculator
{
    private const int Ema9Period = 9;
    private const int Sma20Period = 20;
    private const int Sma200Period = 200;
    private const int VolumeAveragePeriod = 20;

    public static IReadOnlyList<BacktestIndicatorSnapshot> Calculate(IReadOnlyList<BacktestBar> bars)
    {
        if (bars.Count == 0)
        {
            return Array.Empty<BacktestIndicatorSnapshot>();
        }

        var result = new BacktestIndicatorSnapshot[bars.Count];

        decimal? ema9 = null;
        decimal closeSum20 = 0m;
        decimal closeSum200 = 0m;
        decimal volumeSum20 = 0m;
        decimal cumulativeTypicalPriceVolume = 0m;
        decimal cumulativeVolume = 0m;
        decimal ema9Multiplier = 2m / (Ema9Period + 1m);

        for (int i = 0; i < bars.Count; i++)
        {
            BacktestBar bar = bars[i];

            closeSum20 += bar.Close;
            closeSum200 += bar.Close;
            volumeSum20 += bar.Volume;

            if (i >= Sma20Period)
            {
                closeSum20 -= bars[i - Sma20Period].Close;
            }

            if (i >= Sma200Period)
            {
                closeSum200 -= bars[i - Sma200Period].Close;
            }

            if (i >= VolumeAveragePeriod)
            {
                volumeSum20 -= bars[i - VolumeAveragePeriod].Volume;
            }

            if (i == Ema9Period - 1)
            {
                decimal seed = 0m;
                for (int j = 0; j < Ema9Period; j++)
                {
                    seed += bars[j].Close;
                }

                ema9 = seed / Ema9Period;
            }
            else if (i >= Ema9Period && ema9.HasValue)
            {
                ema9 = ((bar.Close - ema9.Value) * ema9Multiplier) + ema9.Value;
            }

            decimal typicalPrice = (bar.High + bar.Low + bar.Close) / 3m;
            cumulativeTypicalPriceVolume += typicalPrice * bar.Volume;
            cumulativeVolume += bar.Volume;

            decimal? sma20 = i >= Sma20Period - 1
                ? closeSum20 / Sma20Period
                : null;

            decimal? sma200 = i >= Sma200Period - 1
                ? closeSum200 / Sma200Period
                : null;

            decimal? vwap = cumulativeVolume > 0m
                ? cumulativeTypicalPriceVolume / cumulativeVolume
                : null;

            decimal? volumeAverage20 = i >= VolumeAveragePeriod - 1
                ? volumeSum20 / VolumeAveragePeriod
                : null;

            result[i] = new BacktestIndicatorSnapshot(
                Time: bar.Time,
                Symbol: bar.Symbol,
                BarIndex: i,
                Ema9: Round(ema9),
                Sma20: Round(sma20),
                Sma200: Round(sma200),
                Vwap: Round(vwap),
                VolumeAverage20: Round(volumeAverage20));
        }

        return result;
    }

    private static decimal? Round(decimal? value)
    {
        return value.HasValue
            ? decimal.Round(value.Value, 4)
            : null;
    }
}
