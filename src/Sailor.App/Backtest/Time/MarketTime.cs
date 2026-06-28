namespace Sailor.App.Backtest;

public static class MarketTime
{
    private static readonly Lazy<TimeZoneInfo?> EasternTimeZone = new(ResolveEasternTimeZone);

    public static int GetEasternMinuteOfDay(DateTimeOffset timestamp)
    {
        DateTimeOffset eastern = ToEastern(timestamp);

        return eastern.Hour * 60 + eastern.Minute;
    }

    public static DateOnly GetEasternDate(DateTimeOffset timestamp)
    {
        DateTimeOffset eastern = ToEastern(timestamp);

        return DateOnly.FromDateTime(eastern.Date);
    }

    private static DateTimeOffset ToEastern(DateTimeOffset timestamp)
    {
        return EasternTimeZone.Value is null
            ? timestamp
            : TimeZoneInfo.ConvertTime(timestamp, EasternTimeZone.Value);
    }

    private static TimeZoneInfo? ResolveEasternTimeZone()
    {
        string[] candidateIds =
        [
            "Eastern Standard Time",
            "America/New_York"
        ];

        foreach (string candidateId in candidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidateId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next platform-specific ID.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the next platform-specific ID.
            }
        }

        return null;
    }
}
