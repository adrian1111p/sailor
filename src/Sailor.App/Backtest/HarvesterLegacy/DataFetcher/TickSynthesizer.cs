using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.DataFetcher;

/// <summary>
/// Generates realistic synthetic L1 bid-ask and trade ticks from 1-minute bar data.
/// This enables L1/L2-enhanced backtesting when historical tick data is not available
/// from the IBKR API.
///
/// For each 1m bar, the synthesizer creates 60 ticks (~1-second intervals):
///   1. 5-phase price path with micro-pullbacks matching real microstructure
///   2. Dynamic spread that widens during volatile phases, tightens during calm
///   3. Directional size imbalance â€” bids outsize asks on bullish moves, vice versa
///   4. Volume clustering with bursts at reversal pivot points
///   5. Generates both bid-ask quotes and trade ticks at each sample point
///
/// The output is persisted via CsvTickStorage in backtest/data/{SYMBOL}/.
/// </summary>
public static class TickSynthesizer
{
    private const int TicksPerBar = 60; // ~1-second spacing within a 1m bar

    /// <summary>
    /// Generate synthetic L1 tick data for a single symbol from its 1-minute bars.
    /// Returns (bidAskCount, tradeCount).
    /// </summary>
    public static (int BidAskCount, int TradeCount) GenerateFromBars(string symbol, BacktestBar[] bars)
    {
        if (bars.Length == 0) return (0, 0);

        var rng = new Random(symbol.GetHashCode()); // deterministic per symbol
        var bidAskTicks = new List<BidAskTick>();
        var tradeTicks = new List<TradeTick>();

        foreach (var bar in bars)
        {
            if (bar.Close <= 0 || bar.Volume <= 0) continue;

            var baseSpread = EstimateSpread(bar.Close);
            var barRange = bar.High - bar.Low;
            var isBullish = bar.Close >= bar.Open;

            // Build 5-phase price path with micro-pullbacks
            var pricePath = BuildPricePath(bar, isBullish, rng);

            // Volume distribution with bursts at pivots
            var tickVolumes = DistributeVolume(bar.Volume, pricePath.Length, rng);

            for (int t = 0; t < pricePath.Length; t++)
            {
                var fraction = (double)t / Math.Max(pricePath.Length - 1, 1);
                var tickTime = bar.Timestamp.AddSeconds(fraction * 59.0 + rng.NextDouble() * 0.9);
                var mid = pricePath[t];

                // Dynamic spread: wider at extremes and during fast moves
                var velocity = t > 0 ? Math.Abs(pricePath[t] - pricePath[t - 1]) : 0;
                var spreadMult = 1.0 + Math.Min(velocity / (baseSpread + 1e-9), 2.0) * 0.5;
                var halfSpread = baseSpread * spreadMult / 2.0;
                halfSpread *= 0.8 + rng.NextDouble() * 0.4; // Â±20% jitter

                var bid = Math.Round(mid - halfSpread, 4);
                var ask = Math.Round(mid + halfSpread, 4);
                if (bid <= 0) bid = 0.01;
                if (ask <= bid) ask = bid + 0.01;

                // Directional size imbalance
                var vol = tickVolumes[t];
                var priceMove = t > 0 ? pricePath[t] - pricePath[t - 1] : 0;
                var dirBias = priceMove > 0 ? 1.3 : (priceMove < 0 ? 0.7 : 1.0);
                var bidSize = Math.Max(100, (int)(vol * (0.3 + rng.NextDouble() * 0.4) * dirBias));
                var askSize = Math.Max(100, (int)(vol * (0.3 + rng.NextDouble() * 0.4) / dirBias));

                bidAskTicks.Add(new BidAskTick(tickTime, bid, ask, bidSize, askSize));

                // Trade tick: biased toward bid on down moves, ask on up moves
                var tradeBias = priceMove >= 0 ? 0.1 : -0.1;
                var tradePrice = Math.Round(mid + tradeBias * baseSpread + (rng.NextDouble() - 0.5) * baseSpread * 0.2, 4);
                if (tradePrice <= 0) tradePrice = mid;
                tradeTicks.Add(new TradeTick(tickTime, tradePrice, vol, "SYNTH"));
            }
        }

        var bidAskArray = bidAskTicks.OrderBy(t => t.TimestampUtc).ToArray();
        var tradeArray = tradeTicks.OrderBy(t => t.TimestampUtc).ToArray();

        CsvTickStorage.SaveBidAskTicks(symbol, bidAskArray);
        CsvTickStorage.SaveTradeTicks(symbol, tradeArray);

        return (bidAskArray.Length, tradeArray.Length);
    }

    /// <summary>
    /// Generate synthetic L1 tick data for all symbols that have 1m bar data.
    /// </summary>
    public static void GenerateForAllSymbols()
    {
        var symbols = CsvBarStorage.ListSymbols();
        Console.WriteLine($"[TickSynth] Generating L1 tick data for {symbols.Length} symbols (60 ticks/bar)...");

        int total = 0;
        foreach (var symbol in symbols)
        {
            var bars = BacktestDataFetcher.TryLoadBars(symbol, "1m");
            if (bars is null || bars.Length == 0)
            {
                Console.WriteLine($"  {symbol}: no 1m bars, skipped");
                continue;
            }

            var (ba, tr) = GenerateFromBars(symbol, bars);
            Console.WriteLine($"  {symbol}: {bars.Length} bars â†’ {ba} bid-ask + {tr} trade ticks");
            total++;
        }

        Console.WriteLine($"[TickSynth] Done. Generated tick data for {total} symbols.");
    }

    /// <summary>Estimate realistic bid-ask spread based on price tier.</summary>
    private static double EstimateSpread(double price) => price switch
    {
        < 1.0   => 0.005,
        < 5.0   => 0.01,
        < 20.0  => 0.02,
        < 50.0  => 0.03,
        < 100.0 => 0.05,
        < 300.0 => 0.10,
        _       => 0.20,
    };

    /// <summary>
    /// Build a 5-phase price path through the bar with micro-pullbacks.
    /// Bullish: O â†’ dip toward L â†’ micro-bounce â†’ rally to H â†’ settle at C
    /// Bearish: O â†’ rally toward H â†’ micro-dip â†’ drop to L â†’ settle at C
    /// </summary>
    private static double[] BuildPricePath(BacktestBar bar, bool isBullish, Random rng)
    {
        var points = new double[TicksPerBar];

        double extreme1, extreme2;
        if (isBullish)
        {
            extreme1 = bar.Low;
            extreme2 = bar.High;
        }
        else
        {
            extreme1 = bar.High;
            extreme2 = bar.Low;
        }

        // Micro-pullback level: 20-40% retracement from extreme1 back toward open
        var pullback1 = extreme1 + (bar.Open - extreme1) * (0.2 + rng.NextDouble() * 0.2);
        // Micro-pullback before close: 15-30% retracement from extreme2 toward mid
        var midPrice = (bar.Open + bar.Close) / 2.0;
        var pullback2 = extreme2 + (midPrice - extreme2) * (0.15 + rng.NextDouble() * 0.15);

        // Phase boundaries (normalized 0-1 across the 60 ticks):
        // Phase 1: 0.00-0.20 â€” Open â†’ extreme1 (initial move)
        // Phase 2: 0.20-0.30 â€” extreme1 â†’ pullback1 (micro-bounce)
        // Phase 3: 0.30-0.65 â€” pullback1 â†’ extreme2 (main trend move)
        // Phase 4: 0.65-0.78 â€” extreme2 â†’ pullback2 (micro-revert)
        // Phase 5: 0.78-1.00 â€” pullback2 â†’ Close (settlement)

        for (int i = 0; i < TicksPerBar; i++)
        {
            double t = (double)i / (TicksPerBar - 1);
            double price;

            if (t <= 0.20)
            {
                price = Lerp(bar.Open, extreme1, SmoothStep(t / 0.20));
            }
            else if (t <= 0.30)
            {
                price = Lerp(extreme1, pullback1, SmoothStep((t - 0.20) / 0.10));
            }
            else if (t <= 0.65)
            {
                price = Lerp(pullback1, extreme2, SmoothStep((t - 0.30) / 0.35));
            }
            else if (t <= 0.78)
            {
                price = Lerp(extreme2, pullback2, SmoothStep((t - 0.65) / 0.13));
            }
            else
            {
                price = Lerp(pullback2, bar.Close, SmoothStep((t - 0.78) / 0.22));
            }

            // Micro-noise: Â±0.05% base + extra noise proportional to bar range
            var noiseScale = 0.0005 + 0.0002 * (bar.High - bar.Low) / Math.Max(bar.Close, 0.01);
            price *= 1.0 + (rng.NextDouble() - 0.5) * noiseScale;
            points[i] = Math.Round(price, 4);
        }

        // Force exact OHLC at boundaries
        points[0] = bar.Open;
        points[^1] = bar.Close;

        return points;
    }

    /// <summary>Smooth step for more natural acceleration/deceleration at phase transitions.</summary>
    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double[] DistributeVolume(double totalVolume, int tickCount, Random rng)
    {
        var weights = new double[tickCount];
        double sum = 0;
        for (int i = 0; i < tickCount; i++)
        {
            var t = (double)i / Math.Max(tickCount - 1, 1);

            // U-shape base (higher volume at bar open/close)
            var uShape = 1.0 + 0.6 * Math.Pow(2 * t - 1, 2);

            // Spikes at phase transition points (0.20, 0.30, 0.65, 0.78)
            var pivotBoost = 1.0;
            if (Math.Abs(t - 0.20) < 0.03) pivotBoost = 2.5;
            else if (Math.Abs(t - 0.30) < 0.03) pivotBoost = 1.8;
            else if (Math.Abs(t - 0.65) < 0.03) pivotBoost = 2.5;
            else if (Math.Abs(t - 0.78) < 0.03) pivotBoost = 1.5;

            weights[i] = uShape * pivotBoost * (0.4 + rng.NextDouble() * 0.6);
            sum += weights[i];
        }

        var volumes = new double[tickCount];
        for (int i = 0; i < tickCount; i++)
            volumes[i] = Math.Max(1, Math.Round(totalVolume * weights[i] / sum));

        return volumes;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

