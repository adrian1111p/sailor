namespace Sailor.App.Scanner.Universe;

public interface ISymbolUniverseProvider
{
    string SourceDescription { get; }

    Task<IReadOnlyList<string>> LoadSymbolsAsync(CancellationToken cancellationToken);
}
