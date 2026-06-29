namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListWorkbookResult(
    ScanListWorkbookOptions Options,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> RejectedTokens,
    IReadOnlyList<string> Warnings,
    DateTimeOffset LoadedUtc)
{
    public int SymbolCount => Symbols.Count;
    public int UniqueCount => Symbols.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int RejectedCount => RejectedTokens.Count;

    public string ToSummaryString()
        => $"scan-list file={Options.FilePath} sheet={Options.SheetName} symbols={SymbolCount} unique={UniqueCount} " +
           $"rejected={RejectedCount} loadedUtc={LoadedUtc:O}";
}
