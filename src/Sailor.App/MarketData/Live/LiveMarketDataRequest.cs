using Sailor.App.Runtime.Common;

namespace Sailor.App.MarketData.Live;

public sealed record LiveMarketDataRequest(
    SailorRuntimeMode Mode,
    string Symbol,
    int RequestId,
    bool UseL1,
    bool UseL2,
    int DepthLevels,
    TimeSpan Duration,
    string PrimaryExchange,
    int MarketDataType,
    bool UseSmartDepth,
    bool UseLocalCacheFallback)
{
    public string NormalizedSymbol => Symbol.Trim().ToUpperInvariant();

    public static LiveMarketDataRequest Create(
        SailorRuntimeMode mode,
        string symbol,
        int requestId,
        bool useL1,
        bool useL2,
        int depthLevels,
        TimeSpan duration,
        string primaryExchange,
        int marketDataType,
        bool useSmartDepth,
        bool useLocalCacheFallback)
        => new(
            mode,
            symbol.Trim().ToUpperInvariant(),
            requestId,
            useL1,
            useL2,
            Math.Clamp(depthLevels, 1, 20),
            duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : duration,
            string.IsNullOrWhiteSpace(primaryExchange) ? "NASDAQ" : primaryExchange.Trim().ToUpperInvariant(),
            marketDataType <= 0 ? 1 : marketDataType,
            useSmartDepth,
            useLocalCacheFallback);

    public string ToDisplayString()
        => $"symbol={NormalizedSymbol} reqId={RequestId} L1={UseL1} L2={UseL2} levels={DepthLevels} " +
           $"duration={Duration.TotalSeconds:F0}s primaryExchange={PrimaryExchange} marketDataType={MarketDataType} smartDepth={UseSmartDepth} localFallback={UseLocalCacheFallback}";
}
