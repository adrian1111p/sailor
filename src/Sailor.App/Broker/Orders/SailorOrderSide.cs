namespace Sailor.App.Broker.Orders;

public enum SailorOrderSide
{
    Buy = 0,
    Sell = 1,
    SellShort = 2,
    BuyToCover = 3,
    Flatten = 4
}

public static class SailorOrderSideExtensions
{
    public static bool OpensLong(this SailorOrderSide side) => side == SailorOrderSide.Buy;

    public static bool OpensShort(this SailorOrderSide side) => side == SailorOrderSide.SellShort;

    public static bool IsExit(this SailorOrderSide side) =>
        side is SailorOrderSide.Sell or SailorOrderSide.BuyToCover or SailorOrderSide.Flatten;
}
