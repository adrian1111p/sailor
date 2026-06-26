namespace Sailor.App.Backtest.Conduct;

public sealed class SailorConductExitState
{
    public SailorConductExitState(
        DateTimeOffset entryTime,
        int entryBarIndex,
        decimal entryPrice,
        int quantity,
        int positionSide = 1)
    {
        EntryTime = entryTime;
        EntryBarIndex = entryBarIndex;
        EntryPrice = entryPrice;
        Quantity = quantity;
        PositionSide = positionSide < 0 ? -1 : 1;
        PeakPrice = entryPrice;
        TroughPrice = entryPrice;
    }

    public DateTimeOffset EntryTime { get; }

    public int EntryBarIndex { get; }

    public decimal EntryPrice { get; }

    public int Quantity { get; }

    public int PositionSide { get; }

    public decimal PeakPrice { get; private set; }

    public decimal TroughPrice { get; private set; }

    public bool BreakevenArmed { get; private set; }

    public bool TrailingArmed { get; private set; }

    public void ObserveBarHigh(decimal high)
    {
        if (high > PeakPrice)
        {
            PeakPrice = high;
        }
    }

    public void ObserveBarLow(decimal low)
    {
        if (low < TroughPrice)
        {
            TroughPrice = low;
        }
    }

    public void ArmBreakeven()
    {
        BreakevenArmed = true;
    }

    public void ArmTrailing()
    {
        TrailingArmed = true;
    }

    public decimal PeakPercent => EntryPrice > 0m
        ? (PeakPrice - EntryPrice) / EntryPrice * 100m
        : 0m;

    public decimal TroughPercent => EntryPrice > 0m
        ? (EntryPrice - TroughPrice) / EntryPrice * 100m
        : 0m;

    public decimal FavorablePercent => PositionSide < 0 ? TroughPercent : PeakPercent;

    public decimal FavorablePrice => PositionSide < 0 ? TroughPrice : PeakPrice;
}
