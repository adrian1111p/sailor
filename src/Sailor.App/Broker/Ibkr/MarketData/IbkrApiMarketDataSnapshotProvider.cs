#if SAILOR_IBAPI
using System.Collections.Concurrent;
using IBApi;
using Sailor.App.MarketData.Live;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Broker.Ibkr.MarketData;

public sealed class IbkrApiMarketDataSnapshotProvider : ILiveMarketDataSnapshotProvider, IDisposable
{
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly SailorMarketDataEWrapper _wrapper = new();
    private readonly EReaderSignal _signal;
    private readonly EClientSocket _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private CancellationTokenSource _readerCancellation = new();
    private bool _connected;

    public IbkrApiMarketDataSnapshotProvider(IbkrConnectionOptions connectionOptions)
    {
        _connectionOptions = connectionOptions;
        _signal = new EReaderMonitorSignal();
        _client = new EClientSocket(_wrapper, _signal);
    }

    public string ProviderName => "ibkr-api-market-data";

    public async Task<LiveMarketDataSnapshotResult> CaptureSnapshotAsync(
        LiveMarketDataRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken);

            _wrapper.Prepare(request.RequestId, request.NormalizedSymbol);
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
                await Task.Delay(request.Duration, captureCts.Token);
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

            SailorMarketSnapshot? snapshot = _wrapper.GetSnapshot(request.NormalizedSymbol, ProviderName, TimeSpan.FromSeconds(Math.Max(5, request.Duration.TotalSeconds * 2)));
            IReadOnlyList<string> events = _wrapper.DrainEvents();
            IReadOnlyList<string> errors = _wrapper.DrainErrors();

            if (snapshot is null)
            {
                return LiveMarketDataSnapshotResult.Failed(
                    request,
                    remoteRequested: true,
                    remoteProviderAvailable: true,
                    $"IBKR did not return a usable L1/L2 snapshot for {request.NormalizedSymbol} during {request.Duration.TotalSeconds:F0}s capture window.",
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
                $"Captured IBKR market snapshot for {request.NormalizedSymbol}. L1={snapshot.HasL1} L2={snapshot.HasL2}.",
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
                $"IBKR market snapshot request failed for {request.NormalizedSymbol}: {ex.Message}",
                _wrapper.DrainErrors(),
                _wrapper.DrainEvents());
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
            Name = "Sailor-IBKR-MarketData-EReader"
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

    private sealed class SailorMarketDataEWrapper : DefaultEWrapper
    {
        private readonly TaskCompletionSource<int> _nextValidIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _managedAccountsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public void Prepare(int requestId, string symbol)
        {
            _requestSymbols[requestId] = symbol;
            _requestSymbols[requestId + 100_000] = symbol;
            _events.Enqueue($"prepared market data request reqId={requestId} symbol={symbol}");
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

            _snapshotStore.UpdateDepth(
                symbol,
                position,
                operation,
                side,
                Convert.ToDecimal(price),
                size);
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
    }
}
#endif
