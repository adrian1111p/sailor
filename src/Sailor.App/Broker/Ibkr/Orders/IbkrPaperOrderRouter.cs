#if SAILOR_IBAPI
using System.Collections.Concurrent;
using IBApi;
using Sailor.App.Broker.Orders;

namespace Sailor.App.Broker.Ibkr.Orders;

public sealed class IbkrPaperOrderRouter : IOrderRouter
{
    private readonly IbkrConnectionOptions _connectionOptions;
    private readonly string _primaryExchange;
    private readonly int _waitSeconds;
    private readonly SailorOrderEWrapper _wrapper = new();
    private readonly EReaderSignal _signal;
    private readonly EClientSocket _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private CancellationTokenSource _readerCancellation = new();
    private bool _connected;
    private int _nextOrderId;

    public IbkrPaperOrderRouter(
        IbkrConnectionOptions connectionOptions,
        string primaryExchange,
        int waitSeconds)
    {
        _connectionOptions = connectionOptions;
        _primaryExchange = string.IsNullOrWhiteSpace(primaryExchange) ? "NASDAQ" : primaryExchange.Trim().ToUpperInvariant();
        _waitSeconds = Math.Max(1, waitSeconds);
        _signal = new EReaderMonitorSignal();
        _client = new EClientSocket(_wrapper, _signal);
    }

    public string RouterName => "ibkr-paper-order-router";

    public async Task<SailorOrderReceipt> SubmitAsync(SailorOrderIntent intent, CancellationToken cancellationToken)
    {
        if (_connectionOptions.Mode != Sailor.App.Runtime.Common.SailorRuntimeMode.Paper)
        {
            return Failed(intent, "SAILOR-028 IBKR order router supports paper mode only. Live order submission is blocked.");
        }

        if (intent.DryRun)
        {
            return await new DryRunOrderRouter().SubmitAsync(intent, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            int orderId = Interlocked.Increment(ref _nextOrderId);
            _wrapper.PrepareOrder(orderId, intent.NormalizedIntentId, intent.NormalizedSymbol);

            Contract contract = IbkrOrderTranslator.BuildStockContract(intent.NormalizedSymbol, _primaryExchange);
            Order order = IbkrOrderTranslator.BuildOrder(intent);
            order.OrderId = orderId;

            _client.placeOrder(orderId, contract, order);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_waitSeconds));

            SailorOrderReceipt receipt;
            try
            {
                receipt = await AwaitWithTimeout(
                    _wrapper.GetOrderAckTask(orderId),
                    timeoutCts.Token,
                    $"order acknowledgement for {intent.NormalizedSymbol} orderId={orderId}").ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                receipt = new SailorOrderReceipt(
                    intent.NormalizedIntentId,
                    intent.NormalizedSymbol,
                    orderId.ToString(),
                    SailorOrderStatus.Submitted,
                    intent.Quantity,
                    FilledQuantity: 0,
                    AverageFillPrice: 0m,
                    $"IBKR paper order was submitted, but no final acknowledgement arrived within {_waitSeconds}s. Check TWS paper order panel.",
                    SentToBroker: true,
                    intent.CreatedAt,
                    DateTimeOffset.Now,
                    _wrapper.DrainEvents(),
                    _wrapper.DrainErrors());
            }

            IReadOnlyList<string> events = Merge(receipt.Events, _wrapper.DrainEvents());
            IReadOnlyList<string> warnings = _wrapper.DrainErrors();
            if (warnings.Count == 0)
            {
                warnings = receipt.Warnings;
            }
            else if (receipt.Warnings.Count > 0)
            {
                warnings = Merge(receipt.Warnings, warnings);
            }

            return receipt with
            {
                Events = events,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return Failed(intent, $"IBKR paper order submission failed: {ex.Message}", _wrapper.DrainEvents(), _wrapper.DrainErrors());
        }
    }

    public Task<SailorCancelResult> CancelAsync(string brokerOrderId, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!int.TryParse(brokerOrderId, out int orderId) || orderId <= 0)
        {
            return Task.FromResult(new SailorCancelResult(
                brokerOrderId,
                Success: false,
                $"Invalid broker order id '{brokerOrderId}'.",
                DateTimeOffset.Now,
                Array.Empty<string>(),
                [$"Invalid broker order id '{brokerOrderId}'."]));
        }

        try
        {
            _client.cancelOrder(orderId);
            return Task.FromResult(new SailorCancelResult(
                brokerOrderId,
                Success: true,
                $"Cancel requested for broker order id {brokerOrderId}.",
                DateTimeOffset.Now,
                [$"cancelOrder orderId={orderId} reason={reason}"],
                _wrapper.DrainErrors()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SailorCancelResult(
                brokerOrderId,
                Success: false,
                $"Cancel failed: {ex.Message}",
                DateTimeOffset.Now,
                _wrapper.DrainEvents(),
                _wrapper.DrainErrors()));
        }
    }

    public Task<SailorFlattenResult> FlattenAsync(string symbol, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SailorFlattenResult(
            symbol.Trim().ToUpperInvariant(),
            Success: false,
            "SAILOR-029 now tracks/reconciles positions, but automatic flatten routing is deferred until the paper conduct/close-only milestone.",
            DateTimeOffset.Now,
            ["flatten-deferred"],
            ["Use paper reconcile first; real flatten order generation is handled by the next paper conduct milestone."]));
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
            Name = "Sailor-IBKR-Order-EReader"
        };
        _readerThread.Start();

        _client.startApi();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_connectionOptions.ConnectTimeoutSeconds));

        int nextValidId = await AwaitWithTimeout(_wrapper.NextValidIdTask, timeoutCts.Token, "nextValidId").ConfigureAwait(false);
        _ = await AwaitWithTimeout(_wrapper.ManagedAccountsTask, timeoutCts.Token, "managedAccounts").ConfigureAwait(false);
        _nextOrderId = Math.Max(0, nextValidId - 1);
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

    private static SailorOrderReceipt Failed(
        SailorOrderIntent intent,
        string message,
        IReadOnlyList<string>? events = null,
        IReadOnlyList<string>? warnings = null)
        => new(
            intent.NormalizedIntentId,
            intent.NormalizedSymbol,
            BrokerOrderId: string.Empty,
            SailorOrderStatus.Failed,
            intent.Quantity,
            FilledQuantity: 0,
            AverageFillPrice: 0m,
            message,
            SentToBroker: false,
            intent.CreatedAt,
            DateTimeOffset.Now,
            events ?? Array.Empty<string>(),
            warnings ?? [message]);

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

    private static IReadOnlyList<string> Merge(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count == 0)
        {
            return second;
        }

        if (second.Count == 0)
        {
            return first;
        }

        return first.Concat(second).ToArray();
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

    private sealed class SailorOrderEWrapper : DefaultEWrapper
    {
        private readonly TaskCompletionSource<int> _nextValidIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _managedAccountsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<SailorOrderReceipt>> _orderAckTasks = new();
        private readonly ConcurrentDictionary<int, (string IntentId, string Symbol)> _orders = new();
        private readonly ConcurrentQueue<string> _events = new();
        private readonly ConcurrentQueue<string> _errors = new();

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

        public void PrepareOrder(int orderId, string intentId, string symbol)
        {
            _orders[orderId] = (intentId, symbol);
            _orderAckTasks[orderId] = new TaskCompletionSource<SailorOrderReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            _events.Enqueue($"prepared paper order orderId={orderId} intentId={intentId} symbol={symbol}");
        }

        public Task<SailorOrderReceipt> GetOrderAckTask(int orderId)
            => _orderAckTasks.GetOrAdd(
                orderId,
                _ => new TaskCompletionSource<SailorOrderReceipt>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

        public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
        {
            _events.Enqueue($"openOrder id={orderId} symbol={contract.Symbol} action={order.Action} type={order.OrderType} qty={order.TotalQuantity} status={orderState.Status}");
            string statusText = orderState.Status ?? "Submitted";
            CompleteOrder(orderId, statusText, 0, Convert.ToDouble(order.TotalQuantity), 0, 0, 0);
        }

        public override void orderStatus(
            int orderId,
            string status,
            double filled,
            double remaining,
            double avgFillPrice,
            int permId,
            int parentId,
            double lastFillPrice,
            int clientId,
            string whyHeld,
            double mktCapPrice)
        {
            _events.Enqueue($"orderStatus id={orderId} status={status} filled={filled} remaining={remaining} avgFillPrice={avgFillPrice} permId={permId} whyHeld={whyHeld}");
            CompleteOrder(orderId, status, filled, remaining, avgFillPrice, permId, lastFillPrice);
        }

        private void CompleteOrder(
            int orderId,
            string statusText,
            double filled,
            double remaining,
            double avgFillPrice,
            int permId,
            double lastFillPrice)
        {
            if (!_orders.TryGetValue(orderId, out (string IntentId, string Symbol) orderInfo))
            {
                return;
            }

            SailorOrderStatus status = MapStatus(statusText, filled, remaining);
            int submitted = Convert.ToInt32(Math.Round(filled + remaining, MidpointRounding.AwayFromZero));
            var receipt = new SailorOrderReceipt(
                orderInfo.IntentId,
                orderInfo.Symbol,
                orderId.ToString(),
                status,
                submitted,
                Convert.ToInt32(Math.Round(filled, MidpointRounding.AwayFromZero)),
                Convert.ToDecimal(avgFillPrice),
                $"IBKR paper order callback status={statusText} permId={permId} lastFill={lastFillPrice:F4}.",
                SentToBroker: true,
                DateTimeOffset.Now,
                DateTimeOffset.Now,
                DrainEvents(),
                DrainErrors());

            _orderAckTasks.GetOrAdd(
                orderId,
                _ => new TaskCompletionSource<SailorOrderReceipt>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(receipt);
        }

        private static SailorOrderStatus MapStatus(string statusText, double filled, double remaining)
        {
            if (string.Equals(statusText, "Filled", StringComparison.OrdinalIgnoreCase) || (filled > 0 && remaining <= 0))
            {
                return SailorOrderStatus.Filled;
            }

            if (filled > 0 && remaining > 0)
            {
                return SailorOrderStatus.PartiallyFilled;
            }

            if (string.Equals(statusText, "Cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(statusText, "ApiCancelled", StringComparison.OrdinalIgnoreCase))
            {
                return SailorOrderStatus.Cancelled;
            }

            if (string.Equals(statusText, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return SailorOrderStatus.Rejected;
            }

            return SailorOrderStatus.Submitted;
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
            string line = $"IBKR error id={id} code={errorCode} msg={errorMsg}";
            _errors.Enqueue(line);
            _events.Enqueue(line);

            if (id > 0 && _orders.TryGetValue(id, out (string IntentId, string Symbol) orderInfo))
            {
                var receipt = new SailorOrderReceipt(
                    orderInfo.IntentId,
                    orderInfo.Symbol,
                    id.ToString(),
                    SailorOrderStatus.Rejected,
                    SubmittedQuantity: 0,
                    FilledQuantity: 0,
                    AverageFillPrice: 0m,
                    line,
                    SentToBroker: true,
                    DateTimeOffset.Now,
                    DateTimeOffset.Now,
                    DrainEvents(),
                    DrainErrors());

                _orderAckTasks.GetOrAdd(
                    id,
                    _ => new TaskCompletionSource<SailorOrderReceipt>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(receipt);
            }
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
