using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Scanner;
using Sailor.App.Backtest.Scanner.Points;
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
        ScannerCandidateSelection scannerSelection = CreateScannerCandidates(
            options,
            csvProvider,
            profile,
            preparedSymbols,
            warnings);
        IReadOnlyList<(ScannerCandidate Candidate, PointsScannerCandidate? PointsCandidate)> scannerCandidates = scannerSelection.Candidates;

        var candidates = new List<PaperScannerCandidate>();
        int snapshotRequestId = 27_500;
        for (int i = 0; i < scannerCandidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (ScannerCandidate candidate, PointsScannerCandidate? pointsCandidate) = scannerCandidates[i];
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
                SnapshotWarnings: snapshot.Warnings,
                PointsCandidate: pointsCandidate,
                Mode: options.ScannerMode));
        }

        string? reportPath = null;
        var result = new PaperScannerRunResult(
            options,
            resolvedSymbols,
            preparedSymbols,
            preparations,
            candidates,
            CandidateReportPath: null,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            scannerSelection.HybridComparisonReportPath);

        if (candidates.Count > 0)
        {
            reportPath = PaperScannerReportWriter.WriteCandidates(result);
            result = result with { CandidateReportPath = reportPath };
        }

        return result;
    }


    private static ScannerCandidateSelection CreateScannerCandidates(
        PaperScannerOptions options,
        CsvBacktestDataProvider csvProvider,
        SailorStrategyProfile profile,
        IReadOnlyList<string> preparedSymbols,
        List<string> warnings)
    {
        var legacyScanner = new SailorScanner(csvProvider);
        if (options.ScannerMode == PointsScannerMode.PointsOnly)
        {
            var pointsScanner = new PointsScanner(csvProvider);
            return new ScannerCandidateSelection(
                pointsScanner.Scan(
                        options.Timeframe,
                        profile,
                        options.TopCount,
                        preparedSymbols)
                    .Select(candidate => (candidate.ToScannerCandidate(), (PointsScannerCandidate?)candidate))
                    .ToArray(),
                HybridComparisonReportPath: null);
        }

        if (options.ScannerMode == PointsScannerMode.HybridCompare)
        {
            var pointsScanner = new PointsScanner(csvProvider);
            IReadOnlyList<ScannerCandidate> legacyCandidates = legacyScanner.Scan(
                options.Timeframe,
                profile,
                options.TopCount,
                preparedSymbols);
            IReadOnlyList<PointsScannerCandidate> pointsCandidates = pointsScanner.Scan(
                options.Timeframe,
                profile,
                options.TopCount,
                preparedSymbols);
            string reportPath = PaperScannerHybridComparisonReportWriter.Write(options, legacyCandidates, pointsCandidates);
            warnings.Add($"scanner-mode=hybrid-compare wrote points-vs-legacy report: {reportPath}");
            warnings.Add("scanner-mode=hybrid-compare routes selected symbols from legacy-blocks; points-only trading remains opt-in.");
            return new ScannerCandidateSelection(
                legacyCandidates.Select(candidate =>
                {
                    PointsScannerCandidate? points = pointsCandidates.FirstOrDefault(row => row.Symbol.Equals(candidate.Symbol, StringComparison.OrdinalIgnoreCase));
                    return (candidate, points);
                }).ToArray(),
                reportPath);
        }

        return new ScannerCandidateSelection(
            legacyScanner.Scan(
                    options.Timeframe,
                    profile,
                    options.TopCount,
                    preparedSymbols)
                .Select(candidate => (candidate, (PointsScannerCandidate?)null))
                .ToArray(),
            HybridComparisonReportPath: null);
    }

    private sealed record ScannerCandidateSelection(
        IReadOnlyList<(ScannerCandidate Candidate, PointsScannerCandidate? PointsCandidate)> Candidates,
        string? HybridComparisonReportPath);

    public void Dispose() => _snapshotProvider.Dispose();
}
