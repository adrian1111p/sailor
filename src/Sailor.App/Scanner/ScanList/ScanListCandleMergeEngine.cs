using Sailor.App.Backtest.Models;

namespace Sailor.App.Scanner.ScanList;

public sealed class ScanListCandleMergeEngine
{
    public ScanListCandleMergeResult Merge(
        string symbol,
        IEnumerable<BacktestBar> historicalBars,
        IEnumerable<ScanListMemoryCandle> realtimeCandles)
    {
        string normalizedSymbol = string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().ToUpperInvariant();
        BacktestBar[] historical = historicalBars.OrderBy(bar => bar.Time).ToArray();
        ScanListMemoryCandle[] realtime = realtimeCandles.OrderBy(candle => candle.MinuteUtc).ToArray();
        var warnings = new List<string>();
        var merged = new SortedDictionary<DateTimeOffset, ScanListMemoryCandle>();

        foreach (BacktestBar bar in historical)
        {
            DateTimeOffset minute = ScanListCandleAccumulator.FloorToMinute(bar.Time);
            merged[minute] = new ScanListMemoryCandle(
                normalizedSymbol,
                minute,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.Volume,
                ScanListCandleQuality.Historical,
                DateTimeOffset.UtcNow);
        }

        int realtimeCount = 0;
        int realtimeAppended = 0;
        int realtimeOverlapped = 0;
        foreach (ScanListMemoryCandle realtimeCandle in realtime)
        {
            realtimeCount++;
            DateTimeOffset minute = ScanListCandleAccumulator.FloorToMinute(realtimeCandle.MinuteUtc);
            if (merged.TryGetValue(minute, out ScanListMemoryCandle? existing))
            {
                realtimeOverlapped++;
                merged[minute] = existing with
                {
                    High = Math.Max(existing.High, realtimeCandle.High),
                    Low = Math.Min(existing.Low, realtimeCandle.Low),
                    Close = realtimeCandle.Close,
                    Volume = existing.Volume + Math.Max(0, realtimeCandle.Volume),
                    Quality = ScanListCandleQuality.MergedHistoricalAndRealtime,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }
            else
            {
                realtimeAppended++;
                merged[minute] = realtimeCandle with
                {
                    Symbol = normalizedSymbol,
                    MinuteUtc = minute,
                    Quality = realtimeCandle.Quality == ScanListCandleQuality.Historical
                        ? ScanListCandleQuality.RealtimeMemory
                        : realtimeCandle.Quality,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }
        }

        if (merged.Count == 0)
        {
            warnings.Add($"No historical or realtime memory candles were available for {normalizedSymbol}.");
        }

        return new ScanListCandleMergeResult(
            normalizedSymbol,
            historical.Length,
            realtimeCount,
            merged.Count,
            realtimeAppended,
            realtimeOverlapped,
            merged.Values.ToArray(),
            warnings);
    }
}
