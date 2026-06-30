using Sailor.App.Backtest;

namespace Sailor.App.Runtime.Paper;

public sealed record PaperLiveBarCurrentness(
    bool IsCurrent,
    DateTimeOffset FrameTime,
    DateTimeOffset ObservedUtc,
    int AgeMinutes,
    DateOnly FrameEasternDate,
    DateOnly CurrentEasternDate,
    string Reason)
{
    public static PaperLiveBarCurrentness Current(
        DateTimeOffset frameTime,
        DateTimeOffset observedUtc,
        int ageMinutes)
        => new(
            true,
            frameTime,
            observedUtc,
            ageMinutes,
            MarketTime.GetEasternDate(frameTime),
            MarketTime.GetEasternDate(observedUtc),
            $"current live-paper candle age={ageMinutes}m");

    public static PaperLiveBarCurrentness Stale(
        DateTimeOffset frameTime,
        DateTimeOffset observedUtc,
        int ageMinutes,
        string reason)
        => new(
            false,
            frameTime,
            observedUtc,
            ageMinutes,
            MarketTime.GetEasternDate(frameTime),
            MarketTime.GetEasternDate(observedUtc),
            reason);


    public static PaperLiveBarCurrentness Evaluate(
        DateTimeOffset frameTime,
        DateTimeOffset observedUtc,
        int maxAgeMinutes,
        int futureToleranceMinutes)
    {
        int ageMinutes = (int)Math.Round((observedUtc - frameTime.ToUniversalTime()).TotalMinutes, MidpointRounding.AwayFromZero);
        DateOnly frameDate = MarketTime.GetEasternDate(frameTime);
        DateOnly currentDate = MarketTime.GetEasternDate(observedUtc);

        if (frameDate != currentDate)
        {
            return Stale(
                frameTime,
                observedUtc,
                ageMinutes,
                "frame belongs to a different Eastern trading date than the current runtime clock");
        }

        if (ageMinutes > Math.Max(1, maxAgeMinutes))
        {
            return Stale(
                frameTime,
                observedUtc,
                ageMinutes,
                "frame is older than the configured live-paper max age");
        }

        if (ageMinutes < -Math.Max(0, futureToleranceMinutes))
        {
            return Stale(
                frameTime,
                observedUtc,
                ageMinutes,
                "frame is ahead of the runtime clock beyond the configured future tolerance");
        }

        return Current(frameTime, observedUtc, ageMinutes);
    }

    public string ToEntryBlockReason(int maxAgeMinutes)
        => $"SAILOR-058 live-paper current-candle gate blocked stale historical replay: " +
           $"frameTime={FrameTime:O} observedUtc={ObservedUtc:O} ageMinutes={AgeMinutes} " +
           $"maxAgeMinutes={maxAgeMinutes} frameEtDate={FrameEasternDate:yyyy-MM-dd} " +
           $"currentEtDate={CurrentEasternDate:yyyy-MM-dd}. {Reason}";
}
