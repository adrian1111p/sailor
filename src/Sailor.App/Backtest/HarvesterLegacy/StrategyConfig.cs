namespace Sailor.App.Backtest;

/// <summary>
/// Tunable parameters for the Conduct strategy family.
/// Mirrors Python's StrategyConfig dataclass.
/// </summary>
public sealed class StrategyConfig
{
    public const int MinimumRequiredSignalHistoryBars = 7;

    // â”€â”€ Risk Sizing â”€â”€
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;
    public int MaxShares { get; set; } = 6_500;
    public double MinRiskPerShare { get; set; } = 0.01;
    public int MinimumSignalHistoryBars { get; set; } = MinimumRequiredSignalHistoryBars;
    public int FirstSignalEvaluationIndex { get; set; } = MinimumRequiredSignalHistoryBars;
    public int CooldownBars { get; set; } = 1;
    public bool UseNextBarOpenEntry { get; set; } = true;
    public bool StrictMissingDataChecks { get; set; } = true;

    // â”€â”€ Entry Filters â”€â”€
    public double AdxThreshold { get; set; } = 12.0;
    public double MinEntryAdx { get; set; } = 0.0;
    public double MaxEntryAdx { get; set; } = 0.0;
    public (double Low, double High) RsiLongRange { get; set; } = (35.0, 70.0);
    public (double Low, double High) RsiShortRange { get; set; } = (30.0, 65.0);
    public double RvolMin { get; set; } = 1.3;
    public int PullbackEmaPeriod { get; set; } = 9;
    public bool RequireSupertrend { get; set; } = true;
    public bool RequireMtfAlignment { get; set; } = false;
    public bool RequireL2EntryConfirmation { get; set; } = true;
    public bool UseRichL2EntryConfirmation { get; set; } = false;
    public double RichL2ImbalanceMinForLong { get; set; } = 1.05;
    public double RichL2ImbalanceMaxForShort { get; set; } = 0.95;
    public double RichL2DeepImbalanceMinForLong { get; set; } = 1.10;
    public double RichL2DeepImbalanceMaxForShort { get; set; } = 0.90;
    public double RichL1SizeRatioMinForLong { get; set; } = 1.05;
    public double RichL1SizeRatioMaxForShort { get; set; } = 0.95;
    public bool IgnoreSelfLearningSetupBlock { get; set; } = false;
    public List<string> IgnoreSelfLearningSetupBlockSources { get; set; } = [];

    // â”€â”€ Price & Time Filters â”€â”€
    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;
        public List<(int Start, int End)> EntryWindows { get; set; } = [(570, 955)]; // 9:30-15:55 ET
    public int SkipFirstNMinutes { get; set; } = 5;
    public int MarketOpenMinute { get; set; } = 570; // 9:30 ET

    // â”€â”€ Alternate Entry Modes â”€â”€
    public bool VwapReversionEnabled { get; set; } = false;
    public bool AllowAlternateEntriesAfterRejectedMainCandidates { get; set; } = false;
    public double VwapStretchAtr { get; set; } = 1.0;
    public bool BbBounceEnabled { get; set; } = false;
    public double BbEntryPctbLow { get; set; } = 0.05;
    public double BbEntryPctbHigh { get; set; } = 0.95;
    public double MainLongMaxBbPctB { get; set; } = 1.0;
    public double MainShortMinBbPctB { get; set; } = 0.0;
    public double MainEntryMaxVwapDeviationAtr { get; set; } = 0.0;
    public double PullbackRvolMin { get; set; } = 0.0;
    public int PullbackReentryCooldownBars { get; set; } = 0;
    public bool AlternateEntryRequireRsiExtreme { get; set; } = false;
    public double AlternateLongRsiMax { get; set; } = 42.0;
    public double AlternateShortRsiMin { get; set; } = 58.0;
    public bool AlternateEntryRequireReversalCandle { get; set; } = false;
    public double AlternateEntryMaxAdx { get; set; } = 0.0;
    public double AlternateEntryMaxCountertrendMaDistAtr { get; set; } = 0.0;
    public double AlternateEntryMaxVwapStretchAtr { get; set; } = 0.0;
    public double AlternateVwapLongMinRiskPerShare { get; set; } = 0.0;

    // â”€â”€ Exit Rules â”€â”€
    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 1.0;
    public double TrailR { get; set; } = 0.15;
    public double GivebackPct { get; set; } = 0.60;
    public bool UseNotionalGivebackCap { get; set; } = false;
    public bool UseVariableGivebackUsdCap { get; set; } = true;
    public bool UseTightTrailOnFixedGiveback { get; set; } = true;
    public double GivebackPctOfNotional { get; set; } = 0.01;
    public double GivebackUsdCap { get; set; } = 30.0;
    public double Tp1R { get; set; } = 0.73;
    public double Tp1ScalePct { get; set; } = 0.50;
    public double Tp1BreakevenBufferAtr { get; set; } = 0.0;
    public double Tp2R { get; set; } = 2.04;
    public bool UseContinuationTp2ScaleOut { get; set; } = false;
    public double ContinuationTp2ScalePct { get; set; } = 0.50;
    public bool UseTrailingTp2 { get; set; } = false;
    public double TrailingTp2AtrMultiplier { get; set; } = 0.50;
    public bool UseL1L2DecisionOnOppositeBarsFlatten { get; set; } = false;
    public int MaxHoldBars { get; set; } = 180;      // 180 Ã— 1min = 180 min (3h)
    public int EodBarMinute { get; set; } = 955;      // 15:55 ET

    // â”€â”€ 20MA Exhaustion Filter (V2.0) â”€â”€
    public double MaxMaDistAtr { get; set; } = 0.5;
    public int MinEntryScore { get; set; } = 0;
    public int SupertrendFlipTriggerPoints { get; set; } = 8;
    public int EmaPullbackTriggerPoints { get; set; } = 5;
    public int VwapReversionTriggerPoints { get; set; } = 4;
    public int BbBounceTriggerPoints { get; set; } = 4;
    public int Sma20TrendAlignedPoints { get; set; } = 20;
    public int MainBbFavorablePoints { get; set; } = 0;
    public int MainVwapProximityPoints { get; set; } = 0;
    public int AlternateRsiExtremePoints { get; set; } = 0;
    public int AlternateReversalCandlePoints { get; set; } = 0;
    public int AlternateContainedStretchPoints { get; set; } = 0;

    // â”€â”€ Slippage & Commission â”€â”€
    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;

    /// <summary>Phase 3.8 â€” project the exit-related fields into an immutable <see cref="ExitProfile"/>.</summary>
    public ExitProfile ToExitProfile() => ExitProfile.FromStrategyConfig(this);

    /// <summary>Phase 3.8 â€” project the entry-filter fields into an immutable <see cref="EntryFilterProfile"/>.</summary>
    public EntryFilterProfile ToEntryFilterProfile() => EntryFilterProfile.FromStrategyConfig(this);
}

