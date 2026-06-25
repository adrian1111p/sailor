namespace Sailor.App.Backtest.Strategies;

internal static class StrategyDiagnosticsEnvironment
{
    private static readonly string[] GlobalVariableNames =
    [
        "HARVESTER_STRATEGY_DIAGNOSTICS",
        "HARVESTER_ALL_STRATEGY_DIAGNOSTICS"
    ];

    internal static bool IsEnabled(string strategyKey)
    {
        foreach (var variableName in GlobalVariableNames)
        {
            if (ReadBool(Environment.GetEnvironmentVariable(variableName)))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(strategyKey))
        {
            return false;
        }

        var normalizedKey = strategyKey.Trim().ToUpperInvariant();
        return ReadBool(Environment.GetEnvironmentVariable($"HARVESTER_{normalizedKey}_DIAGNOSTICS"));
    }

    private static bool ReadBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
