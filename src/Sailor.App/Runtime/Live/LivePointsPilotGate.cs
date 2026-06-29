using Sailor.App.Backtest.Scanner.Points;

namespace Sailor.App.Runtime.Live;

public static class LivePointsPilotGate
{
    public static LivePointsPilotGateResult Evaluate(
        PointsScannerMode scannerMode,
        bool requiresTrading,
        LiveDynamicScanPilotSelection? selection,
        decimal minimumTradeScore)
    {
        decimal safeMinimumTradeScore = minimumTradeScore <= 0m ? 45m : minimumTradeScore;
        var checks = new List<string>();

        if (!requiresTrading)
        {
            checks.Add("PASS dry-run-or-read-only: live points pilot gate is advisory because the command is not allowed to send live orders.");
            return new LivePointsPilotGateResult(
                Required: false,
                Allowed: true,
                ScannerMode: scannerMode.ToConfigValue(),
                SelectedSymbol: selection?.Symbol ?? string.Empty,
                SelectedPointsStatus: selection?.PointsStatus ?? "n/a",
                SelectedPointsScore: selection?.PointsScore,
                MinimumTradeScore: safeMinimumTradeScore,
                Checks: checks,
                Reason: "Live points pilot gate is advisory for dry-run/read-only commands.");
        }

        bool pointsOnly = scannerMode == PointsScannerMode.PointsOnly;
        bool hasSelection = selection?.Passed == true && !string.IsNullOrWhiteSpace(selection.Symbol);
        bool selectedReady = selection?.PointsStatus.Equals(PointsScannerStatus.Ready.ToDisplayName(), StringComparison.OrdinalIgnoreCase) == true;
        bool scoreOk = selection?.PointsScore is decimal score && score >= safeMinimumTradeScore;

        checks.Add($"{(pointsOnly ? "PASS" : "FAIL")} scanner-mode-points-only: live send-orders requires --scanner-mode points-only; actual={scannerMode.ToConfigValue()}.");
        checks.Add($"{(hasSelection ? "PASS" : "FAIL")} dynamic-scan-selection: live send-orders requires a scan-list selected symbol with retained points evidence.");
        checks.Add($"{(selectedReady ? "PASS" : "FAIL")} selected-status-ready: selected candidate status must be Ready; actual={selection?.PointsStatus ?? "n/a"}.");
        checks.Add($"{(scoreOk ? "PASS" : "FAIL")} selected-score-threshold: selected score must be >= {safeMinimumTradeScore:F2}; actual={(selection?.PointsScore is decimal selectedScore ? selectedScore.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "n/a")}.");

        bool allowed = pointsOnly && hasSelection && selectedReady && scoreOk;
        string reason = allowed
            ? "Live points pilot gate passed: selected scan-list symbol is Ready and above the live score threshold."
            : $"Blocked by {checks.FirstOrDefault(check => check.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) ?? "live points pilot gate failed."}";

        return new LivePointsPilotGateResult(
            Required: true,
            Allowed: allowed,
            ScannerMode: scannerMode.ToConfigValue(),
            SelectedSymbol: selection?.Symbol ?? string.Empty,
            SelectedPointsStatus: selection?.PointsStatus ?? "n/a",
            SelectedPointsScore: selection?.PointsScore,
            MinimumTradeScore: safeMinimumTradeScore,
            Checks: checks,
            Reason: reason);
    }
}

public sealed record LivePointsPilotGateResult(
    bool Required,
    bool Allowed,
    string ScannerMode,
    string SelectedSymbol,
    string SelectedPointsStatus,
    decimal? SelectedPointsScore,
    decimal MinimumTradeScore,
    IReadOnlyList<string> Checks,
    string Reason)
{
    public string ToSummaryString()
        => $"livePointsPilot required={Required} allowed={Allowed} scannerMode={ScannerMode} selected={(string.IsNullOrWhiteSpace(SelectedSymbol) ? "n/a" : SelectedSymbol)} pointsStatus={SelectedPointsStatus} pointsScore={(SelectedPointsScore is null ? "n/a" : SelectedPointsScore.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))} minScore={MinimumTradeScore:F2}";
}
