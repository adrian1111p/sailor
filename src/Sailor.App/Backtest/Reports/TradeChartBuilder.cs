using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest;

namespace Sailor.App.Backtest.Reports;

public sealed class TradeChartBuilder
{
    private readonly CsvBacktestDataProvider _provider;
    private readonly string _timeframe;
    private readonly Dictionary<string, CachedSymbolData> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TradeChartBuilder(CsvBacktestDataProvider provider, string timeframe)
    {
        _provider = provider;
        _timeframe = timeframe;
    }

    public TradeChartContext Build(TradeReportRow trade, int globalIndex)
    {
        CachedSymbolData data = GetData(trade.Symbol);
        IReadOnlyList<BacktestBar> bars = data.Bars;
        IReadOnlyList<BacktestIndicatorSnapshot> indicators = data.Indicators;

        if (bars.Count == 0)
        {
            return BuildEmpty(trade, globalIndex);
        }

        int entryIndex = FindBarIndex(bars, trade.EntryTime);
        int exitIndex = FindBarIndex(bars, trade.ExitTime);
        if (exitIndex < entryIndex)
        {
            (entryIndex, exitIndex) = (exitIndex, entryIndex);
        }

        int startIndex = Math.Max(0, entryIndex - 12);
        int endIndex = Math.Min(bars.Count - 1, exitIndex + 12);
        List<TradeChartPoint> points = BuildPrimaryPoints(bars, indicators, startIndex, endIndex);

        IReadOnlyList<BacktestBar> tradeBars = bars.Skip(entryIndex).Take(Math.Max(1, exitIndex - entryIndex + 1)).ToArray();
        decimal mfe = CalculateMfe(trade, tradeBars);
        decimal mae = CalculateMae(trade, tradeBars);
        decimal favorablePercent = trade.Quantity > 0 && trade.EntryPrice > 0m
            ? mfe / trade.Quantity / trade.EntryPrice * 100m
            : 0m;
        decimal adversePercent = trade.Quantity > 0 && trade.EntryPrice > 0m
            ? mae / trade.Quantity / trade.EntryPrice * 100m
            : 0m;

        TradeIndicatorContext? entryIndicators = BuildIndicatorContext(bars, indicators, entryIndex);
        TradeIndicatorContext? exitIndicators = BuildIndicatorContext(bars, indicators, exitIndex);
        HigherTimeframeChartContext? higherTimeframe = BuildHigherTimeframeChart(trade, bars, entryIndex, exitIndex);

        return new TradeChartContext(
            Id: $"trade_{globalIndex}",
            Trade: trade,
            BarsHeld: Math.Max(0, exitIndex - entryIndex),
            MfeDollars: decimal.Round(mfe, 2),
            MaeDollars: decimal.Round(mae, 2),
            FavorableMovePercent: decimal.Round(favorablePercent, 2),
            AdverseMovePercent: decimal.Round(adversePercent, 2),
            EntryIndicators: entryIndicators,
            ExitIndicators: exitIndicators,
            PrimaryPoints: points,
            EntryPointIndex: Math.Max(0, entryIndex - startIndex),
            ExitPointIndex: Math.Max(0, exitIndex - startIndex),
            HigherTimeframeChart: higherTimeframe,
            Timeline: BuildTimeline(trade, entryIndex, exitIndex, mfe, mae),
            Explanations: BuildExplanations(trade, higherTimeframe));
    }

    private CachedSymbolData GetData(string symbol)
    {
        if (_cache.TryGetValue(symbol, out CachedSymbolData? cached))
        {
            return cached;
        }

        BacktestDataSet dataSet = _provider.LoadBars(symbol, _timeframe);
        IReadOnlyList<BacktestBar> bars = dataSet.Bars;
        IReadOnlyList<BacktestIndicatorSnapshot> indicators = TechnicalIndicatorCalculator.Calculate(bars);
        cached = new CachedSymbolData(bars, indicators);
        _cache[symbol] = cached;
        return cached;
    }

    private static TradeChartContext BuildEmpty(TradeReportRow trade, int globalIndex)
    {
        return new TradeChartContext(
            Id: $"trade_{globalIndex}",
            Trade: trade,
            BarsHeld: 0,
            MfeDollars: 0m,
            MaeDollars: 0m,
            FavorableMovePercent: 0m,
            AdverseMovePercent: 0m,
            EntryIndicators: null,
            ExitIndicators: null,
            PrimaryPoints: [],
            EntryPointIndex: 0,
            ExitPointIndex: 0,
            HigherTimeframeChart: null,
            Timeline: ["No chart data found for this symbol/timeframe."],
            Explanations: ["The trade can be listed, but Sailor could not rebuild its chart window from CSV data."]);
    }

    private static int FindBarIndex(IReadOnlyList<BacktestBar> bars, DateTimeOffset time)
    {
        for (int i = 0; i < bars.Count; i++)
        {
            if (bars[i].Time >= time)
            {
                return i;
            }
        }

        return Math.Max(0, bars.Count - 1);
    }

    private static List<TradeChartPoint> BuildPrimaryPoints(
        IReadOnlyList<BacktestBar> bars,
        IReadOnlyList<BacktestIndicatorSnapshot> indicators,
        int startIndex,
        int endIndex)
    {
        var points = new List<TradeChartPoint>();
        for (int i = startIndex; i <= endIndex; i++)
        {
            BacktestBar bar = bars[i];
            BacktestIndicatorSnapshot indicator = indicators[i];
            points.Add(new TradeChartPoint(
                Time: bar.Time.ToString("MM-dd HH:mm"),
                Open: bar.Open,
                High: bar.High,
                Low: bar.Low,
                Close: bar.Close,
                Volume: bar.Volume,
                Ema9: indicator.Ema9,
                Sma20: indicator.Sma20,
                Sma200: indicator.Sma200,
                Vwap: indicator.Vwap));
        }

        return points;
    }

    private static TradeIndicatorContext BuildIndicatorContext(
        IReadOnlyList<BacktestBar> bars,
        IReadOnlyList<BacktestIndicatorSnapshot> indicators,
        int index)
    {
        BacktestBar bar = bars[index];
        BacktestIndicatorSnapshot indicator = indicators[index];
        return new TradeIndicatorContext(
            Time: bar.Time.ToString("yyyy-MM-dd HH:mm"),
            Close: bar.Close,
            Volume: bar.Volume,
            Ema9: indicator.Ema9,
            Sma20: indicator.Sma20,
            Sma200: indicator.Sma200,
            Vwap: indicator.Vwap,
            VolumeAverage20: indicator.VolumeAverage20);
    }

    private static decimal CalculateMfe(TradeReportRow trade, IReadOnlyList<BacktestBar> tradeBars)
    {
        if (tradeBars.Count == 0)
        {
            return 0m;
        }

        if (trade.Side.Equals("SHORT", StringComparison.OrdinalIgnoreCase))
        {
            decimal bestLow = tradeBars.Min(bar => bar.Low);
            return Math.Max(0m, (trade.EntryPrice - bestLow) * trade.Quantity);
        }

        decimal bestHigh = tradeBars.Max(bar => bar.High);
        return Math.Max(0m, (bestHigh - trade.EntryPrice) * trade.Quantity);
    }

    private static decimal CalculateMae(TradeReportRow trade, IReadOnlyList<BacktestBar> tradeBars)
    {
        if (tradeBars.Count == 0)
        {
            return 0m;
        }

        if (trade.Side.Equals("SHORT", StringComparison.OrdinalIgnoreCase))
        {
            decimal worstHigh = tradeBars.Max(bar => bar.High);
            return Math.Min(0m, (trade.EntryPrice - worstHigh) * trade.Quantity);
        }

        decimal worstLow = tradeBars.Min(bar => bar.Low);
        return Math.Min(0m, (worstLow - trade.EntryPrice) * trade.Quantity);
    }

    private static HigherTimeframeChartContext? BuildHigherTimeframeChart(
        TradeReportRow trade,
        IReadOnlyList<BacktestBar> bars,
        int entryIndex,
        int exitIndex)
    {
        int? candleMinutes = ResolveHigherTimeframeMinutes(trade.ProfileName, trade.Strategy);
        if (!candleMinutes.HasValue)
        {
            return null;
        }

        List<HigherTimeframeCandle> candles = BuildHigherTimeframeCandles(bars, candleMinutes.Value);
        List<HigherTimeframeCandle> withIndicators = ApplyHigherTimeframeIndicators(candles);
        if (withIndicators.Count == 0)
        {
            return null;
        }

        DateTimeOffset entryTime = bars[entryIndex].Time;
        DateTimeOffset exitTime = bars[exitIndex].Time;
        int signalIndex = FindHigherTimeframeIndex(withIndicators, entryTime);
        int start = Math.Max(0, signalIndex - 10);
        int end = Math.Min(withIndicators.Count - 1, Math.Max(signalIndex + 6, FindHigherTimeframeIndex(withIndicators, exitTime) + 4));

        IReadOnlyList<HigherTimeframeChartPoint> points = withIndicators
            .Skip(start)
            .Take(end - start + 1)
            .Select(candle => new HigherTimeframeChartPoint(
                Time: candle.StartTime.ToString("MM-dd HH:mm"),
                Open: candle.Open,
                High: candle.High,
                Low: candle.Low,
                Close: candle.Close,
                Volume: candle.Volume,
                Ema9: candle.Ema9,
                Atr14: candle.Atr14,
                AngleDegrees: candle.AngleDegrees))
            .ToArray();

        HigherTimeframeCandle signal = withIndicators[Math.Clamp(signalIndex, 0, withIndicators.Count - 1)];
        string summary = signal.AngleDegrees.HasValue
            ? $"{candleMinutes.Value}m completed candle EMA9 angle {signal.AngleDegrees.Value:F2}°, EMA9={FormatNullable(signal.Ema9)}, ATR={FormatNullable(signal.Atr14)}. Entry waits for this completed higher-timeframe signal and fills on the following 1m bar."
            : $"{candleMinutes.Value}m completed candle view. EMA9 angle was not ready yet for this chart window.";

        return new HigherTimeframeChartContext(
            CandleMinutes: candleMinutes.Value,
            AngleThresholdDegrees: 12m,
            Points: points,
            SignalPointIndex: Math.Max(0, signalIndex - start),
            SignalSummary: summary);
    }

    private static int? ResolveHigherTimeframeMinutes(string profileName, string strategyName)
    {
        string value = profileName + " " + strategyName;
        if (value.Contains("15minutes", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("15Minutes", StringComparison.OrdinalIgnoreCase))
        {
            return 15;
        }

        if (value.Contains("5minutes", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("5Minutes", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return null;
    }

    private static List<HigherTimeframeCandle> BuildHigherTimeframeCandles(IReadOnlyList<BacktestBar> bars, int candleMinutes)
    {
        var result = new List<HigherTimeframeCandle>();

        foreach (BacktestBar bar in bars)
        {
            DateTimeOffset bucketStart = GetBucketStart(bar.Time, candleMinutes);
            HigherTimeframeCandle? current = result.Count > 0 ? result[^1] : null;

            if (current is null || current.StartTime != bucketStart)
            {
                result.Add(new HigherTimeframeCandle(
                    StartTime: bucketStart,
                    EndTime: bar.Time,
                    Open: bar.Open,
                    High: bar.High,
                    Low: bar.Low,
                    Close: bar.Close,
                    Volume: bar.Volume));
            }
            else
            {
                result[^1] = current with
                {
                    EndTime = bar.Time,
                    High = Math.Max(current.High, bar.High),
                    Low = Math.Min(current.Low, bar.Low),
                    Close = bar.Close,
                    Volume = current.Volume + bar.Volume
                };
            }
        }

        return result;
    }

    private static DateTimeOffset GetBucketStart(DateTimeOffset time, int candleMinutes)
    {
        int minuteOfDay = MarketTime.GetEasternMinuteOfDay(time);
        int marketOpenMinute = 570;
        int offsetFromOpen = Math.Max(0, minuteOfDay - marketOpenMinute);
        int bucketOffset = offsetFromOpen / candleMinutes * candleMinutes;
        int bucketMinuteOfDay = marketOpenMinute + bucketOffset;

        return new DateTimeOffset(time.Date.AddMinutes(bucketMinuteOfDay), time.Offset);
    }

    private static List<HigherTimeframeCandle> ApplyHigherTimeframeIndicators(IReadOnlyList<HigherTimeframeCandle> candles)
    {
        List<decimal> closes = candles.Select(candle => candle.Close).ToList();
        List<decimal?> ema9 = CalculateNullableEma(closes, 9);
        List<decimal?> atr14 = CalculateNullableAtr(candles, 14);
        var result = new List<HigherTimeframeCandle>(candles.Count);

        for (int i = 0; i < candles.Count; i++)
        {
            decimal? angle = null;
            if (i > 0 && ema9[i].HasValue && ema9[i - 1].HasValue && atr14[i].HasValue && atr14[i].Value > 0m)
            {
                double normalizedSlope = (double)((ema9[i].Value - ema9[i - 1].Value) / atr14[i].Value);
                angle = (decimal)(Math.Atan(normalizedSlope) * 180.0 / Math.PI);
            }

            result.Add(candles[i] with
            {
                Ema9 = ema9[i],
                Atr14 = atr14[i],
                AngleDegrees = angle
            });
        }

        return result;
    }

    private static List<decimal?> CalculateNullableEma(IReadOnlyList<decimal> values, int period)
    {
        var result = Enumerable.Repeat<decimal?>(null, values.Count).ToList();
        if (values.Count < period)
        {
            return result;
        }

        decimal ema = values.Take(period).Average();
        result[period - 1] = ema;
        decimal multiplier = 2m / (period + 1m);

        for (int i = period; i < values.Count; i++)
        {
            ema = ((values[i] - ema) * multiplier) + ema;
            result[i] = ema;
        }

        return result;
    }

    private static List<decimal?> CalculateNullableAtr(IReadOnlyList<HigherTimeframeCandle> candles, int period)
    {
        var trueRanges = new List<decimal>();
        var result = new List<decimal?>();

        for (int i = 0; i < candles.Count; i++)
        {
            HigherTimeframeCandle candle = candles[i];
            decimal trueRange = i == 0
                ? candle.High - candle.Low
                : Math.Max(
                    candle.High - candle.Low,
                    Math.Max(Math.Abs(candle.High - candles[i - 1].Close), Math.Abs(candle.Low - candles[i - 1].Close)));

            trueRanges.Add(Math.Max(0m, trueRange));
            int atrPeriod = Math.Min(period, trueRanges.Count);
            result.Add(trueRanges.TakeLast(atrPeriod).Average());
        }

        return result;
    }

    private static int FindHigherTimeframeIndex(IReadOnlyList<HigherTimeframeCandle> candles, DateTimeOffset time)
    {
        int latestCompleted = -1;

        for (int i = 0; i < candles.Count; i++)
        {
            if (candles[i].EndTime <= time)
            {
                latestCompleted = i;
            }
            else if (latestCompleted >= 0)
            {
                break;
            }
        }

        if (latestCompleted >= 0)
        {
            return latestCompleted;
        }

        return candles.Count > 0 ? 0 : -1;
    }

    private static IReadOnlyList<string> BuildTimeline(
        TradeReportRow trade,
        int entryIndex,
        int exitIndex,
        decimal mfe,
        decimal mae)
    {
        return
        [
            $"Entry {trade.Side} {trade.Quantity} @ {trade.EntryPrice:F4}: {trade.EntryReason}",
            $"Bars held: {Math.Max(0, exitIndex - entryIndex)}",
            $"MFE {mfe:F2} / MAE {mae:F2}",
            $"Exit @ {trade.ExitPrice:F4}: {trade.ExitReason}"
        ];
    }

    private static IReadOnlyList<string> BuildExplanations(
        TradeReportRow trade,
        HigherTimeframeChartContext? higherTimeframeChart)
    {
        var explanations = new List<string>
        {
            "Primary diagram: rebuilt from the same Sailor CSV bars used by the backtest. Candles show the local entry/exit window with EMA9, SMA20, SMA200, and VWAP overlays.",
            "MFE/MAE: calculated from bar highs/lows between entry and exit using trade side and quantity.",
            "The action timeline is rebuilt from the Sailor entry/exit reasons recorded during the backtest."
        };

        if (higherTimeframeChart is not null)
        {
            explanations.Add($"Higher-timeframe diagram: rebuilt {higherTimeframeChart.CandleMinutes}m completed candles with EMA9 and ATR-normalized angle, matching the V21/V22/V23/V24 conduct approach.");
        }

        if (trade.ExitReason.Contains("trail", StringComparison.OrdinalIgnoreCase) ||
            trade.ExitReason.Contains("giveback", StringComparison.OrdinalIgnoreCase))
        {
            explanations.Add("Exit explanation: the conduct engine detected a trailing/giveback condition after the trade had moved in favor.");
        }
        else if (trade.ExitReason.Contains("force", StringComparison.OrdinalIgnoreCase) ||
                 trade.ExitReason.Contains("session flat", StringComparison.OrdinalIgnoreCase))
        {
            explanations.Add("Exit explanation: the position was flattened by the configured end-of-session rule.");
        }
        else if (trade.ExitReason.Contains("stop", StringComparison.OrdinalIgnoreCase))
        {
            explanations.Add("Exit explanation: the trade hit the configured stop or hard-stop logic.");
        }

        return explanations;
    }

    private static string FormatNullable(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }

    private sealed record CachedSymbolData(
        IReadOnlyList<BacktestBar> Bars,
        IReadOnlyList<BacktestIndicatorSnapshot> Indicators);

    private sealed record HigherTimeframeCandle(
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume,
        decimal? Ema9 = null,
        decimal? Atr14 = null,
        decimal? AngleDegrees = null);
}
