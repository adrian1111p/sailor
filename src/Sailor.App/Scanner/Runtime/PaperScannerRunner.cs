using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Broker.Ibkr;
using Sailor.App.Configuration;
using Sailor.App.MarketData.History;
using Sailor.App.MarketData.Live;
using Sailor.App.Scanner.Universe;

namespace Sailor.App.Scanner.Runtime;

public sealed class PaperScannerRunner : IDisposable
{
    private readonly SailorAppSettings _settings;
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly PaperScannerSnapshotProvider _snapshotProvider;

    public PaperScannerRunner(
        SailorAppSettings settings,
        IbkrConnectionOptions connectionOptions,
        PaperScannerOptions options)
    {
        _settings = settings;
        _connectionOptions = connectionOptions;

        IHistoricalBarProvider historyProvider = HistoricalBarProviderFactory.Create(
            options.RequestIbkrHistory,
            _connectionOptions);

        ILiveMarketDataSnapshotProvider marketDataProvider = LiveMarketDataSnapshotProviderFactory.Create(
            options.RequestIbkrMarketData,
            _connectionOptions);

        _snapshotProvider = new PaperScannerSnapshotProvider(historyProvider, marketDataProvider);
    }

    public string HistoryProviderName => _snapshotProvider.HistoryProviderName;

    public string MarketDataProviderName => _snapshotProvider.MarketDataProviderName;

    public async Task<PaperScannerRunResult> RunAsync(
        PaperScannerOptions options,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var csvProvider = new CsvBacktestDataProvider();
        IReadOnlyList<string> availableSymbols = csvProvider.ListSymbols();
        ISymbolUniverseProvider universeProvider = SymbolUniverseProviderFactory.Create(options.Universe, availableSymbols);
        IReadOnlyList<string> resolvedSymbols = await universeProvider.LoadSymbolsAsync(cancellationToken);

        if (resolvedSymbols.Count == 0)
        {
            warnings.Add($"Universe provider returned no symbols: {universeProvider.SourceDescription}");
            return new PaperScannerRunResult(options, resolvedSymbols, Array.Empty<string>(), Array.Empty<PaperScannerSymbolPreparation>(), Array.Empty<PaperScannerCandidate>(), null, warnings);
        }

        int maxSymbols = options.MaxSymbolsToPrepare <= 0
            ? resolvedSymbols.Count
            : Math.Min(options.MaxSymbolsToPrepare, resolvedSymbols.Count);

        IReadOnlyList<string> symbolsToPrepare = resolvedSymbols
            .Take(maxSymbols)
            .ToArray();

        if (symbolsToPrepare.Count < resolvedSymbols.Count)
        {
            warnings.Add($"Prepared first {symbolsToPrepare.Count} of {resolvedSymbols.Count} resolved symbols. Use --max-symbols {resolvedSymbols.Count} to scan the full universe.");
        }

        var preparations = new List<PaperScannerSymbolPreparation>();
        int historyRequestId = 27_000;
        foreach (string symbol in symbolsToPrepare)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PaperScannerSymbolPreparation preparation = await _snapshotProvider.PrepareHistoryAsync(
                options,
                symbol,
                historyRequestId++,
                cancellationToken);

            preparations.Add(preparation);
            foreach (string warning in preparation.Warnings)
            {
                warnings.Add($"{symbol}: {warning}");
            }
        }

        IReadOnlyList<string> preparedSymbols = preparations
            .Where(row => row.HistorySuccess)
            .Select(row => row.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (preparedSymbols.Count == 0)
        {
            warnings.Add("No symbol has usable 1m history, so the scanner was not run.");
            return new PaperScannerRunResult(options, resolvedSymbols, preparedSymbols, preparations, Array.Empty<PaperScannerCandidate>(), null, warnings);
        }

        SailorStrategyProfile profile = SailorStrategyProfile.FromName(options.ProfileName, _settings);
        var scanner = new SailorScanner(csvProvider);
        IReadOnlyList<ScannerCandidate> scannerCandidates = scanner.Scan(
            options.Timeframe,
            profile,
            options.TopCount,
            preparedSymbols);

        var candidates = new List<PaperScannerCandidate>();
        int snapshotRequestId = 27_500;
        for (int i = 0; i < scannerCandidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScannerCandidate candidate = scannerCandidates[i];
            var snapshot = await _snapshotProvider.CaptureSnapshotAsync(
                options,
                candidate.Symbol,
                snapshotRequestId++,
                cancellationToken);

            foreach (string warning in snapshot.Warnings)
            {
                warnings.Add($"{candidate.Symbol}: {warning}");
            }

            candidates.Add(new PaperScannerCandidate(
                Rank: i + 1,
                Candidate: candidate,
                Snapshot: snapshot.Snapshot,
                SnapshotMessage: snapshot.Message,
                SnapshotWarnings: snapshot.Warnings));
        }

        string? reportPath = null;
        var result = new PaperScannerRunResult(
            options,
            resolvedSymbols,
            preparedSymbols,
            preparations,
            candidates,
            CandidateReportPath: null,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        if (candidates.Count > 0)
        {
            reportPath = PaperScannerReportWriter.WriteCandidates(result);
            result = result with { CandidateReportPath = reportPath };
        }

        return result;
    }

    public void Dispose() => _snapshotProvider.Dispose();
}
