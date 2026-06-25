using System.Globalization;
using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest;

/// <summary>
/// A single entry candidate paired with its evaluated execution-ready gates, ready to be ranked by
/// opportunity score. Produced by the shared scoring seam (<see cref="OpportunityScoring"/>) and
/// consumed by <see cref="CandidateRanker"/> / <see cref="AdaptiveQuotaController"/>.
/// </summary>
public sealed record RankedCandidate(
    string Symbol,
    TradeSide Side,
    double OpportunityScore,
    EntryGateEvaluation Evaluation);

/// <summary>
/// Orders entry candidates by opportunity score so downstream quota logic can prefer the strongest
/// setups. Hard-rejected candidates (data integrity / capital protection) are always dropped.
/// </summary>
public static class CandidateRanker
{
    public static IReadOnlyList<RankedCandidate> Rank(IEnumerable<RankedCandidate> candidates)
    {
        return candidates
            .Where(candidate => !candidate.Evaluation.HardRejected)
            .OrderByDescending(candidate => candidate.OpportunityScore)
            .ThenBy(candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// Inputs for a single quota decision. Kept clock-free and side-effect-free so the controller is
/// deterministic and unit-testable. Capacity caps (max active positions, working orders, cooldowns)
/// are enforced by the caller and surfaced here only as <see cref="AvailableSlots"/>.
/// </summary>
public sealed record QuotaDecisionContext(
    int OrdersAdmittedSoFar,
    double SessionElapsedFraction,
    int AvailableSlots);

/// <summary>
/// Process settings for the adaptive opportunity quota (audit Phase 3). Disabled by default so the
/// entry pipeline keeps its current behavior until explicitly opted in.
/// </summary>
public sealed record AdaptiveQuotaSettings
{
    public bool Enabled { get; init; }

    /// <summary>Target number of admitted entries across a full session (the user's "minimum ~10 orders").</summary>
    public int TargetOrdersPerSession { get; init; } = 10;

    /// <summary>Acceptance score required when perfectly on pace (the legacy quality bar).</summary>
    public double BaseAcceptThreshold { get; init; } = 5.0;

    /// <summary>Hard lower bound the adaptive threshold can never drop below, protecting "reduced losses".</summary>
    public double QualityFloor { get; init; } = 1.0;

    /// <summary>How aggressively the threshold is lowered per missing order of pace deficit.</summary>
    public double PaceRelaxationSlope { get; init; } = 1.0;

    /// <summary>Upper bound on admissions in a single evaluation cycle (0 = unbounded).</summary>
    public int MaxAdmissionsPerCycle { get; init; }

    private static AdaptiveQuotaSettings? s_cached;

    public static AdaptiveQuotaSettings Current => s_cached ??= LoadFromEnvironment();

    public static void ResetCache() => s_cached = null;

    public static AdaptiveQuotaSettings LoadFromEnvironment()
    {
        return new AdaptiveQuotaSettings
        {
            Enabled = ReadBool("HARVESTER_ADAPTIVE_QUOTA_ENABLED", false),
            TargetOrdersPerSession = Math.Max(0, ReadInt("HARVESTER_ADAPTIVE_QUOTA_TARGET", 10)),
            BaseAcceptThreshold = ReadDouble("HARVESTER_ADAPTIVE_QUOTA_BASE_THRESHOLD", 5.0),
            QualityFloor = ReadDouble("HARVESTER_ADAPTIVE_QUOTA_QUALITY_FLOOR", 1.0),
            PaceRelaxationSlope = Math.Max(0.0, ReadDouble("HARVESTER_ADAPTIVE_QUOTA_RELAX_SLOPE", 1.0)),
            MaxAdmissionsPerCycle = Math.Max(0, ReadInt("HARVESTER_ADAPTIVE_QUOTA_MAX_PER_CYCLE", 0)),
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

    private static int ReadInt(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw)
            || !int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? fallback
                : parsed;
    }

    private static double ReadDouble(string name, double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw)
            || !double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? fallback
                : parsed;
    }
}

/// <summary>
/// Adaptive controller that paces entry admissions toward a session target without ever sacrificing a
/// non-negotiable quality floor.
/// <para>
/// The acceptance threshold floats: when the session is <i>behind</i> the pace implied by
/// <see cref="AdaptiveQuotaSettings.TargetOrdersPerSession"/>, the bar is progressively lowered toward
/// the quality floor so more of the highest-ranked candidates are admitted. When <i>ahead</i> of pace
/// the bar stays at the base threshold. This self-corrects on both quiet days (relax to reach the
/// target) and wild days (never flood below the floor), fixing the static-threshold failure mode.
/// </para>
/// </summary>
public sealed class AdaptiveQuotaController
{
    private readonly AdaptiveQuotaSettings _settings;

    public AdaptiveQuotaController(AdaptiveQuotaSettings? settings = null)
    {
        _settings = settings ?? AdaptiveQuotaSettings.Current;
    }

    /// <summary>
    /// Resolve the opportunity score a candidate must meet to be admitted given current pace.
    /// </summary>
    public double ResolveAcceptanceThreshold(QuotaDecisionContext context)
    {
        if (!_settings.Enabled)
        {
            return _settings.BaseAcceptThreshold;
        }

        var elapsed = Math.Clamp(context.SessionElapsedFraction, 0.0, 1.0);
        var expectedByNow = _settings.TargetOrdersPerSession * elapsed;
        var deficit = expectedByNow - context.OrdersAdmittedSoFar;
        if (deficit <= 0.0)
        {
            // On or ahead of pace: keep the full quality bar.
            return _settings.BaseAcceptThreshold;
        }

        var relaxed = _settings.BaseAcceptThreshold - (deficit * _settings.PaceRelaxationSlope);
        return Math.Clamp(relaxed, _settings.QualityFloor, _settings.BaseAcceptThreshold);
    }

    /// <summary>
    /// Select which ranked candidates to admit this cycle: those at or above the adaptive threshold,
    /// limited by available capacity and the per-cycle cap. Input is expected to be pre-ranked
    /// (descending opportunity score) via <see cref="CandidateRanker.Rank"/>.
    /// </summary>
    public IReadOnlyList<RankedCandidate> SelectAdmissions(
        IReadOnlyList<RankedCandidate> rankedCandidates,
        QuotaDecisionContext context)
    {
        if (context.AvailableSlots <= 0 || rankedCandidates.Count == 0)
        {
            return Array.Empty<RankedCandidate>();
        }

        var threshold = ResolveAcceptanceThreshold(context);
        var cycleCap = _settings.MaxAdmissionsPerCycle > 0
            ? Math.Min(_settings.MaxAdmissionsPerCycle, context.AvailableSlots)
            : context.AvailableSlots;

        var admitted = new List<RankedCandidate>(Math.Min(cycleCap, rankedCandidates.Count));
        foreach (var candidate in rankedCandidates)
        {
            if (admitted.Count >= cycleCap)
            {
                break;
            }

            if (candidate.Evaluation.HardRejected)
            {
                continue;
            }

            if (candidate.OpportunityScore >= threshold)
            {
                admitted.Add(candidate);
            }
        }

        return admitted;
    }
}

