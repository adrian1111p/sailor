namespace Sailor.App.Scanner.Universe;

public sealed class FileUniverseProvider : ISymbolUniverseProvider
{
    private readonly string _path;

    public FileUniverseProvider(string path)
    {
        _path = path;
    }

    public string SourceDescription => $"file universe '{_path}'";

    public async Task<IReadOnlyList<string>> LoadSymbolsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException($"Universe file was not found: {_path}", _path);
        }

        string[] lines = await File.ReadAllLinesAsync(_path, cancellationToken);
        var symbols = new List<string>();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            foreach (string token in trimmed.Split([',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string symbol = ExtractSymbol(token);
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    symbols.Add(symbol);
                }
            }
        }

        return BuiltInUniverseProvider.Normalize(symbols);
    }

    private static string ExtractSymbol(string token)
    {
        string value = token.Trim().Trim('"', '\'').ToUpperInvariant();
        if (value.Equals("SYMBOL", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TICKER", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        int pipe = value.IndexOf('|', StringComparison.Ordinal);
        if (pipe > 0)
        {
            value = value[..pipe];
        }

        return new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-').ToArray());
    }
}
