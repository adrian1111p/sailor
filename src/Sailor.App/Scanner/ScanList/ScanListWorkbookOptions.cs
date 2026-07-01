namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListWorkbookOptions(
    string FilePath,
    string SheetName = ScanListWorkbookOptions.DefaultSheetName,
    string SymbolColumn = ScanListWorkbookOptions.DefaultSymbolColumn,
    int RefreshSeconds = ScanListWorkbookOptions.DefaultRefreshSeconds,
    int TradeTop = ScanListWorkbookOptions.DefaultTradeTop,
    int HistoryBatchSize = ScanListWorkbookOptions.DefaultHistoryBatchSize,
    int HistoryBatchIntervalMinutes = ScanListWorkbookOptions.DefaultHistoryBatchIntervalMinutes)
{
    public const string DefaultFilePath = "scan/data/scan_default.xlsx";
    public const string DefaultSheetName = "Candidates";
    public const string DefaultSymbolColumn = "A";
    public const int DefaultRefreshSeconds = 300;
    public const int DefaultTradeTop = 10;
    public const int DefaultHistoryBatchSize = 145;
    public const int DefaultHistoryBatchIntervalMinutes = 10;

    public static ScanListWorkbookOptions Default => new(DefaultFilePath);

    public string ToDisplayString()
        => $"file={FilePath} sheet={SheetName} symbolColumn={SymbolColumn} refreshSeconds={RefreshSeconds} " +
           $"tradeTop={TradeTop} historyBatchSize={HistoryBatchSize} historyBatchIntervalMinutes={HistoryBatchIntervalMinutes}";
}
