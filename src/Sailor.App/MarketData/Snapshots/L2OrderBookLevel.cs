namespace Sailor.App.MarketData.Snapshots;

public sealed record L2OrderBookLevel(
    int Level,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize);
