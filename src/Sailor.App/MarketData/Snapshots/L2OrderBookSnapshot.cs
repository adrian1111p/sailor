namespace Sailor.App.MarketData.Snapshots;

public sealed record L2OrderBookSnapshot(
    DateTimeOffset Time,
    string Symbol,
    IReadOnlyList<L2OrderBookLevel> Levels)
{
    public long TotalBidSize => Levels.Sum(level => level.BidSize);

    public long TotalAskSize => Levels.Sum(level => level.AskSize);

    public decimal BookImbalance
    {
        get
        {
            long total = TotalBidSize + TotalAskSize;
            if (total <= 0)
            {
                return 0m;
            }

            return (decimal)(TotalBidSize - TotalAskSize) / total;
        }
    }

    public decimal TopBid => Levels.Count > 0 ? Levels[0].BidPrice : 0m;

    public decimal TopAsk => Levels.Count > 0 ? Levels[0].AskPrice : 0m;
}
