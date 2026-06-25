using Sailor.App.Backtest.DataFetcher;
using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest;

/// <summary>
/// Enriches backtest EnrichedBar[] with L1/L2 data from stored tick CSVs and
/// computes candle patterns + lookback fields matching the live V3LiveFeatureBuilder.
///
/// Call after TechnicalIndicators.EnrichWithIndicators() to populate:
///   - L1 fields: BidPrice, AskPrice, LastPrice, BidSize, AskSize, SpreadPct
///   - L2 fields: BidDepthN, AskDepthN, ImbalanceRatio, DepthWeightedMid, OfiSignal, etc.
///   - Candle patterns: IsBullishCandle, IsBearishCandle, IsHammer, IsStar
///   - Lookback: HighestClose10, LowestClose10
///   - Previous bar OHLCV
/// </summary>
public static class L1L2Enrichment
{
    /// <summary>
    /// Enrich bars with L1/L2 tick data loaded from CSV storage, plus candle patterns and lookback.
    /// If no tick data is available for the symbol, only candle/lookback fields are populated.
    /// </summary>
    public static void Enrich(string symbol, EnrichedBar[] bars, int depthLevels = 5)
    {
        // Load tick data (may be empty if no CSVs exist)
        var bidAskTicks = CsvTickStorage.BidAskExists(symbol)
            ? CsvTickStorage.LoadBidAskTicks(symbol) : [];
        var tradeTicks = CsvTickStorage.TradesExist(symbol)
            ? CsvTickStorage.LoadTradeTicks(symbol) : [];

        bool hasTicks = bidAskTicks.Length > 0 || tradeTicks.Length > 0;

        // Pre-index ticks by bar window for O(n) total scan instead of O(n*m)
        int baIdx = 0, trIdx = 0;

        for (int i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];

            // â”€â”€ Candle patterns â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            ComputeCandlePatterns(bar);

            // â”€â”€ Previous bar OHLCV â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (i > 0)
            {
                var prev = bars[i - 1].Bar;
                bar.PrevOpen = prev.Open;
                bar.PrevHigh = prev.High;
                bar.PrevLow = prev.Low;
                bar.PrevClose = prev.Close;
                bar.PrevVolume = prev.Volume;
            }

            // â”€â”€ 10-bar lookback extremes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            ComputeLookback(bars, i, 10);

            // â”€â”€ L1/L2 from tick data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (!hasTicks) continue;

            var barStart = bar.Bar.Timestamp;
            var barEnd = i + 1 < bars.Length
                ? bars[i + 1].Bar.Timestamp
                : barStart.AddMinutes(1);

            // Advance bid-ask cursor to this bar window
            double bid = 0, ask = 0, bidSize = 0, askSize = 0;
            bool hasQuote = false;
            while (baIdx < bidAskTicks.Length && bidAskTicks[baIdx].TimestampUtc < barStart)
                baIdx++;

            int baScan = baIdx;
            while (baScan < bidAskTicks.Length && bidAskTicks[baScan].TimestampUtc < barEnd)
            {
                var t = bidAskTicks[baScan];
                if (t.Bid > 0) { bid = t.Bid; bidSize = t.BidSize; }
                if (t.Ask > 0) { ask = t.Ask; askSize = t.AskSize; }
                hasQuote = true;
                baScan++;
            }

            // Advance trade cursor to this bar window
            double lastTrade = 0;
            while (trIdx < tradeTicks.Length && tradeTicks[trIdx].TimestampUtc < barStart)
                trIdx++;

            int trScan = trIdx;
            while (trScan < tradeTicks.Length && tradeTicks[trScan].TimestampUtc < barEnd)
            {
                lastTrade = tradeTicks[trScan].Price;
                trScan++;
            }

            if (!hasQuote) continue;

            // â”€â”€ L1 fields â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            bar.BidPrice = bid;
            bar.AskPrice = ask;
            bar.LastPrice = lastTrade > 0 ? lastTrade : bar.Bar.Close;
            bar.BidSize = bidSize;
            bar.AskSize = askSize;

            var mid = (bid + ask) / 2.0;
            bar.SpreadPct = mid > 0 ? (ask - bid) / mid * 100.0 : 0;

            // â”€â”€ L2 synthesis (same logic as L1L2Synthesizer) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Build synthetic 5-level depth from top-of-book
            var spread = ask - bid;
            var tickSize = Math.Max(spread * 0.5, 0.01);
            const double sizeDecay = 0.7;

            double bidDepth = bidSize, askDepth = askSize;
            double l0BidSize = bidSize, l0AskSize = askSize;
            double deepBidSum = 0, deepAskSum = 0;

            // Synthesize levels 1..depthLevels-1 with decaying size
            for (int lvl = 1; lvl < depthLevels; lvl++)
            {
                var lvlBidSize = bidSize * Math.Pow(sizeDecay, lvl);
                var lvlAskSize = askSize * Math.Pow(sizeDecay, lvl);
                bidDepth += lvlBidSize;
                askDepth += lvlAskSize;
                deepBidSum += lvlBidSize;
                deepAskSum += lvlAskSize;
            }

            bar.BidDepthN = bidDepth;
            bar.AskDepthN = askDepth;
            bar.ImbalanceRatio = askDepth > 0 ? bidDepth / askDepth : 10.0;
            bar.L2Liquidity = Math.Min(bidDepth, askDepth);

            // OFI signal from top-of-book
            var totalTopSize = l0BidSize + l0AskSize;
            bar.OfiSignal = totalTopSize > 0 ? (l0BidSize - l0AskSize) / totalTopSize : 0;

            // Depth-weighted mid price
            var denom = l0BidSize + l0AskSize;
            bar.DepthWeightedMid = denom > 0
                ? (bid * l0AskSize + ask * l0BidSize) / denom
                : mid;

            // L0 imbalance (top-of-book only)
            bar.L0ImbalanceRatio = l0AskSize > 0 ? l0BidSize / l0AskSize : 10.0;

            // Deep imbalance (L1-L4 stacked resting interest)
            bar.DeepImbalanceRatio = deepAskSum > 0 ? deepBidSum / deepAskSum : 1.0;
        }
    }

    private static void ComputeCandlePatterns(EnrichedBar bar)
    {
        var open = bar.Bar.Open;
        var close = bar.Bar.Close;
        var high = bar.Bar.High;
        var low = bar.Bar.Low;
        var range = high - low;
        var body = close - open;

        bar.IsBullishCandle = body > 0;
        bar.IsBearishCandle = body < 0;

        if (range > 0)
        {
            var absBody = Math.Abs(body);
            var lowerWick = Math.Min(open, close) - low;
            var upperWick = high - Math.Max(open, close);

            bar.IsHammer = lowerWick > absBody * 1.5 && upperWick < absBody * 0.5;
            bar.IsStar = upperWick > absBody * 1.5 && lowerWick < absBody * 0.5;
        }
    }

    private static void ComputeLookback(EnrichedBar[] bars, int index, int window)
    {
        if (index < 1) return;

        var lookStart = Math.Max(0, index - window + 1);
        double highest = bars[lookStart].Bar.Close;
        double lowest = bars[lookStart].Bar.Close;

        for (int j = lookStart + 1; j <= index; j++)
        {
            var c = bars[j].Bar.Close;
            if (c > highest) highest = c;
            if (c < lowest) lowest = c;
        }

        bars[index].HighestClose10 = highest;
        bars[index].LowestClose10 = lowest;
    }
}

