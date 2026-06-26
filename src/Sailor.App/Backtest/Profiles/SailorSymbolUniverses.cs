namespace Sailor.App.Backtest.Profiles;

public static class SailorSymbolUniverses
{
    private static readonly string[] SmallCaps =
    [
        "ALIT", "BARK", "BIAF", "BKKT", "BTBT", "BTO", "BYRN", "CDXS", "CHPT", "CIFR",
        "CLOV", "CLSK", "DOUG", "DVLT", "EONR", "EVGO", "F", "GRAB", "HIMS", "HOOD",
        "IMMP", "IMUX", "IONQ", "IXHL", "JOBY", "KLC", "LCID", "MARA", "MVST", "NIO",
        "NTSK", "OPEN", "ORBS", "PATH", "PLTK", "PLUG", "SERV", "SKLZ", "SNDL", "SOFI",
        "SOS", "SRFM", "TLYS", "TMDE", "TPET", "WKHS", "AFRM", "DKNG", "PLTR", "SNAP",
        "UPST", "ZETA", "AIXI", "BMNU", "CAST", "CMPX", "ELPW", "LASE", "ONCY", "REAX",
        "SGMT", "SNOA", "TZA", "UMC", "USEG", "VZ", "XE"
    ];

    public static IReadOnlyList<string> Resolve(string? universeNameOrCsv, IReadOnlyList<string> availableSymbols)
    {
        if (string.IsNullOrWhiteSpace(universeNameOrCsv) ||
            universeNameOrCsv.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return availableSymbols;
        }

        if (universeNameOrCsv.Equals("smallcaps", StringComparison.OrdinalIgnoreCase) ||
            universeNameOrCsv.Equals("small-caps", StringComparison.OrdinalIgnoreCase) ||
            universeNameOrCsv.Equals("small_cap", StringComparison.OrdinalIgnoreCase) ||
            universeNameOrCsv.Equals("small-caps-user-list", StringComparison.OrdinalIgnoreCase))
        {
            return SmallCaps;
        }

        return universeNameOrCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> SmallCapSymbols => SmallCaps;
}
