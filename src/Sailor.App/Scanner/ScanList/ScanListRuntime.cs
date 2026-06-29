using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Models;
using Sailor.App.Broker.Ibkr;
using Sailor.App.Configuration;
using Sailor.App.Runtime.Common;
using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Scanner.ScanList;

public sealed class ScanListRuntime : IDisposable
{
    private readonly SailorAppSettings _settings;
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly ScanListWorkbookReader _reader = new();
    private readonly ScanListMemoryStore _memoryStore = new();
    private readonly ScanListCandleAccumulator _candleAccumulator = new();
    private readonly ScanListCandleMergeEngine _mergeEngine = new();
    private readonly ScanListHistoryBatchScheduler _historyScheduler = new();
    private readonly List<string> _warnings = new();
    private bool _disposed;

    public ScanListRuntime(
        SailorAppSettings settings,
        IbkrConnectionOptions connectionOptions)
    {
        _settings = settings;
        _connectionOptions = connectionOptions;
    }

    public async Task<ScanListRunResult> RunAsync(
        ScanListRunRequest request,
        CancellationToken cancellationToken)
    {
        var cycleResults = new List<ScanListCycleResult>();
        string historyProviderName = "n/a";
        string marketDataProviderName = "n/a";
        DateTimeOffset runtimeStartUtc = DateTimeOffset.UtcNow;

        using var scannerRunner = new PaperScannerRunner(_settings, _connectionOptions, request.ScannerOptions);
        historyProviderName = scannerRunner.HistoryProviderName;
        marketDataProviderName = scannerRunner.MarketDataProviderName;

        for (int cycle = 1; cycle <= request.SafeCycles; cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset cycleObservedUtc = runtimeStartUtc.AddSeconds((cycle - 1) * request.SafeScanRefreshSeconds);
            ScanListCycleResult cycleResult = await RunCycleAsync(
                request,
                scannerRunner,
                cycle,
                request.SafeCycles,
                cycleObservedUtc,
                cancellationToken).ConfigureAwait(false);
            cycleResults.Add(cycleResult);

            foreach (string warning in cycleResult.Warnings)
            {
                if (!_warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
                {
                    _warnings.Add(warning);
                }
            }

            if (cycle < request.SafeCycles && request.WaitBetweenCycles)
            {
                await Task.Delay(TimeSpan.FromSeconds(request.SafeScanRefreshSeconds), cancellationToken).ConfigureAwait(false);
            }
        }

        return new ScanListRunResult(
            historyProviderName,
            marketDataProviderName,
            cycleResults,
            _warnings.ToArray());
    }

    private async Task<ScanListCycleResult> RunCycleAsync(
        ScanListRunRequest request,
        PaperScannerRunner scannerRunner,
        int cycleIndex,
        int totalCycles,
        DateTimeOffset observedUtc,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        ScanListWorkbookResult workbook = _reader.Read(request.WorkbookOptions);
        ScanListReloadResult reload = _memoryStore.ApplyWorkbookSnapshot(workbook, observedUtc: observedUtc);
        IReadOnlyList<ScanListHistoryBatch> plannedBatches = ScanListHistoryBatchScheduler.Plan(
            reload.ActiveSymbols,
            request.WorkbookOptions.HistoryBatchSize,
            request.WorkbookOptions.HistoryBatchIntervalMinutes,
            startUtc: observedUtc);

        ScanListHistoryBatch? dueBatch = _historyScheduler.SelectDueBatch(plannedBatches, observedUtc);
        PaperScannerRunResult scannerResult;
        if (dueBatch is null)
        {
            warnings.Add("No history batch is due in this scan-list cycle. The runtime keeps the last clean workbook snapshot in memory and waits for the next due batch.");
            scannerResult = new PaperScannerRunResult(
                request.ScannerOptions,
                reload.ActiveSymbols,
                Array.Empty<string>(),
                Array.Empty<PaperScannerSymbolPreparation>(),
                Array.Empty<PaperScannerCandidate>(),
                CandidateReportPath: null,
                warnings);
        }
        else
        {
            _historyScheduler.MarkBatchRequested(dueBatch);
            _memoryStore.MarkHistoryRequested(dueBatch.Symbols, observedUtc);

            PaperScannerOptions scannerOptions = request.ScannerOptions with
            {
                Universe = string.Join(',', dueBatch.Symbols),
                MaxSymbolsToPrepare = dueBatch.Symbols.Count
            };
            scannerResult = await scannerRunner.RunAsync(scannerOptions, cancellationToken).ConfigureAwait(false);

            foreach (PaperScannerSymbolPreparation preparation in scannerResult.Preparations)
            {
                _memoryStore.MarkHistoryResult(preparation.Symbol, preparation.HistorySuccess, preparation.BarCount, observedUtc);
                if (preparation.HistorySuccess)
                {
                    _historyScheduler.MarkSymbolSucceeded(preparation.Symbol);
                }
                else
                {
                    _historyScheduler.MarkSymbolFailed(preparation.Symbol, observedUtc, request.WorkbookOptions.HistoryBatchIntervalMinutes);
                }
            }
        }

        _memoryStore.RetainTradeCandidates(scannerResult.Candidates, request.SafeTradeTop, observedUtc);
        IReadOnlyList<string> tradeEligibleSymbols = _memoryStore.TradeEligibleSymbols();

        (int memoryCandleSymbols, int memoryCandles, int mergedSymbols, int mergedCandles) = MergePreparedSymbols(
            scannerResult,
            request.ScannerOptions.Timeframe,
            observedUtc,
            warnings);

        RuntimeSafetyState safetyState = BuildSafetyState(scannerResult, workbook.Warnings.Concat(warnings), dueBatch);
        ScanListRuntimeEvidence evidence = ScanListRuntimeEvidenceWriter.Create(
            request.Mode,
            request.WorkbookOptions,
            workbook,
            reload,
            tradeEligibleSymbols,
            plannedBatches,
            safetyState,
            workbook.Warnings.Concat(scannerResult.Warnings).Concat(warnings).ToArray(),
            cycleIndex,
            totalCycles,
            dueBatch,
            scannerResult.PreparedSymbols.Count,
            scannerResult.HistorySuccessCount,
            memoryCandleSymbols,
            memoryCandles,
            mergedSymbols,
            mergedCandles);
        (string evidenceJson, string evidenceCsv) = ScanListRuntimeEvidenceWriter.Write(evidence);

        return new ScanListCycleResult(
            cycleIndex,
            totalCycles,
            observedUtc,
            workbook,
            reload,
            plannedBatches,
            dueBatch,
            scannerResult,
            tradeEligibleSymbols,
            safetyState,
            evidence,
            evidenceJson,
            evidenceCsv,
            memoryCandleSymbols,
            memoryCandles,
            mergedSymbols,
            mergedCandles,
            workbook.Warnings.Concat(scannerResult.Warnings).Concat(warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private (int MemoryCandleSymbols, int MemoryCandles, int MergedSymbols, int MergedCandles) MergePreparedSymbols(
        PaperScannerRunResult scannerResult,
        string timeframe,
        DateTimeOffset observedUtc,
        List<string> warnings)
    {
        var csvProvider = new CsvBacktestDataProvider();
        int mergedSymbols = 0;
        int mergedCandles = 0;

        foreach (string symbol in scannerResult.PreparedSymbols)
        {
            IReadOnlyList<BacktestBar> historicalBars;
            try
            {
                historicalBars = csvProvider.LoadBars(symbol, timeframe).Bars;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
            {
                warnings.Add($"{symbol}: merge skipped because no local {timeframe} history could be loaded after preparation: {ex.Message}");
                continue;
            }

            IReadOnlyList<ScanListMemoryCandle> realtimeCandles = _candleAccumulator.GetCandles(symbol);
            ScanListCandleMergeResult mergeResult = _mergeEngine.Merge(symbol, historicalBars, realtimeCandles);
            _memoryStore.UpdateCandleCounts(symbol, realtimeCandles.Count, mergeResult.MergedCount, observedUtc);
            mergedSymbols++;
            mergedCandles += mergeResult.MergedCount;
            foreach (string warning in mergeResult.Warnings)
            {
                warnings.Add(warning);
            }
        }

        return (_candleAccumulator.SymbolCount, _candleAccumulator.CandleCount, mergedSymbols, mergedCandles);
    }

    private static RuntimeSafetyState BuildSafetyState(
        PaperScannerRunResult result,
        IEnumerable<string> warnings,
        ScanListHistoryBatch? dueBatch)
    {
        string[] warningArray = warnings
            .Concat(result.Warnings)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        bool historyBatchWasDue = dueBatch is not null;
        bool degraded = warningArray.Any(IsDegradedSignal) || (historyBatchWasDue && result.HistorySuccessCount == 0);
        if (degraded)
        {
            string reason = historyBatchWasDue && result.HistorySuccessCount == 0
                ? "Scan-list runtime has no clean usable 1m history in the due batch yet. Entries stay close-only while workbook memory is retained."
                : "Scan-list runtime detected degraded history/market-data/server state. Entries stay close-only while the last clean list remains in memory.";
            return RuntimeSafetyState.CloseOnly(reason);
        }

        if (!historyBatchWasDue)
        {
            return RuntimeSafetyState.Normal("No history batch is due in this scan-list cycle. The runtime keeps the last clean workbook snapshot and retained top symbols in memory.");
        }

        return RuntimeSafetyState.Normal("Scan-list runtime cycle is clean. Entry eligibility may use the retained top symbols, while exits remain allowed for managed positions.");
    }

    private static bool IsDegradedSignal(string warning)
        => warning.Contains("connection", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("timeout", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("timed out", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("socket", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("disabled", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("not available", StringComparison.OrdinalIgnoreCase)
           || warning.Contains("failed", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
