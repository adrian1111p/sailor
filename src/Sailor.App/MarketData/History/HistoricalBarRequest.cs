using Sailor.App.Runtime.Common;

namespace Sailor.App.MarketData.History;

public sealed record HistoricalBarRequest(
    SailorRuntimeMode Mode,
    string Symbol,
    string Timeframe,
    DateTimeOffset EndTimeUtc,
    TimeSpan Lookback,
    string DurationString,
    string BarSizeSetting,
    string WhatToShow,
    bool UseRegularTradingHours,
    int RequestId,
    string PrimaryExchange,
    bool MirrorToBacktestData)
{
    public static HistoricalBarRequest CreateOneMinute(
        SailorRuntimeMode mode,
        string symbol,
        TimeSpan lookback,
        int requestId,
        bool useRegularTradingHours,
        string primaryExchange,
        bool mirrorToBacktestData)
    {
        string normalizedSymbol = NormalizeSymbol(symbol);
        TimeSpan effectiveLookback = lookback <= TimeSpan.Zero
            ? TimeSpan.FromDays(5)
            : lookback;

        return new HistoricalBarRequest(
            mode,
            normalizedSymbol,
            "1m",
            DateTimeOffset.UtcNow,
            effectiveLookback,
            ToIbkrDuration(effectiveLookback),
            "1 min",
            "TRADES",
            useRegularTradingHours,
            requestId,
            string.IsNullOrWhiteSpace(primaryExchange) ? "NASDAQ" : primaryExchange.Trim().ToUpperInvariant(),
            mirrorToBacktestData);
    }

    public string ModeName => Mode.ToDisplayName();

    public string ToDisplayString()
        => $"mode={ModeName} symbol={Symbol} timeframe={Timeframe} duration={DurationString} barSize='{BarSizeSetting}' what={WhatToShow} useRth={UseRegularTradingHours} reqId={RequestId} primaryExchange={PrimaryExchange} mirrorBacktest={MirrorToBacktestData}";

    public static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));
        }

        return symbol.Trim().ToUpperInvariant();
    }

    private static string ToIbkrDuration(TimeSpan lookback)
    {
        if (lookback.TotalDays >= 1)
        {
            int days = Math.Max(1, (int)Math.Ceiling(lookback.TotalDays));
            return $"{days} D";
        }

        if (lookback.TotalHours >= 1)
        {
            int seconds = Math.Max(60, (int)Math.Ceiling(lookback.TotalSeconds));
            return $"{seconds} S";
        }

        return "1 D";
    }
}
