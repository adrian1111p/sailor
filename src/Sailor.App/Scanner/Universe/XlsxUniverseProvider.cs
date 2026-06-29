using Sailor.App.Scanner.ScanList;

namespace Sailor.App.Scanner.Universe;

public sealed class XlsxUniverseProvider : ISymbolUniverseProvider
{
    private readonly ScanListWorkbookOptions _options;
    private readonly ScanListWorkbookReader _reader = new();

    public XlsxUniverseProvider(string filePath, string? sheetName = null, string? symbolColumn = null)
    {
        _options = new ScanListWorkbookOptions(
            filePath,
            string.IsNullOrWhiteSpace(sheetName) ? ScanListWorkbookOptions.DefaultSheetName : sheetName.Trim(),
            string.IsNullOrWhiteSpace(symbolColumn) ? ScanListWorkbookOptions.DefaultSymbolColumn : symbolColumn.Trim());
    }

    public string SourceDescription => $"xlsx universe '{_options.FilePath}' sheet '{_options.SheetName}' column '{_options.SymbolColumn}'";

    public async Task<IReadOnlyList<string>> LoadSymbolsAsync(CancellationToken cancellationToken)
    {
        ScanListWorkbookResult result = await _reader.ReadAsync(_options, cancellationToken).ConfigureAwait(false);
        return result.Symbols;
    }
}
