using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct.AngleEma;

internal enum AngleConductSide
{
    Flat = 0,
    Long = 1,
    Short = -1
}

public sealed record AngleEmaConductSettings(
    string ProfileName,
    string StrategyName,
    string VariantName,
    int CandleMinutes,
    decimal AngleThresholdDegrees,
    decimal NeutralAngleDegrees,
    decimal MinimumPrice,
    decimal MaximumPrice,
    long MinimumVolume,
    decimal MinimumVolumeRatio,
    bool AllowShort,
    int EmaPeriod,
    int MaxRecentRawBars,
    bool DirectInitialEntryFromAngle = false);

internal sealed record AngleEmaState(
    decimal? Ema9,
    decimal? PreviousEma9,
    decimal? Atr,
    decimal AngleDegrees,
    AngleConductCandle? SignalCandle,
    AngleConductCandle? PreviousSignalCandle,
    AngleConductCandle? LastGreenCandle,
    AngleConductCandle? LastRedCandle)
{
    public bool IsReady => Ema9.HasValue && PreviousEma9.HasValue && Atr.HasValue && SignalCandle is not null;
}

internal sealed record AngleConductCandle(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string Symbol,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Atr14 = null)
{
    public bool IsGreen => Close > Open;

    public bool IsRed => Close < Open;

    public bool Crosses(decimal value)
    {
        return Low <= value && High >= value;
    }

    public bool IsAbove(decimal value)
    {
        return Low >= value || Close >= value;
    }

    public bool IsBelow(decimal value)
    {
        return High <= value || Close <= value;
    }

    public decimal BodyHigh => Math.Max(Open, Close);

    public decimal BodyLow => Math.Min(Open, Close);

    public decimal LongSupport => Math.Min(Low, Open);

    public decimal ShortResistance => Math.Max(High, Open);
}
