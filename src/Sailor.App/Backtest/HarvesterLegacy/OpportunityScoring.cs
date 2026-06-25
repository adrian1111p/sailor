using System.Globalization;

namespace Sailor.App.Backtest;

/// <summary>
/// A single soft execution-ready gate that, under the opportunity-scoring model, is converted
/// from a hard reject into a score penalty (a classification) instead of discarding the signal.
/// </summary>
public readonly record struct EntrySoftGate(string ReasonCode, double Penalty);

/// <summary>
/// Result of evaluating the shared execution-ready gates for a single signal.
/// Carries both the legacy reject decision and the continuous opportunity score so that
/// downstream ranking / quota logic (later audit phases) can prefer the strongest candidates.
/// </summary>
public sealed record EntryGateEvaluation
{
    public bool Rejected { get; init; }

    /// <summary>True only when a genuine capital-protection / data-integrity gate rejected the signal.</summary>
    public bool HardRejected { get; init; }

    public string RejectReason { get; init; } = string.Empty;

    /// <summary>EntryScore minus accumulated soft-gate penalties (legacy mode reports the raw EntryScore).</summary>
    public double OpportunityScore { get; init; }

    public IReadOnlyList<EntrySoftGate> SoftGates { get; init; } = Array.Empty<EntrySoftGate>();

    /// <summary>Soft-gate reason codes that were tripped (penalised) but did not by themselves reject the signal.</summary>
    public IReadOnlyList<string> Classifications => SoftGates.Select(gate => gate.ReasonCode).ToArray();

    public static EntryGateEvaluation HardReject(string reason) => new()
    {
        Rejected = true,
        HardRejected = true,
        RejectReason = reason,
        OpportunityScore = double.NaN,
        SoftGates = Array.Empty<EntrySoftGate>(),
    };

    public static EntryGateEvaluation Reject(string reason, double opportunityScore, IReadOnlyList<EntrySoftGate> softGates) => new()
    {
        Rejected = true,
        HardRejected = false,
        RejectReason = reason,
        OpportunityScore = opportunityScore,
        SoftGates = softGates,
    };

    public static EntryGateEvaluation Pass(double opportunityScore, IReadOnlyList<EntrySoftGate> softGates) => new()
    {
        Rejected = false,
        HardRejected = false,
        RejectReason = string.Empty,
        OpportunityScore = opportunityScore,
        SoftGates = softGates,
    };
}

/// <summary>
/// Process-level settings for the adaptive opportunity-scoring entry model (audit Phases 1-2).
/// <para>
/// When <see cref="Enabled"/> is false (default) the entry pipeline preserves its exact legacy
/// behavior: the first soft execution-ready gate that trips is a hard reject, in the historical order.
/// </para>
/// <para>
/// When enabled, soft gates (entry-bar direction, second-confirmation bar, L1/L2 trend confirmation,
/// score-below-min) are converted from hard rejects into score penalties. A signal is only rejected
/// when a genuine hard gate trips or when its resulting opportunity score falls below
/// <see cref="QualityFloor"/>. This lets a strong setup that is, e.g., one classification short
/// ("insufficient" rather than "blocked") still reach execution instead of being silently discarded.
/// </para>
/// </summary>
public sealed record OpportunityScoringSettings
{
    public bool Enabled { get; init; }

    /// <summary>Minimum opportunity score required to remain execution-ready once penalties are applied.</summary>
    public double QualityFloor { get; init; } = 1.0;

    public double EntryCandlePenalty { get; init; } = 4.0;

    public double SecondConfirmationPenalty { get; init; } = 3.0;

    public double L1ConfirmationPenalty { get; init; } = 2.0;

    public double L2ConfirmationPenalty { get; init; } = 3.0;

    // Defaults to 0: the EntryScore already forms the base of the opportunity score, so a
    // below-min score is recorded as a classification rather than penalised a second time.
    public double ScoreShortfallPenaltyPerPoint { get; init; }

    private static OpportunityScoringSettings? s_cached;

    // Ambient per-flow override used by the live session pace controller (audit Phase 5) to inject a
    // pace-relaxed quality floor around a single symbol evaluation without threading settings through
    // the entire signal-engine call chain. AsyncLocal keeps each evaluation flow (and each parallel
    // test) isolated, so it never reintroduces the static-cache cross-test race.
    private static readonly AsyncLocal<OpportunityScoringSettings?> s_ambientOverride = new();

    /// <summary>
    /// Effective process settings: the ambient per-flow override when one is active (see
    /// <see cref="PushScope"/>), otherwise the lazily-loaded environment settings.
    /// </summary>
    public static OpportunityScoringSettings Current => s_ambientOverride.Value ?? (s_cached ??= LoadFromEnvironment());

    /// <summary>Clears the cached settings so the next access re-reads the environment (used by tests).</summary>
    public static void ResetCache() => s_cached = null;

    /// <summary>
    /// Push an ambient settings override for the current async flow. Dispose the returned scope to
    /// restore the previous value. Used by the live pace controller to apply a pace-relaxed quality
    /// floor around a single symbol evaluation.
    /// </summary>
    public static IDisposable PushScope(OpportunityScoringSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new AmbientScope(settings);
    }

    private sealed class AmbientScope : IDisposable
    {
        private readonly OpportunityScoringSettings? _previous;
        private bool _disposed;

        public AmbientScope(OpportunityScoringSettings settings)
        {
            _previous = s_ambientOverride.Value;
            s_ambientOverride.Value = settings;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            s_ambientOverride.Value = _previous;
        }
    }

    public static OpportunityScoringSettings LoadFromEnvironment()
    {
        return new OpportunityScoringSettings
        {
            Enabled = ReadBool("HARVESTER_OPPORTUNITY_SCORING_ENABLED", false),
            QualityFloor = ReadDouble("HARVESTER_OPPORTUNITY_QUALITY_FLOOR", 1.0),
            EntryCandlePenalty = ReadDouble("HARVESTER_OPPORTUNITY_PENALTY_ENTRY_CANDLE", 4.0),
            SecondConfirmationPenalty = ReadDouble("HARVESTER_OPPORTUNITY_PENALTY_SECOND_CONFIRMATION", 3.0),
            L1ConfirmationPenalty = ReadDouble("HARVESTER_OPPORTUNITY_PENALTY_L1", 2.0),
            L2ConfirmationPenalty = ReadDouble("HARVESTER_OPPORTUNITY_PENALTY_L2", 3.0),
            ScoreShortfallPenaltyPerPoint = ReadDouble("HARVESTER_OPPORTUNITY_PENALTY_SCORE_SHORTFALL", 0.0),
        };
    }

    private static bool ReadBool(string name, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        raw = raw.Trim();
        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static double ReadDouble(string name, double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}

