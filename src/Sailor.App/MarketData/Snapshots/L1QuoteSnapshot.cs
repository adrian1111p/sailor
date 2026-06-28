namespace Sailor.App.MarketData.Snapshots;

public sealed record L1QuoteSnapshot(
    DateTimeOffset Time,
    string Symbol,
    decimal Bid,
    decimal Ask,
    decimal Last,
    long BidSize,
    long AskSize)
{
    public decimal Mid => Bid > 0m && Ask > 0m
        ? (Bid + Ask) / 2m
        : Last;

    public decimal Spread => Bid > 0m && Ask > 0m
        ? Math.Max(0m, Ask - Bid)
        : 0m;

    public decimal SpreadBps => Mid > 0m
        ? Spread / Mid * 10_000m
        : 0m;

    public decimal SizeImbalance
    {
        get
        {
            long total = BidSize + AskSize;
            if (total <= 0)
            {
                return 0m;
            }

            return (decimal)(BidSize - AskSize) / total;
        }
    }
}
