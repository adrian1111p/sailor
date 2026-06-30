namespace Sailor.App.Runtime.Paper;

public sealed record ScannerSlotReplenishmentReport(
    DateTimeOffset ObservedUtc,
    int TargetScannerTrades,
    int ActiveScannerTrades,
    int ManualManagedTrades,
    int Shortfall,
    int NewSlotsRequested,
    int NewSlotsCreated,
    IReadOnlyList<string> BlockedSymbols,
    string Reason,
    string? JsonPath = null,
    string? CsvPath = null)
{
    public ScannerSlotReplenishmentReport WithPaths(string jsonPath, string csvPath)
        => this with { JsonPath = jsonPath, CsvPath = csvPath };

    public string ToSummaryString()
    {
        string paths = string.IsNullOrWhiteSpace(JsonPath)
            ? string.Empty
            : $" json={JsonPath} csv={CsvPath}";
        return $"targetScannerTrades={TargetScannerTrades} activeScannerTrades={ActiveScannerTrades} manualManagedTrades={ManualManagedTrades} " +
               $"shortfall={Shortfall} newSlotsRequested={NewSlotsRequested} newSlotsCreated={NewSlotsCreated} blockedSymbols={BlockedSymbols.Count} reason={Reason}{paths}";
    }
}
