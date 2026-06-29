using Sailor.App.Runtime.Common;
using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListRunRequest(
    SailorRuntimeMode Mode,
    ScanListWorkbookOptions WorkbookOptions,
    PaperScannerOptions ScannerOptions,
    int Cycles,
    int ScanRefreshSeconds,
    int TradeTop,
    bool WaitBetweenCycles)
{
    public int SafeCycles => Math.Max(1, Cycles);

    public int SafeScanRefreshSeconds => Math.Max(1, ScanRefreshSeconds);

    public int SafeTradeTop => Math.Max(1, TradeTop);
}
