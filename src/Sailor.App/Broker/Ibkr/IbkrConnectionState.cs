namespace Sailor.App.Broker.Ibkr;

public enum IbkrConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    TcpConnected = 2,
    ApiHandshakePending = 3,
    ApiReady = 4,
    Degraded = 5,
    Disconnecting = 6,
    Failed = 7
}
