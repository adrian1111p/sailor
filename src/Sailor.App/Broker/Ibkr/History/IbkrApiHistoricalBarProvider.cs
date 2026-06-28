#if SAILOR_IBAPI
using System.Collections.Concurrent;
using IBApi;
using Sailor.App.Backtest.Models;
using Sailor.App.MarketData.History;

namespace Sailor.App.Broker.Ibkr.History;

public sealed class IbkrApiHistoricalBarProvider : IHistoricalBarProvider, IDisposable
{
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly SailorHistoricalEWrapper _wrapper = new();
    private readonly EReaderSignal _signal;
    private readonly EClientSocket _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private CancellationTokenSource _readerCancellation = new();
    private bool _connected;

    public IbkrApiHistoricalBarProvider(IbkrConnectionOptions connectionOptions)
    {
        _connectionOptions = connectionOptions;
        _signal = new EReaderMonitorSignal();
        _client = new EClientSocket(_wrapper, _signal);
    }

    public string ProviderName => "ibkr-api";

    public async Task<HistoricalBarLoadResult> GetOneMinuteHistoryAsync(
        HistoricalBarRequest request,
        CancellationToken cancellationToken)
    {
        string cachePath = HistoricalCachePaths.GetCacheFilePath(request.Symbol, request.Timeframe, request.EndTimeUtc);

        if (!string.Equals(request.Timeframe, "1m", StringComparison.OrdinalIgnoreCase))
        {
            return HistoricalBarLoadResult.Failed(
                request,
                remoteRequested: true,
                remoteProviderAvailable: true,
                cachePath,
                $"SAILOR-025 supports only 1m IBKR history, but received timeframe '{request.Timeframe}'.");
        }

        try
        {
            await EnsureConnectedAsync(cancellationToken);

            _wrapper.ResetHistoricalRequest(request.RequestId);
            Contract contract = BuildStockContract(request.Symbol, request.PrimaryExchange);
            string endDateTime = request.EndTimeUtc.UtcDateTime.ToString("yyyyMMdd HH:mm:ss") + " UTC";

            _client.reqHistoricalData(
                request.RequestId,
                contract,
                endDateTime,
                request.DurationString,
                request.BarSizeSetting,
                request.WhatToShow,
                request.UseRegularTradingHours ? 1 : 0,
                1,
                false,
                new List<TagValue>());

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, _connectionOptions.ConnectTimeoutSeconds * 3)));

            await AwaitWithTimeout(
                _wrapper.GetHistoricalDataEndTask(request.RequestId),
                timeoutCts.Token,
                $"historicalDataEnd for {request.Symbol} reqId={request.RequestId}");

            IReadOnlyList<BacktestBar> bars = _wrapper
                .DrainHistoricalBars(request.RequestId, request.Symbol)
                .Where(bar => bar.Time <= request.EndTimeUtc)
                .OrderBy(bar => bar.Time)
                .ToArray();

            if (bars.Count == 0)
            {
                return HistoricalBarLoadResult.Failed(
                    request,
                    remoteRequested: true,
                    remoteProviderAvailable: true,
                    cachePath,
                    $"IBKR returned no historical bars for {request.Symbol}.",
                    _wrapper.DrainErrors());
            }

            (string writtenCachePath, string? mirrorPath) = HistoricalCacheWriter.Write(request, bars);
            return new HistoricalBarLoadResult(
                request.Symbol,
                request.Timeframe,
                Success: true,
                RemoteRequested: true,
                RemoteProviderAvailable: true,
                Bars: bars,
                CachePath: writtenCachePath,
                BacktestMirrorPath: mirrorPath,
                Message: $"Downloaded {bars.Count} one-minute bars from IBKR and wrote SAILOR-025 cache.",
                Warnings: _wrapper.DrainErrors());
        }
        catch (Exception ex)
        {
            return HistoricalBarLoadResult.Failed(
                request,
                remoteRequested: true,
                remoteProviderAvailable: true,
                cachePath,
                $"IBKR historical request failed for {request.Symbol}: {ex.Message}",
                _wrapper.DrainErrors());
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connected && _client.IsConnected())
        {
            return;
        }

        _client.eConnect(_connectionOptions.Host, _connectionOptions.Port, _connectionOptions.ClientId);
        if (!_client.IsConnected())
        {
            throw new InvalidOperationException($"IBKR socket did not connect to {_connectionOptions.Host}:{_connectionOptions.Port}.");
        }

        _reader = new EReader(_client, _signal);
        _reader.Start();

        _readerCancellation.Dispose();
        _readerCancellation = new CancellationTokenSource();
        _readerThread = new Thread(ProcessMessages)
        {
            IsBackground = true,
            Name = "Sailor-IBKR-History-EReader"
        };
        _readerThread.Start();

        _client.startApi();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_connectionOptions.ConnectTimeoutSeconds));

        _ = await AwaitWithTimeout(_wrapper.NextValidIdTask, timeoutCts.Token, "nextValidId");
        _ = await AwaitWithTimeout(_wrapper.ManagedAccountsTask, timeoutCts.Token, "managedAccounts");
        _connected = true;
    }

    private void ProcessMessages()
    {
        while (!_readerCancellation.IsCancellationRequested && _client.IsConnected())
        {
            _signal.waitForSignal();
            if (_readerCancellation.IsCancellationRequested)
            {
                break;
            }

            _reader?.processMsgs();
        }
    }

    private static Contract BuildStockContract(string symbol, string primaryExchange)
        => new()
        {
            Symbol = symbol,
            SecType = "STK",
            Exchange = "SMART",
            PrimaryExch = string.IsNullOrWhiteSpace(primaryExchange) ? "NASDAQ" : primaryExchange,
            Currency = "USD"
        };

    private static async Task<T> AwaitWithTimeout<T>(Task<T> task, CancellationToken cancellationToken, string stage)
    {
        Task delay = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        Task winner = await Task.WhenAny(task, delay);
        if (winner == task)
        {
            return await task;
        }

        throw new TimeoutException($"Timed out waiting for {stage}.");
    }

    public void Dispose()
    {
        try
        {
            _readerCancellation.Cancel();
            _signal.issueSignal();
        }
        catch
        {
            // best-effort shutdown
        }

        try
        {
            if (_client.IsConnected())
            {
                _client.eDisconnect();
            }
        }
        catch
        {
            // best-effort shutdown
        }

        _readerThread?.Join(TimeSpan.FromSeconds(3));
        _readerCancellation.Dispose();
    }

    private sealed class SailorHistoricalEWrapper : DefaultEWrapper
    {
        private readonly TaskCompletionSource<int> _nextValidIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _managedAccountsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _historicalEndTasks = new();
        private readonly ConcurrentDictionary<int, ConcurrentQueue<Bar>> _historicalBars = new();
        private readonly ConcurrentQueue<string> _errors = new();

        public Task<int> NextValidIdTask => _nextValidIdTcs.Task;

        public Task<string> ManagedAccountsTask => _managedAccountsTcs.Task;

        public override void nextValidId(int orderId)
        {
            _nextValidIdTcs.TrySetResult(orderId);
        }

        public override void managedAccounts(string accountsList)
        {
            _managedAccountsTcs.TrySetResult(accountsList);
        }

        public void ResetHistoricalRequest(int requestId)
        {
            _historicalEndTasks[requestId] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _historicalBars[requestId] = new ConcurrentQueue<Bar>();
        }

        public Task<bool> GetHistoricalDataEndTask(int requestId)
            => _historicalEndTasks.GetOrAdd(
                requestId,
                _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

        public override void historicalData(int reqId, Bar bar)
        {
            _historicalBars.GetOrAdd(reqId, _ => new ConcurrentQueue<Bar>()).Enqueue(bar);
        }

        public override void historicalDataEnd(int reqId, string startDateStr, string endDateStr)
        {
            _historicalEndTasks.GetOrAdd(
                reqId,
                _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(true);
        }

        public IReadOnlyList<BacktestBar> DrainHistoricalBars(int requestId, string symbol)
        {
            var bars = new List<BacktestBar>();
            if (!_historicalBars.TryGetValue(requestId, out ConcurrentQueue<Bar>? queue))
            {
                return bars;
            }

            while (queue.TryDequeue(out Bar? bar))
            {
                if (!TryParseIbkrBarTime(bar.Time, out DateTimeOffset time))
                {
                    continue;
                }

                bars.Add(new BacktestBar(
                    Time: time,
                    Symbol: symbol,
                    Open: Convert.ToDecimal(bar.Open, System.Globalization.CultureInfo.InvariantCulture),
                    High: Convert.ToDecimal(bar.High, System.Globalization.CultureInfo.InvariantCulture),
                    Low: Convert.ToDecimal(bar.Low, System.Globalization.CultureInfo.InvariantCulture),
                    Close: Convert.ToDecimal(bar.Close, System.Globalization.CultureInfo.InvariantCulture),
                    Volume: Convert.ToInt64(bar.Volume, System.Globalization.CultureInfo.InvariantCulture)));
            }

            return bars;
        }

        public IReadOnlyList<string> DrainErrors()
        {
            var rows = new List<string>();
            while (_errors.TryDequeue(out string? row))
            {
                rows.Add(row);
            }

            return rows;
        }

        public override void error(int id, int errorCode, string errorMsg)
        {
            _errors.Enqueue($"IBKR error id={id} code={errorCode} msg={errorMsg}");
        }

        public override void error(Exception e)
        {
            _errors.Enqueue($"IBKR exception: {e.Message}");
        }

        public override void error(string str)
        {
            _errors.Enqueue($"IBKR raw error: {str}");
        }

        private static bool TryParseIbkrBarTime(string value, out DateTimeOffset result)
        {
            if (DateTimeOffset.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out result))
            {
                return true;
            }

            if (DateTime.TryParseExact(
                    value,
                    "yyyyMMdd  HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out DateTime parsedWithDoubleSpace))
            {
                result = new DateTimeOffset(parsedWithDoubleSpace, TimeSpan.Zero);
                return true;
            }

            if (DateTime.TryParseExact(
                    value,
                    "yyyyMMdd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed))
            {
                result = new DateTimeOffset(parsed, TimeSpan.Zero);
                return true;
            }

            result = default;
            return false;
        }
    }
}
#endif
