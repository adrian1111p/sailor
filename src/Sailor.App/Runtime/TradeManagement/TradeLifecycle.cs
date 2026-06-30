namespace Sailor.App.Runtime.TradeManagement;

public sealed record TradeLifecycle(
    string TradeId,
    string Symbol,
    string ProfileName,
    SailorTradeOrigin Origin,
    string? ScannerSlotId,
    TradeLifecycleStatus Status,
    int BrokerQuantity,
    decimal BrokerAveragePrice,
    bool ManualStoppedForDay,
    DateOnly TradeDate,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? Timeframe = null,
    string? Account = null,
    string? LastOrderIntentId = null,
    string? LastBrokerOrderId = null,
    string? LastReason = null,
    DateTimeOffset? CompletedUtc = null)
{
    public string NormalizedSymbol => Symbol.Trim().ToUpperInvariant();

    public bool IsActive => Status.IsActive();

    public bool CountsTowardScannerTarget => Origin.CountsTowardScannerTarget() && IsActive;

    public string ToDisplayString()
    {
        string slot = string.IsNullOrWhiteSpace(ScannerSlotId) ? "slot=n/a" : $"slot={ScannerSlotId}";
        string account = string.IsNullOrWhiteSpace(Account) ? "account=n/a" : $"account={Account}";
        return $"{TradeId} {NormalizedSymbol} status={Status.ToDisplayName()} origin={Origin.ToDisplayName()} qty={BrokerQuantity} avg={BrokerAveragePrice:F4} profile={ProfileName} {slot} stoppedForDay={ManualStoppedForDay} {account} updatedUtc={UpdatedUtc:O}";
    }
}
