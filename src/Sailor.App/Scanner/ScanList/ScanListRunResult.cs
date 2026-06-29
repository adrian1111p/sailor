namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListRunResult(
    string HistoryProviderName,
    string MarketDataProviderName,
    IReadOnlyList<ScanListCycleResult> Cycles,
    IReadOnlyList<string> Warnings)
{
    public ScanListCycleResult? LatestCycle => Cycles.Count == 0 ? null : Cycles[^1];

    public string ToSummaryString()
        => $"cycles={Cycles.Count} latest={(LatestCycle?.ToSummaryString() ?? "n/a")} warnings={Warnings.Count}";
}
