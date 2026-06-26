namespace Sailor.App.Backtest.Scanner;

public sealed record ScannerCandidate(
    string Symbol,
    string Timeframe,
    string Side,
    decimal Close,
    long Volume,
    decimal? Ema9,
    decimal? Sma20,
    decimal? Sma200,
    decimal? Vwap,
    decimal? VolumeAverage20,
    decimal VolumeRatio,
    decimal MomentumPercent,
    decimal Score,
    string Reason)
{
    public string ToDisplayLine(int rank)
    {
        return $"{rank,2}. {Symbol,-6} | Side={Side,-5} | Close={Close,8:F2} | Score={Score,7:F2} | " +
               $"Mom={MomentumPercent,7:F2}% | VolRatio={VolumeRatio,5:F2} | " +
               $"EMA9={Format(Ema9),8} | SMA20={Format(Sma20),8} | SMA200={Format(Sma200),8} | VWAP={Format(Vwap),8} | {Reason}";
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }
}
