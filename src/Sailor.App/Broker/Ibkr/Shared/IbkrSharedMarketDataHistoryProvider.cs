#if SAILOR_IBAPI
using System.Collections.Concurrent;
using IBApi;
using Sailor.App.Backtest.Models;
using Sailor.App.MarketData.History;
using Sailor.App.MarketData.Live;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Broker.Ibkr.Shared;

/// <summary>
/// SAILOR-060 shared data session for IBKR history and market-data requests.
///
/// The previous paper runtime could create one IBKR socket for historical bars and
/// another socket for L1/L2 snapshots using the same client id. During a paper run
/// that also owns an order-router client this produced code=326/501 collisions and
/// stale-candle refresh failures. This provider shares one IBKR EClient per
/// host/port/clientId and serializes history + snapshot requests through it.
/// </summary>
public sealed class IbkrSharedMarketDataHistoryProvider : IHistoricalBarProvider, ILiveMarketDataSnapshotProvider, IDisposable
{
    private static readonly ConcurrentDictionary<string, SharedCore> Cores = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object RegistrySync = new();

    private readonly string _key;
    private readonly SharedCore _core;
    private bool _disposed;

    public IbkrSharedMarketDataHistoryProvider(IbkrConnectionOptions connectionOptions)
    {
        IbkrConnectionOptions dataOptions = connectionOptions with
        {
            SendOrders = false
        };

        _key = BuildKey(dataOptions);
        lock (RegistrySync)
        {
            _core = Cores.GetOrAdd(_key, _ => new SharedCore(dataOptions));
            _core.AddRef();
        }
    }

    public string ProviderName => $"ibkr-shared-data-session clientId={_core.ClientId}";

    public Task<HistoricalBarLoadResult> GetOneMinuteHistoryAsync(
        HistoricalBarRequest request,
        CancellationToken cancellationToken)
        => _core.GetOneMinuteHistoryAsync(request, cancellationToken);

    public Task<LiveMarketDataSnapshotResult> CaptureSnapshotAsync(
        LiveMarketDataRequest request,
        CancellationToken cancellationToken)
        => _core.CaptureSnapshotAsync(request, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (RegistrySync)
        {
            if (_core.ReleaseRef() <= 0 && Cores.TryRemove(_key, out SharedCore? removed))
            {
                removed.Dispose();
            }
        }
    }

    private static string BuildKey(IbkrConnectionOptions options)
        => $"{options.Mode}:{options.Host}:{options.Port}:{options.ClientId}";

    private sealed class SharedCore : IDisposable
    {
        private readonly IbkrConnectionOptions _connectionOptions;
        private readonly SharedEWrapper _wrapper = new();
        private readonly EReaderSignal _signal;
        private readonly EClientSocket _client;
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private EReader? _reader;
        private Thread? _readerThread;
        private CancellationTokenSource _readerCancellation = new();
        private bool _connected;
        private bool _disposed;
        private int _refCount;

        public SharedCore(IbkrConnectionOptions connectionOptions)
        {
            _connectionOptions = connectionOptions;
            _signal = new EReaderMonitorSignal();
            _client = new EClientSocket(_wrapper, _signal);
        }

        public int ClientId => _connectionOptions.ClientId;

        public void AddRef() => _refCount++;

        public int ReleaseRef() => --_refCount;

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
                    $"SAILOR-060 shared IBKR data session supports only 1m history, but received timeframe '{request.Timeframe}'.");
            }

            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

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
                    $"historicalDataEnd for {request.Symbol} reqId={request.RequestId}").ConfigureAwait(false);

                IReadOnlyList<BacktestBar> bars = _wrapper
                    .DrainHistoricalBars(request.RequestId, request.Symbol)
                    .Where(bar => bar.Time <= request.EndTimeUtc)
                    .OrderBy(bar => bar.Time)
                    .ToArray();

                IReadOnlyList<string> warnings = _wrapper.DrainErrors();
                IReadOnlyList<string> events = _wrapper.DrainEvents();
                if (bars.Count == 0)
                {
                    IReadOnlyList<string> diagnosticWarnings = warnings
                        .Concat(events.Select(row => $"event: {row}"))
                        .Append($"SAILOR-061 shared-history-request {request.ToDisplayString()} endUtc={request.EndTimeUtc:O} clientId={_connectionOptions.ClientId}")
                        .ToArray();

                    return HistoricalBarLoadResult.Failed(
                        request,
                        remoteRequested: true,
                        remoteProviderAvailable: true,
                        cachePath,
                        $"SAILOR-060 shared IBKR data session returned no historical bars for {request.Symbol} using clientId={_connectionOptions.ClientId}.",
                        diagnosticWarnings);
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
                    Message: $"SAILOR-060 shared IBKR data session downloaded {bars.Count} one-minute bars using clientId={_connectionOptions.ClientId}.",
                    Warnings: warnings);
            }
            catch (Exception ex)
            {
                return HistoricalBarLoadResult.Failed(
                    request,
                    remoteRequested: true,
                    remoteProviderAvailable: true,
                    cachePath,
                    $"SAILOR-060 shared IBKR historical request failed for {request.Symbol}: {ex.Message}",
                    _wrapper.DrainErrors()
                        .Concat(_wrapper.DrainEvents().Select(row => $"event: {row}"))
                        .Append($"SAILOR-061 shared-history-request {request.ToDisplayString()} endUtc={request.EndTimeUtc:O} clientId={_connectionOptions.ClientId}")
                        .ToArray());
            }
            finally
            {
                _requestLock.Release();
            }
        }

        public async Task<LiveMarketDataSnapshotResult> CaptureSnapshotAsync(
            LiveMarketDataRequest request,
            CancellationToken cancellationToken)
        {
            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

                _wrapper.PrepareMarketData(request.RequestId, request.NormalizedSymbol);
                Contract contract = BuildStockContract(request.NormalizedSymbol, request.PrimaryExchange);

                if (request.MarketDataType > 0)
                {
                    _client.reqMarketDataType(request.MarketDataType);
                }

                if (request.UseL1)
                {
                    _client.reqMktData(
                        request.RequestId,
                        contract,
                        string.Empty,
                        false,
                        false,
                        new List<TagValue>());
                }

                if (request.UseL2)
                {
                    _client.reqMarketDepth(
                        request.RequestId + 100_000,
                        contract,
                        request.DepthLevels,
                        request.UseSmartDepth,
                        new List<TagValue>());
                }

                using var captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                captureCts.CancelAfter(request.Duration);

                try
                {
                    await Task.Delay(request.Duration, captureCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Expected when capture duration elapses.
                }

                if (request.UseL1)
                {
                    _client.cancelMktData(request.RequestId);
                }

                if (request.UseL2)
                {
                    _client.cancelMktDepth(request.RequestId + 100_000, request.UseSmartDepth);
                }

                SailorMarketSnapshot? snapshot = _wrapper.GetSnapshot(
                    request.NormalizedSymbol,
                    $"ibkr-shared-data-session clientId={_connectionOptions.ClientId}",
                    TimeSpan.FromSeconds(Math.Max(5, request.Duration.TotalSeconds * 2)));
                IReadOnlyList<string> events = _wrapper.DrainEvents();
                IReadOnlyList<string> errors = _wrapper.DrainErrors();

                if (snapshot is null)
                {
                    return LiveMarketDataSnapshotResult.Failed(
                        request,
                        remoteRequested: true,
                        remoteProviderAvailable: true,
                        $"SAILOR-060 shared IBKR data session did not return a usable L1/L2 snapshot for {request.NormalizedSymbol} during {request.Duration.TotalSeconds:F0}s capture window.",
                        errors,
                        events);
                }

                string logPath = MarketSnapshotLogWriter.WriteSnapshot(request.Mode, request, snapshot);
                return new LiveMarketDataSnapshotResult(
                    request.NormalizedSymbol,
                    Success: true,
                    RemoteRequested: true,
                    RemoteProviderAvailable: true,
                    snapshot,
                    $"SAILOR-060 shared IBKR data session captured snapshot for {request.NormalizedSymbol}. L1={snapshot.HasL1} L2={snapshot.HasL2} clientId={_connectionOptions.ClientId}.",
                    errors,
                    events,
                    logPath);
            }
            catch (Exception ex)
            {
                return LiveMarketDataSnapshotResult.Failed(
                    request,
                    remoteRequested: true,
                    remoteProviderAvailable: true,
                    $"SAILOR-060 shared IBKR market snapshot request failed for {request.NormalizedSymbol}: {ex.Message}",
                    _wrapper.DrainErrors(),
                    _wrapper.DrainEvents());
            }
            finally
            {
                _requestLock.Release();
            }
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(IbkrSharedMarketDataHistoryProvider));
            }

            if (_connected && _client.IsConnected())
            {
                return;
            }

            if (_client.IsConnected())
            {
                _connected = true;
                return;
            }

            _client.eConnect(_connectionOptions.Host, _connectionOptions.Port, _connectionOptions.ClientId);
            if (!_client.IsConnected())
            {
                throw new InvalidOperationException($"IBKR shared data socket did not connect to {_connectionOptions.Host}:{_connectionOptions.Port} with clientId={_connectionOptions.ClientId}.");
            }

            _reader = new EReader(_client, _signal);
            _reader.Start();

            _readerCancellation.Dispose();
            _readerCancellation = new CancellationTokenSource();
            _readerThread = new Thread(ProcessMessages)
            {
                IsBackground = true,
                Name = $"Sailor-IBKR-SharedData-EReader-{_connectionOptions.ClientId}"
            };
            _readerThread.Start();

            _client.startApi();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_connectionOptions.ConnectTimeoutSeconds));

            _ = await AwaitWithTimeout(_wrapper.NextValidIdTask, timeoutCts.Token, "nextValidId").ConfigureAwait(false);
            _ = await AwaitWithTimeout(_wrapper.ManagedAccountsTask, timeoutCts.Token, "managedAccounts").ConfigureAwait(false);
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
            Task winner = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (winner == task)
            {
                return await task.ConfigureAwait(false);
            }

            throw new TimeoutException($"Timed out waiting for {stage}.");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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
            _requestLock.Dispose();
        }
    }

    private sealed class SharedEWrapper : DefaultEWrapper
    {
        private readonly TaskCompletionSource<int> _nextValidIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _managedAccountsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _historicalEndTasks = new();
        private readonly ConcurrentDictionary<int, ConcurrentQueue<Bar>> _historicalBars = new();
        private readonly ConcurrentDictionary<int, string> _requestSymbols = new();
        private readonly LiveMarketSnapshotStore _snapshotStore = new();
        private readonly ConcurrentQueue<string> _errors = new();
        private readonly ConcurrentQueue<string> _events = new();

        public Task<int> NextValidIdTask => _nextValidIdTcs.Task;

        public Task<string> ManagedAccountsTask => _managedAccountsTcs.Task;

        public override void nextValidId(int orderId)
        {
            _nextValidIdTcs.TrySetResult(orderId);
            _events.Enqueue($"nextValidId={orderId}");
        }

        public override void managedAccounts(string accountsList)
        {
            _managedAccountsTcs.TrySetResult(accountsList);
            _events.Enqueue($"managedAccounts={accountsList}");
        }

        public void ResetHistoricalRequest(int requestId)
        {
            _historicalEndTasks[requestId] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _historicalBars[requestId] = new ConcurrentQueue<Bar>();
            _events.Enqueue($"prepared shared historical request reqId={requestId}");
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
            _events.Enqueue($"historicalDataEnd reqId={reqId} start={startDateStr} end={endDateStr}");
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

        public void PrepareMarketData(int requestId, string symbol)
        {
            _requestSymbols[requestId] = symbol;
            _requestSymbols[requestId + 100_000] = symbol;
            _events.Enqueue($"prepared shared market data request reqId={requestId} symbol={symbol}");
        }

        public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            if (!_requestSymbols.TryGetValue(tickerId, out string? symbol))
            {
                return;
            }

            decimal value = Convert.ToDecimal(price);
            switch (field)
            {
                case 1:
                    _snapshotStore.UpdateBid(symbol, value);
                    break;
                case 2:
                    _snapshotStore.UpdateAsk(symbol, value);
                    break;
                case 4:
                case 6:
                case 9:
                    _snapshotStore.UpdateLast(symbol, value);
                    break;
            }
        }

        public override void tickSize(int tickerId, int field, int size)
        {
            if (!_requestSymbols.TryGetValue(tickerId, out string? symbol))
            {
                return;
            }

            long normalizedSize = size;
            switch (field)
            {
                case 0:
                    _snapshotStore.UpdateBidSize(symbol, normalizedSize);
                    break;
                case 3:
                    _snapshotStore.UpdateAskSize(symbol, normalizedSize);
                    break;
            }
        }

        public override void marketDataType(int reqId, int marketDataType)
        {
            _events.Enqueue($"marketDataType reqId={reqId} type={marketDataType}");
        }

        public override void updateMktDepth(int tickerId, int position, int operation, int side, double price, int size)
        {
            if (!_requestSymbols.TryGetValue(tickerId, out string? symbol))
            {
                return;
            }

            _snapshotStore.UpdateDepth(symbol, position, operation, side, Convert.ToDecimal(price), size);
        }

        public override void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, int size, bool isSmartDepth)
        {
            updateMktDepth(tickerId, position, operation, side, price, size);
            _events.Enqueue($"depthL2 reqId={tickerId} pos={position} side={side} mm={marketMaker} smart={isSmartDepth}");
        }

        public SailorMarketSnapshot? GetSnapshot(string symbol, string source, TimeSpan staleAfter)
            => _snapshotStore.GetSnapshot(symbol, source, staleAfter);

        public IReadOnlyList<string> DrainErrors()
        {
            var rows = new List<string>();
            while (_errors.TryDequeue(out string? row))
            {
                rows.Add(row);
            }

            return rows;
        }

        public IReadOnlyList<string> DrainEvents()
        {
            var rows = new List<string>();
            while (_events.TryDequeue(out string? row))
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
