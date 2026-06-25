namespace Sailor.App.Backtest.Strategies;

/// <summary>
/// Phase 6.15 â€” purely documentary marker for strategies that are <b>frozen</b>: kept in the codebase
/// and resolvable when explicitly requested (e.g. <c>--strategy V11</c> or <c>folder-all</c>) for
/// historical/regression comparison, but excluded from the default/active comparison plans because a
/// newer strategy supersedes them.
/// </summary>
/// <remarks>
/// This attribute intentionally does NOT use <see cref="System.ObsoleteAttribute"/> so it produces no
/// compiler warnings at the (still valid) factory call sites in
/// <c>StrategyComparisonRunner.BuildPlans()</c>. It is informational only and is asserted on by tests so
/// the frozen status stays in sync with the runner's archived/frozen plan set.
///
/// Freezing a strategy never changes runtime trade behavior: the frozen strategy still opens and conducts
/// trades exactly as before whenever it is explicitly selected, honoring the "open as many trades as
/// possible / free the symbol after each trade" constraints unchanged.
/// </remarks>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FrozenStrategyAttribute : System.Attribute
{
    public FrozenStrategyAttribute(string supersededBy, string reason)
    {
        SupersededBy = supersededBy;
        Reason = reason;
    }

    /// <summary>The strategy (plan name) that supersedes this frozen one in the default comparison set.</summary>
    public string SupersededBy { get; }

    /// <summary>Short human-readable note on why the strategy was frozen.</summary>
    public string Reason { get; }
}

