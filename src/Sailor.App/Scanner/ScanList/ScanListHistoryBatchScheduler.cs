namespace Sailor.App.Scanner.ScanList;

public sealed class ScanListHistoryBatchScheduler
{
    private readonly HashSet<int> _requestedBatchNumbers = new();
    private readonly Dictionary<string, DateTimeOffset> _failedSymbolBackoff = new(StringComparer.OrdinalIgnoreCase);

    public int RequestedBatchCount => _requestedBatchNumbers.Count;

    public int FailedSymbolCount => _failedSymbolBackoff.Count;

    public static IReadOnlyList<ScanListHistoryBatch> Plan(
        IReadOnlyList<string> symbols,
        int historyBatchSize,
        int historyBatchIntervalMinutes,
        DateTimeOffset? startUtc = null)
    {
        DateTimeOffset start = startUtc ?? DateTimeOffset.UtcNow;
        int safeBatchSize = Math.Max(1, historyBatchSize);
        int safeInterval = Math.Max(1, historyBatchIntervalMinutes);
        var batches = new List<ScanListHistoryBatch>();

        string[] normalized = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (int index = 0; index < normalized.Length; index += safeBatchSize)
        {
            int batchIndex = index / safeBatchSize;
            string[] batchSymbols = normalized.Skip(index).Take(safeBatchSize).ToArray();
            batches.Add(new ScanListHistoryBatch(
                batchIndex + 1,
                start.AddMinutes(batchIndex * safeInterval),
                batchSymbols));
        }

        return batches;
    }

    public ScanListHistoryBatch? SelectDueBatch(
        IReadOnlyList<ScanListHistoryBatch> batches,
        DateTimeOffset observedUtc)
    {
        foreach (ScanListHistoryBatch batch in batches.OrderBy(batch => batch.BatchNumber))
        {
            if (_requestedBatchNumbers.Contains(batch.BatchNumber))
            {
                continue;
            }

            if (batch.NotBeforeUtc > observedUtc)
            {
                continue;
            }

            string[] eligibleSymbols = batch.Symbols
                .Where(symbol => !_failedSymbolBackoff.TryGetValue(symbol, out DateTimeOffset retryAfterUtc) || retryAfterUtc <= observedUtc)
                .ToArray();

            if (eligibleSymbols.Length == 0)
            {
                continue;
            }

            return eligibleSymbols.Length == batch.Symbols.Count
                ? batch
                : batch with { Symbols = eligibleSymbols };
        }

        return null;
    }

    public void MarkBatchRequested(ScanListHistoryBatch? batch)
    {
        if (batch is not null)
        {
            _requestedBatchNumbers.Add(batch.BatchNumber);
        }
    }

    public void MarkSymbolSucceeded(string symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            _failedSymbolBackoff.Remove(symbol.Trim().ToUpperInvariant());
        }
    }

    public void MarkSymbolFailed(string symbol, DateTimeOffset observedUtc, int retryBackoffMinutes = 10)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        _failedSymbolBackoff[symbol.Trim().ToUpperInvariant()] = observedUtc.AddMinutes(Math.Max(1, retryBackoffMinutes));
    }
}
