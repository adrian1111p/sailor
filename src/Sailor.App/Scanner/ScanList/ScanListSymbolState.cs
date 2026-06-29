namespace Sailor.App.Scanner.ScanList;

public enum ScanListSymbolStatus
{
    Active = 0,
    RetainedForOpenPosition = 1,
    RetainedForRecentSelection = 2,
    Removed = 3
}

public sealed record ScanListSymbolState(
    string Symbol,
    ScanListSymbolStatus Status,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset? RemovedUtc,
    bool HasOpenPosition,
    bool IsRetainedTradeCandidate,
    int? LastRank,
    decimal? LastScore,
    string Source,
    string HistoryStatus = "NotRequested",
    DateTimeOffset? LastHistoryRequestUtc = null,
    DateTimeOffset? LastHistorySuccessUtc = null,
    DateTimeOffset? LastHistoryFailureUtc = null,
    int HistoryRequestCount = 0,
    int HistoricalBarCount = 0,
    int RealtimeCandleCount = 0,
    int MergedCandleCount = 0)
{
    public bool IsTradableCandidate => Status == ScanListSymbolStatus.Active && IsRetainedTradeCandidate;

    public string ToDisplayLine()
    {
        string rank = LastRank?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
        string score = LastScore?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
        return $"{Symbol,-8} status={Status} openPosition={HasOpenPosition} retainedTrade={IsRetainedTradeCandidate} rank={rank} score={score} " +
               $"history={HistoryStatus} histBars={HistoricalBarCount} rtCandles={RealtimeCandleCount} mergedCandles={MergedCandleCount} " +
               $"firstSeen={FirstSeenUtc:O} lastSeen={LastSeenUtc:O}";
    }
}
