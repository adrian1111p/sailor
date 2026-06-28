using Sailor.App.Runtime.Common;

namespace Sailor.App.Broker.Orders;

public sealed record SailorOrderIntent(
    SailorRuntimeMode Mode,
    string Symbol,
    SailorOrderSide Side,
    SailorOrderType OrderType,
    int Quantity,
    decimal? LimitPrice,
    string StrategyName,
    string Reason,
    bool DryRun,
    DateTimeOffset CreatedAt)
{
    public static SailorOrderIntent Flatten(
        SailorRuntimeMode mode,
        string symbol,
        string strategyName,
        string reason,
        bool dryRun)
    {
        return new SailorOrderIntent(
            mode,
            symbol.Trim().ToUpperInvariant(),
            SailorOrderSide.Flatten,
            SailorOrderType.Market,
            0,
            null,
            strategyName,
            reason,
            dryRun,
            DateTimeOffset.Now);
    }

    public string ToDisplayString()
    {
        string price = LimitPrice.HasValue ? $" limit={LimitPrice.Value:F2}" : string.Empty;
        string qty = Quantity > 0 ? $" qty={Quantity}" : string.Empty;
        string dry = DryRun ? " DRY-RUN" : string.Empty;
        return $"{CreatedAt:yyyy-MM-dd HH:mm:ss} | {Mode.ToDisplayName()} | {Symbol} | {Side} {OrderType}{qty}{price} | strategy={StrategyName} | {Reason}{dry}";
    }
}
