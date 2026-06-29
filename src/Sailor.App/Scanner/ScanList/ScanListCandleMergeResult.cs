namespace Sailor.App.Scanner.ScanList;

public sealed record ScanListCandleMergeResult(
    string Symbol,
    int HistoricalCount,
    int RealtimeCount,
    int MergedCount,
    int RealtimeAppended,
    int RealtimeOverlapped,
    IReadOnlyList<ScanListMemoryCandle> Candles,
    IReadOnlyList<string> Warnings)
{
    public string ToSummaryString()
        => $"{Symbol}: historical={HistoricalCount} realtime={RealtimeCount} merged={MergedCount} realtimeAppended={RealtimeAppended} realtimeOverlapped={RealtimeOverlapped}";
}
