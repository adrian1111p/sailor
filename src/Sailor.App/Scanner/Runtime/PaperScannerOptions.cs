using Sailor.App.Runtime.Common;

namespace Sailor.App.Scanner.Runtime;

public sealed record PaperScannerOptions(
    SailorRuntimeMode Mode,
    string Timeframe,
    string ProfileName,
    string Universe,
    int TopCount,
    int MaxSymbolsToPrepare,
    int HistoryDays,
    bool RequestIbkrHistory,
    bool MirrorHistoryToBacktest,
    bool UseRegularTradingHours,
    bool CaptureSnapshots,
    bool RequestIbkrMarketData,
    bool UseL1,
    bool UseL2,
    int SnapshotSeconds,
    int DepthLevels,
    int MarketDataType,
    string PrimaryExchange,
    bool UseSmartDepth)
{
    public string ModeName => Mode.ToDisplayName();

    public string ToDisplayString()
        => $"mode={ModeName} timeframe={Timeframe} profile={ProfileName} universe={Universe} top={TopCount} " +
           $"maxSymbols={MaxSymbolsToPrepare} historyDays={HistoryDays} requestHistoryIbkr={RequestIbkrHistory} " +
           $"mirrorBacktest={MirrorHistoryToBacktest} useRth={UseRegularTradingHours} captureSnapshots={CaptureSnapshots} " +
           $"requestMarketDataIbkr={RequestIbkrMarketData} L1={UseL1} L2={UseL2} snapshotSeconds={SnapshotSeconds} " +
           $"depthLevels={DepthLevels} marketDataType={MarketDataType} primaryExchange={PrimaryExchange} smartDepth={UseSmartDepth}";
}
