using Sailor.App.MarketData.History;

namespace Sailor.App.Scanner.Runtime;

public sealed record PaperScannerSymbolPreparation(
    string Symbol,
    bool HistorySuccess,
    int BarCount,
    string? CachePath,
    string? BacktestMirrorPath,
    string Message,
    IReadOnlyList<string> Warnings)
{
    public static PaperScannerSymbolPreparation FromHistory(HistoricalBarLoadResult result)
        => new(
            result.Symbol,
            result.Success,
            result.BarCount,
            string.IsNullOrWhiteSpace(result.CachePath) ? null : result.CachePath,
            string.IsNullOrWhiteSpace(result.BacktestMirrorPath) ? null : result.BacktestMirrorPath,
            result.Message,
            result.Warnings);

    public string ToDisplayString()
        => $"{Symbol,-6} history={(HistorySuccess ? "OK" : "NOK")} bars={BarCount,5} cache={CachePath ?? "n/a"}";
}
