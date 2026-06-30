namespace Sailor.App.Runtime.Paper;

public sealed record PaperLiveCandleRefreshResult(
    string Symbol,
    bool Success,
    bool Updated,
    bool Current,
    DateTimeOffset? PreviousFrameTime,
    DateTimeOffset? PreviousLoadedLastTime,
    DateTimeOffset? RefreshedLastTime,
    DateTimeOffset? AppliedFrameTime,
    int RefreshedBarCount,
    int AppliedBarIndex,
    string Message,
    IReadOnlyList<string> Warnings)
{
    public static PaperLiveCandleRefreshResult Skipped(string symbol, string message)
        => new(
            NormalizeSymbol(symbol),
            Success: true,
            Updated: false,
            Current: true,
            PreviousFrameTime: null,
            PreviousLoadedLastTime: null,
            RefreshedLastTime: null,
            AppliedFrameTime: null,
            RefreshedBarCount: 0,
            AppliedBarIndex: -1,
            Message: message,
            Warnings: Array.Empty<string>());

    public static PaperLiveCandleRefreshResult Failed(
        string symbol,
        DateTimeOffset? previousFrameTime,
        DateTimeOffset? previousLoadedLastTime,
        string message,
        IEnumerable<string>? warnings = null)
        => new(
            NormalizeSymbol(symbol),
            Success: false,
            Updated: false,
            Current: false,
            previousFrameTime,
            previousLoadedLastTime,
            RefreshedLastTime: null,
            AppliedFrameTime: previousFrameTime,
            RefreshedBarCount: 0,
            AppliedBarIndex: -1,
            Message: message,
            Warnings: warnings?.ToArray() ?? Array.Empty<string>());

    public string ToDisplayString()
    {
        string previous = PreviousFrameTime?.ToString("O") ?? "n/a";
        string refreshed = RefreshedLastTime?.ToString("O") ?? "n/a";
        string applied = AppliedFrameTime?.ToString("O") ?? "n/a";
        return $"{Symbol}: refresh success={Success} updated={Updated} current={Current} previous={previous} refreshedLast={refreshed} applied={applied} bars={RefreshedBarCount} index={AppliedBarIndex} message={Message}";
    }

    private static string NormalizeSymbol(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim().ToUpperInvariant();
}
