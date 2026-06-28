namespace Sailor.App.MarketData.Snapshots;

public sealed record SailorMarketSnapshot(
    DateTimeOffset Time,
    string Symbol,
    SailorMarketSnapshotQuality Quality,
    L1QuoteSnapshot? L1,
    L2OrderBookSnapshot? L2,
    decimal LiquidityScore,
    string Source)
{
    public bool HasL1 => L1 is not null;

    public bool HasL2 => L2 is not null && L2.Levels.Count > 0;

    public decimal SpreadBps => L1?.SpreadBps ?? 0m;

    public decimal BookImbalance => L2?.BookImbalance ?? L1?.SizeImbalance ?? 0m;

    public string ToCompactString()
    {
        string quality = Quality.ToString();
        string l1 = L1 is null
            ? "L1=n/a"
            : $"L1 bid={L1.Bid:F2} ask={L1.Ask:F2} spread={L1.SpreadBps:F1}bps sizeImb={L1.SizeImbalance:F2}";

        string l2 = L2 is null
            ? "L2=n/a"
            : $"L2 imbalance={L2.BookImbalance:F2} bidSize={L2.TotalBidSize} askSize={L2.TotalAskSize}";

        return $"{quality} | {l1} | {l2} | liquidity={LiquidityScore:F1}";
    }
}
