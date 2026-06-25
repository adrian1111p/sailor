using Harvester.Contracts;
using Harvester.App.Strategy;

namespace Sailor.App.Backtest.DataFetcher;

/// <summary>
/// Converts stored historical tick data (BidAsk + Trade CSVs) into the same
/// <see cref="TopTickRow"/> and <see cref="DepthRow"/> structures used by the
/// live <see cref="V3LiveFeatureBuilder"/>. This enables the backtest to use
/// the identical L1/L2 feature pipeline as live trading.
///
/// For each 1-minute bar window, the synthesizer:
///   1. Gathers all bid-ask and trade ticks within that bar's time range
///   2. Converts them to TopTickRow (L1 quotes) and DepthRow (L2 depth proxy)
///   3. Builds a StrategyDataSlice that V3LiveFeatureBuilder.Build() can consume
///
/// L2 Synthesis: Since IBKR reqHistoricalTicks only provides BID_ASK (top-of-book),
/// true L2 multi-level depth is not available historically. The synthesizer creates
/// a realistic L2 proxy from the bid-ask spread structure:
///   - Level 0: actual bid/ask price and size from the tick
///   - Levels 1-4: synthetic resting orders derived from bid-ask spread patterns,
///     with sizes decaying by a configurable factor per level
///
/// This gives the backtest access to:
///   - V3LiveL1Snapshot: Bid, Ask, Last, BidSize, AskSize, SpreadPct
///   - V3LiveL2Snapshot: ImbalanceRatio, OfiSignal, DepthWeightedMid, L0/Deep ratios
/// </summary>
public static class L1L2Synthesizer
{
    // â”€â”€ Phase 6.17: Synthetic-L2 calibration constants (documented provenance) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // These named constants replace inline magic numbers in the L2 depth proxy. They are the single
    // source of truth for the synthesizer's calibration and are exercised by the structural calibration
    // test (Phase6SyntheticL2CalibrationTests). They are calibration defaults only â€” every public method
    // still accepts overrides â€” so changing them never blocks a trade and never alters entry eligibility.
    //
    // Provenance:
    //   * DefaultDepthLevels = 5 â€” mirrors the live TWS depth subscription depth used by
    //     V3LiveFeatureBuilder (top-of-book + 4 synthetic resting levels = 5 total).
    //   * DefaultSizeDecayFactor = 0.7 â€” geometric per-level size decay. Chosen so resting size falls to
    //     ~24% by level 4 (0.7^4 â‰ˆ 0.240), approximating the observed thinning of displayed depth away
    //     from the inside quote on the small/mid-cap names this system trades. Tunable; see the
    //     calibration test for the documented expected per-level multipliers.
    //   * SyntheticTickSpreadFraction = 0.5 â€” synthetic price step between levels is half the inside
    //     spread, so each successive level steps out by half a spread from the inside quote.
    //   * MinSyntheticTickSizeDollars = 0.01 â€” floor for the synthetic tick step (one cent) so levels
    //     never collapse onto the same price when the spread is near zero.
    /// <summary>Default number of synthesized order-book levels (top-of-book + resting levels).</summary>
    public const int DefaultDepthLevels = 5;

    /// <summary>Default geometric per-level size decay base applied to the inside-quote size.</summary>
    public const double DefaultSizeDecayFactor = 0.7;

    /// <summary>Fraction of the inside spread used as the synthetic price step between levels.</summary>
    public const double SyntheticTickSpreadFraction = 0.5;

    /// <summary>Floor (USD) for the synthetic per-level price step so levels never overlap.</summary>
    public const double MinSyntheticTickSizeDollars = 0.01;

    /// <summary>
    /// Geometric size multiplier applied at a given synthetic depth <paramref name="level"/>
    /// (level 0 = 1.0, level 1 = <see cref="DefaultSizeDecayFactor"/>, etc.). Exposed so calibration
    /// tests and callers can reason about the decay curve without duplicating the formula.
    /// </summary>
    public static double SizeMultiplierForLevel(int level, double sizeDecayFactor = DefaultSizeDecayFactor)
        => Math.Pow(sizeDecayFactor, level);

    /// <summary>
    /// Synthetic per-level price step (USD) for a given inside <paramref name="spread"/>: half the spread,
    /// floored at <see cref="MinSyntheticTickSizeDollars"/>.
    /// </summary>
    public static double SyntheticTickSize(double spread)
        => Math.Max(spread * SyntheticTickSpreadFraction, MinSyntheticTickSizeDollars);

    /// <summary>
    /// Synthesize TopTickRow[] from stored bid-ask ticks for a specific time window.
    /// Each BidAskTick produces up to 4 TopTickRow entries (bid price, ask price, bid size, ask size).
    /// Trade ticks produce 2 entries (last price, last size proxy = 0).
    /// </summary>
    public static TopTickRow[] SynthesizeTopTicks(
        BidAskTick[] bidAskTicks,
        TradeTick[] tradeTicks,
        DateTime windowStart,
        DateTime windowEnd,
        int tickerId = 0)
    {
        var rows = new List<TopTickRow>();

        foreach (var t in bidAskTicks)
        {
            if (t.TimestampUtc < windowStart || t.TimestampUtc >= windowEnd) continue;
            if (t.Bid <= 0 || t.Ask <= 0) continue;

            // Field 1 = bid price
            rows.Add(new TopTickRow(t.TimestampUtc, tickerId, "tickPrice", 1, t.Bid, 0, ""));
            // Field 2 = ask price
            rows.Add(new TopTickRow(t.TimestampUtc, tickerId, "tickPrice", 2, t.Ask, 0, ""));
            // Field 0 = bid size
            if (t.BidSize > 0)
                rows.Add(new TopTickRow(t.TimestampUtc, tickerId, "tickSize", 0, 0, (int)t.BidSize, ""));
            // Field 3 = ask size
            if (t.AskSize > 0)
                rows.Add(new TopTickRow(t.TimestampUtc, tickerId, "tickSize", 3, 0, (int)t.AskSize, ""));
        }

        foreach (var t in tradeTicks)
        {
            if (t.TimestampUtc < windowStart || t.TimestampUtc >= windowEnd) continue;
            if (t.Price <= 0) continue;

            // Field 4 = last trade price
            rows.Add(new TopTickRow(t.TimestampUtc, tickerId, "tickPrice", 4, t.Price, 0, ""));
        }

        return rows.ToArray();
    }

    /// <summary>
    /// Synthesize DepthRow[] (L2 proxy) from bid-ask ticks for a specific time window.
    /// Creates a synthetic 5-level order book from top-of-book data.
    /// </summary>
    public static DepthRow[] SynthesizeDepthRows(
        BidAskTick[] bidAskTicks,
        DateTime windowStart,
        DateTime windowEnd,
        int levels = DefaultDepthLevels,
        double sizeDecayFactor = DefaultSizeDecayFactor,
        int tickerId = 0)
    {
        var rows = new List<DepthRow>();

        foreach (var t in bidAskTicks)
        {
            if (t.TimestampUtc < windowStart || t.TimestampUtc >= windowEnd) continue;
            if (t.Bid <= 0 || t.Ask <= 0) continue;

            var spread = t.Ask - t.Bid;
            var tickSize = SyntheticTickSize(spread);

            for (int level = 0; level < levels; level++)
            {
                var sizeFactor = SizeMultiplierForLevel(level, sizeDecayFactor);
                var bidPrice = t.Bid - (level * tickSize);
                var askPrice = t.Ask + (level * tickSize);
                var bidSize = (int)(t.BidSize * sizeFactor);
                var askSize = (int)(t.AskSize * sizeFactor);

                // Side 1 = bid, Side 0 = ask, Operation 1 = update
                rows.Add(new DepthRow(t.TimestampUtc, tickerId, level, 1, 1, bidPrice, bidSize, "", false));
                rows.Add(new DepthRow(t.TimestampUtc, tickerId, level, 1, 0, askPrice, askSize, "", false));
            }
        }

        return rows.ToArray();
    }

    /// <summary>
    /// Build a complete StrategyDataSlice for a single bar window, suitable for
    /// V3LiveFeatureBuilder.Build(). Combines historical bars with synthesized L1/L2 ticks.
    /// </summary>
    public static StrategyDataSlice BuildSliceForBar(
        BidAskTick[] allBidAskTicks,
        TradeTick[] allTradeTicks,
        IReadOnlyList<HistoricalBarRow> historicalBars,
        DateTime barStart,
        DateTime barEnd,
        string symbol,
        int depthLevels = DefaultDepthLevels)
    {
        var topTicks = SynthesizeTopTicks(allBidAskTicks, allTradeTicks, barStart, barEnd);
        var depthRows = SynthesizeDepthRows(allBidAskTicks, barStart, barEnd, depthLevels);

        return new StrategyDataSlice(
            TimestampUtc: barEnd,
            Mode: "backtest-l1l2",
            TopTicks: topTicks,
            DepthRows: depthRows,
            HistoricalBars: historicalBars,
            Positions: [],
            AccountSummary: [],
            CanonicalOrderEvents: [],
            Symbol: symbol);
    }

    /// <summary>
    /// Build a full sequence of StrategyDataSlices â€” one per bar â€” for use in
    /// the L1/L2-enhanced backtest runner. Each slice contains all historical bars
    /// up to and including the current bar, plus the L1/L2 ticks within that bar window.
    /// </summary>
    public static StrategyDataSlice[] BuildSlicesForAllBars(
        string symbol,
        Engine.BacktestBar[] bars,
        BidAskTick[] bidAskTicks,
        TradeTick[] tradeTicks,
        int depthLevels = 5)
    {
        // Convert BacktestBars to HistoricalBarRows for the slice
        var histRows = bars.Select((b, idx) =>
            BacktestDataFetcher.ToHistoricalBarRow(b, requestId: 0)).ToArray();

        var slices = new StrategyDataSlice[bars.Length];

        for (int i = 0; i < bars.Length; i++)
        {
            var barStart = bars[i].Timestamp;
            var barEnd = i + 1 < bars.Length
                ? bars[i + 1].Timestamp
                : barStart.AddMinutes(1);

            // Include all bars up to and including current for indicator computation
            var barsUpToNow = new ArraySegment<HistoricalBarRow>(histRows, 0, i + 1);

            slices[i] = BuildSliceForBar(
                bidAskTicks,
                tradeTicks,
                barsUpToNow.ToArray(),
                barStart,
                barEnd,
                symbol,
                depthLevels);
        }

        return slices;
    }

    /// <summary>
    /// Compute per-bar L1/L2 coverage statistics. Returns the percentage of bars
    /// that have at least one bid-ask tick, useful for data quality assessment.
    /// </summary>
    public static L1L2CoverageStats ComputeCoverage(
        Engine.BacktestBar[] bars,
        BidAskTick[] bidAskTicks,
        TradeTick[] tradeTicks)
    {
        if (bars.Length == 0)
            return new L1L2CoverageStats(0, 0, 0, 0, 0);

        int barsWithBidAsk = 0;
        int barsWithTrades = 0;
        int totalBidAskTicks = bidAskTicks.Length;
        int totalTradeTicks = tradeTicks.Length;

        for (int i = 0; i < bars.Length; i++)
        {
            var start = bars[i].Timestamp;
            var end = i + 1 < bars.Length ? bars[i + 1].Timestamp : start.AddMinutes(1);

            if (bidAskTicks.Any(t => t.TimestampUtc >= start && t.TimestampUtc < end))
                barsWithBidAsk++;
            if (tradeTicks.Any(t => t.TimestampUtc >= start && t.TimestampUtc < end))
                barsWithTrades++;
        }

        return new L1L2CoverageStats(
            TotalBars: bars.Length,
            BarsWithBidAsk: barsWithBidAsk,
            BarsWithTrades: barsWithTrades,
            TotalBidAskTicks: totalBidAskTicks,
            TotalTradeTicks: totalTradeTicks);
    }
}

/// <summary>Coverage statistics for L1/L2 tick data vs bar data.</summary>
public sealed record L1L2CoverageStats(
    int TotalBars,
    int BarsWithBidAsk,
    int BarsWithTrades,
    int TotalBidAskTicks,
    int TotalTradeTicks)
{
    public double BidAskCoveragePct => TotalBars > 0 ? (double)BarsWithBidAsk / TotalBars * 100.0 : 0;
    public double TradeCoveragePct => TotalBars > 0 ? (double)BarsWithTrades / TotalBars * 100.0 : 0;
}

