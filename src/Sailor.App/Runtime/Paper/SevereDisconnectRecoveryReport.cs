namespace Sailor.App.Runtime.Paper;

public sealed record SevereDisconnectRecoveryReport(
    DateTimeOffset ObservedUtc,
    string Mode,
    string TriggerReason,
    bool ReconnectRecovered,
    string ReconciliationStatus,
    bool BrokerTruthAvailable,
    bool SessionsRebuilt,
    bool HistoryRefreshAttempted,
    int HistoryRefreshOk,
    int HistoryRefreshTotal,
    int EasternMinuteOfDay,
    int LastEntryMinute,
    bool CanResumeEntries,
    bool ScannerReplenishmentAllowed,
    int SessionsBefore,
    int SessionsAfter,
    IReadOnlyList<string> ActiveSymbolsBefore,
    IReadOnlyList<string> BrokerPositionSymbols,
    IReadOnlyList<string> BrokerOpenOrderSymbols,
    IReadOnlyList<string> RebuiltSymbols,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Warnings,
    string? JsonPath = null,
    string? CsvPath = null)
{
    public bool ExitOnly => !CanResumeEntries;

    public SevereDisconnectRecoveryReport WithPaths(string jsonPath, string csvPath)
        => this with { JsonPath = jsonPath, CsvPath = csvPath };

    public string ToSummaryString()
    {
        string paths = string.IsNullOrWhiteSpace(JsonPath)
            ? string.Empty
            : $" json={JsonPath} csv={CsvPath}";

        return $"severeRecovery reconnectRecovered={ReconnectRecovered} reconciliation={ReconciliationStatus} " +
               $"brokerTruth={BrokerTruthAvailable} sessionsRebuilt={SessionsRebuilt} sessionsBefore={SessionsBefore} sessionsAfter={SessionsAfter} " +
               $"historyRefresh={HistoryRefreshOk}/{HistoryRefreshTotal} etMinute={EasternMinuteOfDay} lastEntryMinute={LastEntryMinute} " +
               $"canResumeEntries={CanResumeEntries} exitOnly={ExitOnly} scannerReplenishmentAllowed={ScannerReplenishmentAllowed} " +
               $"rebuiltSymbols={RebuiltSymbols.Count} warnings={Warnings.Count} reason={TriggerReason}{paths}";
    }
}
