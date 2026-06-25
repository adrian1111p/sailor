using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Runner;

internal static class BacktestRepeatedEntryTimeShuffler
{
    public static List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> Apply(
        IReadOnlyList<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)> trades,
        IReadOnlyDictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData)
    {
        if (trades.Count == 0)
        {
            return [];
        }

        var shuffledTrades = new List<(string Strategy, string Variant, string Symbol, BacktestTradeResult Trade)>(trades.Count);

        foreach (var group in trades
            .GroupBy(
                trade => new
                {
                    trade.Strategy,
                    trade.Variant,
                    trade.Symbol,
                    SessionDate = trade.Trade.EntryTime.Date,
                }))
        {
            var orderedGroup = group
                .OrderBy(trade => trade.Trade.EntryTime)
                .ThenBy(trade => trade.Trade.ExitTime)
                .ThenBy(trade => trade.Trade.EntryBar)
                .ToArray();
            var triggerBars = allData.TryGetValue(group.Key.Symbol, out var data)
                ? data.Trigger
                : Array.Empty<EnrichedBar>();

            for (var ordinal = 0; ordinal < orderedGroup.Length; ordinal++)
            {
                var tradeEnvelope = orderedGroup[ordinal];
                var trade = tradeEnvelope.Trade;

                if (ordinal == 0)
                {
                    shuffledTrades.Add(tradeEnvelope with
                    {
                        Trade = trade with
                        {
                            OriginalEntryTime = trade.EntryTime,
                            OriginalEntryBar = trade.EntryBar,
                            ShuffleReason = "first-trade-kept-original"
                        }
                    });
                    continue;
                }

                var (shuffledEntryTime, shuffledEntryBar, shuffleReason) = ResolveShuffledEntry(trade, triggerBars, ordinal);
                shuffledTrades.Add(tradeEnvelope with
                {
                    Trade = trade with
                    {
                        OriginalEntryTime = trade.EntryTime,
                        OriginalEntryBar = trade.EntryBar,
                        ShuffledEntryTime = shuffledEntryTime,
                        ShuffledEntryBar = shuffledEntryBar,
                        ShuffleReason = shuffleReason,
                    }
                });
            }
        }

        return shuffledTrades;
    }

    private static (DateTime ShuffledEntryTime, int? ShuffledEntryBar, string ShuffleReason) ResolveShuffledEntry(
        BacktestTradeResult trade,
        IReadOnlyList<EnrichedBar> triggerBars,
        int repeatedTradeOrdinal)
    {
        if (triggerBars.Count > 0)
        {
            var maxValidBar = Math.Min(trade.ExitBar, triggerBars.Count - 1);
            var candidateBar = Math.Min(trade.EntryBar + Math.Max(1, repeatedTradeOrdinal), maxValidBar);
            if (candidateBar > trade.EntryBar && candidateBar >= 0 && candidateBar < triggerBars.Count)
            {
                return (
                    triggerBars[candidateBar].Bar.Timestamp,
                    candidateBar,
                    $"future-bar:+{candidateBar - trade.EntryBar}");
            }
        }

        var microOffsetMilliseconds = Math.Max(1, repeatedTradeOrdinal);
        return (
            trade.EntryTime.AddMilliseconds(microOffsetMilliseconds),
            trade.EntryBar,
            $"offset-window:+{microOffsetMilliseconds}ms");
    }
}
