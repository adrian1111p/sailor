namespace Sailor.App.Backtest;

/// <summary>
/// Phase 3.8 â€” immutable projection of the exit-related fields on <see cref="StrategyConfig"/>.
/// This is an additive, read-only view used to consolidate exit configuration across strategies.
/// It performs no normalization and changes no behavior: every field maps 1:1 to the source config
/// so a strategy composing this profile behaves byte-for-byte identically to one reading the raw config.
/// </summary>
public sealed record ExitProfile(
    double HardStopR,
    double BreakevenR,
    double TrailR,
    double GivebackPct,
    bool UseNotionalGivebackCap,
    bool UseVariableGivebackUsdCap,
    bool UseTightTrailOnFixedGiveback,
    double GivebackPctOfNotional,
    double GivebackUsdCap,
    double Tp1R,
    double Tp1ScalePct,
    double Tp1BreakevenBufferAtr,
    double Tp2R,
    bool UseContinuationTp2ScaleOut,
    double ContinuationTp2ScalePct,
    bool UseTrailingTp2,
    double TrailingTp2AtrMultiplier,
    bool UseL1L2DecisionOnOppositeBarsFlatten,
    int MaxHoldBars,
    int EodBarMinute)
{
    /// <summary>Project the exit-related fields of a <see cref="StrategyConfig"/> into an immutable profile.</summary>
    public static ExitProfile FromStrategyConfig(StrategyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new ExitProfile(
            HardStopR: config.HardStopR,
            BreakevenR: config.BreakevenR,
            TrailR: config.TrailR,
            GivebackPct: config.GivebackPct,
            UseNotionalGivebackCap: config.UseNotionalGivebackCap,
            UseVariableGivebackUsdCap: config.UseVariableGivebackUsdCap,
            UseTightTrailOnFixedGiveback: config.UseTightTrailOnFixedGiveback,
            GivebackPctOfNotional: config.GivebackPctOfNotional,
            GivebackUsdCap: config.GivebackUsdCap,
            Tp1R: config.Tp1R,
            Tp1ScalePct: config.Tp1ScalePct,
            Tp1BreakevenBufferAtr: config.Tp1BreakevenBufferAtr,
            Tp2R: config.Tp2R,
            UseContinuationTp2ScaleOut: config.UseContinuationTp2ScaleOut,
            ContinuationTp2ScalePct: config.ContinuationTp2ScalePct,
            UseTrailingTp2: config.UseTrailingTp2,
            TrailingTp2AtrMultiplier: config.TrailingTp2AtrMultiplier,
            UseL1L2DecisionOnOppositeBarsFlatten: config.UseL1L2DecisionOnOppositeBarsFlatten,
            MaxHoldBars: config.MaxHoldBars,
            EodBarMinute: config.EodBarMinute);
    }
}

