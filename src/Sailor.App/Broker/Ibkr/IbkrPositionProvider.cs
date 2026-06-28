#if SAILOR_IBAPI
using System.Collections.Concurrent;
using IBApi;
using Sailor.App.Broker.State;

namespace Sailor.App.Broker.Ibkr;

public sealed class IbkrPositionProvider : IPositionProvider
{
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly SailorPositionEWrapper _wrapper = new();
    private readonly EReaderSignal _signal;
    private readonly EClientSocket _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private CancellationTokenSource _readerCancellation = new();
    private bool _connected;

    public IbkrPositionProvider(IbkrConnectionOptions connectionOptions)
    {
        _connectionOptions = connectionOptions;
        _signal = new EReaderMonitorSignal();
        _client = new EClientSocket(_wrapper, _signal);
    }

    public string ProviderName => "ibkr-position-provider";

    public async Task<BrokerStateSnapshot> GetBrokerStateAsync(
        PositionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            _wrapper.Prepare(request.ExecutionRequestId);

            _client.reqPositions();
            _client.reqAllOpenOrders();

            var filter = new ExecutionFilter();
            if (!string.IsNullOrWhiteSpace(request.NormalizedAccount))
            {
                filter.AcctCode = request.NormalizedAccount;
            }

            _client.reqExecutions(request.ExecutionRequestId, filter);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(request.Timeout);

            await AwaitWithTimeout(_wrapper.AllRequestsCompleteTask, timeoutCts.Token, "positions/open orders/executions").ConfigureAwait(false);

            IReadOnlyList<BrokerPositionRow> positions = _wrapper.GetPositions()
                .Where(position => string.IsNullOrWhiteSpace(request.NormalizedAccount) || position.Account.Equals(request.NormalizedAccount, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            IReadOnlyList<BrokerOpenOrderRow> openOrders = _wrapper.GetOpenOrders()
                .Where(order => string.IsNullOrWhiteSpace(request.NormalizedAccount) || string.IsNullOrWhiteSpace(order.Account) || order.Account.Equals(request.NormalizedAccount, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            IReadOnlyList<BrokerExecutionRow> executions = _wrapper.GetExecutions()
                .Where(execution => string.IsNullOrWhiteSpace(request.NormalizedAccount) || string.IsNullOrWhiteSpace(execution.Account) || execution.Account.Equals(request.NormalizedAccount, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            IReadOnlyList<string> events = _wrapper.DrainEvents();
            IReadOnlyList<string> warnings = _wrapper.DrainErrors();

            return new BrokerStateSnapshot(
                Success: true,
                ProviderName,
                $"IBKR broker state captured. positions={positions.Count} openOrders={openOrders.Count} executions={executions.Count}.",
                DateTimeOffset.UtcNow,
                positions,
                openOrders,
                executions,
                events,
                warnings);
        }
        catch (Exception ex)
        {
            IReadOnlyList<string> events = _wrapper.DrainEvents();
            IReadOnlyList<string> warnings = _wrapper.DrainErrors()
                .Concat([$"IBKR broker state request failed: {ex.Message}"])
                .ToArray();

            return new BrokerStateSnapshot(
                Success: false,
                ProviderName,
                $"IBKR broker state request failed: {ex.Message}",
                DateTimeOffset.UtcNow,
                Array.Empty<BrokerPositionRow>(),
                Array.Empty<BrokerOpenOrderRow>(),
                Array.Empty<BrokerExecutionRow>(),
                events,
                warnings);
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
            Name = "Sailor-IBKR-Position-EReader"
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

    public async ValueTask DisposeAsync()
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

        if (_readerThread is not null)
        {
            await Task.Run(() => _readerThread.Join(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
        }

        _readerCancellation.Dispose();
    }

    private sealed class SailorPositionEWrapper : DefaultEWrapper
    {
        private readonly TaskCompletionSource<int> _nextValidIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _managedAccountsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _positionsEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _openOrdersEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _executionsEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<BrokerPositionRow> _positions = new();
        private readonly ConcurrentQueue<BrokerOpenOrderRow> _openOrders = new();
        private readonly ConcurrentQueue<BrokerExecutionRow> _executions = new();
        private readonly ConcurrentQueue<string> _events = new();
        private readonly ConcurrentQueue<string> _errors = new();
        private int _executionRequestId;

        public Task<int> NextValidIdTask => _nextValidIdTcs.Task;

        public Task<string> ManagedAccountsTask => _managedAccountsTcs.Task;

        public Task<bool> AllRequestsCompleteTask
            => Task.WhenAll(_positionsEndTcs.Task, _openOrdersEndTcs.Task, _executionsEndTcs.Task)
                .ContinueWith(task => true, TaskScheduler.Default);

        public void Prepare(int executionRequestId)
        {
            _executionRequestId = executionRequestId;
            _positionsEndTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _openOrdersEndTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _executionsEndTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            while (_positions.TryDequeue(out _))
            {
            }

            while (_openOrders.TryDequeue(out _))
            {
            }

            while (_executions.TryDequeue(out _))
            {
            }

            _events.Enqueue($"prepared-position-request executionRequestId={executionRequestId}");
        }

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

        public override void position(string account, Contract contract, double position, double avgCost)
        {
            string symbol = contract.Symbol?.Trim().ToUpperInvariant() ?? string.Empty;
            int quantity = Convert.ToInt32(Math.Round(position, MidpointRounding.AwayFromZero));
            var row = new BrokerPositionRow(
                account ?? string.Empty,
                symbol,
                quantity,
                Convert.ToDecimal(avgCost),
                Source: "ibkr-position",
                DateTimeOffset.UtcNow).Normalize();

            _positions.Enqueue(row);
            _events.Enqueue($"position account={row.Account} symbol={row.Symbol} qty={row.Quantity} avgCost={row.AverageCost:F4}");
        }

        public override void positionEnd()
        {
            _events.Enqueue("positionEnd");
            _positionsEndTcs.TrySetResult(true);
        }

        public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
        {
            int totalQuantity = Convert.ToInt32(Math.Round(Convert.ToDecimal(order.TotalQuantity), MidpointRounding.AwayFromZero));
            var row = new BrokerOpenOrderRow(
                orderId,
                Convert.ToInt32(order.PermId),
                order.Account ?? string.Empty,
                contract.Symbol ?? string.Empty,
                order.Action ?? string.Empty,
                order.OrderType ?? string.Empty,
                totalQuantity,
                orderState.Status ?? string.Empty,
                order.OrderRef ?? string.Empty,
                DateTimeOffset.UtcNow).Normalize();

            _openOrders.Enqueue(row);
            _events.Enqueue($"openOrder {row.ToDisplayLine()}");
        }

        public override void openOrderEnd()
        {
            _events.Enqueue("openOrderEnd");
            _openOrdersEndTcs.TrySetResult(true);
        }

        public override void execDetails(int reqId, Contract contract, Execution execution)
        {
            int quantity = Convert.ToInt32(Math.Round(Convert.ToDecimal(execution.Shares), MidpointRounding.AwayFromZero));
            var row = new BrokerExecutionRow(
                reqId,
                execution.AcctNumber ?? string.Empty,
                contract.Symbol ?? string.Empty,
                execution.OrderId,
                execution.Side ?? string.Empty,
                quantity,
                Convert.ToDecimal(execution.Price),
                execution.ExecId ?? string.Empty,
                execution.Time ?? string.Empty,
                execution.OrderRef ?? string.Empty,
                DateTimeOffset.UtcNow).Normalize();

            _executions.Enqueue(row);
            _events.Enqueue($"execDetails {row.ToDisplayLine()}");
        }

        public override void execDetailsEnd(int reqId)
        {
            if (reqId == _executionRequestId)
            {
                _events.Enqueue($"execDetailsEnd reqId={reqId}");
                _executionsEndTcs.TrySetResult(true);
            }
        }

        public override void error(int id, int errorCode, string errorMsg)
        {
            string line = $"IBKR error id={id} code={errorCode} msg={errorMsg}";
            _errors.Enqueue(line);
            _events.Enqueue(line);
        }

        public override void error(Exception e)
        {
            _errors.Enqueue($"IBKR exception: {e.Message}");
        }

        public override void error(string str)
        {
            _errors.Enqueue($"IBKR raw error: {str}");
        }

        public IReadOnlyList<BrokerPositionRow> GetPositions() => _positions.ToArray();

        public IReadOnlyList<BrokerOpenOrderRow> GetOpenOrders() => _openOrders.ToArray();

        public IReadOnlyList<BrokerExecutionRow> GetExecutions() => _executions.ToArray();

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
    }
}
#endif
