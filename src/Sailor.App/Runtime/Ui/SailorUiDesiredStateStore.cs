using System.Globalization;
using System.Text.Json;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.Ui;

public sealed record SailorUiDesiredStateSnapshot(
    string Mode,
    string? Account,
    DateTimeOffset UpdatedUtc,
    int MaxActiveStrategies,
    IReadOnlyList<SailorUiDesiredStateRow> Rows)
{
    public SailorUiDesiredStateRow? FindRow(string symbol)
    {
        string normalized = SailorUiDesiredStateStore.NormalizeSymbol(symbol);
        return Rows.FirstOrDefault(row => row.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> ActiveStrategies => Rows
        .Where(row => row.DesiredTradeEnabled)
        .Select(row => SailorUiDesiredStateStore.NormalizeStrategy(row.SelectedStrategy))
        .Where(strategy => strategy.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(strategy => strategy, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed record SailorUiDesiredStateRow(
    string Symbol,
    bool DesiredTradeEnabled,
    string SelectedStrategy,
    DateTimeOffset UpdatedUtc,
    string UpdatedBy,
    string Source);

public sealed record SailorUiDesiredStateUpdate(
    string Symbol,
    bool? DesiredTradeEnabled,
    string? SelectedStrategy,
    string? Source);

public sealed record SailorUiDesiredStateUpdateResult(
    bool Accepted,
    string RejectedReason,
    SailorUiDesiredStateSnapshot State,
    SailorUiDesiredStateRow? Row);

public static class SailorUiDesiredStateValidator
{
    public static string Validate(IReadOnlyList<SailorUiDesiredStateRow> rows, int maxActiveStrategies)
    {
        int safeMax = Math.Max(1, maxActiveStrategies);
        string[] activeStrategies = rows
            .Where(row => row.DesiredTradeEnabled)
            .Select(row => SailorUiDesiredStateStore.NormalizeStrategy(row.SelectedStrategy))
            .Where(strategy => strategy.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (activeStrategies.Length > safeMax)
        {
            return $"Rejected because {activeStrategies.Length} active strategies would be selected; maxActiveStrategies={safeMax}: {string.Join(',', activeStrategies)}.";
        }

        return string.Empty;
    }
}

public sealed class SailorUiDesiredStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SailorRuntimeMode _mode;
    private readonly string? _account;
    private readonly int _maxActiveStrategies;
    private readonly string _repositoryRoot;

    public SailorUiDesiredStateStore(
        SailorRuntimeMode mode,
        string? account,
        int maxActiveStrategies = SailorUiContract.DefaultMaxActiveStrategies,
        string? repositoryRoot = null)
    {
        _mode = mode;
        _account = string.IsNullOrWhiteSpace(account) ? null : account.Trim();
        _maxActiveStrategies = Math.Max(1, maxActiveStrategies);
        _repositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot) ? FindRepositoryRoot() : repositoryRoot;
    }

    public string StateDirectory => EnsureDirectory(Path.Combine(_repositoryRoot, "state", _mode.ToDisplayName(), "ui"));

    public string LogDirectory => EnsureDirectory(Path.Combine(_repositoryRoot, "logs", _mode.ToDisplayName(), "SailorUI"));

    public string LatestStatePath => Path.Combine(StateDirectory, "desired_state_latest.json");

    public string EventJsonlPath => Path.Combine(StateDirectory, $"desired_state_{DateTime.UtcNow:yyyyMMdd}.jsonl");

    public string ActionCsvPath => Path.Combine(LogDirectory, $"sailor_ui_actions_{DateTime.UtcNow:yyyyMMdd}.csv");

    public SailorUiDesiredStateSnapshot LoadSnapshot()
    {
        if (!File.Exists(LatestStatePath))
        {
            return EmptySnapshot();
        }

        try
        {
            string json = File.ReadAllText(LatestStatePath);
            SailorUiDesiredStateSnapshot? snapshot = JsonSerializer.Deserialize<SailorUiDesiredStateSnapshot>(json, JsonOptions);
            if (snapshot is null)
            {
                return EmptySnapshot();
            }

            return NormalizeSnapshot(snapshot);
        }
        catch
        {
            return EmptySnapshot();
        }
    }

    public SailorUiDesiredStateUpdateResult TryUpdate(
        SailorUiDesiredStateUpdate update,
        string updatedBy,
        string userAgent)
    {
        if (_mode != SailorRuntimeMode.Paper)
        {
            return new SailorUiDesiredStateUpdateResult(false, SailorUiLiveHardening.LiveControlsForbiddenReason, EmptySnapshot(), null);
        }

        string symbol = NormalizeSymbol(update.Symbol);
        SailorUiDesiredStateSnapshot before = LoadSnapshot();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            var rejected = new SailorUiDesiredStateUpdateResult(false, "Symbol is required.", before, null);
            AppendAction(before, null, update, rejected, userAgent);
            return rejected;
        }

        var rows = before.Rows.ToDictionary(row => row.Symbol, StringComparer.OrdinalIgnoreCase);
        rows.TryGetValue(symbol, out SailorUiDesiredStateRow? previous);

        bool desiredTradeEnabled = update.DesiredTradeEnabled ?? previous?.DesiredTradeEnabled ?? false;
        string selectedStrategy = NormalizeStrategy(update.SelectedStrategy ?? previous?.SelectedStrategy ?? string.Empty);
        string source = string.IsNullOrWhiteSpace(update.Source) ? "SailorUI" : update.Source!.Trim();
        string safeUpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "SailorUI" : updatedBy.Trim();

        SailorUiDesiredStateRow nextRow = new(
            symbol,
            desiredTradeEnabled,
            selectedStrategy,
            DateTimeOffset.UtcNow,
            safeUpdatedBy,
            source);

        rows[symbol] = nextRow;
        IReadOnlyList<SailorUiDesiredStateRow> nextRows = rows.Values
            .OrderByDescending(row => row.DesiredTradeEnabled)
            .ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string validationError = SailorUiDesiredStateValidator.Validate(nextRows, _maxActiveStrategies);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            var rejected = new SailorUiDesiredStateUpdateResult(false, validationError, before, previous);
            AppendAction(before, previous, update, rejected, userAgent);
            return rejected;
        }

        SailorUiDesiredStateSnapshot saved = new(
            _mode.ToDisplayName(),
            _account ?? before.Account,
            DateTimeOffset.UtcNow,
            _maxActiveStrategies,
            nextRows);

        Save(saved);
        var accepted = new SailorUiDesiredStateUpdateResult(true, string.Empty, saved, nextRow);
        AppendAction(before, previous, update, accepted, userAgent);
        return accepted;
    }

    public static string NormalizeSymbol(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().ToUpperInvariant();

    public static string NormalizeStrategy(string strategy)
        => SailorUiStrategyProfileNames.Normalize(strategy);

    private SailorUiDesiredStateSnapshot EmptySnapshot()
        => new(_mode.ToDisplayName(), _account, DateTimeOffset.MinValue, _maxActiveStrategies, Array.Empty<SailorUiDesiredStateRow>());

    private SailorUiDesiredStateSnapshot NormalizeSnapshot(SailorUiDesiredStateSnapshot snapshot)
    {
        IReadOnlyList<SailorUiDesiredStateRow> rows = snapshot.Rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Symbol))
            .Select(row => row with
            {
                Symbol = NormalizeSymbol(row.Symbol),
                SelectedStrategy = NormalizeStrategy(row.SelectedStrategy),
                UpdatedBy = string.IsNullOrWhiteSpace(row.UpdatedBy) ? "SailorUI" : row.UpdatedBy.Trim(),
                Source = string.IsNullOrWhiteSpace(row.Source) ? "SailorUI" : row.Source.Trim()
            })
            .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.UpdatedUtc).First())
            .OrderByDescending(row => row.DesiredTradeEnabled)
            .ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SailorUiDesiredStateSnapshot(
            _mode.ToDisplayName(),
            _account ?? snapshot.Account,
            snapshot.UpdatedUtc,
            _maxActiveStrategies,
            rows);
    }

    private void Save(SailorUiDesiredStateSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(LatestStatePath, json);
        File.AppendAllText(EventJsonlPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + Environment.NewLine);
    }

    private void AppendAction(
        SailorUiDesiredStateSnapshot before,
        SailorUiDesiredStateRow? previous,
        SailorUiDesiredStateUpdate update,
        SailorUiDesiredStateUpdateResult result,
        string userAgent)
    {
        bool writeHeader = !File.Exists(ActionCsvPath);
        using var writer = new StreamWriter(ActionCsvPath, append: true);
        if (writeHeader)
        {
            writer.WriteLine("TimeUtc,Mode,Account,Symbol,OldEnabled,NewEnabled,OldStrategy,NewStrategy,Accepted,RejectedReason,UserAgent,Source");
        }

        string symbol = NormalizeSymbol(update.Symbol);
        bool oldEnabled = previous?.DesiredTradeEnabled ?? false;
        bool newEnabled = result.Row?.DesiredTradeEnabled ?? update.DesiredTradeEnabled ?? oldEnabled;
        string oldStrategy = previous?.SelectedStrategy ?? string.Empty;
        string newStrategy = result.Row?.SelectedStrategy ?? NormalizeStrategy(update.SelectedStrategy ?? oldStrategy);
        string source = string.IsNullOrWhiteSpace(update.Source) ? "SailorUI" : update.Source!.Trim();

        writer.WriteLine(string.Join(',', new[]
        {
            Csv(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            Csv(_mode.ToDisplayName()),
            Csv(_account ?? before.Account ?? string.Empty),
            Csv(symbol),
            Csv(oldEnabled.ToString(CultureInfo.InvariantCulture)),
            Csv(newEnabled.ToString(CultureInfo.InvariantCulture)),
            Csv(oldStrategy),
            Csv(newStrategy),
            Csv(result.Accepted.ToString(CultureInfo.InvariantCulture)),
            Csv(result.RejectedReason),
            Csv(userAgent),
            Csv(source)
        }));
    }

    private static string Csv(string value)
    {
        string safe = value ?? string.Empty;
        if (safe.Contains(',', StringComparison.Ordinal) || safe.Contains('"', StringComparison.Ordinal) || safe.Contains('\n', StringComparison.Ordinal) || safe.Contains('\r', StringComparison.Ordinal))
        {
            return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return safe;
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sailor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
