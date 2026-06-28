namespace Sailor.App.Strategy.Runtime;

public enum SailorStrategyDecisionType
{
    Hold = 0,
    EnterLong = 1,
    EnterShort = 2,
    ExitLong = 3,
    ExitShort = 4,
    Flatten = 5,
    CancelOrders = 6
}
