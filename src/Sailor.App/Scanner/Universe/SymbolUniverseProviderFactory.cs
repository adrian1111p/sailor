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

        if (TryParseXlsxUniverse(value, out string? xlsxPath, out string? sheetName, out string? symbolColumn) && File.Exists(xlsxPath))
        {
            return new XlsxUniverseProvider(xlsxPath, sheetName, symbolColumn);
        }

        if (LooksLikeFilePath(value) && File.Exists(value))
        {
            return new FileUniverseProvider(value);
        }

        return new BuiltInUniverseProvider(value, availableSymbols);
    }

    public static string BuildXlsxUniverseArgument(string filePath, string? sheetName = null, string? symbolColumn = null)
    {
        string value = filePath.Trim();
        var parts = new List<string> { value };
        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            parts.Add(sheetName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(symbolColumn))
        {
            parts.Add(symbolColumn.Trim());
        }

        return string.Join("#", parts);
    }

    private static bool LooksLikeFilePath(string value)
        => value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
           value.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
           value.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
           value.Contains(Path.DirectorySeparatorChar) ||
           value.Contains(Path.AltDirectorySeparatorChar) ||
           value.Contains(':');

    private static bool TryParseXlsxUniverse(
        string value,
        out string path,
        out string? sheetName,
        out string? symbolColumn)
    {
        path = value;
        sheetName = null;
        symbolColumn = null;

        string[] hashParts = value.Split('#', StringSplitOptions.TrimEntries);
        if (hashParts.Length > 1)
        {
            path = hashParts[0];
            sheetName = hashParts.Length > 1 && !string.IsNullOrWhiteSpace(hashParts[1]) ? hashParts[1] : null;
            symbolColumn = hashParts.Length > 2 && !string.IsNullOrWhiteSpace(hashParts[2]) ? hashParts[2] : null;
        }

        return path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
    }
}
