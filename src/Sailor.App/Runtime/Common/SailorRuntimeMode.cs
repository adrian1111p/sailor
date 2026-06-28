namespace Sailor.App.Runtime.Common;

public enum SailorRuntimeMode
{
    Backtest = 0,
    Paper = 1,
    Live = 2
}

public static class SailorRuntimeModeExtensions
{
    public static string ToDisplayName(this SailorRuntimeMode mode)
    {
        return mode switch
        {
            SailorRuntimeMode.Backtest => "backtest",
            SailorRuntimeMode.Paper => "paper",
            SailorRuntimeMode.Live => "live",
            _ => mode.ToString().ToLowerInvariant()
        };
    }
}
