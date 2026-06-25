using Sailor.App.Backtest.Engine;
using Harvester.App.Strategy;
using Harvester.App.Strategy.Conduct;

namespace Sailor.App.Backtest;

/// <summary>
/// Common exit-simulation engine used by all strategy variants.
/// Factors out the shared exit chain (HardStop â†’ TP2 â†’ TP1 â†’ BE â†’ Trail â†’ Giveback â†’ TimeStop)
/// with optional micro-trail, 9EMA trail, and reversal-flatten features.
/// </summary>
public static class ExitEngine
{
    /// <summary>Configuration for the exit simulation.</summary>
    public sealed class ExitConfig
    {
        // â”€â”€ Core exit parameters â”€â”€
        public double HardStopR { get; init; } = 1.0;
        public double BreakevenR { get; init; } = 1.0;
        public double TrailR { get; init; } = 0.5;
        public double GivebackPct { get; init; } = 0.50;
        public double Tp1R { get; init; } = 1.5;
        public double Tp2R { get; init; } = 3.0;
        public bool UseContinuationTp2ScaleOut { get; init; } = false;
        public double ContinuationTp2ScalePct { get; init; } = 0.50;
        public bool UseTrailingTp2 { get; init; } = false;
        public double TrailingTp2AtrMultiplier { get; init; } = 0.50;
        public int MaxHoldBars { get; init; } = 180;

        // â”€â”€ Giveback threshold (minimum peak_r before checking giveback) â”€â”€
        public double GivebackMinPeakR { get; init; } = 0.0;
        public bool UseFixedGivebackUsdCap { get; init; } = false;
        public bool UseNotionalGivebackCap { get; init; } = false;
        public double GivebackPctOfNotional { get; init; } = 0.01;
        public double GivebackUsdCap { get; init; } = 38.0;
        public bool UseVariableGivebackUsdCap { get; init; } = true;
        public double GivebackCapAnchorLowPrice { get; init; } = 1.0;
        public double GivebackCapAnchorHighPrice { get; init; } = 300.0;
        public double GivebackCapAtLowPrice { get; init; } = 8.0;
        public double GivebackCapAtHighPrice { get; init; } = 30.0;
        public double GivebackCapMinUsd { get; init; } = 5.0;
        public double GivebackCapMaxUsd { get; init; } = 60.0;
        public bool UseTightTrailOnFixedGiveback { get; init; } = true;
        public double TightTrailAnchorLowPrice { get; init; } = 1.0;
        public double TightTrailAnchorHighPrice { get; init; } = 300.0;
        public double TightTrailAtLowPrice { get; init; } = 0.30;
        public double TightTrailAtHighPrice { get; init; } = 1.00;

        // â”€â”€ Slippage & Commission â”€â”€
        public double SlippageCents { get; init; } = 1.0;
        public double CommissionPerShare { get; init; } = 0.005;
        public bool DeductCommission { get; init; } = true;

        // â”€â”€ Optional features â”€â”€
        public double Tp1PartialClosePct { get; init; } = 0.0;
        public bool Tp1TightenToBe { get; init; } = true;
        public double Tp1BreakevenBufferAtr { get; init; } = 0.0;
        public bool ReversalFlatten { get; init; } = false;
        public bool MicroTrail { get; init; } = false;
        public double MicroTrailCents { get; init; } = 3.0;
        public double MicroTrailActivateCents { get; init; } = 5.0;
        public bool EmaTrail { get; init; } = false;
        public double EmaTrailBufferAtr { get; init; } = 0.1;

        // â”€â”€ Optional protective exits â”€â”€
        public bool FlattenOnEntryLossCross { get; init; } = false;
        public double EntryLossBufferCents { get; init; } = 0.0;
        public bool EntryLossFlattenUseMarketPrice { get; init; } = false;
        public double SymbolLossFlattenUsd { get; init; } = 0.0;
        public bool FlattenOnPeakGiveback { get; init; } = false;
        public double PeakGivebackKeepFraction { get; init; } = 2.0 / 3.0;
        public double PeakGivebackActivateR { get; init; } = 0.15;
        public bool FlattenOnStagnation { get; init; } = false;
        public int StagnationBars { get; init; } = 3;
        public double StagnationMinPeakR { get; init; } = 0.15;
        public double StagnationMaxAdverseR { get; init; } = -0.10;

        // â”€â”€ P2: Price-tier micro trail on winners â”€â”€
        public bool UsePriceTierMicroTrail { get; init; } = false;

        // â”€â”€ P3: Price-tier hard stop floor â”€â”€
        public bool UsePriceTierStopFloor { get; init; } = false;

        // â”€â”€ P4: 20MA extension + L2 flip exit â”€â”€
        public bool UseMaExtensionL2Flip { get; init; } = false;
        public double MaExtensionMinR { get; init; } = 0.30;
        public double MaExtensionAtrThreshold { get; init; } = 1.50;
        public bool UseL1L2DecisionOnOppositeBarsFlatten { get; init; } = false;

        /// <summary>
        /// Creates a new ExitConfig with non-null override values applied.
        /// All other properties preserve their current values.
        /// </summary>
        public ExitConfig WithOverrides(SelfLearningV3ExitOverrides overrides)
        {
            if (overrides == null || !overrides.HasAnyOverride)
                return this;

            return new ExitConfig
            {
                HardStopR = overrides.HardStopR ?? HardStopR,
                BreakevenR = overrides.BreakevenR ?? BreakevenR,
                TrailR = overrides.TrailR ?? TrailR,
                GivebackPct = overrides.GivebackPct ?? GivebackPct,
                Tp1R = overrides.Tp1R ?? Tp1R,
                Tp2R = overrides.Tp2R ?? Tp2R,
                UseContinuationTp2ScaleOut = UseContinuationTp2ScaleOut,
                ContinuationTp2ScalePct = ContinuationTp2ScalePct,
                UseTrailingTp2 = UseTrailingTp2,
                TrailingTp2AtrMultiplier = TrailingTp2AtrMultiplier,
                MaxHoldBars = overrides.MaxHoldBars ?? MaxHoldBars,
                GivebackMinPeakR = GivebackMinPeakR,
                UseFixedGivebackUsdCap = UseFixedGivebackUsdCap,
                UseNotionalGivebackCap = UseNotionalGivebackCap,
                GivebackPctOfNotional = GivebackPctOfNotional,
                GivebackUsdCap = GivebackUsdCap,
                UseVariableGivebackUsdCap = UseVariableGivebackUsdCap,
                GivebackCapAnchorLowPrice = GivebackCapAnchorLowPrice,
                GivebackCapAnchorHighPrice = GivebackCapAnchorHighPrice,
                GivebackCapAtLowPrice = GivebackCapAtLowPrice,
                GivebackCapAtHighPrice = GivebackCapAtHighPrice,
                GivebackCapMinUsd = GivebackCapMinUsd,
                GivebackCapMaxUsd = GivebackCapMaxUsd,
                UseTightTrailOnFixedGiveback = UseTightTrailOnFixedGiveback,
                TightTrailAnchorLowPrice = TightTrailAnchorLowPrice,
                TightTrailAnchorHighPrice = TightTrailAnchorHighPrice,
                TightTrailAtLowPrice = TightTrailAtLowPrice,
                TightTrailAtHighPrice = TightTrailAtHighPrice,
                SlippageCents = SlippageCents,
                CommissionPerShare = CommissionPerShare,
                DeductCommission = DeductCommission,
                Tp1PartialClosePct = Tp1PartialClosePct,
                Tp1TightenToBe = Tp1TightenToBe,
                Tp1BreakevenBufferAtr = Tp1BreakevenBufferAtr,
                ReversalFlatten = overrides.ReversalFlatten ?? ReversalFlatten,
                MicroTrail = overrides.MicroTrail ?? MicroTrail,
                MicroTrailCents = overrides.MicroTrailCents ?? MicroTrailCents,
                MicroTrailActivateCents = overrides.MicroTrailActivateCents ?? MicroTrailActivateCents,
                EmaTrail = overrides.EmaTrail ?? EmaTrail,
                EmaTrailBufferAtr = overrides.EmaTrailBufferAtr ?? EmaTrailBufferAtr,
                FlattenOnEntryLossCross = FlattenOnEntryLossCross,
                EntryLossBufferCents = EntryLossBufferCents,
                EntryLossFlattenUseMarketPrice = EntryLossFlattenUseMarketPrice,
                SymbolLossFlattenUsd = SymbolLossFlattenUsd,
                FlattenOnPeakGiveback = overrides.FlattenOnPeakGiveback ?? FlattenOnPeakGiveback,
                PeakGivebackKeepFraction = overrides.PeakGivebackKeepFraction ?? PeakGivebackKeepFraction,
                PeakGivebackActivateR = overrides.PeakGivebackActivateR ?? PeakGivebackActivateR,
                FlattenOnStagnation = overrides.FlattenOnStagnation ?? FlattenOnStagnation,
                StagnationBars = overrides.StagnationBars ?? StagnationBars,
                StagnationMinPeakR = StagnationMinPeakR,
                StagnationMaxAdverseR = StagnationMaxAdverseR,
                UsePriceTierMicroTrail = UsePriceTierMicroTrail,
                UsePriceTierStopFloor = UsePriceTierStopFloor,
                UseMaExtensionL2Flip = UseMaExtensionL2Flip,
                MaExtensionMinR = MaExtensionMinR,
                MaExtensionAtrThreshold = MaExtensionAtrThreshold,
                UseL1L2DecisionOnOppositeBarsFlatten = UseL1L2DecisionOnOppositeBarsFlatten,
            };
        }
    }

    /// <summary>
    /// Run the shared exit simulation for a single trade.
    /// </summary>
    public static BacktestNormalizedExitProfile ToNormalizedExitProfile(ExitConfig cfg) => new(
        HardStopR: cfg.HardStopR,
        BreakevenR: cfg.BreakevenR,
        TrailR: cfg.TrailR,
        GivebackPct: cfg.GivebackPct,
        Tp1R: cfg.Tp1R,
        Tp2R: cfg.Tp2R,
        UseContinuationTp2ScaleOut: cfg.UseContinuationTp2ScaleOut,
        ContinuationTp2ScalePct: cfg.ContinuationTp2ScalePct,
        UseTrailingTp2: cfg.UseTrailingTp2,
        TrailingTp2AtrMultiplier: cfg.TrailingTp2AtrMultiplier,
        MaxHoldBars: cfg.MaxHoldBars,
        GivebackMinPeakR: cfg.GivebackMinPeakR,
        UseFixedGivebackUsdCap: cfg.UseFixedGivebackUsdCap,
        UseNotionalGivebackCap: cfg.UseNotionalGivebackCap,
        GivebackPctOfNotional: cfg.GivebackPctOfNotional,
        GivebackUsdCap: cfg.GivebackUsdCap,
        UseVariableGivebackUsdCap: cfg.UseVariableGivebackUsdCap,
        GivebackCapAnchorLowPrice: cfg.GivebackCapAnchorLowPrice,
        GivebackCapAnchorHighPrice: cfg.GivebackCapAnchorHighPrice,
        GivebackCapAtLowPrice: cfg.GivebackCapAtLowPrice,
        GivebackCapAtHighPrice: cfg.GivebackCapAtHighPrice,
        GivebackCapMinUsd: cfg.GivebackCapMinUsd,
        GivebackCapMaxUsd: cfg.GivebackCapMaxUsd,
        UseTightTrailOnFixedGiveback: cfg.UseTightTrailOnFixedGiveback,
        TightTrailAnchorLowPrice: cfg.TightTrailAnchorLowPrice,
        TightTrailAnchorHighPrice: cfg.TightTrailAnchorHighPrice,
        TightTrailAtLowPrice: cfg.TightTrailAtLowPrice,
        TightTrailAtHighPrice: cfg.TightTrailAtHighPrice,
        SlippageCents: cfg.SlippageCents,
        CommissionPerShare: cfg.CommissionPerShare,
        DeductCommission: cfg.DeductCommission,
        Tp1PartialClosePct: cfg.Tp1PartialClosePct,
        Tp1TightenToBe: cfg.Tp1TightenToBe,
        Tp1BreakevenBufferAtr: cfg.Tp1BreakevenBufferAtr,
        ReversalFlatten: cfg.ReversalFlatten,
        MicroTrail: cfg.MicroTrail,
        MicroTrailCents: cfg.MicroTrailCents,
        MicroTrailActivateCents: cfg.MicroTrailActivateCents,
        EmaTrail: cfg.EmaTrail,
        EmaTrailBufferAtr: cfg.EmaTrailBufferAtr,
        FlattenOnEntryLossCross: cfg.FlattenOnEntryLossCross,
        EntryLossBufferCents: cfg.EntryLossBufferCents,
        EntryLossFlattenUseMarketPrice: cfg.EntryLossFlattenUseMarketPrice,
        FlattenOnPeakGiveback: cfg.FlattenOnPeakGiveback,
        PeakGivebackKeepFraction: cfg.PeakGivebackKeepFraction,
        PeakGivebackActivateR: cfg.PeakGivebackActivateR,
        FlattenOnStagnation: cfg.FlattenOnStagnation,
        StagnationBars: cfg.StagnationBars,
        StagnationMinPeakR: cfg.StagnationMinPeakR,
        StagnationMaxAdverseR: cfg.StagnationMaxAdverseR,
        UsePriceTierMicroTrail: cfg.UsePriceTierMicroTrail,
        UsePriceTierStopFloor: cfg.UsePriceTierStopFloor,
        UseMaExtensionL2Flip: cfg.UseMaExtensionL2Flip,
        MaExtensionMinR: cfg.MaExtensionMinR,
        MaExtensionAtrThreshold: cfg.MaExtensionAtrThreshold,
        UseL1L2DecisionOnOppositeBarsFlatten: cfg.UseL1L2DecisionOnOppositeBarsFlatten,
        SymbolLossFlattenUsd: cfg.SymbolLossFlattenUsd);

    public static BacktestTradeResult SimulateTrade(
        BacktestSignal signal,
        EnrichedBar[] triggerBars,
        ExitConfig cfg,
        BacktestSelectedEntryIntent? selectedEntryIntent = null)
        => SimulateTradeLifecycle(signal, triggerBars, cfg, selectedEntryIntent).Trade;

    public static BacktestTradeLifecycleResult SimulateTradeLifecycle(
        BacktestSignal signal,
        EnrichedBar[] triggerBars,
        ExitConfig cfg,
        BacktestSelectedEntryIntent? selectedEntryIntent = null)
    {
        var replayActions = new List<BacktestTradeAction>();
        var resolvedSelectedEntryIntent = selectedEntryIntent ?? CreateDefaultSelectedEntryIntent(signal, cfg);
        var side = signal.Side;
        double entryPrice = signal.EntryPrice
            + (side == TradeSide.Long ? cfg.SlippageCents / 100.0 : -cfg.SlippageCents / 100.0);
        double stopPrice = signal.StopPrice;
        double riskPerShare = signal.RiskPerShare;
        int originalPositionSize = signal.PositionSize;
        int openPositionSize = originalPositionSize;
        double realizedGrossPnl = 0.0;
        double realizedExitNotional = 0.0;

        double peakPrice = entryPrice;
        double troughPrice = entryPrice;
        bool beActivated = false;
        bool tp1Activated = false;
        double trailingStop = stopPrice;
        ExitReason exitReason = ExitReason.TimeStop;
        double exitPrice = entryPrice;
        int exitBar = signal.BarIndex;
        bool exited = false;

        bool tightGivebackTrailActive = false;
        double tightGivebackStop = double.NaN;
        bool peakGivebackArmed = false;
        bool priceTierMicroTrailArmed = false;
        bool trailingStopArmed = false;
        int consecutiveOppositeBars = 0;
        bool profitExtensionArmed = false;
        bool continuationTp2ScaleOutTaken = false;
        bool trailingTp2Active = false;
        double trailingTp2Stop = double.NaN;

        var lifecycleEvents = new List<BacktestTradeLifecycleEvent>
        {
            new(
                BacktestTradeLifecycleEventType.EntryAccepted,
                signal.BarIndex,
                signal.Timestamp,
                signal.EntryPrice,
                originalPositionSize,
                Reason: "synthetic-entry-accepted",
                Detail: "Backtest accepted the execution-ready signal for immediate synthetic fill."),
            new(
                BacktestTradeLifecycleEventType.EntryFilled,
                signal.BarIndex,
                signal.Timestamp,
                entryPrice,
                originalPositionSize,
                Reason: "synthetic-entry-filled",
                Detail: "Backtest projects an immediate full entry fill at the configured synthetic entry price.")
        };

        int lastBar = Math.Min(signal.BarIndex + cfg.MaxHoldBars + 1, triggerBars.Length);

        // Pre-compute the base canonical params once (only giveback fields vary per bar)
        var baseParams = new ExitCascadeParams
        {
            HardStopR = cfg.HardStopR,
            BreakevenR = cfg.BreakevenR,
            TrailR = cfg.TrailR,
            Tp1R = cfg.Tp1R,
            Tp2R = cfg.Tp2R,
            Tp1PartialClosePct = cfg.Tp1PartialClosePct,
            MaxHoldSeconds = cfg.MaxHoldBars * 60,
            TimeStopMinProgressR = double.MaxValue,
            Tp1TightenToBe = cfg.Tp1TightenToBe,
            Tp1BreakevenBufferAtr = cfg.Tp1BreakevenBufferAtr,
            MicroTrailCents = cfg.MicroTrail ? cfg.MicroTrailCents : 0.0,
            MicroTrailActivateCents = cfg.MicroTrail ? cfg.MicroTrailActivateCents : 0.0,
            CheckHardStop = true,
            ReversalFlatten = cfg.ReversalFlatten,
        };

        for (int j = signal.BarIndex + 1; j < lastBar; j++)
        {
            var row = triggerBars[j];
            var bar = row.Bar;
            double price = bar.Close;
            double high = bar.High;
            double low = bar.Low;
            double peakPriceBeforeBar = peakPrice;
            double troughPriceBeforeBar = troughPrice;
            var continuationConfirmed = DirectionalConfirmationEngine.HasStrictDirectionalContinuation(row, side);
            double monitoringR = riskPerShare > 0
                ? (side == TradeSide.Long ? price - entryPrice : entryPrice - price) / riskPerShare
                : 0.0;

            lifecycleEvents.Add(new BacktestTradeLifecycleEvent(
                BacktestTradeLifecycleEventType.MonitoringStep,
                j,
                bar.Timestamp,
                price,
                openPositionSize,
                Reason: "monitoring-step",
                Detail: "Backtest evaluated the next monitoring bar against the canonical exit policy.",
                ReferencePrice: side == TradeSide.Long ? peakPrice : troughPrice,
                RMultiple: monitoringR));

            // Track peak/trough
            if (side == TradeSide.Long)
                peakPrice = Math.Max(peakPrice, high);
            else
                troughPrice = Math.Min(troughPrice, low);

            double currentPeakR = side == TradeSide.Long
                ? (peakPrice - entryPrice) / riskPerShare
                : (entryPrice - troughPrice) / riskPerShare;

            double unrealizedR = side == TradeSide.Long
                ? (price - entryPrice) / riskPerShare
                : (entryPrice - price) / riskPerShare;

            if (LegacyBacktestProtectivePolicy.TryEvaluateStatefulProtectivePolicies(
                signal,
                row,
                cfg,
                side,
                entryPrice,
                riskPerShare,
                peakPrice,
                troughPrice,
                peakPriceBeforeBar,
                troughPriceBeforeBar,
                stopPrice,
                continuationConfirmed,
                currentPeakR,
                ref tightGivebackTrailActive,
                ref tightGivebackStop,
                ref peakGivebackArmed,
                ref trailingTp2Active,
                ref trailingTp2Stop,
                ref profitExtensionArmed,
                replayActions,
                j,
                out var continueMonitoring,
                out exitPrice,
                out exitReason))
            {
                exitBar = j;
                exited = true;
                break;
            }

            if (continueMonitoring)
                continue;

            if (LegacyBacktestProtectivePolicy.TryEvaluateDirectProtectivePolicies(
                signal,
                triggerBars,
                row,
                cfg,
                side,
                entryPrice,
                stopPrice,
                riskPerShare,
                openPositionSize,
                peakPrice,
                troughPrice,
                currentPeakR,
                unrealizedR,
                ref priceTierMicroTrailArmed,
                ref consecutiveOppositeBars,
                replayActions,
                j,
                out exitPrice,
                out exitReason))
            {
                exitBar = j;
                exited = true;
                break;
            }

            // Reversal-flatten is now handled entirely by ExitCascadeEngine (step 3b)

            var emaAdjustedTrailingStop = trailingStop;
            if (cfg.EmaTrail && beActivated)
            {
                double ema9 = triggerBars[j].Ema9;
                double atr = triggerBars[j].Atr14;
                if (!double.IsNaN(ema9) && !double.IsNaN(atr))
                {
                    double emaStop = side == TradeSide.Long
                        ? ema9 - cfg.EmaTrailBufferAtr * atr
                        : ema9 + cfg.EmaTrailBufferAtr * atr;

                    if (side == TradeSide.Long)
                        emaAdjustedTrailingStop = Math.Max(emaAdjustedTrailingStop, emaStop);
                    else
                        emaAdjustedTrailingStop = Math.Min(emaAdjustedTrailingStop, emaStop);
                }
            }

            double effectiveGivebackUsdCap = 0.0;
            double peakRForGiveback = side == TradeSide.Long
                ? (peakPrice - entryPrice) / riskPerShare
                : (entryPrice - troughPrice) / riskPerShare;
            double effectiveGivebackPct = peakRForGiveback > cfg.GivebackMinPeakR
                ? cfg.GivebackPct
                : 1.01;

            if ((cfg.UseFixedGivebackUsdCap && cfg.GivebackUsdCap > 0) || cfg.UseNotionalGivebackCap)
            {
                if (cfg.UseNotionalGivebackCap)
                {
                    double positionNotional = Math.Abs(entryPrice * openPositionSize);
                    double notionalCap = Math.Max(0.0, cfg.GivebackPctOfNotional * positionNotional);
                    effectiveGivebackUsdCap = cfg.GivebackUsdCap > 0
                        ? Math.Min(notionalCap, cfg.GivebackUsdCap)
                        : notionalCap;
                }
                else
                {
                    effectiveGivebackUsdCap = cfg.UseVariableGivebackUsdCap
                        ? ExitCascadeEngine.ComputeVariableGivebackUsdCap(
                            price,
                            cfg.GivebackCapAnchorLowPrice, cfg.GivebackCapAnchorHighPrice,
                            cfg.GivebackCapAtLowPrice, cfg.GivebackCapAtHighPrice,
                            cfg.GivebackCapMinUsd, cfg.GivebackCapMaxUsd)
                        : cfg.GivebackUsdCap;
                }
            }

            var prevBeActivated = beActivated;
            var prevTp1Activated = tp1Activated;
            var prevTrailingStop = trailingStop;
            var prevStopPrice = stopPrice;
            var prevProfitExtensionArmed = profitExtensionArmed;
            var conductEvaluation = EvaluateSharedConduct(
                signal,
                row,
                j > signal.BarIndex + 1 ? triggerBars[j - 1] : null,
                side,
                cfg,
                entryPrice,
                riskPerShare,
                originalPositionSize,
                openPositionSize,
                stopPrice,
                emaAdjustedTrailingStop,
                peakPrice,
                troughPrice,
                beActivated,
                tp1Activated,
                prevProfitExtensionArmed,
                continuationConfirmed,
                continuationTp2ScaleOutTaken,
                effectiveGivebackPct,
                effectiveGivebackUsdCap,
                trailingTp2Active);

            if (side == TradeSide.Long)
                peakPrice = conductEvaluation.StatePatch.PeakPrice;
            else
                troughPrice = conductEvaluation.StatePatch.PeakPrice;

            stopPrice = conductEvaluation.StatePatch.StopPrice;
            trailingStop = conductEvaluation.StatePatch.TrailingStopPrice;
            beActivated = conductEvaluation.StatePatch.BreakevenActivated;
            tp1Activated = conductEvaluation.StatePatch.Tp1Activated;
            profitExtensionArmed = conductEvaluation.StatePatch.ProfitExtensionArmed;

            if (!prevProfitExtensionArmed && profitExtensionArmed)
            {
                replayActions.Add(new BacktestTradeAction(
                    BarIndex: j,
                    Timestamp: bar.Timestamp,
                    Price: price,
                    ActionType: "profit-extension-armed",
                    Description: "Deferred profit exit while candle, L1, and L2 still confirmed continuation",
                    ReferencePrice: price,
                    RMultiple: unrealizedR));
            }

            if (conductEvaluation.Decision.IsPartialExit)
            {
                int partialQuantity = Math.Clamp(conductEvaluation.Decision.Quantity, 1, Math.Max(1, openPositionSize - 1));
                double partialExitPrice = conductEvaluation.Decision.Price ?? price;
                double partialPnlPerShare = side == TradeSide.Long
                    ? partialExitPrice - entryPrice
                    : entryPrice - partialExitPrice;

                realizedGrossPnl += partialPnlPerShare * partialQuantity;
                realizedExitNotional += partialExitPrice * partialQuantity;
                openPositionSize -= partialQuantity;
                continuationTp2ScaleOutTaken |= string.Equals(conductEvaluation.Decision.Reason, "TP2_PARTIAL", StringComparison.OrdinalIgnoreCase);

                replayActions.Add(new BacktestTradeAction(
                    BarIndex: j,
                    Timestamp: bar.Timestamp,
                    Price: partialExitPrice,
                    ActionType: string.Equals(conductEvaluation.Decision.Reason, "TP2_PARTIAL", StringComparison.OrdinalIgnoreCase)
                        ? "tp2-partial-activated"
                        : "partial-exit",
                    Description: conductEvaluation.Decision.Detail ?? $"Partial exit qty={partialQuantity}",
                    ReferencePrice: price,
                    RMultiple: unrealizedR));

                lifecycleEvents.Add(new BacktestTradeLifecycleEvent(
                    BacktestTradeLifecycleEventType.PartialExit,
                    j,
                    bar.Timestamp,
                    partialExitPrice,
                    partialQuantity,
                    Reason: conductEvaluation.Decision.Reason ?? "TP1_PARTIAL",
                    Detail: conductEvaluation.Decision.Detail ?? $"Partial exit qty={partialQuantity}",
                    ReferencePrice: price,
                    RMultiple: unrealizedR));

                continue;
            }

            if (!prevBeActivated && beActivated)
            {
                replayActions.Add(new BacktestTradeAction(
                    BarIndex: j,
                    Timestamp: bar.Timestamp,
                    Price: stopPrice,
                    ActionType: "breakeven-armed",
                    Description: "Breakeven stop activated",
                    ReferencePrice: price,
                    RMultiple: unrealizedR));
            }

            if (!prevTp1Activated && tp1Activated)
            {
                replayActions.Add(new BacktestTradeAction(
                    BarIndex: j,
                    Timestamp: bar.Timestamp,
                    Price: price,
                    ActionType: "tp1-activated",
                    Description: "First profit target activation detected",
                    ReferencePrice: stopPrice,
                    RMultiple: unrealizedR));
            }

            if (Math.Abs(trailingStop - prevTrailingStop) > 1e-9)
            {
                if (!trailingStopArmed || Math.Abs(trailingStop - prevTrailingStop) >= Math.Max(0.01, riskPerShare * 0.10))
                {
                    replayActions.Add(new BacktestTradeAction(
                        BarIndex: j,
                        Timestamp: bar.Timestamp,
                        Price: trailingStop,
                        ActionType: trailingStopArmed ? "trail-update" : "trail-armed",
                        Description: trailingStopArmed ? "Trailing stop updated" : "Trailing stop armed",
                        ReferencePrice: prevTrailingStop == 0 ? prevStopPrice : prevTrailingStop,
                        RMultiple: unrealizedR));
                    trailingStopArmed = true;
                }
            }

            if (conductEvaluation.Decision.ShouldExit)
            {
                if (cfg.UseTightTrailOnFixedGiveback &&
                    string.Equals(conductEvaluation.Decision.Reason, "giveback-usd-cap", StringComparison.OrdinalIgnoreCase))
                {
                    replayActions.Add(new BacktestTradeAction(
                        BarIndex: j,
                        Timestamp: bar.Timestamp,
                        Price: trailingStop,
                        ActionType: "tight-giveback-trail-armed",
                        Description: "Giveback USD cap transitioned into tight giveback trail",
                        ReferencePrice: price,
                        RMultiple: unrealizedR));
                    tightGivebackTrailActive = true;
                    double trailPerShare = ExitCascadeEngine.ComputeTightGivebackTrailPerShare(
                        price,
                        cfg.TightTrailAnchorLowPrice, cfg.TightTrailAnchorHighPrice,
                        cfg.TightTrailAtLowPrice, cfg.TightTrailAtHighPrice);
                    double candidateStop = side == TradeSide.Long
                        ? peakPrice - trailPerShare
                        : troughPrice + trailPerShare;

                    if (double.IsNaN(tightGivebackStop))
                        tightGivebackStop = candidateStop;
                    else if (side == TradeSide.Long)
                        tightGivebackStop = Math.Max(tightGivebackStop, candidateStop);
                    else
                        tightGivebackStop = Math.Min(tightGivebackStop, candidateStop);

                    if ((side == TradeSide.Long && low <= tightGivebackStop) ||
                        (side == TradeSide.Short && high >= tightGivebackStop))
                    {
                        exitPrice = side == TradeSide.Long
                            ? Math.Min(bar.Open, tightGivebackStop)
                            : Math.Max(bar.Open, tightGivebackStop);
                        exitReason = ExitReason.Giveback;
                        exitBar = j; exited = true; break;
                    }

                    continue;
                }

                exitPrice = conductEvaluation.Decision.Price ?? price;
                exitReason = MapExitReason(conductEvaluation.Decision.Reason, cfg);
                exitBar = j; exited = true; break;
            }
        }

        // Exhausted bars â†’ force close
        if (!exited)
        {
            int forceBar = Math.Min(signal.BarIndex + cfg.MaxHoldBars, triggerBars.Length - 1);
            exitPrice = triggerBars[forceBar].Bar.Close;
            exitReason = ExitReason.TimeStop;
            exitBar = forceBar;
        }

        // Exit slippage
        if (side == TradeSide.Long) exitPrice -= cfg.SlippageCents / 100.0;
        else exitPrice += cfg.SlippageCents / 100.0;

        // PnL
        double pnlPerShare = side == TradeSide.Long
            ? exitPrice - entryPrice
            : entryPrice - exitPrice;
        double grossTotalPnl = realizedGrossPnl + (pnlPerShare * openPositionSize);
        double weightedExitPrice = originalPositionSize > 0
            ? (realizedExitNotional + (exitPrice * openPositionSize)) / originalPositionSize
            : exitPrice;
        double commission = cfg.DeductCommission ? cfg.CommissionPerShare * originalPositionSize * 2 : 0;
        double pnl = grossTotalPnl - commission;
        double commissionPerShare = cfg.DeductCommission ? cfg.CommissionPerShare * 2 : 0;
        double averageGrossPnlPerShare = originalPositionSize > 0 ? grossTotalPnl / originalPositionSize : 0.0;
        double pnlR = riskPerShare > 0 ? (averageGrossPnlPerShare - commissionPerShare) / riskPerShare : 0;

        double finalPeakR = side == TradeSide.Long
            ? (peakPrice - entryPrice) / riskPerShare
            : (entryPrice - troughPrice) / riskPerShare;

        foreach (var action in replayActions)
        {
            if (lifecycleEvents.Any(existing => existing.EventType == BacktestTradeLifecycleEventType.PartialExit
                && existing.BarIndex == action.BarIndex
                && existing.Timestamp == action.Timestamp
                && Math.Abs(existing.Price - action.Price) < 1e-9))
            {
                continue;
            }

            lifecycleEvents.Add(new BacktestTradeLifecycleEvent(
                action.ActionType is "partial-exit" or "tp2-partial-activated"
                    ? BacktestTradeLifecycleEventType.PartialExit
                    : BacktestTradeLifecycleEventType.StateTransition,
                action.BarIndex,
                action.Timestamp,
                action.Price,
                Reason: action.ActionType,
                Detail: action.Description,
                ReferencePrice: action.ReferencePrice,
                RMultiple: action.RMultiple));
        }

        var finalState = new BacktestTradeLifecycleState(
            OriginalQuantity: originalPositionSize,
            OpenQuantity: 0,
            PeakPrice: peakPrice,
            TroughPrice: troughPrice,
            StopPrice: stopPrice,
            TrailingStop: trailingStop,
            BreakevenActivated: beActivated,
            Tp1Activated: tp1Activated,
            ProfitExtensionArmed: profitExtensionArmed,
            ContinuationTp2ScaleOutTaken: continuationTp2ScaleOutTaken,
            TrailingTp2Active: trailingTp2Active,
            TrailingTp2Stop: double.IsNaN(trailingTp2Stop) ? null : trailingTp2Stop,
            GrossPnl: grossTotalPnl,
            WeightedExitPrice: weightedExitPrice);

        lifecycleEvents.Add(new BacktestTradeLifecycleEvent(
            BacktestTradeLifecycleEventType.TradeClosed,
            exitBar,
            triggerBars[exitBar].Bar.Timestamp,
            exitPrice,
            openPositionSize,
            Reason: exitReason.ToString(),
            Detail: $"Backtest closed the remaining position with {exitReason}.",
            ReferencePrice: weightedExitPrice,
            RMultiple: pnlR));

        lifecycleEvents.Add(new BacktestTradeLifecycleEvent(
            BacktestTradeLifecycleEventType.Finalized,
            exitBar,
            triggerBars[exitBar].Bar.Timestamp,
            weightedExitPrice,
            originalPositionSize,
            Reason: "trade-finalized",
            Detail: $"Backtest finalized the canonical trade result with weighted exit {weightedExitPrice:F4}.",
            ReferencePrice: exitPrice,
            RMultiple: pnlR));

        var orderedLifecycleEvents = lifecycleEvents
            .OrderBy(evt => evt.BarIndex)
            .ThenBy(evt => evt.Timestamp)
            .ToArray();

        var trade = new BacktestTradeResult(
            EntryBar: signal.BarIndex,
            EntryTime: signal.Timestamp,
            ExitBar: exitBar,
            ExitTime: triggerBars[exitBar].Bar.Timestamp,
            Side: side,
            EntryPrice: entryPrice,
            ExitPrice: weightedExitPrice,
            StopPrice: signal.StopPrice,
            PositionSize: originalPositionSize,
            Pnl: pnl,
            PnlR: pnlR,
            ExitReason: exitReason,
            PeakR: finalPeakR,
            BarsHeld: exitBar - signal.BarIndex,
            SubStrategy: signal.SubStrategy,
            ReplayActions: replayActions,
            SelectedEntryIntent: resolvedSelectedEntryIntent,
            LifecycleFinalState: finalState,
            LifecycleEvents: orderedLifecycleEvents);

        return new BacktestTradeLifecycleResult(
            signal,
            trade,
            finalState,
            orderedLifecycleEvents);
    }

    private static BacktestSelectedEntryIntent CreateDefaultSelectedEntryIntent(BacktestSignal signal, ExitConfig cfg)
    {
        return new BacktestSelectedEntryIntent(
            IntentId: $"BT::{signal.Timestamp:yyyyMMddHHmmss}::{signal.BarIndex}::{signal.Side}",
            Signal: signal,
            ExitProfile: ToNormalizedExitProfile(cfg),
            LifecycleMetadata: new BacktestStrategyLifecycleMetadata(
                StrategyName: "BacktestStrategy",
                StrategyVersion: "Legacy",
                Symbol: string.Empty,
                TriggerTimeframe: "1m",
                SubStrategy: signal.SubStrategy,
                EntryScore: signal.EntryScore));
    }

    private static ExitReason MapExitReason(string? exitReason, ExitConfig cfg)
    {
        return exitReason switch
        {
            "hard-stop" => ExitReason.HardStop,
            "adverse-selection-flatten" => ExitReason.TrendChangeFlatten,
            "TP2" => ExitReason.Tp2,
            "GIVEBACK" => ExitReason.Giveback,
            "giveback-usd-cap" => ExitReason.Giveback,
            "REVERSAL_FLATTEN" => ExitReason.ReversalFlatten,
            "MICRO_TRAIL" => ExitReason.MicroTrail,
            "TRAIL" => cfg.EmaTrail ? ExitReason.EmaTrail : ExitReason.Trailing,
            "TIME" => ExitReason.TimeStop,
            "TP1_PARTIAL" => ExitReason.Tp1,
            "MA_EXTENSION_L2_FLIP" => ExitReason.MaExtensionL2Flip,
            "PRICE_TIER_STOP" => ExitReason.PriceTierStop,
            "trend-change-flatten" => ExitReason.TrendChangeFlatten,
            _ => ExitReason.TimeStop,
        };
    }

    internal static ConductMarketFrame CreateSyntheticConductFrame(EnrichedBar row, EnrichedBar? previousRow)
    {
        var features = CreateSyntheticFeatures(row);
        return ConductMarketFrame.FromLiveInputs(
            features,
            CreateSyntheticCandleSnapshot(row, previousRow),
            timestampUtc: row.Bar.Timestamp);
    }

    private static PostFillConductExecutionResult EvaluateSharedConduct(
        BacktestSignal signal,
        EnrichedBar row,
        EnrichedBar? previousRow,
        TradeSide side,
        ExitConfig cfg,
        double entryPrice,
        double riskPerShare,
        int originalPositionSize,
        int openPositionSize,
        double stopPrice,
        double trailingStopPrice,
        double peakPrice,
        double troughPrice,
        bool breakevenActivated,
        bool tp1Activated,
        bool profitExtensionArmed,
        bool continuationConfirmed,
        bool continuationTp2ScaleOutTaken,
        double effectiveGivebackPct,
        double effectiveGivebackUsdCap,
        bool trailingTp2Active)
    {
        var bar = row.Bar;
        var sideIsLong = side == TradeSide.Long;
        var state = new FilledTradeState(
            IntentId: $"BT::{signal.Timestamp:yyyyMMddHHmmss}::{signal.BarIndex}::{signal.Side}",
            Account: string.Empty,
            Symbol: string.Empty,
            Profile: LiveStrategyProfile.Default,
            Side: sideIsLong ? PositionSide.Long : PositionSide.Short,
            FilledQuantity: originalPositionSize,
            OpenQuantity: openPositionSize,
            AverageFillPrice: entryPrice,
            EntryUtc: signal.Timestamp,
            EntryAtr14: signal.AtrValue,
            RiskPerShare: riskPerShare,
            StopPrice: stopPrice,
            TakeProfitPrice: sideIsLong ? entryPrice + cfg.Tp2R * riskPerShare : entryPrice - cfg.Tp2R * riskPerShare,
            RealizedPnlUsd: 0.0,
            UnrealizedPnlUsd: sideIsLong ? (bar.Close - entryPrice) * openPositionSize : (entryPrice - bar.Close) * openPositionSize,
            UnrealizedPnlPeakUsd: Math.Max(0.0, (sideIsLong ? peakPrice - entryPrice : entryPrice - troughPrice) * openPositionSize),
            MostFavorablePrice: sideIsLong ? peakPrice : troughPrice,
            MostAdversePrice: sideIsLong ? troughPrice : peakPrice,
            ManagedTrailingStopPrice: trailingStopPrice,
            BreakevenActivated: breakevenActivated,
            Tp1Activated: tp1Activated,
            ProfitExtensionArmed: profitExtensionArmed);

        var frame = CreateSyntheticConductFrame(row, previousRow);
        var policy = ConductPolicy.FromExitCascadeParams(new ExitCascadeParams
        {
            HardStopR = cfg.HardStopR,
            BreakevenR = cfg.BreakevenR,
            TrailR = cfg.TrailR,
            Tp1R = cfg.Tp1R,
            Tp2R = cfg.UseTrailingTp2 && trailingTp2Active ? double.MaxValue : cfg.Tp2R,
            GivebackPct = effectiveGivebackPct,
            UseFixedGivebackUsdCap = effectiveGivebackUsdCap > 0,
            GivebackUsdCap = effectiveGivebackUsdCap,
            MaxHoldSeconds = cfg.MaxHoldBars * 60,
            TimeStopMinProgressR = double.MaxValue,
            Tp1TightenToBe = cfg.Tp1TightenToBe,
            Tp1PartialClosePct = cfg.Tp1PartialClosePct,
            Tp1BreakevenBufferAtr = cfg.Tp1BreakevenBufferAtr,
            MicroTrailCents = cfg.MicroTrail ? cfg.MicroTrailCents : 0.0,
            MicroTrailActivateCents = cfg.MicroTrail ? cfg.MicroTrailActivateCents : 0.0,
            CheckHardStop = true,
            ReversalFlatten = cfg.ReversalFlatten,
        });

        return PostFillConductExecutor.Execute(
            state,
            frame,
            policy,
            new DailyTradeConductOptions(
                ContinuationConfirmedOverride: continuationConfirmed,
                ContinuationTp2ScaleOutPct: cfg.UseContinuationTp2ScaleOut ? cfg.ContinuationTp2ScalePct : 0.0,
                ContinuationTp2ScaleOutTaken: continuationTp2ScaleOutTaken,
                SourcePolicy: "Backtest"));
    }

    internal static V3LiveFeatureSnapshot CreateSyntheticFeatures(EnrichedBar row)
    {
        var bar = row.Bar;
        var bid = !double.IsNaN(row.BidPrice) ? row.BidPrice : bar.Close;
        var ask = !double.IsNaN(row.AskPrice) ? row.AskPrice : bar.Close;
        var last = !double.IsNaN(row.LastPrice) ? row.LastPrice : bar.Close;
        var hasQuote = (!double.IsNaN(row.BidPrice) && row.BidPrice > 0 && !double.IsNaN(row.AskPrice) && row.AskPrice > 0)
            || (!double.IsNaN(row.LastPrice) && row.LastPrice > 0);
        var bidDepth = !double.IsNaN(row.BidDepthN) ? row.BidDepthN : 0.0;
        var askDepth = !double.IsNaN(row.AskDepthN) ? row.AskDepthN : 0.0;

        return new V3LiveFeatureSnapshot(
            TimestampUtc: bar.Timestamp,
            L1: new V3LiveL1Snapshot(
                bar.Timestamp,
                bid,
                ask,
                last,
                !double.IsNaN(row.BidSize) ? row.BidSize : 0.0,
                !double.IsNaN(row.AskSize) ? row.AskSize : 0.0,
                !double.IsNaN(row.SpreadPct) ? row.SpreadPct : 0.0,
                hasQuote),
            L2: new V3LiveL2Snapshot(
                bidDepth,
                askDepth,
                !double.IsNaN(row.ImbalanceRatio) ? row.ImbalanceRatio : 0.0,
                !double.IsNaN(row.OfiSignal) ? row.OfiSignal : 0.0,
                bidDepth > 0 && askDepth > 0,
                !double.IsNaN(row.DepthWeightedMid) ? row.DepthWeightedMid : double.NaN,
                !double.IsNaN(row.L0ImbalanceRatio) ? row.L0ImbalanceRatio : double.NaN,
                !double.IsNaN(row.DeepImbalanceRatio) ? row.DeepImbalanceRatio : double.NaN),
            IsReady: true,
            Price: bar.Close,
            Atr14: row.Atr14,
            Rsi14: row.Rsi14,
            Vwap: row.Vwap,
            DistFromVwapAtr: !double.IsNaN(row.Atr14) && row.Atr14 > 0 ? (bar.Close - row.Vwap) / row.Atr14 : double.NaN,
            BbPctB: row.BbPctB,
            KcMid: row.KcMid,
            StochK: row.StochK,
            StochD: row.StochD,
            Adx14: row.Adx,
            Rvol: row.Rvol,
            VolAccel: row.VolAccel,
            OfiSignal: row.OfiSignal,
            SqueezeOn: false,
            BbBandwidth: row.BbBandwidth,
            AtrRatio: 1.0,
            RejectReason: string.Empty,
            Ema9: row.Ema9,
            Ema21: row.Ema21,
            Mfi14: row.Mfi14,
            WillR14: row.WillR14,
            Dpo20: row.Dpo20,
            DcPct: row.DcPct,
            MacdLine: row.Macd,
            MacdSignalLine: row.MacdSignal,
            MacdHist: row.MacdHist,
            SupertrendBullish: row.StDirection >= 0,
            IsBullishCandle: row.IsBullishCandle,
            IsBearishCandle: row.IsBearishCandle,
            IsHammer: row.IsHammer,
            IsStar: row.IsStar,
            PrevClose: row.PrevClose,
            PrevOpen: row.PrevOpen,
            PrevHigh: row.PrevHigh,
            PrevLow: row.PrevLow,
            PrevVolume: row.PrevVolume,
            HighestClose10: row.HighestClose10,
            LowestClose10: row.LowestClose10,
            Sma20: row.Sma20,
            CurrentOpen: bar.Open,
            CurrentHigh: bar.High,
            CurrentLow: bar.Low);
    }

    internal static V3LiveCandleSnapshot CreateSyntheticCandleSnapshot(EnrichedBar row, EnrichedBar? previousRow)
    {
        var completedCandles = previousRow is null
            ? Array.Empty<LiveCandle>()
            : new[]
            {
                new LiveCandle(
                    previousRow.Bar.Timestamp,
                    previousRow.Bar.Open,
                    previousRow.Bar.High,
                    previousRow.Bar.Low,
                    previousRow.Bar.Close,
                    previousRow.Bar.Volume,
                    1)
            };

        return new V3LiveCandleSnapshot(
            string.Empty,
            row.Bar.Timestamp,
            new Dictionary<int, V3LiveTimeframeCandleData>
            {
                [60] = new(
                    60,
                    completedCandles,
                    new LiveCandle(
                        row.Bar.Timestamp,
                        row.Bar.Open,
                        row.Bar.High,
                        row.Bar.Low,
                        row.Bar.Close,
                        row.Bar.Volume,
                        1))
            });
    }

    internal static bool ShouldFlattenOnTwoOppositeBars(
        TradeSide side,
        double barOpen,
        double barClose,
        double entryPrice,
        double stopPrice,
        ref int consecutiveOppositeBars)
        => LegacyBacktestProtectivePolicy.ShouldFlattenOnTwoOppositeBars(
            side,
            barOpen,
            barClose,
            entryPrice,
            stopPrice,
            ref consecutiveOppositeBars);

    internal static bool ShouldFlattenOnTwoOppositeBars(
        TradeSide side,
        double barOpen,
        double barClose,
        double entryPrice,
        double stopPrice,
        EnrichedBar? row,
        ExitConfig? cfg,
        ref int consecutiveOppositeBars)
        => LegacyBacktestProtectivePolicy.ShouldFlattenOnTwoOppositeBars(
            side,
            barOpen,
            barClose,
            entryPrice,
            stopPrice,
            row,
            cfg,
            ref consecutiveOppositeBars);

    internal static bool IsOppositeTrendBar(TradeSide side, double barOpen, double barClose)
        => LegacyBacktestProtectivePolicy.IsOppositeTrendBar(side, barOpen, barClose);

    internal static bool IsPriceBetweenEntryAndStop(TradeSide side, double price, double entryPrice, double stopPrice)
        => LegacyBacktestProtectivePolicy.IsPriceBetweenEntryAndStop(side, price, entryPrice, stopPrice);

    // â”€â”€ P2: Price-tier micro trail (cents from peak, scaled by share price) â”€â”€
    internal static (double trailCents, double activateCents) ComputePriceTierMicroTrail(double price)
        => LegacyBacktestProtectivePolicy.ComputePriceTierMicroTrail(price);

    // â”€â”€ P3: Price-tier hard stop floor (max adverse cents per share) â”€â”€
    internal static double ComputePriceTierStopFloorCents(double price)
        => LegacyBacktestProtectivePolicy.ComputePriceTierStopFloorCents(price);
}

