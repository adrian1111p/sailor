namespace Sailor.App.Models;

public sealed class Position
{
    public Position(string symbol, int quantity, decimal entryPrice, DateTimeOffset entryTime)
    {
        Symbol = symbol;
        Quantity = quantity;
        EntryPrice = entryPrice;
        EntryTime = entryTime;
    }

    public string Symbol { get; }

    public int Quantity { get; }

    public decimal EntryPrice { get; }

    public DateTimeOffset EntryTime { get; }

    public decimal UnrealizedPnL(decimal currentPrice)
    {
        return (currentPrice - EntryPrice) * Quantity;
    }
}
