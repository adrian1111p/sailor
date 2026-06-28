using System.Globalization;
using System.Text.Json;
using Sailor.App.Logging;

namespace Sailor.App.Runtime.Common;

public sealed class RuntimeIncidentReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public RuntimeIncidentReporter(SailorRuntimeMode mode)
    {
        Mode = mode;
        string logRoot = mode == SailorRuntimeMode.Live ? SailorLogPaths.Live : SailorLogPaths.Paper;
        IncidentDirectory = EnsureDirectory(Path.Combine(logRoot, "Incidents"));
        DailyJsonlPath = Path.Combine(IncidentDirectory, $"incidents_{DateTime.Now:yyyyMMdd}.jsonl");
        DailyCsvPath = Path.Combine(IncidentDirectory, $"incidents_{DateTime.Now:yyyyMMdd}.csv");
        LatestIncidentPath = Path.Combine(IncidentDirectory, "latest_incident.json");
    }

    public SailorRuntimeMode Mode { get; }

    public string IncidentDirectory { get; }

    public string DailyJsonlPath { get; }

    public string DailyCsvPath { get; }

    public string LatestIncidentPath { get; }

    public RuntimeIncident Report(
        string kind,
        string severity,
        RuntimeSafetyState safetyState,
        string message,
        IEnumerable<string>? details = null,
        string? symbol = null)
    {
        var incident = new RuntimeIncident(
            IncidentId: CreateIncidentId(),
            Mode: Mode.ToDisplayName(),
            Kind: Normalize(kind, "runtime"),
            Severity: Normalize(severity, "warning"),
            Symbol: string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant(),
            Message: Normalize(message, "Runtime incident."),
            Details: (details ?? Array.Empty<string>())
                .Where(detail => !string.IsNullOrWhiteSpace(detail))
                .Select(detail => detail.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SafetyState: safetyState,
            ObservedUtc: DateTimeOffset.UtcNow);

        Directory.CreateDirectory(IncidentDirectory);
        File.AppendAllText(DailyJsonlPath, JsonSerializer.Serialize(incident, JsonOptions) + Environment.NewLine);
        File.WriteAllText(LatestIncidentPath, JsonSerializer.Serialize(incident, JsonOptions));
        AppendCsv(incident);
        return incident;
    }

    public RuntimeIncident? LoadLatestIncident()
    {
        if (!File.Exists(LatestIncidentPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RuntimeIncident>(File.ReadAllText(LatestIncidentPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateIncidentId()
    {
        string value = $"RI-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        return value.Length <= 32 ? value : value[..32];
    }

    private void AppendCsv(RuntimeIncident incident)
    {
        bool writeHeader = !File.Exists(DailyCsvPath);
        using var writer = new StreamWriter(new FileStream(DailyCsvPath, FileMode.Append, FileAccess.Write, FileShare.Read));
        if (writeHeader)
        {
            writer.WriteLine("observedUtc,mode,incidentId,kind,severity,symbol,safetyMode,canOpenEntries,canRouteExits,message,details");
        }

        writer.WriteLine(string.Join(',',
            Csv(incident.ObservedUtc.ToString("O", CultureInfo.InvariantCulture)),
            Csv(incident.Mode),
            Csv(incident.IncidentId),
            Csv(incident.Kind),
            Csv(incident.Severity),
            Csv(incident.Symbol),
            Csv(incident.SafetyState.Mode.ToString()),
            incident.SafetyState.CanOpenNewEntries.ToString(CultureInfo.InvariantCulture),
            incident.SafetyState.CanRouteExits.ToString(CultureInfo.InvariantCulture),
            Csv(incident.Message),
            Csv(string.Join(" | ", incident.Details))));
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}

public sealed record RuntimeIncident(
    string IncidentId,
    string Mode,
    string Kind,
    string Severity,
    string? Symbol,
    string Message,
    IReadOnlyList<string> Details,
    RuntimeSafetyState SafetyState,
    DateTimeOffset ObservedUtc)
{
    public string ToDisplayString()
    {
        string symbolText = string.IsNullOrWhiteSpace(Symbol) ? "n/a" : Symbol;
        return $"incident={IncidentId} mode={Mode} kind={Kind} severity={Severity} symbol={symbolText} safety={SafetyState.Mode} observedUtc={ObservedUtc:O} message={Message}";
    }
}
