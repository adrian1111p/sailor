namespace Sailor.App.Runtime.TradeManagement;

public sealed record TradeLifecycleRegistrySnapshot(
    string Mode,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<TradeLifecycle> Trades)
{
    public int TotalCount => Trades.Count;

    public int ActiveCount => Trades.Count(trade => trade.IsActive);

    public int ScannerActiveCount => Trades.Count(trade => trade.CountsTowardScannerTarget);

    public int ManualActiveCount => Trades.Count(trade => trade.IsActive && !trade.Origin.CountsTowardScannerTarget());

    public int StoppedForDayCount => Trades.Count(trade => trade.ManualStoppedForDay || trade.Status == TradeLifecycleStatus.StoppedForDay);

    public string ToSummaryString()
        => $"trades={TotalCount} active={ActiveCount} scannerActive={ScannerActiveCount} manualOrExternalActive={ManualActiveCount} stoppedForDay={StoppedForDayCount} updatedUtc={UpdatedUtc:O}";
}
