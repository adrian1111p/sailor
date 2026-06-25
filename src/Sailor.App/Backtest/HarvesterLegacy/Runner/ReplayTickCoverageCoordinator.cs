using Sailor.App.Backtest.DataFetcher;
using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Connection;
using Harvester.App.IBKR.Runtime;

namespace Sailor.App.Backtest.Runner;

public sealed record ReplayTickCoverageStatus(
    string Symbol,
    bool HasBars,
    bool HasTicks,
    bool HasOverlap,
    DateTime? BarStartUtc,
    DateTime? BarEndUtc,
    DateTime? TickStartUtc,
    DateTime? TickEndUtc);

public static class ReplayTickCoverageCoordinator
{
    public static string[] ResolvePreferredUniverse(
        IReadOnlyList<string> baselineSymbols,
        bool symbolsExplicitlyProvided,
        bool replayExportEnabled,
        int targetCount,
        Action<string>? log = null)
    {
        var normalizedBaseline = baselineSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!replayExportEnabled || symbolsExplicitlyProvided)
        {
            return normalizedBaseline;
        }

        var candidateSymbols = CsvBarStorage.ListSymbols()
            .Where(symbol => CsvBarStorage.Exists(symbol, BacktestRunner.TriggerTf))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var statuses = DescribeMany(candidateSymbols);
        var preferred = OrderPreferredUniverse(normalizedBaseline, statuses, targetCount);
        var overlapCount = statuses.Count(status => status.HasOverlap);

        log?.Invoke($"Replay export enabled: {overlapCount}/{statuses.Length} cached symbols currently have overlapping 1m bar + L1 tick coverage.");
        if (!preferred.SequenceEqual(normalizedBaseline, StringComparer.OrdinalIgnoreCase))
        {
            log?.Invoke($"Replay export enabled: preferred compare universe={string.Join(", ", preferred)}");
        }

        return preferred;
    }

    public static string[] OrderPreferredUniverse(
        IReadOnlyList<string> baselineSymbols,
        IReadOnlyList<ReplayTickCoverageStatus> statuses,
        int targetCount)
    {
        var normalizedBaseline = baselineSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var statusBySymbol = statuses.ToDictionary(status => status.Symbol, StringComparer.OrdinalIgnoreCase);
        var selected = new List<string>(Math.Max(targetCount, normalizedBaseline.Length));

        void Append(IEnumerable<string> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (selected.Count >= targetCount)
                {
                    break;
                }

                if (!selected.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                {
                    selected.Add(symbol);
                }
            }
        }

        Append(normalizedBaseline.Where(symbol => statusBySymbol.TryGetValue(symbol, out var status) && status.HasOverlap));
        Append(statuses
            .Where(status => status.HasOverlap && !normalizedBaseline.Contains(status.Symbol, StringComparer.OrdinalIgnoreCase))
            .OrderBy(status => status.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(status => status.Symbol));
        Append(normalizedBaseline.Where(symbol => !selected.Contains(symbol, StringComparer.OrdinalIgnoreCase)));
        Append(statuses
            .Where(status => !selected.Contains(status.Symbol, StringComparer.OrdinalIgnoreCase))
            .OrderBy(status => status.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(status => status.Symbol));

        return selected.Take(Math.Max(1, targetCount)).ToArray();
    }

    public static ReplayTickCoverageStatus[] DescribeMany(IEnumerable<string> symbols)
        => symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(Describe)
            .ToArray();

    public static ReplayTickCoverageStatus Describe(string symbol)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var hasBars = CsvBarStorage.Exists(normalizedSymbol, BacktestRunner.TriggerTf);
        DateTime? barStartUtc = null;
        DateTime? barEndUtc = null;

        if (hasBars)
        {
            var bars = CsvBarStorage.LoadBars(normalizedSymbol, BacktestRunner.TriggerTf);
            if (bars.Length > 0)
            {
                barStartUtc = bars[0].Timestamp;
                barEndUtc = bars[^1].Timestamp;
            }
        }

        var tickRange = CsvTickStorage.GetTickRange(normalizedSymbol);
        var hasTicks = tickRange.HasValue;
        var hasOverlap = hasBars && hasTicks && barStartUtc.HasValue && barEndUtc.HasValue
            && tickRange!.Value.End >= barStartUtc.Value
            && tickRange.Value.Start <= barEndUtc.Value;

        return new ReplayTickCoverageStatus(
            Symbol: normalizedSymbol,
            HasBars: hasBars,
            HasTicks: hasTicks,
            HasOverlap: hasOverlap,
            BarStartUtc: barStartUtc,
            BarEndUtc: barEndUtc,
            TickStartUtc: tickRange?.Start,
            TickEndUtc: tickRange?.End);
    }

    public static ReplayTickCoverageStatus[] GetBackfillTargets(IEnumerable<string> symbols)
        => DescribeMany(symbols)
            .Where(status => status.HasBars && !status.HasOverlap)
            .ToArray();

    public static async Task<ReplayTickCoverageStatus[]> BackfillMissingOverlapAsync(
        IEnumerable<string> symbols,
        AppOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var targets = GetBackfillTargets(symbols);
        if (targets.Length == 0)
        {
            return DescribeMany(symbols);
        }

        var wrapper = new SnapshotEWrapper(new IbBrokerAdapter());
        using var session = new IbkrSession(wrapper);

        try
        {
            log?.Invoke($"Replay export backfill: connecting to IBKR for {targets.Length} symbols lacking overlapping tick coverage.");
            await session.ConnectAsync(options.Host, options.Port, options.ClientId, options.TimeoutSeconds, cancellationToken);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Replay export backfill skipped: IBKR connection failed ({ex.Message}).");
            return DescribeMany(symbols);
        }

        var adapter = new IbBrokerAdapter();
        var fetcher = new HistoricalTickFetcher(session.Client, adapter, () => wrapper);
        foreach (var target in targets)
        {
            if (!target.BarStartUtc.HasValue || !target.BarEndUtc.HasValue)
            {
                continue;
            }

            var rangeStartUtc = target.BarStartUtc.Value.Date;
            var rangeEndUtc = target.BarEndUtc.Value.Date.AddDays(1);
            log?.Invoke($"Replay export backfill: fetching {target.Symbol} ticks for {rangeStartUtc:yyyy-MM-dd} â†’ {rangeEndUtc:yyyy-MM-dd}.");

            try
            {
                await fetcher.FetchAndStoreRangeAsync(
                    target.Symbol,
                    rangeStartUtc,
                    rangeEndUtc,
                    options.PrimaryExchange,
                    useRth: true,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Replay export backfill failed for {target.Symbol}: {ex.Message}");
            }
        }

        return DescribeMany(symbols);
    }
}
