using System.Globalization;
using System.Text.Json;
using Sailor.App.Configuration;
using Sailor.App.Logging;
using Sailor.App.Reporting;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.Live;

public sealed class LiveReadinessGate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SailorAppSettings _settings;

    public LiveReadinessGate(SailorAppSettings settings)
    {
        _settings = settings;
    }

    public string LatestPaperCertificationPath
        => Path.Combine(SailorLogPaths.Paper, "Reports", "paper_certification_latest.json");

    public string ReadinessDirectory
        => EnsureDirectory(Path.Combine(SailorLogPaths.Live, "Readiness"));

    public string LatestReadinessJsonPath
        => Path.Combine(ReadinessDirectory, "live_readiness_latest.json");

    public string DailyReadinessCsvPath
        => Path.Combine(ReadinessDirectory, $"live_readiness_{DateTime.Now:yyyyMMdd}.csv");

    public LiveReadinessGateResult Evaluate(LiveReadinessGateRequest request)
    {
        SailorRuntimeModeSettings live = _settings.Runtime.Live;
        var checks = new List<LiveReadinessCheck>();
        PaperCertificationReport? paperReport = LoadLatestPaperReport(out string? reportLoadError);

        bool readOnlyAllowed = request.ReadOnly || !request.RequiresTrading;
        if (request.RequiresTrading)
        {
            Add(checks, "read-only-start", true, "Trading command requested; read-only start is not required for this gate.");
        }
        else
        {
            Add(checks, "read-only-start", readOnlyAllowed, request.ReadOnly
                ? "Live command is explicitly read-only."
                : "Command is read-only by design; no order router is enabled.");
        }

        if (request.RequiresTrading)
        {
            Add(checks, "config-allow-live-trading", live.AllowLiveTrading, live.AllowLiveTrading
                ? "Runtime.Live.AllowLiveTrading is true."
                : "Runtime.Live.AllowLiveTrading is false by default; live trading remains blocked.");
            Add(checks, "command-confirm-live", request.ConfirmLive, request.ConfirmLive
                ? "--confirm-live was supplied."
                : "Manual confirmation is missing. Add --confirm-live only after reviewing the live-readiness evidence.");
        }
        else
        {
            Add(checks, "config-allow-live-trading", true, "Not required for read-only live command.");
            Add(checks, "command-confirm-live", true, "Not required for read-only live command.");
        }

        if (paperReport is null)
        {
            Add(checks, "paper-certification-report", !request.RequiresTrading, reportLoadError ?? $"No paper certification report found at {LatestPaperCertificationPath}.");
            Add(checks, "paper-certification-passed", !request.RequiresTrading, "No usable paper certification report was loaded.");
            Add(checks, "paper-certification-recent", !request.RequiresTrading, "No usable paper certification report was loaded.");
            Add(checks, "account-match", !request.RequiresTrading, "No usable paper certification report was loaded.");
        }
        else
        {
            TimeSpan maxAge = TimeSpan.FromHours(Math.Max(1, live.CertificationMaxAgeHours));
            TimeSpan age = DateTimeOffset.UtcNow - paperReport.GeneratedUtc;
            bool recent = !request.RequiresTrading || age <= maxAge;
            string expectedAccount = NormalizeAccount(request.Account);
            string paperAccount = NormalizeAccount(paperReport.Account);
            bool certificationPassed = !request.RequiresTrading || paperReport.CanPromoteToLiveReadiness;
            bool accountMatch = !request.RequiresTrading || (!string.IsNullOrWhiteSpace(expectedAccount) && expectedAccount.Equals(paperAccount, StringComparison.OrdinalIgnoreCase));

            Add(checks, "paper-certification-report", true, $"Loaded latest paper certification report {paperReport.ReportId} from {LatestPaperCertificationPath}.");
            Add(checks, "paper-certification-passed", certificationPassed, !request.RequiresTrading
                ? $"Not required for read-only command. Latest paper status is {paperReport.CertificationStatus}."
                : paperReport.CanPromoteToLiveReadiness
                    ? "Paper certification can promote to the live-readiness gate."
                    : $"Paper certification is not promotable: {paperReport.CertificationStatus} - {paperReport.PromotionBlockReason}");
            Add(checks, "paper-certification-recent", recent, !request.RequiresTrading
                ? $"Not required for read-only command. Paper report age is {FormatAge(age)}."
                : $"Paper report age is {FormatAge(age)}; allowed maximum is {FormatAge(maxAge)}.");
            Add(checks, "account-match", accountMatch, !request.RequiresTrading
                ? $"Not required for read-only command. live={DisplayAccount(expectedAccount)} paper={DisplayAccount(paperAccount)}."
                : accountMatch
                    ? $"Live account matches paper certification account: {DisplayAccount(expectedAccount)}."
                    : $"Live account must match paper certification account. live={DisplayAccount(expectedAccount)} paper={DisplayAccount(paperAccount)}.");
        }

        decimal configMaxNotional = live.MaxOrderNotional <= 0m ? 100m : live.MaxOrderNotional;
        bool notionalSmall = request.RequestedMaxNotional > 0m && request.RequestedMaxNotional <= configMaxNotional;
        Add(checks, "max-notional-small", request.RequiresTrading ? notionalSmall : true, request.RequiresTrading
            ? $"Requested max notional={request.RequestedMaxNotional.ToString("F2", CultureInfo.InvariantCulture)}; configured maximum={configMaxNotional.ToString("F2", CultureInfo.InvariantCulture)}."
            : $"Not required for read-only command. Configured live trading maximum would be {configMaxNotional.ToString("F2", CultureInfo.InvariantCulture)}.");

        bool tradingChecksPass = checks.All(check => check.Passed || (!request.RequiresTrading && IsTradingOnlyCheck(check.Name)));
        bool allowTrading = request.RequiresTrading && tradingChecksPass;
        string status = allowTrading
            ? "Passed"
            : request.RequiresTrading ? "Blocked" : "ReadOnly";
        string reason = allowTrading
            ? "All live-readiness checks passed. SAILOR-034 live pilot may consume this gate."
            : request.RequiresTrading
                ? BuildBlockReason(checks)
                : "Read-only live command is allowed. Live trading remains blocked unless the explicit trading gate passes.";

        return new LiveReadinessGateResult(
            CommandName: request.CommandName,
            Status: status,
            ReadOnlyAllowed: readOnlyAllowed,
            LiveTradingAllowed: allowTrading,
            ManualConfirmationRequiredText: "MANUAL LIVE CONFIRMATION REQUIRED: use --confirm-live only after verifying the latest paper certification, account match, and max notional.",
            Reason: reason,
            Account: request.Account,
            RequestedMaxNotional: request.RequestedMaxNotional,
            ConfigMaxNotional: configMaxNotional,
            PaperCertificationPath: LatestPaperCertificationPath,
            PaperReport: paperReport,
            Checks: checks);
    }


    public LiveReadinessGateOutput WriteResult(LiveReadinessGateResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LatestReadinessJsonPath)!);
        File.WriteAllText(LatestReadinessJsonPath, JsonSerializer.Serialize(result, JsonOptions));

        bool writeHeader = !File.Exists(DailyReadinessCsvPath);
        using var writer = new StreamWriter(new FileStream(DailyReadinessCsvPath, FileMode.Append, FileAccess.Write, FileShare.Read));
        if (writeHeader)
        {
            writer.WriteLine("observedUtc,command,status,readOnlyAllowed,liveTradingAllowed,account,requestedMaxNotional,configMaxNotional,paperReportId,paperCanPromote,reason");
        }

        writer.WriteLine(string.Join(',',
            Csv(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            Csv(result.CommandName),
            Csv(result.Status),
            result.ReadOnlyAllowed.ToString(CultureInfo.InvariantCulture),
            result.LiveTradingAllowed.ToString(CultureInfo.InvariantCulture),
            Csv(result.Account),
            result.RequestedMaxNotional.ToString(CultureInfo.InvariantCulture),
            result.ConfigMaxNotional.ToString(CultureInfo.InvariantCulture),
            Csv(result.PaperReport?.ReportId),
            (result.PaperReport?.CanPromoteToLiveReadiness ?? false).ToString(CultureInfo.InvariantCulture),
            Csv(result.Reason)));

        return new LiveReadinessGateOutput(LatestReadinessJsonPath, DailyReadinessCsvPath, result);
    }

    private PaperCertificationReport? LoadLatestPaperReport(out string? error)
    {
        error = null;
        if (!File.Exists(LatestPaperCertificationPath))
        {
            error = $"No paper certification report found at {LatestPaperCertificationPath}. Run: sailor paper report latest.";
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PaperCertificationReport>(File.ReadAllText(LatestPaperCertificationPath), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"Could not read latest paper certification report: {ex.Message}";
            return null;
        }
    }

    private static void Add(List<LiveReadinessCheck> checks, string name, bool passed, string message)
        => checks.Add(new LiveReadinessCheck(name, passed, message));

    private static string NormalizeAccount(string? account)
        => string.IsNullOrWhiteSpace(account) ? string.Empty : account.Trim().ToUpperInvariant();

    private static string DisplayAccount(string? account)
        => string.IsNullOrWhiteSpace(account) ? "not-configured" : account.Trim();

    private static string BuildBlockReason(IReadOnlyList<LiveReadinessCheck> checks)
    {
        LiveReadinessCheck? failed = checks.FirstOrDefault(check => !check.Passed);
        return failed is null
            ? "Live-readiness gate is blocked."
            : $"Blocked by {failed.Name}: {failed.Message}";
    }

    private static bool IsTradingOnlyCheck(string name)
        => name is "config-allow-live-trading" or "command-confirm-live" or "max-notional-small" or "read-only-start";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            return "0m";
        }

        if (age.TotalHours >= 1)
        {
            return $"{age.TotalHours:F1}h";
        }

        return $"{Math.Max(0, age.TotalMinutes):F0}m";
    }
}

public sealed record LiveReadinessGateRequest(
    string CommandName,
    bool RequiresTrading,
    bool ReadOnly,
    bool ConfirmLive,
    string Account,
    decimal RequestedMaxNotional);

public sealed record LiveReadinessGateOutput(
    string JsonPath,
    string CsvPath,
    LiveReadinessGateResult Result);

public sealed record LiveReadinessGateResult(
    string CommandName,
    string Status,
    bool ReadOnlyAllowed,
    bool LiveTradingAllowed,
    string ManualConfirmationRequiredText,
    string Reason,
    string Account,
    decimal RequestedMaxNotional,
    decimal ConfigMaxNotional,
    string PaperCertificationPath,
    PaperCertificationReport? PaperReport,
    IReadOnlyList<LiveReadinessCheck> Checks)
{
    public string ToSummaryString()
        => $"live-readiness command={CommandName} status={Status} readOnlyAllowed={ReadOnlyAllowed} liveTradingAllowed={LiveTradingAllowed} account={(string.IsNullOrWhiteSpace(Account) ? "not-configured" : Account)} requestedMaxNotional={RequestedMaxNotional:F2} configMaxNotional={ConfigMaxNotional:F2}";
}

public sealed record LiveReadinessCheck(
    string Name,
    bool Passed,
    string Message)
{
    public string ToDisplayLine()
        => $"{(Passed ? "PASS" : "FAIL")} {Name}: {Message}";
}
