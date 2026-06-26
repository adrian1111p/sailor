namespace Sailor.App.Backtest.Models;

public sealed record BacktestIndicatorSnapshot(
    DateTimeOffset Time,
    string Symbol,
    int BarIndex,
    decimal? Ema9,
    decimal? Sma20,
    decimal? Sma200,
    decimal? Vwap,
    decimal? VolumeAverage20)
{
    public bool HasFastTrend => Ema9.HasValue && Sma20.HasValue;

    public bool HasLongTrend => Sma200.HasValue;

    public bool HasVwap => Vwap.HasValue;

    public bool HasVolumeAverage => VolumeAverage20.HasValue;

    public string ToCompactString()
    {
        return $"EMA9={Format(Ema9),8} | SMA20={Format(Sma20),8} | SMA200={Format(Sma200),8} | VWAP={Format(Vwap),8} | VolAvg20={Format(VolumeAverage20),10}";
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }
}
