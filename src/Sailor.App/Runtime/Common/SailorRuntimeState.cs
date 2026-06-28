namespace Sailor.App.Runtime.Common;

public sealed class SailorRuntimeState
{
    private readonly List<string> _activeSymbols = [];
    private readonly List<string> _messages = [];

    public SailorRuntimeState(SailorRuntimeMode mode)
    {
        Mode = mode;
        StartedUtc = DateTimeOffset.UtcNow;
        LastHeartbeatUtc = StartedUtc;
    }

    public SailorRuntimeMode Mode { get; }

    public SailorRuntimeStatus Status { get; private set; } = SailorRuntimeStatus.Stopped;

    public bool IsConnected { get; private set; }

    public bool IsRunning { get; private set; }

    public DateTimeOffset StartedUtc { get; }

    public DateTimeOffset LastHeartbeatUtc { get; private set; }

    public string? LastError { get; private set; }

    public IReadOnlyList<string> ActiveSymbols => _activeSymbols;

    public IReadOnlyList<string> Messages => _messages;

    public void SetStatus(SailorRuntimeStatus status, string? message = null)
    {
        Status = status;
        LastHeartbeatUtc = DateTimeOffset.UtcNow;

        IsConnected = status is SailorRuntimeStatus.Connected or SailorRuntimeStatus.Scanning or SailorRuntimeStatus.Running;
        IsRunning = status is SailorRuntimeStatus.Scanning or SailorRuntimeStatus.Running or SailorRuntimeStatus.Flattening;

        if (!string.IsNullOrWhiteSpace(message))
        {
            _messages.Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} | {message}");
        }
    }

    public void SetError(string error)
    {
        LastError = error;
        SetStatus(SailorRuntimeStatus.Error, error);
    }

    public void SetActiveSymbols(IEnumerable<string> symbols)
    {
        _activeSymbols.Clear();
        _activeSymbols.AddRange(symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase));
    }

    public string ToDisplayString()
    {
        string symbols = _activeSymbols.Count == 0
            ? "none"
            : string.Join(",", _activeSymbols);

        return $"mode={Mode.ToDisplayName()} status={Status} connected={IsConnected} running={IsRunning} activeSymbols={symbols} lastHeartbeatUtc={LastHeartbeatUtc:O} lastError={LastError ?? "none"}";
    }
}
