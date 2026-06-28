using Sailor.App.Backtest.Models;
using Sailor.App.Configuration;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Backtest.Snapshots;

public sealed class SyntheticBacktestMarketSnapshotProvider
{
    private readonly L1L2SnapshotSettings _settings;

    public SyntheticBacktestMarketSnapshotProvider(L1L2SnapshotSettings settings)
    {
        _settings = settings;
    }

    public SailorMarketSnapshot? CreateSnapshot(
        IReadOnlyList<BacktestBar> bars,
        IReadOnlyList<BacktestIndicatorSnapshot> indicators,
        int index)
    {
        if (!_settings.EnableBacktestSyntheticSnapshots || index < 0 || index >= bars.Count)
        {
            return null;
        }

        BacktestBar bar = bars[index];
        BacktestIndicatorSnapshot indicator = indicators[index];

        decimal close = Math.Max(0.0001m, bar.Close);
        decimal minimumSpread = Math.Max(0m, _settings.SyntheticMinimumSpreadCents) / 100m;
        decimal spreadFromBps = close * Math.Max(0m, _settings.SyntheticSpreadBps) / 10_000m;
        decimal spread = Math.Max(minimumSpread, spreadFromBps);
        decimal halfSpread = spread / 2m;

        decimal bid = Math.Max(0.0001m, close - halfSpread);
        decimal ask = close + halfSpread;

        decimal momentum = index > 0 && bars[index - 1].Close > 0m
            ? (bar.Close - bars[index - 1].Close) / bars[index - 1].Close
            : 0m;

        decimal candleRange = Math.Max(0.0001m, bar.High - bar.Low);
        decimal closeLocation = (bar.Close - bar.Low) / candleRange; // 0 low, 1 high
        decimal directionalPressure = Clamp((closeLocation - 0.5m) * 2m + momentum * 25m, -0.85m, 0.85m);

        long baseSize = Math.Max(100, bar.Volume / 200);
        long bidSize = Math.Max(1, (long)Math.Round(baseSize * (1m + directionalPressure)));
        long askSize = Math.Max(1, (long)Math.Round(baseSize * (1m - directionalPressure)));

        var l1 = new L1QuoteSnapshot(
            Time: bar.Time,
            Symbol: bar.Symbol,
            Bid: decimal.Round(bid, 4),
            Ask: decimal.Round(ask, 4),
            Last: bar.Close,
            BidSize: bidSize,
            AskSize: askSize);

        IReadOnlyList<L2OrderBookLevel> levels = CreateLevels(
            bar,
            l1,
            directionalPressure,
            Math.Clamp(_settings.DepthLevels, 1, 10));

        var l2 = new L2OrderBookSnapshot(
            Time: bar.Time,
            Symbol: bar.Symbol,
            Levels: levels);

        decimal liquidityScore = CalculateLiquidityScore(bar, indicator, l1, l2);

        return new SailorMarketSnapshot(
            Time: bar.Time,
            Symbol: bar.Symbol,
            Quality: SailorMarketSnapshotQuality.SyntheticBacktest,
            L1: l1,
            L2: l2,
            LiquidityScore: liquidityScore,
            Source: "synthetic-backtest-from-1m-bars");
    }

    private static IReadOnlyList<L2OrderBookLevel> CreateLevels(
        BacktestBar bar,
        L1QuoteSnapshot l1,
        decimal directionalPressure,
        int depthLevels)
    {
        var levels = new List<L2OrderBookLevel>(depthLevels);
        decimal tick = EstimateTickSize(bar.Close);
        long levelBaseSize = Math.Max(50, bar.Volume / 350);

        for (int i = 0; i < depthLevels; i++)
        {
            int level = i + 1;
            decimal bidPrice = Math.Max(0.0001m, l1.Bid - tick * i);
            decimal askPrice = l1.Ask + tick * i;

            decimal depthDecay = 1m / (1m + i * 0.25m);
            long bidSize = Math.Max(1, (long)Math.Round(levelBaseSize * depthDecay * (1m + directionalPressure)));
            long askSize = Math.Max(1, (long)Math.Round(levelBaseSize * depthDecay * (1m - directionalPressure)));

            levels.Add(new L2OrderBookLevel(
                Level: level,
                BidPrice: decimal.Round(bidPrice, 4),
                BidSize: bidSize,
                AskPrice: decimal.Round(askPrice, 4),
                AskSize: askSize));
        }

        return levels;
    }

    private static decimal CalculateLiquidityScore(
        BacktestBar bar,
        BacktestIndicatorSnapshot indicator,
        L1QuoteSnapshot l1,
        L2OrderBookSnapshot l2)
    {
        decimal spreadScore = l1.SpreadBps <= 0m
            ? 0m
            : Clamp(100m - l1.SpreadBps, 0m, 100m);

        decimal volumeScore = indicator.VolumeAverage20.HasValue && indicator.VolumeAverage20.Value > 0m
            ? Clamp(bar.Volume / indicator.VolumeAverage20.Value * 50m, 0m, 100m)
            : Clamp(bar.Volume / 100_000m * 50m, 0m, 100m);

        decimal depthNotional = (l2.TotalBidSize + l2.TotalAskSize) * bar.Close;
        decimal depthScore = Clamp(depthNotional / 100_000m * 100m, 0m, 100m);

        return decimal.Round(spreadScore * 0.35m + volumeScore * 0.35m + depthScore * 0.30m, 2);
    }

    private static decimal EstimateTickSize(decimal price)
    {
        if (price < 1m)
        {
            return 0.0001m;
        }

        if (price < 10m)
        {
            return 0.001m;
        }

        return 0.01m;
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return value < min ? min : value > max ? max : value;
    }
}
