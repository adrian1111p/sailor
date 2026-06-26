namespace Sailor.App.Backtest.Conduct;

public sealed record SailorConductExitDecision(
    bool ShouldExit,
    decimal ExitPrice,
    string Reason)
{
    public static SailorConductExitDecision Hold(string reason) => new(false, 0m, reason);

    public static SailorConductExitDecision Exit(decimal exitPrice, string reason) => new(true, exitPrice, reason);
}
