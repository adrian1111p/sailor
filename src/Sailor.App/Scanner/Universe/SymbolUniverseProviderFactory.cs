namespace Sailor.App.Scanner.Universe;

public static class SymbolUniverseProviderFactory
{
    public static ISymbolUniverseProvider Create(
        string universeNameOrPathOrCsv,
        IReadOnlyList<string>? availableSymbols = null)
    {
        string value = string.IsNullOrWhiteSpace(universeNameOrPathOrCsv)
            ? "smallcaps"
            : universeNameOrPathOrCsv.Trim();

        if (LooksLikeFilePath(value) && File.Exists(value))
        {
            return new FileUniverseProvider(value);
        }

        return new BuiltInUniverseProvider(value, availableSymbols);
    }

    private static bool LooksLikeFilePath(string value)
        => value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
           value.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
           value.Contains(Path.DirectorySeparatorChar) ||
           value.Contains(Path.AltDirectorySeparatorChar) ||
           value.Contains(':');
}
