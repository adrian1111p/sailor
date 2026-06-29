using Sailor.App.Scanner.Runtime;

namespace Sailor.App.Scanner.ScanList;

public sealed class ScanListMemoryStore
{
    private readonly Dictionary<string, ScanListSymbolState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ScanListReloadResult ApplyWorkbookSnapshot(
        ScanListWorkbookResult workbook,
        IEnumerable<string>? symbolsWithOpenPositions = null,
        DateTimeOffset? observedUtc = null)
    {
        DateTimeOffset now = observedUtc ?? DateTimeOffset.UtcNow;
        var openPositionSet = new HashSet<string>(symbolsWithOpenPositions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var workbookSet = new HashSet<string>(workbook.Symbols, StringComparer.OrdinalIgnoreCase);
        var previousActive = _states.Values
            .Where(state => state.Status == ScanListSymbolStatus.Active)
            .Select(state => state.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = workbook.Symbols
            .Where(symbol => !previousActive.Contains(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var removed = previousActive
            .Where(symbol => !workbookSet.Contains(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string symbol in workbook.Symbols)
        {
            if (_states.TryGetValue(symbol, out ScanListSymbolState? existing))
            {
                _states[symbol] = existing with
                {
                    Status = ScanListSymbolStatus.Active,
                    LastSeenUtc = now,
                    RemovedUtc = null,
                    HasOpenPosition = openPositionSet.Contains(symbol),
                    Source = SourceDescription(workbook)
                };
            }
            else
            {
                _states[symbol] = new ScanListSymbolState(
                    symbol,
                    ScanListSymbolStatus.Active,
                    now,
                    now,
                    RemovedUtc: null,
                    HasOpenPosition: openPositionSet.Contains(symbol),
                    IsRetainedTradeCandidate: false,
                    LastRank: null,
                    LastScore: null,
                    Source: SourceDescription(workbook));
            }
        }

        foreach (string symbol in _states.Keys.ToArray())
        {
            if (workbookSet.Contains(symbol))
            {
                continue;
            }

            ScanListSymbolState state = _states[symbol];
            bool hasOpenPosition = openPositionSet.Contains(symbol) || state.HasOpenPosition;
            if (hasOpenPosition)
            {
                _states[symbol] = state with
                {
                    Status = ScanListSymbolStatus.RetainedForOpenPosition,
                    HasOpenPosition = true,
                    RemovedUtc = state.RemovedUtc ?? now,
                    LastSeenUtc = now
                };
            }
            else if (state.IsRetainedTradeCandidate)
            {
                _states[symbol] = state with
                {
                    Status = ScanListSymbolStatus.RetainedForRecentSelection,
                    RemovedUtc = state.RemovedUtc ?? now,
                    LastSeenUtc = now
                };
            }
            else
            {
                _states[symbol] = state with
                {
                    Status = ScanListSymbolStatus.Removed,
                    RemovedUtc = state.RemovedUtc ?? now,
                    LastSeenUtc = now
                };
            }
        }

        IReadOnlyList<ScanListSymbolState> states = _states.Values
            .OrderBy(state => state.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<string> retainedRemoved = states
            .Where(state => state.Status is ScanListSymbolStatus.RetainedForOpenPosition or ScanListSymbolStatus.RetainedForRecentSelection)
            .Select(state => state.Symbol)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ScanListReloadResult(
            now,
            SourceDescription(workbook),
            workbook.Symbols,
            added,
            removed,
            retainedRemoved,
            states,
            workbook.Warnings);
    }

    public void RetainTradeCandidates(IEnumerable<PaperScannerCandidate> candidates, int tradeTop, DateTimeOffset? observedUtc = null)
    {
        DateTimeOffset now = observedUtc ?? DateTimeOffset.UtcNow;
        var retained = candidates
            .Take(Math.Max(1, tradeTop))
            .ToDictionary(
                candidate => candidate.Candidate.Symbol,
                candidate => candidate,
                StringComparer.OrdinalIgnoreCase);

        foreach (string symbol in _states.Keys.ToArray())
        {
            ScanListSymbolState state = _states[symbol];
            if (retained.TryGetValue(symbol, out PaperScannerCandidate? candidate))
            {
                _states[symbol] = state with
                {
                    IsRetainedTradeCandidate = true,
                    LastRank = candidate.Rank,
                    LastScore = candidate.Candidate.Score,
                    LastSeenUtc = now
                };
            }
            else if (state.Status == ScanListSymbolStatus.Active)
            {
                _states[symbol] = state with
                {
                    IsRetainedTradeCandidate = false,
                    LastRank = null,
                    LastScore = null,
                    LastSeenUtc = now
                };
            }
        }
    }

    public IReadOnlyList<ScanListSymbolState> Snapshot()
        => _states.Values
            .OrderBy(state => state.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<string> ActiveSymbols()
        => _states.Values
            .Where(state => state.Status == ScanListSymbolStatus.Active)
            .Select(state => state.Symbol)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<string> TradeEligibleSymbols()
        => _states.Values
            .Where(state => state.IsTradableCandidate)
            .OrderBy(state => state.LastRank ?? int.MaxValue)
            .ThenBy(state => state.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(state => state.Symbol)
            .ToArray();

    private static string SourceDescription(ScanListWorkbookResult workbook)
        => $"{workbook.Options.FilePath}#{workbook.Options.SheetName}#{workbook.Options.SymbolColumn}";
}
