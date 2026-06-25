namespace Sailor.App.Backtest;

/// <summary>
/// Phase 3.8 â€” immutable projection of the entry-filter fields on <see cref="StrategyConfig"/>.
/// Additive, read-only view used to consolidate entry-gating configuration across strategies.
/// It performs no normalization and changes no behavior: every field maps 1:1 to the source config.
/// </summary>
public sealed record EntryFilterProfile(
    double AdxThreshold,
    double MinEntryAdx,
    double MaxEntryAdx,
    (double Low, double High) RsiLongRange,
    (double Low, double High) RsiShortRange,
    double RvolMin,
    int PullbackEmaPeriod,
    bool RequireSupertrend,
    bool RequireMtfAlignment,
    bool RequireL2EntryConfirmation,
    bool UseRichL2EntryConfirmation,
    double RichL2ImbalanceMinForLong,
    double RichL2ImbalanceMaxForShort,
    double RichL2DeepImbalanceMinForLong,
    double RichL2DeepImbalanceMaxForShort,
    double RichL1SizeRatioMinForLong,
    double RichL1SizeRatioMaxForShort,
    bool IgnoreSelfLearningSetupBlock,
    IReadOnlyList<string> IgnoreSelfLearningSetupBlockSources,
    double MaxMaDistAtr,
    int MinEntryScore)
{
    /// <summary>Project the entry-filter fields of a <see cref="StrategyConfig"/> into an immutable profile.</summary>
    public static EntryFilterProfile FromStrategyConfig(StrategyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new EntryFilterProfile(
            AdxThreshold: config.AdxThreshold,
            MinEntryAdx: config.MinEntryAdx,
            MaxEntryAdx: config.MaxEntryAdx,
            RsiLongRange: config.RsiLongRange,
            RsiShortRange: config.RsiShortRange,
            RvolMin: config.RvolMin,
            PullbackEmaPeriod: config.PullbackEmaPeriod,
            RequireSupertrend: config.RequireSupertrend,
            RequireMtfAlignment: config.RequireMtfAlignment,
            RequireL2EntryConfirmation: config.RequireL2EntryConfirmation,
            UseRichL2EntryConfirmation: config.UseRichL2EntryConfirmation,
            RichL2ImbalanceMinForLong: config.RichL2ImbalanceMinForLong,
            RichL2ImbalanceMaxForShort: config.RichL2ImbalanceMaxForShort,
            RichL2DeepImbalanceMinForLong: config.RichL2DeepImbalanceMinForLong,
            RichL2DeepImbalanceMaxForShort: config.RichL2DeepImbalanceMaxForShort,
            RichL1SizeRatioMinForLong: config.RichL1SizeRatioMinForLong,
            RichL1SizeRatioMaxForShort: config.RichL1SizeRatioMaxForShort,
            IgnoreSelfLearningSetupBlock: config.IgnoreSelfLearningSetupBlock,
            IgnoreSelfLearningSetupBlockSources: config.IgnoreSelfLearningSetupBlockSources.ToArray(),
            MaxMaDistAtr: config.MaxMaDistAtr,
            MinEntryScore: config.MinEntryScore);
    }
}

