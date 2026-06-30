using Sailor.App.Configuration;

namespace Sailor.App.Runtime.TradeManagement;

public sealed class StrategyLifecyclePolicyResolver
{
    private const string DefaultKey = "default";
    private readonly IReadOnlyDictionary<string, StrategyLifecycleMode> _policies;

    public StrategyLifecyclePolicyResolver(SailorAppSettings settings)
    {
        _policies = BuildPolicyMap(settings.StrategyLifecyclePolicies);
    }

    public StrategyLifecyclePolicy Resolve(string? profileName, SailorTradeOrigin origin)
    {
        string normalizedProfile = NormalizeProfile(profileName);
        StrategyLifecycleMode mode = IsManualExitOnlyOrigin(origin)
            ? StrategyLifecycleMode.ManualManagedExitOnly
            : ResolveMode(normalizedProfile);

        return new StrategyLifecyclePolicy(
            normalizedProfile,
            mode,
            ManualCloseBlocksForDay: true,
            AllowScannerReselectAfterManualStop: true);
    }

    public string ToSummaryString()
    {
        string configured = string.Join(", ", _policies
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value.ToDisplayName()}"));
        return string.IsNullOrWhiteSpace(configured)
            ? "strategyLifecyclePolicies=n/a"
            : $"strategyLifecyclePolicies={configured}";
    }

    private StrategyLifecycleMode ResolveMode(string normalizedProfile)
    {
        if (_policies.TryGetValue(normalizedProfile, out StrategyLifecycleMode configured))
        {
            return configured;
        }

        return _policies.TryGetValue(DefaultKey, out StrategyLifecycleMode defaultMode)
            ? defaultMode
            : StrategyLifecycleMode.SingleLifecycleUntilStrategyExit;
    }

    private static IReadOnlyDictionary<string, StrategyLifecycleMode> BuildPolicyMap(IReadOnlyDictionary<string, string>? configuredPolicies)
    {
        var policies = new Dictionary<string, StrategyLifecycleMode>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultKey] = StrategyLifecycleMode.SingleLifecycleUntilStrategyExit,
            ["v21-15minutes"] = StrategyLifecycleMode.MultiCycleUntilLastEntryMinute,
            ["v22-15minutes"] = StrategyLifecycleMode.MultiCycleUntilLastEntryMinute,
            ["v23-5minutes"] = StrategyLifecycleMode.MultiCycleUntilLastEntryMinute,
            ["v24-5minutes"] = StrategyLifecycleMode.MultiCycleUntilLastEntryMinute
        };

        if (configuredPolicies is null)
        {
            return policies;
        }

        foreach (KeyValuePair<string, string> pair in configuredPolicies)
        {
            string key = NormalizeProfile(pair.Key);
            if (key.Length == 0)
            {
                continue;
            }

            if (StrategyLifecycleModeExtensions.TryParseDisplayName(pair.Value, out StrategyLifecycleMode mode))
            {
                policies[key] = mode;
            }
        }

        return policies;
    }

    private static bool IsManualExitOnlyOrigin(SailorTradeOrigin origin)
        => origin is SailorTradeOrigin.ManualPreStart
            or SailorTradeOrigin.ManualIntraday
            or SailorTradeOrigin.UnknownBroker
            or SailorTradeOrigin.SailorManualCommand;

    private static string NormalizeProfile(string? profileName)
        => string.IsNullOrWhiteSpace(profileName) ? DefaultKey : profileName.Trim().ToLowerInvariant();
}
