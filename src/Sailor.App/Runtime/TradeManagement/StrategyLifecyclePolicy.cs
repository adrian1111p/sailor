namespace Sailor.App.Runtime.TradeManagement;

public sealed record StrategyLifecyclePolicy(
    string ProfileName,
    StrategyLifecycleMode Mode,
    bool ManualCloseBlocksForDay,
    bool AllowScannerReselectAfterManualStop)
{
    public bool AllowsAutomaticReEntry => Mode == StrategyLifecycleMode.MultiCycleUntilLastEntryMinute;

    public bool IsManualManagedExitOnly => Mode == StrategyLifecycleMode.ManualManagedExitOnly;

    public StrategyLifecycleEntryDecision EvaluateEntry(
        SailorTradeOrigin origin,
        bool scannerSlotActive,
        bool lifecycleClosedForEntry,
        int easternMinuteOfDay,
        int lastEntryMinute)
    {
        if (IsManualManagedExitOnly)
        {
            return StrategyLifecycleEntryDecision.Block(
                "SAILOR-054 lifecycle policy is configured as manual-managed-exit-only; automatic entries and re-entries are blocked.");
        }

        if (lastEntryMinute > 0 && easternMinuteOfDay >= lastEntryMinute)
        {
            return StrategyLifecycleEntryDecision.Block(
                $"SAILOR-054 universal last-entry policy blocked entry at ET minute {easternMinuteOfDay}; configured LastEntryMinute={lastEntryMinute}.");
        }

        if (lifecycleClosedForEntry && Mode == StrategyLifecycleMode.SingleLifecycleUntilStrategyExit)
        {
            return StrategyLifecycleEntryDecision.Block(
                "SAILOR-054 single-lifecycle policy blocked re-entry after the strategy already closed this lifecycle.");
        }

        if (lifecycleClosedForEntry && Mode == StrategyLifecycleMode.MultiCycleUntilLastEntryMinute && origin == SailorTradeOrigin.ScannerOwned && !scannerSlotActive)
        {
            return StrategyLifecycleEntryDecision.Block(
                "SAILOR-054 multi-cycle policy blocked re-entry because the scanner slot is no longer active.");
        }

        return StrategyLifecycleEntryDecision.Allow(
            $"SAILOR-054 entry allowed by {Mode.ToDisplayName()} before LastEntryMinute={lastEntryMinute}.");
    }

    public bool ShouldCloseEntryWindowAfterStrategyExit(SailorTradeOrigin origin)
    {
        _ = origin;
        return Mode != StrategyLifecycleMode.MultiCycleUntilLastEntryMinute;
    }

    public string ToDisplayString()
        => $"profile={ProfileName} lifecycle={Mode.ToDisplayName()} manualCloseBlocksForDay={ManualCloseBlocksForDay} scannerReselectException={AllowScannerReselectAfterManualStop}";
}
