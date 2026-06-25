namespace Sailor.App.Models;

public enum SignalType
{
    Hold = 0,
    Buy = 1,
    Sell = 2
}

public sealed record TradeSignal(
    SignalType Type,
    string Reason)
{
    public static TradeSignal Hold(string reason) => new(SignalType.Hold, reason);

    public static TradeSignal Buy(string reason) => new(SignalType.Buy, reason);

    public static TradeSignal Sell(string reason) => new(SignalType.Sell, reason);
}
