namespace Sailor.App.Runtime.TradeManagement;

public sealed record StrategyLifecycleEntryDecision(bool AllowEntry, string Reason)
{
    public static StrategyLifecycleEntryDecision Allow(string reason) => new(true, reason);

    public static StrategyLifecycleEntryDecision Block(string reason) => new(false, reason);
}
