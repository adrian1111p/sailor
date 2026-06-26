using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Reports;

public sealed record TradeChartContext(
    string Id,
    TradeReportRow Trade,
    int BarsHeld,
    decimal MfeDollars,
    decimal MaeDollars,
    decimal FavorableMovePercent,
    decimal AdverseMovePercent,
    TradeIndicatorContext? EntryIndicators,
    TradeIndicatorContext? ExitIndicators,
    IReadOnlyList<TradeChartPoint> PrimaryPoints,
    int EntryPointIndex,
    int ExitPointIndex,
    HigherTimeframeChartContext? HigherTimeframeChart,
    IReadOnlyList<string> Timeline,
    IReadOnlyList<string> Explanations);

public sealed record TradeIndicatorContext(
    string Time,
    decimal Close,
    long Volume,
    decimal? Ema9,
    decimal? Sma20,
    decimal? Sma200,
    decimal? Vwap,
    decimal? VolumeAverage20);

public sealed record TradeChartPoint(
    string Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Ema9,
    decimal? Sma20,
    decimal? Sma200,
    decimal? Vwap);

public sealed record HigherTimeframeChartContext(
    int CandleMinutes,
    decimal AngleThresholdDegrees,
    IReadOnlyList<HigherTimeframeChartPoint> Points,
    int SignalPointIndex,
    string SignalSummary);

public sealed record HigherTimeframeChartPoint(
    string Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Ema9,
    decimal? Atr14,
    decimal? AngleDegrees);
