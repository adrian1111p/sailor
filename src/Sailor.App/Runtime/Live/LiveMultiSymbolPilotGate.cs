using System.Globalization;
using Sailor.App.Configuration;

namespace Sailor.App.Runtime.Live;

public sealed class LiveMultiSymbolPilotGate
{
    public static LiveMultiSymbolPilotGateResult Evaluate(
        SailorAppSettings settings,
        string[] args,
        int requestedTopCount,
        decimal requestedMaxNotional)
    {
        SailorRuntimeModeSettings live = settings.Runtime.Live;
        bool commandRequested = args.Any(arg => arg.Equals("--allow-multi-symbol-live-pilot", StringComparison.OrdinalIgnoreCase));
        bool multiSymbolShapeRequested = requestedTopCount > 1;
        bool requested = commandRequested || multiSymbolShapeRequested;

        int maxConcurrent = Math.Max(1, live.MaxConcurrentPositions);
        decimal maxTotal = live.MaxTotalPilotNotional <= 0m ? 100.00m : live.MaxTotalPilotNotional;
        decimal maxPerSymbol = live.MaxPerSymbolNotional <= 0m ? 100.00m : live.MaxPerSymbolNotional;
        decimal safeRequestedNotional = requestedMaxNotional <= 0m ? maxPerSymbol : requestedMaxNotional;
        decimal estimatedTotal = safeRequestedNotional * Math.Max(1, requestedTopCount);

        var checks = new List<string>
        {
            $"{(commandRequested ? "PASS" : "FAIL")} command-flag: --allow-multi-symbol-live-pilot must be supplied for any future multi-symbol live pilot.",
            $"{(live.AllowMultiSymbolPilot ? "PASS" : "FAIL")} config-allow-multi-symbol-pilot: Runtime.Live.AllowMultiSymbolPilot is {(live.AllowMultiSymbolPilot ? "true" : "false")}. Default is false.",
            $"{(requestedTopCount <= maxConcurrent ? "PASS" : "FAIL")} max-concurrent-positions: requestedTop={requestedTopCount}, configuredMaxConcurrentPositions={maxConcurrent}.",
            $"{(safeRequestedNotional <= maxPerSymbol ? "PASS" : "FAIL")} max-per-symbol-notional: requestedPerSymbol={safeRequestedNotional.ToString("F2", CultureInfo.InvariantCulture)}, configuredMaxPerSymbol={maxPerSymbol.ToString("F2", CultureInfo.InvariantCulture)}.",
            $"{(estimatedTotal <= maxTotal ? "PASS" : "FAIL")} max-total-pilot-notional: estimatedTotal={estimatedTotal.ToString("F2", CultureInfo.InvariantCulture)}, configuredMaxTotalPilotNotional={maxTotal.ToString("F2", CultureInfo.InvariantCulture)}."
        };

        bool allowed = requested
            && commandRequested
            && live.AllowMultiSymbolPilot
            && requestedTopCount > 1
            && requestedTopCount <= maxConcurrent
            && safeRequestedNotional <= maxPerSymbol
            && estimatedTotal <= maxTotal;

        string reason;
        if (!requested)
        {
            reason = "Multi-symbol live pilot was not requested. SAILOR uses the one-symbol live pilot path.";
        }
        else if (allowed)
        {
            reason = "Future multi-symbol live pilot gate passed. Current SAILOR implementation still keeps live routing on the one-symbol pilot path until the execution engine is explicitly upgraded.";
        }
        else
        {
            string firstFailed = checks.FirstOrDefault(check => check.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
                ?? "Multi-symbol live pilot gate failed.";
            reason = $"Blocked by {firstFailed}";
        }

        return new LiveMultiSymbolPilotGateResult(
            Requested: requested,
            CommandRequested: commandRequested,
            ConfigAllowed: live.AllowMultiSymbolPilot,
            Allowed: allowed,
            RequestedTopCount: requestedTopCount,
            MaxConcurrentPositions: maxConcurrent,
            MaxTotalPilotNotional: maxTotal,
            MaxPerSymbolNotional: maxPerSymbol,
            EstimatedTotalPilotNotional: estimatedTotal,
            Checks: checks,
            Reason: reason);
    }
}

public sealed record LiveMultiSymbolPilotGateResult(
    bool Requested,
    bool CommandRequested,
    bool ConfigAllowed,
    bool Allowed,
    int RequestedTopCount,
    int MaxConcurrentPositions,
    decimal MaxTotalPilotNotional,
    decimal MaxPerSymbolNotional,
    decimal EstimatedTotalPilotNotional,
    IReadOnlyList<string> Checks,
    string Reason)
{
    public string ToSummaryString()
        => $"multiSymbolPilot requested={Requested} commandFlag={CommandRequested} configAllowed={ConfigAllowed} allowed={Allowed} requestedTop={RequestedTopCount} maxConcurrent={MaxConcurrentPositions} maxTotal={MaxTotalPilotNotional:F2} maxPerSymbol={MaxPerSymbolNotional:F2} estimatedTotal={EstimatedTotalPilotNotional:F2}";
}
