namespace Sailor.App.Runtime.TradeManagement;

public enum StrategyLifecycleMode
{
    SingleLifecycleUntilStrategyExit = 0,
    MultiCycleUntilLastEntryMinute = 1,
    ManualManagedExitOnly = 2
}

public static class StrategyLifecycleModeExtensions
{
    public static string ToDisplayName(this StrategyLifecycleMode mode)
        => mode switch
        {
            StrategyLifecycleMode.SingleLifecycleUntilStrategyExit => "single-lifecycle-until-strategy-exit",
            StrategyLifecycleMode.MultiCycleUntilLastEntryMinute => "multi-cycle-until-last-entry-minute",
            StrategyLifecycleMode.ManualManagedExitOnly => "manual-managed-exit-only",
            _ => mode.ToString().ToLowerInvariant()
        };

    public static bool TryParseDisplayName(string? value, out StrategyLifecycleMode mode)
    {
        mode = StrategyLifecycleMode.SingleLifecycleUntilStrategyExit;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        switch (normalized)
        {
            case "single":
            case "single-lifecycle":
            case "single-lifecycle-until-strategy-exit":
            case "singlelifecycleuntilstrategyexit":
                mode = StrategyLifecycleMode.SingleLifecycleUntilStrategyExit;
                return true;
            case "multi":
            case "multi-cycle":
            case "multicycle":
            case "multi-cycle-until-last-entry-minute":
            case "multicycleuntillastentryminute":
                mode = StrategyLifecycleMode.MultiCycleUntilLastEntryMinute;
                return true;
            case "manual":
            case "manual-exit-only":
            case "manual-managed-exit-only":
            case "manualmanagedexitonly":
                mode = StrategyLifecycleMode.ManualManagedExitOnly;
                return true;
            default:
                return Enum.TryParse(value.Trim(), ignoreCase: true, out mode);
        }
    }
}
