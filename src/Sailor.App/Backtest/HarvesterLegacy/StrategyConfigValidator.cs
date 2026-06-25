namespace Sailor.App.Backtest;

/// <summary>
/// Result of validating a <see cref="StrategyConfig"/>.
/// </summary>
/// <param name="Errors">Genuinely contradictory / nonsensical settings that would break math or
/// make entries impossible. These should be corrected before use.</param>
/// <param name="Warnings">Suspicious but tolerable settings worth surfacing in logs.</param>
/// <param name="ResolvedSummary">A single-line summary of the resolved exit/entry configuration for logs.</param>
public sealed record StrategyConfigValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string ResolvedSummary)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Thrown when <see cref="StrategyConfigValidator.ValidateAndThrow"/> finds errors.</summary>
public sealed class StrategyConfigValidationException(IReadOnlyList<string> errors)
    : Exception($"Invalid StrategyConfig: {string.Join("; ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Phase 3.9 â€” strategy config validation layer.
/// <para>
/// Design rule (audit constraint 1): this validator NEVER flags a configuration merely because it
/// would open more trades. It only reports settings that are genuinely contradictory â€” inverted
/// ranges, non-positive risk sizing, out-of-[0,1] scale fractions â€” i.e. settings that would BLOCK
/// all trades or break exit math. Flagging those actually protects throughput.
/// </para>
/// </summary>
public static class StrategyConfigValidator
{
    public static StrategyConfigValidationResult Validate(StrategyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();
        var warnings = new List<string>();

        // â”€â”€ Risk sizing must be positive (otherwise sizing math is undefined / zero-share) â”€â”€
        if (config.RiskPerTradeDollars <= 0)
        {
            errors.Add($"RiskPerTradeDollars must be > 0 (was {config.RiskPerTradeDollars}).");
        }

        if (config.AccountSize <= 0)
        {
            errors.Add($"AccountSize must be > 0 (was {config.AccountSize}).");
        }

        if (config.MaxShares <= 0)
        {
            errors.Add($"MaxShares must be > 0 (was {config.MaxShares}).");
        }

        if (config.MinRiskPerShare <= 0)
        {
            errors.Add($"MinRiskPerShare must be > 0 (was {config.MinRiskPerShare}).");
        }

        // â”€â”€ Stop / exit math must be well-formed â”€â”€
        if (config.HardStopR <= 0)
        {
            errors.Add($"HardStopR must be > 0 (was {config.HardStopR}).");
        }

        if (config.MaxHoldBars <= 0)
        {
            errors.Add($"MaxHoldBars must be > 0 (was {config.MaxHoldBars}).");
        }

        if (config.GivebackPct is <= 0 or > 1)
        {
            errors.Add($"GivebackPct must be in (0, 1] (was {config.GivebackPct}).");
        }

        if (config.Tp1ScalePct is < 0 or > 1)
        {
            errors.Add($"Tp1ScalePct must be in [0, 1] (was {config.Tp1ScalePct}).");
        }

        if (config.ContinuationTp2ScalePct is < 0 or > 1)
        {
            errors.Add($"ContinuationTp2ScalePct must be in [0, 1] (was {config.ContinuationTp2ScalePct}).");
        }

        if (config.UseNotionalGivebackCap && config.GivebackPctOfNotional <= 0)
        {
            errors.Add($"UseNotionalGivebackCap is enabled but GivebackPctOfNotional must be > 0 (was {config.GivebackPctOfNotional}).");
        }

        // â”€â”€ Inverted ranges make entries impossible (contradictory; blocks all trades) â”€â”€
        if (config.MaxEntryAdx > 0 && config.MinEntryAdx > config.MaxEntryAdx)
        {
            errors.Add($"MinEntryAdx ({config.MinEntryAdx}) must not exceed MaxEntryAdx ({config.MaxEntryAdx}); no entry could ever pass.");
        }

        if (config.RsiLongRange.Low > config.RsiLongRange.High)
        {
            errors.Add($"RsiLongRange Low ({config.RsiLongRange.Low}) must not exceed High ({config.RsiLongRange.High}).");
        }

        if (config.RsiShortRange.Low > config.RsiShortRange.High)
        {
            errors.Add($"RsiShortRange Low ({config.RsiShortRange.Low}) must not exceed High ({config.RsiShortRange.High}).");
        }

        if (config.MinPrice > config.MaxPrice)
        {
            errors.Add($"MinPrice ({config.MinPrice}) must not exceed MaxPrice ({config.MaxPrice}); no symbol could ever qualify.");
        }

        // â”€â”€ Warnings (tolerated, surfaced for visibility) â”€â”€
        if (config.UseNotionalGivebackCap && config.UseVariableGivebackUsdCap)
        {
            warnings.Add("UseNotionalGivebackCap and UseVariableGivebackUsdCap are both enabled; notional cap takes precedence and the variable USD cap is ignored.");
        }

        if (config.Tp2R > 0 && config.Tp1R > config.Tp2R)
        {
            warnings.Add($"Tp1R ({config.Tp1R}) exceeds Tp2R ({config.Tp2R}); the second target is closer than the first.");
        }

        var summary = BuildResolvedSummary(config);
        return new StrategyConfigValidationResult(errors, warnings, summary);
    }

    /// <summary>Validate and throw <see cref="StrategyConfigValidationException"/> if any errors are present.</summary>
    public static StrategyConfigValidationResult ValidateAndThrow(StrategyConfig config)
    {
        var result = Validate(config);
        if (!result.IsValid)
        {
            throw new StrategyConfigValidationException(result.Errors);
        }

        return result;
    }

    private static string BuildResolvedSummary(StrategyConfig config)
        => $"StrategyConfig resolved: risk=${config.RiskPerTradeDollars} maxShares={config.MaxShares} "
           + $"hardStopR={config.HardStopR} beR={config.BreakevenR} trailR={config.TrailR} "
           + $"tp1R={config.Tp1R}/{config.Tp1ScalePct:P0} tp2R={config.Tp2R} "
           + $"givebackPct={config.GivebackPct} notionalCap={config.UseNotionalGivebackCap} "
           + $"variableUsdCap={config.UseVariableGivebackUsdCap} tightTrail={config.UseTightTrailOnFixedGiveback} "
           + $"adx>={config.AdxThreshold} rvolMin={config.RvolMin} requireSupertrend={config.RequireSupertrend} "
           + $"requireL2={config.RequireL2EntryConfirmation} maxHoldBars={config.MaxHoldBars}";
}

