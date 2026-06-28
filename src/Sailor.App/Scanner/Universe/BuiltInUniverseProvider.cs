using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Scanner.Universe;

public sealed class BuiltInUniverseProvider : ISymbolUniverseProvider
{
    private readonly string _universeNameOrCsv;
    private readonly IReadOnlyList<string> _availableSymbols;

    public BuiltInUniverseProvider(
        string universeNameOrCsv,
        IReadOnlyList<string>? availableSymbols = null)
    {
        _universeNameOrCsv = string.IsNullOrWhiteSpace(universeNameOrCsv)
            ? "smallcaps"
            : universeNameOrCsv.Trim();
        _availableSymbols = availableSymbols ?? Array.Empty<string>();
    }

    public string SourceDescription => $"built-in/list universe '{_universeNameOrCsv}'";

    public Task<IReadOnlyList<string>> LoadSymbolsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> symbols = SailorSymbolUniverses.Resolve(_universeNameOrCsv, _availableSymbols);
        return Task.FromResult(Normalize(symbols));
    }

    internal static IReadOnlyList<string> Normalize(IEnumerable<string> symbols)
        => symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
