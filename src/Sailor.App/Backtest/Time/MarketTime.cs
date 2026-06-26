namespace Sailor.App.Backtest;

public static class MarketTime
{
    private static readonly Lazy<TimeZoneInfo?> EasternTimeZone = new(ResolveEasternTimeZone);

    public static int GetEasternMinuteOfDay(DateTimeOffset timestamp)
    {
        DateTimeOffset eastern = EasternTimeZone.Value is null
            ? timestamp
            : TimeZoneInfo.ConvertTime(timestamp, EasternTimeZone.Value);

        return eastern.Hour * 60 + eastern.Minute;
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
