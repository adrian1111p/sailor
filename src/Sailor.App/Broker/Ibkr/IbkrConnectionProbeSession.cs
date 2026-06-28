using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Sailor.App.Broker.Ibkr;

/// <summary>
/// First Sailor-native IBKR session implementation.
///
/// This class intentionally uses only the .NET BCL so SAILOR-024 keeps the project buildable
/// without importing the large Harvester runtime or pinning the IBApi package yet.
/// It validates that TWS/Gateway is reachable on the configured host/port and establishes
/// the runtime connection state/logging contract. The full IBApi EReader handshake
/// (nextValidId + managed accounts) is the next implementation layer.
/// </summary>
public sealed class IbkrConnectionProbeSession : IIbkrConnectionSession
{
    private TcpClient? _tcpClient;
    private IbkrConnectionSnapshot _snapshot = CreateInitialSnapshot();

    public IbkrConnectionSnapshot Snapshot => _snapshot;

    public async Task<IbkrConnectionResult> ConnectAsync(
        IbkrConnectionOptions options,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        _snapshot = new IbkrConnectionSnapshot(
            IbkrConnectionState.Connecting,
            options.Host,
            options.Port,
            options.ClientId,
            startedUtc,
            null,
            null,
            TimeSpan.Zero,
            null,
            null,
            null,
            Array.Empty<IbkrAccountSnapshot>(),
            null,
            IsApiHandshakeComplete: false);

        messages.Add($"Connecting TCP to IBKR/TWS/Gateway at {options.Host}:{options.Port} with clientId={options.ClientId}.");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));

            _tcpClient = new TcpClient
            {
                NoDelay = true
            };

            await _tcpClient.ConnectAsync(options.Host, options.Port, timeoutCts.Token).ConfigureAwait(false);

            DateTimeOffset connectedUtc = DateTimeOffset.UtcNow;
            stopwatch.Stop();

            messages.Add("TCP socket connected successfully.");
            messages.Add("No market data requested. No orders sent.");
            messages.Add("IBApi protocol handshake is not enabled in this SAILOR-024 probe session.");
            messages.Add("NextValidId and managed account callbacks require the next IBApi adapter layer.");

            _snapshot = new IbkrConnectionSnapshot(
                IbkrConnectionState.TcpConnected,
                options.Host,
                options.Port,
                options.ClientId,
                startedUtc,
                connectedUtc,
                null,
                stopwatch.Elapsed,
                ServerVersion: "tcp-probe-only",
                ServerTime: null,
                NextValidOrderId: null,
                Accounts: BuildConfiguredAccountSnapshot(options),
                ErrorMessage: null,
                IsApiHandshakeComplete: false);

            return new IbkrConnectionResult(true, _snapshot, messages);
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            string message = $"Connection timeout after {options.ConnectTimeoutSeconds}s: {ex.Message}";
            messages.Add(message);
            _snapshot = BuildFailedSnapshot(options, startedUtc, stopwatch.Elapsed, message);
            return new IbkrConnectionResult(false, _snapshot, messages);
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            string message = $"Socket error {ex.SocketErrorCode}: {ex.Message}";
            messages.Add(message);
            messages.Add("Check that TWS/Gateway is running and API socket is enabled on the configured port.");
            _snapshot = BuildFailedSnapshot(options, startedUtc, stopwatch.Elapsed, message);
            return new IbkrConnectionResult(false, _snapshot, messages);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            string message = $"Unexpected connection error: {ex.GetType().Name}: {ex.Message}";
            messages.Add(message);
            _snapshot = BuildFailedSnapshot(options, startedUtc, stopwatch.Elapsed, message);
            return new IbkrConnectionResult(false, _snapshot, messages);
        }
    }

    public Task<IbkrConnectionSnapshot> DisconnectAsync(string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _tcpClient?.Close();
            _tcpClient?.Dispose();
            _tcpClient = null;
        }
        finally
        {
            DateTimeOffset disconnectedUtc = DateTimeOffset.UtcNow;
            _snapshot = _snapshot with
            {
                State = IbkrConnectionState.Disconnected,
                DisconnectedUtc = disconnectedUtc,
                ErrorMessage = string.IsNullOrWhiteSpace(reason) ? null : reason
            };
        }

        return Task.FromResult(_snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        if (_tcpClient is not null)
        {
            await DisconnectAsync("dispose", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static IbkrConnectionSnapshot CreateInitialSnapshot()
        => new(
            IbkrConnectionState.Disconnected,
            Host: string.Empty,
            Port: 0,
            ClientId: 0,
            StartedUtc: DateTimeOffset.UtcNow,
            TcpConnectedUtc: null,
            DisconnectedUtc: null,
            Elapsed: TimeSpan.Zero,
            ServerVersion: null,
            ServerTime: null,
            NextValidOrderId: null,
            Accounts: Array.Empty<IbkrAccountSnapshot>(),
            ErrorMessage: null,
            IsApiHandshakeComplete: false);

    private static IbkrConnectionSnapshot BuildFailedSnapshot(
        IbkrConnectionOptions options,
        DateTimeOffset startedUtc,
        TimeSpan elapsed,
        string message)
        => new(
            IbkrConnectionState.Failed,
            options.Host,
            options.Port,
            options.ClientId,
            startedUtc,
            TcpConnectedUtc: null,
            DisconnectedUtc: DateTimeOffset.UtcNow,
            elapsed,
            ServerVersion: null,
            ServerTime: null,
            NextValidOrderId: null,
            Accounts: BuildConfiguredAccountSnapshot(options),
            ErrorMessage: message,
            IsApiHandshakeComplete: false);

    private static IReadOnlyList<IbkrAccountSnapshot> BuildConfiguredAccountSnapshot(IbkrConnectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Account))
        {
            return Array.Empty<IbkrAccountSnapshot>();
        }

        return
        [
            new IbkrAccountSnapshot(
                options.Account.Trim(),
                IsConfiguredAccount: true,
                ObservedUtc: DateTimeOffset.UtcNow)
        ];
    }
}
