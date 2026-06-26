namespace Sailor.App.Backtest.Models;

public enum BacktestSideMode
{
    LongOnly = 0,
    ShortOnly = 1,
    LongAndShort = 2
}

public static class BacktestSideModeExtensions
{
    public static bool AllowsLong(this BacktestSideMode sideMode)
    {
        return sideMode is BacktestSideMode.LongOnly or BacktestSideMode.LongAndShort;
    }

    public static bool AllowsShort(this BacktestSideMode sideMode)
    {
        return sideMode is BacktestSideMode.ShortOnly or BacktestSideMode.LongAndShort;
    }

    public static BacktestSideMode ParseOrDefault(string? value, BacktestSideMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();

        return normalized switch
        {
            "long" or "longonly" or "longs" => BacktestSideMode.LongOnly,
            "short" or "shortonly" or "shorts" => BacktestSideMode.ShortOnly,
            "both" or "longshort" or "longandshort" or "longsandshorts" or "longshorts" => BacktestSideMode.LongAndShort,
            _ => fallback
        };
    }
}
