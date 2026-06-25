namespace Sailor.App.Backtest;

/// <summary>
/// Shared Eastern-Time conversion utilities used across all backtest strategies.
/// Resolves the timezone on both Windows ("Eastern Standard Time") and Linux ("America/New_York").
/// </summary>
internal static class TradingTime
{
    private static readonly TimeZoneInfo _et = ResolveEasternTimeZone();

    private static bool _fallbackWarningEmitted;

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch { /* ignore */ }
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { /* ignore */ }
        if (!_fallbackWarningEmitted)
        {
            Console.Error.WriteLine("WARNING: Could not resolve Eastern timezone. All ET conversions will use UTC. Trading time windows and VWAP resets will be incorrect.");
            _fallbackWarningEmitted = true;
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>Convert a timestamp to Eastern Time.</summary>
    public static DateTime ToEt(DateTime ts) => ts.Kind switch
    {
        DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(ts, _et),
        DateTimeKind.Local => TimeZoneInfo.ConvertTime(ts, _et),
        _ => ts // Unspecified â†’ assume already ET
    };

    /// <summary>Get the minute-of-day (0â€“1439) in Eastern Time.</summary>
    public static int GetMinuteOfDayEt(DateTime ts)
    {
        var et = ToEt(ts);
        return et.Hour * 60 + et.Minute;
    }

    /// <summary>Get the trading date in Eastern Time.</summary>
    public static DateOnly GetDateEt(DateTime ts) => DateOnly.FromDateTime(ToEt(ts));
}

