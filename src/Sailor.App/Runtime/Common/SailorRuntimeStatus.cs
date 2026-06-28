namespace Sailor.App.Runtime.Common;

public enum SailorRuntimeStatus
{
    Stopped = 0,
    Starting = 1,
    Connecting = 2,
    Connected = 3,
    Scanning = 4,
    Running = 5,
    Flattening = 6,
    Reconnecting = 7,
    Error = 8
}
