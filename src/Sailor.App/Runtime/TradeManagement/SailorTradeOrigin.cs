namespace Sailor.App.Runtime.TradeManagement;

public enum SailorTradeOrigin
{
    ScannerOwned = 0,
    SailorPreExisting = 1,
    ManualPreStart = 2,
    ManualIntraday = 3,
    UnknownBroker = 4,
    ExplicitRuntime = 5,
    SailorManualCommand = 6
}

public static class SailorTradeOriginExtensions
{
    public static bool CountsTowardScannerTarget(this SailorTradeOrigin origin)
        => origin == SailorTradeOrigin.ScannerOwned;

    public static string ToDisplayName(this SailorTradeOrigin origin)
        => origin switch
        {
            SailorTradeOrigin.ScannerOwned => "scanner-owned",
            SailorTradeOrigin.SailorPreExisting => "sailor-pre-existing",
            SailorTradeOrigin.ManualPreStart => "manual-pre-start",
            SailorTradeOrigin.ManualIntraday => "manual-intraday",
            SailorTradeOrigin.UnknownBroker => "unknown-broker",
            SailorTradeOrigin.ExplicitRuntime => "explicit-runtime",
            SailorTradeOrigin.SailorManualCommand => "sailor-manual-command",
            _ => origin.ToString().ToLowerInvariant()
        };
}
