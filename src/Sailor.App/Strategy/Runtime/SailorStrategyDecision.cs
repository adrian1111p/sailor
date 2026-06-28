using Sailor.App.Broker.Orders;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Strategy.Runtime;

public sealed record SailorStrategyDecision(
    SailorStrategyDecisionType Type,
    string Symbol,
    int Quantity,
    SailorOrderType OrderType,
    decimal? LimitPrice,
    string Reason)
{
    public static SailorStrategyDecision Hold(string symbol, string reason) =>
        new(SailorStrategyDecisionType.Hold, symbol, 0, SailorOrderType.Market, null, reason);

    public bool CreatesOrder => Type != SailorStrategyDecisionType.Hold;

    public SailorOrderIntent ToOrderIntent(
        SailorRuntimeMode mode,
        string strategyName,
        bool dryRun)
    {
        SailorOrderSide side = Type switch
        {
            SailorStrategyDecisionType.EnterLong => SailorOrderSide.Buy,
            SailorStrategyDecisionType.EnterShort => SailorOrderSide.SellShort,
            SailorStrategyDecisionType.ExitLong => SailorOrderSide.Sell,
            SailorStrategyDecisionType.ExitShort => SailorOrderSide.BuyToCover,
            SailorStrategyDecisionType.Flatten => SailorOrderSide.Flatten,
            SailorStrategyDecisionType.CancelOrders => SailorOrderSide.Flatten,
            _ => SailorOrderSide.Flatten
        };

        return new SailorOrderIntent(
            mode,
            Symbol.Trim().ToUpperInvariant(),
            side,
            OrderType,
            Math.Max(0, Quantity),
            LimitPrice,
            strategyName,
            Reason,
            dryRun,
            DateTimeOffset.Now);
    }
}
