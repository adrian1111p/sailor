using Sailor.App.Runtime.Common;

namespace Sailor.App.Broker.Orders;

public sealed record SailorOrderIntent(
    SailorRuntimeMode Mode,
    string Symbol,
    SailorOrderSide Side,
    SailorOrderType OrderType,
    int Quantity,
    decimal? LimitPrice,
    string StrategyName,
    string Reason,
    bool DryRun,
    DateTimeOffset CreatedAt,
    string IntentId = "",
    string TimeInForce = "DAY",
    string? Account = null)
{
    public string NormalizedIntentId => string.IsNullOrWhiteSpace(IntentId)
        ? $"SI-{CreatedAt:yyyyMMddHHmmssfff}-{Symbol.Trim().ToUpperInvariant()}"
        : IntentId.Trim();

    public string NormalizedSymbol => Symbol.Trim().ToUpperInvariant();

    public string NormalizedTimeInForce => string.IsNullOrWhiteSpace(TimeInForce)
        ? "DAY"
        : TimeInForce.Trim().ToUpperInvariant();

    public bool IsMarketOrder => OrderType == SailorOrderType.Market;

    public bool IsLimitOrder => OrderType == SailorOrderType.Limit;

    public static SailorOrderIntent CreateManual(
        SailorRuntimeMode mode,
        string symbol,
        SailorOrderSide side,
        SailorOrderType orderType,
        int quantity,
        decimal? limitPrice,
        string strategyName,
        string reason,
        bool dryRun,
        string? account,
        string timeInForce)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return new SailorOrderIntent(
            mode,
            symbol.Trim().ToUpperInvariant(),
            side,
            orderType,
            Math.Max(0, quantity),
            limitPrice,
            string.IsNullOrWhiteSpace(strategyName) ? "manual-runtime-command" : strategyName.Trim(),
            string.IsNullOrWhiteSpace(reason) ? "Manual order command." : reason.Trim(),
            dryRun,
            now,
            $"SI-{now:yyyyMMddHHmmssfff}-{symbol.Trim().ToUpperInvariant()}",
            string.IsNullOrWhiteSpace(timeInForce) ? "DAY" : timeInForce.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(account) ? null : account.Trim());
    }

    public static SailorOrderIntent Flatten(
        SailorRuntimeMode mode,
        string symbol,
        string strategyName,
        string reason,
        bool dryRun)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return new SailorOrderIntent(
            mode,
            symbol.Trim().ToUpperInvariant(),
            SailorOrderSide.Flatten,
            SailorOrderType.Market,
            0,
            null,
            strategyName,
            reason,
            dryRun,
            now,
            $"SI-{now:yyyyMMddHHmmssfff}-{symbol.Trim().ToUpperInvariant()}",
            "DAY",
            null);
    }

    public string ToDisplayString()
    {
        string price = LimitPrice.HasValue ? $" limit={LimitPrice.Value:F2}" : string.Empty;
        string qty = Quantity > 0 ? $" qty={Quantity}" : string.Empty;
        string dry = DryRun ? " DRY-RUN" : string.Empty;
        string account = string.IsNullOrWhiteSpace(Account) ? string.Empty : $" account={Account}";
        return $"{CreatedAt:yyyy-MM-dd HH:mm:ss} | {Mode.ToDisplayName()} | id={NormalizedIntentId} | {NormalizedSymbol} | {Side} {OrderType}{qty}{price} tif={NormalizedTimeInForce}{account} | strategy={StrategyName} | {Reason}{dry}";
    }
}
