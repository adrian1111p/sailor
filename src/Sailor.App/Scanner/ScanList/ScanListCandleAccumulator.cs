namespace Sailor.App.Scanner.ScanList;

public sealed class ScanListCandleAccumulator
{
    private readonly Dictionary<string, SortedDictionary<DateTimeOffset, ScanListMemoryCandle>> _candles = new(StringComparer.OrdinalIgnoreCase);

    public ScanListMemoryCandle AddTrade(
        string symbol,
        DateTimeOffset observedUtc,
        decimal price,
        long size)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        DateTimeOffset minute = FloorToMinute(observedUtc);
        long safeSize = Math.Max(0, size);

        if (!_candles.TryGetValue(normalizedSymbol, out SortedDictionary<DateTimeOffset, ScanListMemoryCandle>? byMinute))
        {
            byMinute = new SortedDictionary<DateTimeOffset, ScanListMemoryCandle>();
            _candles[normalizedSymbol] = byMinute;
        }

        if (!byMinute.TryGetValue(minute, out ScanListMemoryCandle? existing))
        {
            var created = new ScanListMemoryCandle(
                normalizedSymbol,
                minute,
                price,
                price,
                price,
                price,
                safeSize,
                ScanListCandleQuality.PartialRealtime,
                DateTimeOffset.UtcNow);
            byMinute[minute] = created;
            return created;
        }

        var updated = existing with
        {
            High = Math.Max(existing.High, price),
            Low = Math.Min(existing.Low, price),
            Close = price,
            Volume = existing.Volume + safeSize,
            Quality = ScanListCandleQuality.RealtimeMemory,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        byMinute[minute] = updated;
        return updated;
    }

    public void UpsertRealtimeCandle(ScanListMemoryCandle candle)
    {
        string symbol = NormalizeSymbol(candle.Symbol);
        DateTimeOffset minute = FloorToMinute(candle.MinuteUtc);
        if (!_candles.TryGetValue(symbol, out SortedDictionary<DateTimeOffset, ScanListMemoryCandle>? byMinute))
        {
            byMinute = new SortedDictionary<DateTimeOffset, ScanListMemoryCandle>();
            _candles[symbol] = byMinute;
        }

        byMinute[minute] = candle with
        {
            Symbol = symbol,
            MinuteUtc = minute,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    public IReadOnlyList<ScanListMemoryCandle> GetCandles(string symbol)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        if (!_candles.TryGetValue(normalizedSymbol, out SortedDictionary<DateTimeOffset, ScanListMemoryCandle>? byMinute))
        {
            return Array.Empty<ScanListMemoryCandle>();
        }

        return byMinute.Values.ToArray();
    }

    public int SymbolCount => _candles.Count;

    public int CandleCount => _candles.Values.Sum(map => map.Count);

    public static DateTimeOffset FloorToMinute(DateTimeOffset timestamp)
        => new(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, 0, timestamp.Offset);

    private static string NormalizeSymbol(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().ToUpperInvariant();
}
