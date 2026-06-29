namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListReloadResult(
    DateTimeOffset ObservedUtc,
    string SourceDescription,
    IReadOnlyList<string> ActiveSymbols,
    IReadOnlyList<string> AddedSymbols,
    IReadOnlyList<string> RemovedSymbols,
    IReadOnlyList<string> RetainedRemovedSymbols,
    IReadOnlyList<ScanListSymbolState> States,
    IReadOnlyList<string> Warnings)
{
    public string ToSummaryString()
        => $"active={ActiveSymbols.Count} added={AddedSymbols.Count} removed={RemovedSymbols.Count} retainedRemoved={RetainedRemovedSymbols.Count} states={States.Count} observedUtc={ObservedUtc:O}";
}
