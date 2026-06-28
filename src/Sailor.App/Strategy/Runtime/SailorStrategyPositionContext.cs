namespace Sailor.App.Strategy.Runtime;

public sealed record SailorStrategyPositionContext(
    bool HasOpenPosition,
    int Quantity,
    decimal AveragePrice,
    int EntryBarIndex)
{
    public int PositionSide => Quantity < 0 ? -1 : Quantity > 0 ? 1 : 0;

    public int AbsoluteQuantity => Math.Abs(Quantity);

    public static SailorStrategyPositionContext Flat { get; } = new(false, 0, 0m, -1);
}
