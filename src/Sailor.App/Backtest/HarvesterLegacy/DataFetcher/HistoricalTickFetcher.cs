using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Contracts;
using Harvester.App.IBKR.Runtime;
using IBApi;

namespace Sailor.App.Backtest.DataFetcher;

/// <summary>
/// Fetches historical L1 tick data from IBKR TWS/Gateway using reqHistoricalTicks.
/// Supports paginated fetching across multiple days (IBKR returns max 1000 ticks per request).
///
/// Fetches two tick types:
///   - BID_ASK: bid/ask prices and sizes at each quote update
///   - TRADES:  last trade price, size, and exchange
///
/// Results are stored in CSV format via <see cref="CsvTickStorage"/> for backtest replay.
///
/// Usage:
///   var fetcher = new HistoricalTickFetcher(client, adapter, wrapper);
///   await fetcher.FetchAndStoreAsync("AAPL", days: 5, "NASDAQ");
///
/// IBKR Limitations:
///   - Max 1000 ticks per request
///   - Pacing: ~10 seconds between requests recommended
///   - Historical tick data availability varies by exchange/symbol
///   - RTH (Regular Trading Hours) filter available
/// </summary>
public sealed class HistoricalTickFetcher
{
    private readonly EClientSocket _client;
    private readonly IBrokerAdapter _adapter;
    private readonly Func<SnapshotEWrapper> _wrapperFactory;
    private readonly int _pacingDelayMs;

    /// <param name="client">Connected IBKR client socket.</param>
    /// <param name="adapter">Broker adapter for building contracts and sending requests.</param>
    /// <param name="wrapperFactory">Factory to get the EWrapper instance (for reading response queues).</param>
    /// <param name="pacingDelayMs">Delay between paginated requests to respect IBKR pacing rules.</param>
    public HistoricalTickFetcher(
        EClientSocket client,
        IBrokerAdapter adapter,
        Func<SnapshotEWrapper> wrapperFactory,
        int pacingDelayMs = 11_000)
    {
        _client = client;
        _adapter = adapter;
        _wrapperFactory = wrapperFactory;
        _pacingDelayMs = pacingDelayMs;
    }

    /// <summary>
    /// Fetch BID_ASK and TRADES historical ticks for a symbol over the last N trading days,
    /// then persist to CSV in backtest/data/{SYMBOL}/.
    /// </summary>
    public async Task<TickFetchResult> FetchAndStoreAsync(
        string symbol,
        int days = 5,
        string primaryExchange = "NASDAQ",
        bool useRth = true,
        CancellationToken ct = default)
    {
        var endDate = DateTime.UtcNow.Date;
        var startDate = endDate.AddDays(-days - 2); // add buffer for weekends

        return await FetchAndStoreRangeAsync(symbol, startDate, endDate, primaryExchange, useRth, ct);
    }

    public async Task<TickFetchResult> FetchAndStoreRangeAsync(
        string symbol,
        DateTime startDateUtc,
        DateTime endDateUtc,
        string primaryExchange = "NASDAQ",
        bool useRth = true,
        CancellationToken ct = default)
    {
        var normalizedStart = startDateUtc.Kind == DateTimeKind.Utc ? startDateUtc : startDateUtc.ToUniversalTime();
        var normalizedEnd = endDateUtc.Kind == DateTimeKind.Utc ? endDateUtc : endDateUtc.ToUniversalTime();
        if (normalizedEnd <= normalizedStart)
        {
            throw new ArgumentException("Historical tick range end must be after start.", nameof(endDateUtc));
        }

        Console.WriteLine($"[L1L2-FETCH] Starting tick fetch for {symbol}, {normalizedStart:yyyy-MM-dd} â†’ {normalizedEnd:yyyy-MM-dd}, exchange={primaryExchange}");

        var contract = ContractFactory.Stock(symbol, exchange: "SMART", primaryExchange: primaryExchange);

        var bidAskTicks = new List<BidAskTick>();
        var tradeTicks = new List<TradeTick>();
        int requestCount = 0;

        // â”€â”€ Fetch BID_ASK ticks (paginated) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Console.WriteLine($"[L1L2-FETCH] Fetching BID_ASK ticks from {normalizedStart:yyyy-MM-dd} to {normalizedEnd:yyyy-MM-dd}...");
        var currentStart = normalizedStart;
        DateTime? prevLast = null;

        while (currentStart < normalizedEnd)
        {
            ct.ThrowIfCancellationRequested();

            var startStr = currentStart.ToString("yyyyMMdd HH:mm:ss");
            var wrapper = _wrapperFactory();
            var reqId = 20_000 + requestCount;

            // Drain stale data, let EReader settle, then reset completion signal
            DrainQueue(wrapper.HistoricalTicksBidAsk);
            await Task.Delay(200, ct);
            DrainQueue(wrapper.HistoricalTicksBidAsk);
            wrapper.ResetHistoricalTicksDone();

            _adapter.RequestHistoricalTicks(
                _client, reqId, contract,
                startDateTime: startStr,
                endDateTime: "",
                numberOfTicks: 1000,
                whatToShow: "BID_ASK",
                useRth: useRth ? 1 : 0,
                ignoreSize: false);

            // Wait for completion with timeout
            var completed = await WaitForCompletion(wrapper.HistoricalTicksDoneTask, TimeSpan.FromSeconds(30), ct);
            if (!completed)
            {
                Console.Error.WriteLine($"[L1L2-FETCH] Timeout waiting for BID_ASK ticks (req {reqId})");
                break;
            }

            // Drain results â€” only keep ticks strictly after cursor to avoid stale leaks
            var rawTicks = DrainBidAsk(wrapper.HistoricalTicksBidAsk);
            var batchTicks = rawTicks.Where(t => t.TimestampUtc >= currentStart).ToArray();
            if (batchTicks.Length == 0)
            {
                Console.WriteLine($"[L1L2-FETCH] No more BID_ASK ticks after {currentStart:yyyy-MM-dd HH:mm:ss}");
                break;
            }

            bidAskTicks.AddRange(batchTicks);
            requestCount++;

            // Advance cursor to last tick timestamp + 1 second
            var newLast = batchTicks[^1].TimestampUtc;
            Console.WriteLine($"[L1L2-FETCH] BID_ASK batch {requestCount}: {batchTicks.Length} ticks, last={newLast:yyyy-MM-dd HH:mm:ss}");

            // If cursor didn't advance, we're stuck â€” break
            if (prevLast.HasValue && newLast <= prevLast.Value)
            {
                Console.WriteLine($"[L1L2-FETCH] Cursor stalled at {newLast:yyyy-MM-dd HH:mm:ss}, stopping BID_ASK pagination.");
                break;
            }
            prevLast = newLast;
            currentStart = newLast.AddSeconds(1);

            // Data exhausted when IBKR returns fewer than requested
            if (batchTicks.Length < 1000)
                break;

            // Respect IBKR pacing
            if (currentStart < normalizedEnd)
                await Task.Delay(_pacingDelayMs, ct);
        }

        // â”€â”€ Fetch TRADES ticks (paginated) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Console.WriteLine($"[L1L2-FETCH] Fetching TRADES ticks from {normalizedStart:yyyy-MM-dd} to {normalizedEnd:yyyy-MM-dd}...");
        currentStart = normalizedStart;
        prevLast = null;

        while (currentStart < normalizedEnd)
        {
            ct.ThrowIfCancellationRequested();

            var startStr = currentStart.ToString("yyyyMMdd HH:mm:ss");
            var wrapper = _wrapperFactory();
            var reqId = 25_000 + requestCount;

            DrainQueue(wrapper.HistoricalTicksLast);
            await Task.Delay(200, ct);
            DrainQueue(wrapper.HistoricalTicksLast);
            wrapper.ResetHistoricalTicksDone();

            _adapter.RequestHistoricalTicks(
                _client, reqId, contract,
                startDateTime: startStr,
                endDateTime: "",
                numberOfTicks: 1000,
                whatToShow: "TRADES",
                useRth: useRth ? 1 : 0,
                ignoreSize: false);

            var completed = await WaitForCompletion(wrapper.HistoricalTicksDoneTask, TimeSpan.FromSeconds(30), ct);
            if (!completed)
            {
                Console.Error.WriteLine($"[L1L2-FETCH] Timeout waiting for TRADES ticks (req {reqId})");
                break;
            }

            var rawTicks = DrainTrades(wrapper.HistoricalTicksLast);
            var batchTicks = rawTicks.Where(t => t.TimestampUtc >= currentStart).ToArray();
            if (batchTicks.Length == 0)
            {
                Console.WriteLine($"[L1L2-FETCH] No more TRADES ticks after {currentStart:yyyy-MM-dd HH:mm:ss}");
                break;
            }

            tradeTicks.AddRange(batchTicks);
            requestCount++;

            var newLast = batchTicks[^1].TimestampUtc;
            Console.WriteLine($"[L1L2-FETCH] TRADES batch {requestCount}: {batchTicks.Length} ticks, last={newLast:yyyy-MM-dd HH:mm:ss}");

            if (prevLast.HasValue && newLast <= prevLast.Value)
            {
                Console.WriteLine($"[L1L2-FETCH] Cursor stalled at {newLast:yyyy-MM-dd HH:mm:ss}, stopping TRADES pagination.");
                break;
            }
            prevLast = newLast;
            currentStart = newLast.AddSeconds(1);

            if (batchTicks.Length < 1000)
                break;

            if (currentStart < normalizedEnd)
                await Task.Delay(_pacingDelayMs, ct);
        }

        // â”€â”€ Persist to CSV â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var bidAskArr = bidAskTicks.OrderBy(t => t.TimestampUtc).ToArray();
        var tradeArr = tradeTicks.OrderBy(t => t.TimestampUtc).ToArray();

        if (bidAskArr.Length > 0)
            CsvTickStorage.MergeBidAskTicks(symbol, bidAskArr);
        if (tradeArr.Length > 0)
            CsvTickStorage.MergeTradeTicks(symbol, tradeArr);

        var result = new TickFetchResult(
            Symbol: symbol,
            BidAskTickCount: bidAskArr.Length,
            TradeTickCount: tradeArr.Length,
            RequestCount: requestCount,
            DateRange: bidAskArr.Length > 0
                ? (bidAskArr[0].TimestampUtc, bidAskArr[^1].TimestampUtc)
                : null);

        Console.WriteLine($"[L1L2-FETCH] Complete: {symbol} â€” {bidAskArr.Length} bid-ask + {tradeArr.Length} trade ticks in {requestCount} requests");
        return result;
    }

    private static BidAskTick[] DrainBidAsk(System.Collections.Concurrent.ConcurrentQueue<HistoricalTickBidAskRow> queue)
    {
        var ticks = new List<BidAskTick>();
        while (queue.TryDequeue(out var row))
        {
            var ts = DateTimeOffset.FromUnixTimeSeconds(row.EpochSeconds).UtcDateTime;
            ticks.Add(new BidAskTick(ts, row.PriceBid, row.PriceAsk, (double)row.SizeBid, (double)row.SizeAsk));
        }
        return ticks.OrderBy(t => t.TimestampUtc).ToArray();
    }

    private static TradeTick[] DrainTrades(System.Collections.Concurrent.ConcurrentQueue<HistoricalTickLastRow> queue)
    {
        var ticks = new List<TradeTick>();
        while (queue.TryDequeue(out var row))
        {
            var ts = DateTimeOffset.FromUnixTimeSeconds(row.EpochSeconds).UtcDateTime;
            ticks.Add(new TradeTick(ts, row.Price, (double)row.Size, row.Exchange));
        }
        return ticks.OrderBy(t => t.TimestampUtc).ToArray();
    }

    private static void DrainQueue<T>(System.Collections.Concurrent.ConcurrentQueue<T> queue)
    {
        while (queue.TryDequeue(out _)) { }
    }

    private static async Task<bool> WaitForCompletion(Task<bool> doneTask, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await doneTask.WaitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // timeout
        }
    }
}

/// <summary>Result of a historical tick fetch operation.</summary>
public sealed record TickFetchResult(
    string Symbol,
    int BidAskTickCount,
    int TradeTickCount,
    int RequestCount,
    (DateTime Start, DateTime End)? DateRange);

