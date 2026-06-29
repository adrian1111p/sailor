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
            writer.WriteLine("observedUtc,evidenceId,mode,file,sheet,symbolColumn,refreshSeconds,tradeTop,historyBatchSize,historyBatchIntervalMinutes,workbookSymbols,activeSymbols,addedSymbols,removedSymbols,retainedRemovedSymbols,tradeEligibleSymbols,historyBatches,safetyMode,safetyReason");
        }

        writer.WriteLine(string.Join(',',
            Csv(evidence.ObservedUtc.ToString("O", CultureInfo.InvariantCulture)),
            Csv(evidence.EvidenceId),
            Csv(evidence.Mode),
            Csv(evidence.File),
            Csv(evidence.Sheet),
            Csv(evidence.SymbolColumn),
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
        IReadOnlyList<string> warnings)
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
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
