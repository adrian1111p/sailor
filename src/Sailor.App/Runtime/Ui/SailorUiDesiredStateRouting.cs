using Sailor.App.Runtime.Common;
namespace Sailor.App.Runtime.Ui;

public sealed record SailorUiDesiredStateRoutingSnapshot(
    bool Enabled,
    bool HasActiveDesiredRows,
    bool HasAnyRows,
    int MaxActiveStrategies,
    IReadOnlyList<string> ActiveStrategies,
    IReadOnlyDictionary<string, SailorUiDesiredStateRow> Rows,
    IReadOnlyList<string> Warnings)
{
    public static SailorUiDesiredStateRoutingSnapshot Disabled(int maxActiveStrategies)
        => new(
            false,
            false,
            false,
            Math.Max(1, maxActiveStrategies),
            Array.Empty<string>(),
            new Dictionary<string, SailorUiDesiredStateRow>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<string>());

    public SailorUiDesiredStateRow? FindRow(string symbol)
    {
        string normalized = SailorUiDesiredStateStore.NormalizeSymbol(symbol);
        return Rows.TryGetValue(normalized, out SailorUiDesiredStateRow? row) ? row : null;
    }

    public bool ShouldSkipFlatScannerEntry(string symbol, out string reason)
    {
        reason = string.Empty;
        if (!Enabled)
        {
            return false;
        }

        SailorUiDesiredStateRow? row = FindRow(symbol);
        if (row is not null && !row.DesiredTradeEnabled)
        {
            reason = $"SAILOR-068 UI desired state disabled {SailorUiDesiredStateStore.NormalizeSymbol(symbol)}; flat scanner entry is skipped.";
            return true;
        }

        if (HasActiveDesiredRows && row?.DesiredTradeEnabled != true)
        {
            reason = $"SAILOR-068 UI desired state has active strategy selections; {SailorUiDesiredStateStore.NormalizeSymbol(symbol)} is not checked/enabled and remains inactive.";
            return true;
        }

        return false;
    }

    public bool ShouldForceExit(string symbol)
    {
        SailorUiDesiredStateRow? row = FindRow(symbol);
        return Enabled && row is not null && !row.DesiredTradeEnabled;
    }

    public string ResolveProfileName(string symbol, string fallbackProfileName)
    {
        SailorUiDesiredStateRow? row = FindRow(symbol);
        if (Enabled && row?.DesiredTradeEnabled == true)
        {
            string profile = SailorUiStrategyProfileNames.Normalize(row.SelectedStrategy);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                return profile;
            }
        }

        return SailorUiStrategyProfileNames.Normalize(fallbackProfileName);
    }

    public string ToSummaryString()
        => Enabled
            ? $"enabled=True activeStrategies={ActiveStrategies.Count}/{MaxActiveStrategies} active={string.Join(',', ActiveStrategies)} rows={Rows.Count} hasActiveRows={HasActiveDesiredRows} warnings={Warnings.Count}"
            : $"enabled=False maxActiveStrategies={MaxActiveStrategies}";
}

public static class SailorUiDesiredStateRouter
{
    public static SailorUiDesiredStateRoutingSnapshot Load(
        bool enabled,
        SailorRuntimeMode mode,
        string? account,
        int maxActiveStrategies,
        string? repositoryRoot = null)
    {
        int safeMax = Math.Max(1, maxActiveStrategies);
        if (!enabled || mode != SailorRuntimeMode.Paper)
        {
            return SailorUiDesiredStateRoutingSnapshot.Disabled(safeMax);
        }

        var warnings = new List<string>();
        try
        {
            var store = new SailorUiDesiredStateStore(mode, account, safeMax, repositoryRoot);
            SailorUiDesiredStateSnapshot snapshot = store.LoadSnapshot();
            IReadOnlyList<SailorUiDesiredStateRow> normalizedRows = snapshot.Rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Symbol))
                .Select(row => row with
                {
                    Symbol = SailorUiDesiredStateStore.NormalizeSymbol(row.Symbol),
                    SelectedStrategy = SailorUiStrategyProfileNames.Normalize(row.SelectedStrategy)
                })
                .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(row => row.UpdatedUtc).First())
                .ToArray();

            IReadOnlyList<string> activeStrategies = normalizedRows
                .Where(row => row.DesiredTradeEnabled)
                .Select(row => SailorUiStrategyProfileNames.Normalize(row.SelectedStrategy))
                .Where(strategy => !string.IsNullOrWhiteSpace(strategy))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(strategy => strategy, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (activeStrategies.Count > safeMax)
            {
                warnings.Add($"SAILOR-068 desired state contains {activeStrategies.Count} active strategies, maxActiveStrategies={safeMax}. Runtime ignores desired entries until SailorUI state is corrected.");
                return new SailorUiDesiredStateRoutingSnapshot(
                    true,
                    false,
                    normalizedRows.Count > 0,
                    safeMax,
                    activeStrategies,
                    normalizedRows.ToDictionary(row => row.Symbol, row => row, StringComparer.OrdinalIgnoreCase),
                    warnings);
            }

            return new SailorUiDesiredStateRoutingSnapshot(
                true,
                activeStrategies.Count > 0,
                normalizedRows.Count > 0,
                safeMax,
                activeStrategies,
                normalizedRows.ToDictionary(row => row.Symbol, row => row, StringComparer.OrdinalIgnoreCase),
                warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"SAILOR-068 desired-state routing could not load SailorUI state: {ex.GetType().Name}: {ex.Message}");
            return new SailorUiDesiredStateRoutingSnapshot(
                true,
                false,
                false,
                safeMax,
                Array.Empty<string>(),
                new Dictionary<string, SailorUiDesiredStateRow>(StringComparer.OrdinalIgnoreCase),
                warnings);
        }
    }
}
