namespace Sailor.App.Backtest.Strategies;

internal static class StrategyV16VariantFactory
{
    public const string FloorBalancedPlusVariantName = "floor-balanced-plus";
    public const string TimeContextAdaptiveVariantName = "floor-balanced-plus-time-context-adaptive";
    public const string DepthAdaptiveVariantName = "floor-balanced-plus-depth-adaptive";
    public const string VolNormalizedVariantName = "floor-balanced-plus-vol-normalized";
    public const string SymbolContextAdaptiveVariantName = "floor-balanced-plus-symbol-context-adaptive";
    public const string HardStopPreemptVariantName = "floor-balanced-plus-hardstop-preempt";
    public const string Loss20VariantName = "floor-balanced-plus-loss20";

    public static string NormalizeVariantName(string? variantName)
        => string.IsNullOrWhiteSpace(variantName)
            ? TimeContextAdaptiveVariantName
            : variantName.Trim().ToLowerInvariant();

    public static V16Config CreateVariant(string? variantName)
        => NormalizeVariantName(variantName) switch
        {
            TimeContextAdaptiveVariantName => CreateFloorBalancedPlusBase() with
            {
                DiagnosticsLabel = TimeContextAdaptiveVariantName,
                EnableTimeContextAdaptation = true,
                TimeContextWeakHoursUtc = [15, 16, 18, 19],
                TimeContextWeakHourScoreBoost = 2,
                TimeContextWeakHourPositionScale = 0.58,
                TimeContextLateSessionScoreBoost = 2,
                TimeContextLateSessionPositionScale = 0.62,
                TimeContextOpeningPositionScale = 1.05,
                ShortRetryLockoutBarsAfterLowExcursionHardStop = 12,
                ShortRetryLockoutMaxPeakR = 0.30,
            },
            DepthAdaptiveVariantName => CreateFloorBalancedPlusBase() with
            {
                DiagnosticsLabel = DepthAdaptiveVariantName,
                EnableDepthContextAdaptation = true,
                DepthContextPoorBookScoreBoost = 2,
                DepthContextPoorBookPositionScale = 0.58,
                ShortRetryLockoutBarsAfterLowExcursionHardStop = 10,
            },
            VolNormalizedVariantName => CreateFloorBalancedPlusBase() with
            {
                DiagnosticsLabel = VolNormalizedVariantName,
                EnableVolatilityNormalizedSizing = true,
                VolatilityNormalizedHighAtrPct = 0.06,
                VolatilityNormalizedLowAtrPct = 0.012,
                VolatilityNormalizedHighAtrPositionScale = 0.54,
                VolatilityNormalizedLowAtrPositionScale = 0.78,
                ShortRetryLockoutBarsAfterLowExcursionHardStop = 10,
            },
            SymbolContextAdaptiveVariantName => CreateFloorBalancedPlusBase() with
            {
                DiagnosticsLabel = SymbolContextAdaptiveVariantName,
                EnableSymbolContextAdaptation = true,
                SymbolContextFragileScoreBoost = 2,
                SymbolContextFragilePositionScale = 0.55,
                ShortRetryLockoutBarsAfterLowExcursionHardStop = 10,
            },
            HardStopPreemptVariantName => CreateFloorBalancedPlusBase() with
            {
                DiagnosticsLabel = HardStopPreemptVariantName,
                EnableHardStopPreemptAdaptation = true,
                EnableTimeContextAdaptation = true,
                TimeContextWeakHoursUtc = [15, 16, 18, 19],
                TimeContextWeakHourScoreBoost = 1,
                TimeContextWeakHourPositionScale = 0.68,
                HardStopPreemptScoreBoost = 2,
                HardStopPreemptPositionScale = 0.48,
                HardStopPreemptRiskScale = 0.62,
                HardStopPreemptStopDistanceMultiplier = 1.22,
                GivebackPct = 0.30,
                PeakGivebackKeepFraction = 0.78,
                StagnationBars = 6,
                ShortRetryLockoutBarsAfterLowExcursionHardStop = 14,
                ShortRetryLockoutMaxPeakR = 0.35,
            },
            Loss20VariantName => CreateFloorBalancedPlusBase() with
            {
                DiagnosticsLabel = Loss20VariantName,
                SymbolLossFlattenUsd = 20.0,
            },
            _ => CreateVariant(TimeContextAdaptiveVariantName),
        };

    public static V16Config CreateFloorBalancedPlusBase()
        => new()
        {
            DiagnosticsLabel = FloorBalancedPlusVariantName,
            RespectSelfLearningExitOverrides = false,
            MinRiskPerShare = 0.03,
            MinConfluenceScore = 1,
            LongMinConfluenceScore = 1,
            SqueezeMinBars = 1,
            SqueezeLookback = 14,
            SqueezeReleaseMaxBars = 12,
            SqueezeBandwidthMaxPctile = 0.85,
            BreakoutDirectionMinScore = 2,
            RequireBullishBreakoutBarForLongs = true,
            LongBreakoutMinCloseLocationPct = 0.50,
            LongBreakoutMaxUpperWickPct = 0.45,
            RequireHtfBias = false,
            AllowWeakCounterTrendHtf = true,
            RequireL2EntryFilter = false,
            SkipFirstNMinutes = 0,
            LastEntryMinuteBeforeClose = 15,
            EntryWindows = [(570, 950)],
            MaxSignalsPerDay = 12,
            CooldownBars = 0,
            RvolMin = 0.15,
            L2LiquidityMin = 0.0,
            SpreadZMax = 7.0,
            MinVolAccel = -0.90,
            AdxMin = 0.0,
            HardStopR = 0.60,
            BreakevenR = 0.25,
            TrailR = 0.16,
            GivebackPct = 0.34,
            GivebackUsdCap = 22.0,
            Tp1R = 1.10,
            Tp2R = 1.90,
            MaxHoldBars = 26,
            PeakGivebackKeepFraction = 0.70,
            PeakGivebackActivateR = 0.25,
            StagnationBars = 7,
            StagnationMinPeakR = 0.30,
            StagnationMaxAdverseR = -0.10,
            Tp1PartialClosePct = 0.25,
            Tp1BreakevenBufferAtr = 0.05,
            UseEmaTrail = true,
            EmaTrailBufferAtr = 0.20,
            UseTrailingTp2 = true,
            TrailingTp2AtrMultiplier = 0.80,
            UseL1L2DecisionOnOppositeBarsFlatten = true,
            LongRetryLockoutBarsAfterLowExcursionHardStop = 15,
            LongRetryLockoutMaxPeakR = 0.25,
            ShortRetryLockoutBarsAfterLowExcursionHardStop = 8,
            ShortRetryLockoutMaxPeakR = 0.25,
            RejectOverextendedLongContinuationForLongs = true,
            LongContinuationMaxRsi = 85.0,
            LongContinuationPriorBarMinRangeAtr = 2.5,
            LongContinuationPriorBarMinRvol = 4.0,
            MaExtensionMinR = 0.20,
            MaExtensionAtrThreshold = 1.25,
        };
}

