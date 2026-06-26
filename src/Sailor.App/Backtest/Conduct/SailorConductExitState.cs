namespace Sailor.App.Backtest.Conduct;

public sealed class SailorConductExitState
{
    public SailorConductExitState(
        DateTimeOffset entryTime,
        int entryBarIndex,
        decimal entryPrice,
        int quantity)
    {
        EntryTime = entryTime;
        EntryBarIndex = entryBarIndex;
        EntryPrice = entryPrice;
        Quantity = quantity;
        PeakPrice = entryPrice;
    }

    public DateTimeOffset EntryTime { get; }

    public int EntryBarIndex { get; }

    public decimal EntryPrice { get; }

    public int Quantity { get; }

    public decimal PeakPrice { get; private set; }

    public bool BreakevenArmed { get; private set; }

    public bool TrailingArmed { get; private set; }

    public void ObserveBarHigh(decimal high)
    {
        if (high > PeakPrice)
        {
            PeakPrice = high;
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
}
