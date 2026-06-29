using Sailor.App.Backtest.Scanner.Points;
using Sailor.App.Scanner.Runtime;
using Sailor.App.Scanner.ScanList;

namespace Sailor.App.Runtime.Live;

public sealed class LiveDynamicScanPilotHost
{
    public LiveDynamicScanPilotSelection SelectBestOne(ScanListRunResult scanListResult)
    {
        ScanListCycleResult? latestCycle = scanListResult.LatestCycle;
        if (latestCycle is null)
        {
            return LiveDynamicScanPilotSelection.Blocked(
                "No scan-list runtime cycle was available, so the live dynamic pilot cannot select a symbol.");
        }

        string? selected = latestCycle.TradeEligibleSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(selected))
        {
            return LiveDynamicScanPilotSelection.Blocked(
                "No trade-eligible scan-list symbol was retained, so the live dynamic pilot remains blocked.",
                latestCycle.Evidence,
                latestCycle.EvidenceJsonPath);
        }

        PaperScannerCandidate? selectedCandidate = latestCycle.ScannerResult.Candidates
            .FirstOrDefault(candidate => candidate.Symbol.Equals(selected, StringComparison.OrdinalIgnoreCase));
        string pointsStatus = selectedCandidate?.PointsCandidate?.Status.ToDisplayName() ?? "n/a";
        decimal? pointsScore = selectedCandidate?.PointsCandidate?.FinalScore;

        return new LiveDynamicScanPilotSelection(
            Passed: true,
            Symbol: selected,
            Reason: $"Selected best retained scan-list symbol {selected} for the one-symbol live pilot.",
            Evidence: latestCycle.Evidence,
            EvidencePath: latestCycle.EvidenceJsonPath,
            ScannerMode: latestCycle.ScannerResult.Options.ScannerMode.ToConfigValue(),
            PointsStatus: pointsStatus,
            PointsScore: pointsScore);
    }
}

public sealed record LiveDynamicScanPilotSelection(
    bool Passed,
    string Symbol,
    string Reason,
    ScanListRuntimeEvidence? Evidence,
    string? EvidencePath,
    string ScannerMode = "legacy-blocks",
    string PointsStatus = "n/a",
    decimal? PointsScore = null)
{
    public static LiveDynamicScanPilotSelection Blocked(
        string reason,
        ScanListRuntimeEvidence? evidence = null,
        string? evidencePath = null)
        => new(false, string.Empty, reason, evidence, evidencePath);

    public string ToSummaryString()
        => Passed
            ? $"live-dynamic-scan-pilot selected={Symbol} scannerMode={ScannerMode} pointsStatus={PointsStatus} pointsScore={(PointsScore is null ? "n/a" : PointsScore.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))} evidence={EvidencePath ?? "n/a"} dataQuality={Evidence?.DataQualityStatus ?? "n/a"}"
            : $"live-dynamic-scan-pilot blocked reason={Reason} evidence={EvidencePath ?? "n/a"}";
}
