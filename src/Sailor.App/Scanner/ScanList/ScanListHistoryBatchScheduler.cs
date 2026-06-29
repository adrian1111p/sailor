namespace Sailor.App.Scanner.ScanList;

public sealed class ScanListHistoryBatchScheduler
{
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
}
