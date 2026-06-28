using System.Collections.Concurrent;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.MarketData.Live;

public sealed class LiveMarketSnapshotStore
{
    private readonly ConcurrentDictionary<string, MutableSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public void UpdateBid(string symbol, decimal price) => Get(symbol).UpdateBid(price);

    public void UpdateAsk(string symbol, decimal price) => Get(symbol).UpdateAsk(price);

    public void UpdateLast(string symbol, decimal price) => Get(symbol).UpdateLast(price);

    public void UpdateBidSize(string symbol, long size) => Get(symbol).UpdateBidSize(size);

    public void UpdateAskSize(string symbol, long size) => Get(symbol).UpdateAskSize(size);

    public void UpdateDepth(
        string symbol,
        int position,
        int operation,
        int side,
        decimal price,
        long size)
        => Get(symbol).UpdateDepth(position, operation, side, price, size);

    public SailorMarketSnapshot? GetSnapshot(string symbol, string source, TimeSpan staleAfter)
        => Get(symbol).ToSnapshot(source, staleAfter);

    private MutableSnapshot Get(string symbol)
        => _snapshots.GetOrAdd(symbol.Trim().ToUpperInvariant(), normalized => new MutableSnapshot(normalized));

    private sealed class MutableSnapshot
    {
        private readonly object _sync = new();
        private readonly SortedDictionary<int, DepthSideLevel> _bids = new();
        private readonly SortedDictionary<int, DepthSideLevel> _asks = new();
        private readonly string _symbol;
        private DateTimeOffset _lastUpdateUtc;
        private decimal _bid;
        private decimal _ask;
        private decimal _last;
        private long _bidSize;
        private long _askSize;

        public MutableSnapshot(string symbol)
        {
            _symbol = symbol;
        }

        public void UpdateBid(decimal price)
        {
            lock (_sync)
            {
                if (price > 0m)
                {
                    _bid = price;
                    Touch();
                }
            }
        }

        public void UpdateAsk(decimal price)
        {
            lock (_sync)
            {
                if (price > 0m)
                {
                    _ask = price;
                    Touch();
                }
            }
        }

        public void UpdateLast(decimal price)
        {
            lock (_sync)
            {
                if (price > 0m)
                {
                    _last = price;
                    Touch();
                }
            }
        }

        public void UpdateBidSize(long size)
        {
            lock (_sync)
            {
                _bidSize = Math.Max(0, size);
                Touch();
            }
        }

        public void UpdateAskSize(long size)
        {
            lock (_sync)
            {
                _askSize = Math.Max(0, size);
                Touch();
            }
        }

        public void UpdateDepth(int position, int operation, int side, decimal price, long size)
        {
            lock (_sync)
            {
                SortedDictionary<int, DepthSideLevel> levels = side == 1 ? _bids : _asks;
                if (operation == 2 || price <= 0m || size <= 0)
                {
                    levels.Remove(position);
                }
                else
                {
                    levels[position] = new DepthSideLevel(price, Math.Max(0, size));
                }

                Touch();
            }
        }

        public SailorMarketSnapshot? ToSnapshot(string source, TimeSpan staleAfter)
        {
            lock (_sync)
            {
                if (_lastUpdateUtc == default)
                {
                    return null;
                }

                var l1 = new L1QuoteSnapshot(
                    _lastUpdateUtc,
                    _symbol,
                    _bid,
                    _ask,
                    _last,
                    _bidSize,
                    _askSize);

                IReadOnlyList<L2OrderBookLevel> levels = BuildLevels();
                L2OrderBookSnapshot? l2 = levels.Count == 0
                    ? null
                    : new L2OrderBookSnapshot(_lastUpdateUtc, _symbol, levels);

                bool isStale = DateTimeOffset.UtcNow - _lastUpdateUtc > staleAfter;
                SailorMarketSnapshotQuality quality = isStale
                    ? SailorMarketSnapshotQuality.Delayed
                    : SailorMarketSnapshotQuality.Live;

                decimal liquidityScore = CalculateLiquidityScore(l1, l2, isStale);
                return new SailorMarketSnapshot(
                    _lastUpdateUtc,
                    _symbol,
                    quality,
                    l1,
                    l2,
                    liquidityScore,
                    source);
            }
        }

        private IReadOnlyList<L2OrderBookLevel> BuildLevels()
        {
            int max = Math.Max(_bids.Count, _asks.Count);
            if (max == 0)
            {
                return Array.Empty<L2OrderBookLevel>();
            }

            var rows = new List<L2OrderBookLevel>(max);
            for (int i = 0; i < max; i++)
            {
                _ = _bids.TryGetValue(i, out DepthSideLevel bid);
                _ = _asks.TryGetValue(i, out DepthSideLevel ask);
                rows.Add(new L2OrderBookLevel(
                    i,
                    bid.Price,
                    bid.Size,
                    ask.Price,
                    ask.Size));
            }

            return rows;
        }

        private static decimal CalculateLiquidityScore(L1QuoteSnapshot l1, L2OrderBookSnapshot? l2, bool isStale)
        {
            if (isStale)
            {
                return 0m;
            }

            decimal spreadPenalty = l1.SpreadBps <= 0m ? 0m : Math.Min(90m, l1.SpreadBps);
            decimal l1Score = Math.Max(0m, 100m - spreadPenalty);
            decimal depthScore = l2 is null
                ? 0m
                : Math.Min(100m, (l2.TotalBidSize + l2.TotalAskSize) / 200m);

            return Math.Round(l2 is null ? l1Score : (l1Score * 0.65m) + (depthScore * 0.35m), 2);
        }

        private void Touch() => _lastUpdateUtc = DateTimeOffset.UtcNow;

        private readonly record struct DepthSideLevel(decimal Price, long Size);
    }
}
