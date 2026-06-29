namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListHistoryBatch(
    int BatchNumber,
    DateTimeOffset NotBeforeUtc,
    IReadOnlyList<string> Symbols)
{
    public string ToDisplayLine()
        => $"batch={BatchNumber} notBeforeUtc={NotBeforeUtc:O} symbols={Symbols.Count} {string.Join(",", Symbols.Take(12))}{(Symbols.Count > 12 ? ",..." : string.Empty)}";
}
