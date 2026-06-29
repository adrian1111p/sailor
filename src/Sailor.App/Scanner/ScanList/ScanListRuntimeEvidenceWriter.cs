using System.Globalization;
using System.Text;
using System.Text.Json;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Scanner.ScanList;

public static class ScanListRuntimeEvidenceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static (string JsonPath, string CsvPath) Write(ScanListRuntimeEvidence evidence)
    {
        string root = Path.Combine(
            evidence.Mode.Equals("live", StringComparison.OrdinalIgnoreCase) ? SailorLogPaths.Live : SailorLogPaths.Paper,
            "ScanList");
        Directory.CreateDirectory(root);

        string latestJson = Path.Combine(root, "scanlist_latest.json");
        string datedCsv = Path.Combine(root, $"scanlist_{DateTime.Now:yyyyMMdd}.csv");

        File.WriteAllText(latestJson, JsonSerializer.Serialize(evidence, JsonOptions));

        bool writeHeader = !File.Exists(datedCsv);
        using var writer = new StreamWriter(datedCsv, append: true, Encoding.UTF8);
        if (writeHeader)
        {
            writer.WriteLine("observedUtc,evidenceId,mode,file,sheet,symbolColumn,cycleIndex,totalCycles,refreshSeconds,tradeTop,historyBatchSize,historyBatchIntervalMinutes,workbookSymbols,activeSymbols,addedSymbols,removedSymbols,retainedRemovedSymbols,tradeEligibleSymbols,historyBatches,dueHistoryBatch,dueHistorySymbols,preparedSymbols,historySuccessCount,memoryCandleSymbols,memoryCandles,mergedSymbols,mergedCandles,safetyMode,safetyReason");
        }

        writer.WriteLine(string.Join(',',
            Csv(evidence.ObservedUtc.ToString("O", CultureInfo.InvariantCulture)),
            Csv(evidence.EvidenceId),
            Csv(evidence.Mode),
            Csv(evidence.File),
            Csv(evidence.Sheet),
            Csv(evidence.SymbolColumn),
            evidence.CycleIndex.ToString(CultureInfo.InvariantCulture),
            evidence.TotalCycles.ToString(CultureInfo.InvariantCulture),
            evidence.RefreshSeconds.ToString(CultureInfo.InvariantCulture),
            evidence.TradeTop.ToString(CultureInfo.InvariantCulture),
            evidence.HistoryBatchSize.ToString(CultureInfo.InvariantCulture),
            evidence.HistoryBatchIntervalMinutes.ToString(CultureInfo.InvariantCulture),
            evidence.WorkbookSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.ActiveSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.AddedSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.RemovedSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.RetainedRemovedSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.TradeEligibleSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.HistoryBatches.ToString(CultureInfo.InvariantCulture),
            evidence.DueHistoryBatch.ToString(CultureInfo.InvariantCulture),
            evidence.DueHistorySymbols.ToString(CultureInfo.InvariantCulture),
            evidence.PreparedSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.HistorySuccessCount.ToString(CultureInfo.InvariantCulture),
            evidence.MemoryCandleSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.MemoryCandles.ToString(CultureInfo.InvariantCulture),
            evidence.MergedSymbols.ToString(CultureInfo.InvariantCulture),
            evidence.MergedCandles.ToString(CultureInfo.InvariantCulture),
            Csv(evidence.SafetyMode),
            Csv(evidence.SafetyReason)));

        return (latestJson, datedCsv);
    }

    public static ScanListRuntimeEvidence Create(
        SailorRuntimeMode mode,
        ScanListWorkbookOptions options,
        ScanListWorkbookResult workbook,
        ScanListReloadResult reload,
        IReadOnlyList<string> tradeEligibleSymbols,
        IReadOnlyList<ScanListHistoryBatch> batches,
        RuntimeSafetyState safetyState,
        IReadOnlyList<string> warnings,
        int cycleIndex = 1,
        int totalCycles = 1,
        ScanListHistoryBatch? dueHistoryBatch = null,
        int preparedSymbols = 0,
        int historySuccessCount = 0,
        int memoryCandleSymbols = 0,
        int memoryCandles = 0,
        int mergedSymbols = 0,
        int mergedCandles = 0)
    {
        string id = $"SLR-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..31];
        return new ScanListRuntimeEvidence(
            id,
            mode.ToDisplayName(),
            options.FilePath,
            options.SheetName,
            options.SymbolColumn,
            DateTimeOffset.UtcNow,
            options.RefreshSeconds,
            options.TradeTop,
            options.HistoryBatchSize,
            options.HistoryBatchIntervalMinutes,
            workbook.SymbolCount,
            reload.ActiveSymbols.Count,
            reload.AddedSymbols.Count,
            reload.RemovedSymbols.Count,
            reload.RetainedRemovedSymbols.Count,
            tradeEligibleSymbols.Count,
            batches.Count,
            safetyState.Mode.ToString(),
            safetyState.Reason,
            tradeEligibleSymbols.Take(20).ToArray(),
            reload.AddedSymbols.Take(20).ToArray(),
            reload.RemovedSymbols.Take(20).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cycleIndex,
            totalCycles,
            dueHistoryBatch?.BatchNumber ?? 0,
            dueHistoryBatch?.Symbols.Count ?? 0,
            preparedSymbols,
            historySuccessCount,
            memoryCandleSymbols,
            memoryCandles,
            mergedSymbols,
            mergedCandles);
    }

    private static string Csv(string? value)
    {
        string safe = value ?? string.Empty;
        if (safe.Contains(',') || safe.Contains('"') || safe.Contains('\n') || safe.Contains('\r'))
        {
            return $"\"{safe.Replace("\"", "\"\"")}\"";
        }

        return safe;
    }
}
