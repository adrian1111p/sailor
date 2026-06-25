using Sailor.App.Backtest.Strategies;
using Harvester.App.Strategy;
using Harvester.Contracts.Risk;

namespace Sailor.App.Backtest.Engine;

/// <summary>
/// Core backtest engine:  statistics, equity curve, and full run loop.
/// Ported from Python engine.py V1.1.
/// </summary>
public static class BacktestEngine
{
    // â”€â”€ Statistics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Compute performance statistics from a list of completed trades.</summary>
    public static BacktestStatistics ComputeStatistics(
        IReadOnlyList<BacktestTradeResult> trades,
        double initialCapital,
        BacktestGovernorReport? governorReport = null)
    {
        governorReport ??= BacktestGovernorReport.None;
        if (trades.Count == 0)
        {
            return new BacktestStatistics(
                TotalTrades: 0, Winners: 0, Losers: 0,
                WinRate: 0, AvgWin: 0, AvgLoss: 0,
                ProfitFactor: 0, ExpectancyR: 0,
                TotalPnl: 0, MaxDrawdown: 0, MaxDrawdownPct: 0,
                Sharpe: 0, AvgBarsHeld: 0,
                LongTrades: 0, ShortTrades: 0,
                LongWinRate: 0, ShortWinRate: 0,
                ExitReasons: new Dictionary<ExitReason, int>(),
                Governor: governorReport,
                EquityCurveSharpe: 0,
                DownsideDeviation: 0,
                EquityCurveDownsideDeviation: 0,
                Sortino: 0,
                EquityCurveSortino: 0);
        }

        var winners = trades.Where(t => t.Pnl > 0).ToList();
        var losers = trades.Where(t => t.Pnl < 0).ToList();

        double grossProfit = winners.Sum(t => t.Pnl);
        double grossLoss = Math.Abs(losers.Sum(t => t.Pnl));
        double totalPnl = trades.Sum(t => t.Pnl);

        // Equity curve for drawdown calculation
        var equityCurve = BuildEquityCurve(trades, initialCapital);

        double peakEquity = equityCurve[0].Equity;
        double maxDd = 0;
        double peakAtMaxDd = equityCurve[0].Equity;
        for (int i = 1; i < equityCurve.Count; i++)
        {
            var equity = equityCurve[i].Equity;
            if (equity > peakEquity) peakEquity = equity;
            double dd = peakEquity - equity;
            if (dd > maxDd) { maxDd = dd; peakAtMaxDd = peakEquity; }
        }

        double maxDdPct = peakAtMaxDd > 0 ? maxDd / peakAtMaxDd : 0;

        var tradeRiskAdjustedMetrics = ComputeTradeRiskAdjustedMetrics(trades);
        var equityCurveRiskAdjustedMetrics = ComputeEquityCurveRiskAdjustedMetrics(equityCurve);

        // Long / Short breakdown
        var longs = trades.Where(t => t.Side == TradeSide.Long).ToList();
        var shorts = trades.Where(t => t.Side == TradeSide.Short).ToList();
        int longWinners = longs.Count(t => t.Pnl > 0);
        int shortWinners = shorts.Count(t => t.Pnl > 0);

        // Exit reason distribution
        var exitReasons = new Dictionary<ExitReason, int>();
        foreach (var t in trades)
        {
            if (!exitReasons.TryGetValue(t.ExitReason, out int count))
                count = 0;
            exitReasons[t.ExitReason] = count + 1;
        }

        return new BacktestStatistics(
            TotalTrades: trades.Count,
            Winners: winners.Count,
            Losers: losers.Count,
            WinRate: (double)winners.Count / trades.Count,
            AvgWin: winners.Count > 0 ? grossProfit / winners.Count : 0,
            AvgLoss: losers.Count > 0 ? -grossLoss / losers.Count : 0,
            ProfitFactor: grossLoss > 0 ? grossProfit / grossLoss : double.PositiveInfinity,
            ExpectancyR: trades.Average(t => t.PnlR),
            TotalPnl: totalPnl,
            MaxDrawdown: maxDd,
            MaxDrawdownPct: maxDdPct,
                Sharpe: tradeRiskAdjustedMetrics.Sharpe,
            AvgBarsHeld: trades.Average(t => t.BarsHeld),
            LongTrades: longs.Count,
            ShortTrades: shorts.Count,
            LongWinRate: longs.Count > 0 ? (double)longWinners / longs.Count : 0,
            ShortWinRate: shorts.Count > 0 ? (double)shortWinners / shorts.Count : 0,
            ExitReasons: exitReasons,
            Governor: governorReport,
                EquityCurveSharpe: equityCurveRiskAdjustedMetrics.Sharpe,
                DownsideDeviation: tradeRiskAdjustedMetrics.DownsideDeviation,
                EquityCurveDownsideDeviation: equityCurveRiskAdjustedMetrics.DownsideDeviation,
                Sortino: tradeRiskAdjustedMetrics.Sortino,
                EquityCurveSortino: equityCurveRiskAdjustedMetrics.Sortino);
    }

    // â”€â”€ Equity Curve â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Build a time-indexed equity curve from trades.</summary>
    public static IReadOnlyList<(DateTime Time, double Equity)> BuildEquityCurve(
        IReadOnlyList<BacktestTradeResult> trades,
        double initialCapital)
    {
        if (trades.Count == 0)
            return [(DateTime.UtcNow, initialCapital)];

        var curve = new List<(DateTime, double)>(trades.Count + 1)
        {
            (trades[0].EntryTime, initialCapital)
        };

        double cumulative = initialCapital;
        foreach (var t in trades)
        {
            cumulative += t.Pnl;
            curve.Add((t.ExitTime, cumulative));
        }

        return curve;
    }

    private static RiskAdjustedMetrics ComputeTradeRiskAdjustedMetrics(IReadOnlyList<BacktestTradeResult> trades)
    {
        if (trades.Count <= 1)
        {
            return RiskAdjustedMetrics.Zero;
        }

        var returns = trades.Select(t =>
        {
            var notional = t.EntryPrice * t.PositionSize;
            return notional > 0 ? t.Pnl / notional : 0.0;
        }).ToArray();

        return ComputeRiskAdjustedMetrics(returns);
    }

    private static RiskAdjustedMetrics ComputeEquityCurveRiskAdjustedMetrics(IReadOnlyList<(DateTime Time, double Equity)> equityCurve)
    {
        if (equityCurve.Count <= 2)
        {
            return RiskAdjustedMetrics.Zero;
        }

        var returns = new double[equityCurve.Count - 1];
        for (var i = 1; i < equityCurve.Count; i++)
        {
            var previousEquity = equityCurve[i - 1].Equity;
            returns[i - 1] = previousEquity > 0.0
                ? (equityCurve[i].Equity - previousEquity) / previousEquity
                : 0.0;
        }

        return ComputeRiskAdjustedMetrics(returns);
    }

    private static RiskAdjustedMetrics ComputeRiskAdjustedMetrics(IReadOnlyList<double> returns)
    {
        if (returns.Count <= 1)
        {
            return RiskAdjustedMetrics.Zero;
        }

        var mean = returns.Average();
        var variance = returns.Select(value => (value - mean) * (value - mean)).Average();
        var stdDev = Math.Sqrt(variance);
        var downsideVariance = returns
            .Select(value => Math.Min(0.0, value))
            .Select(value => value * value)
            .Average();
        var downsideStdDev = Math.Sqrt(downsideVariance);
        var annualizationFactor = Math.Sqrt(252.0);

        return new RiskAdjustedMetrics(
            Sharpe: stdDev > 0.0 ? (mean / stdDev) * annualizationFactor : 0.0,
            DownsideDeviation: downsideStdDev * annualizationFactor,
            Sortino: downsideStdDev > 0.0 ? (mean / downsideStdDev) * annualizationFactor : 0.0);
    }

    private readonly record struct RiskAdjustedMetrics(
        double Sharpe,
        double DownsideDeviation,
        double Sortino)
    {
        public static RiskAdjustedMetrics Zero { get; } = new(0.0, 0.0, 0.0);
    }

    // â”€â”€ Run Backtest â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Execute a full backtest for one symbol using the given strategy.
    /// </summary>
    /// <param name="symbol">Ticker symbol.</param>
    /// <param name="strategy">Strategy instance implementing IBacktestStrategy.</param>
    /// <param name="bars1m">Mandatory enriched 1-minute trigger bars.</param>
    /// <param name="triggerTf">Timeframe label (e.g. "1m").</param>
    /// <param name="bars5m">Optional enriched 5-min bars.</param>
    /// <param name="bars15m">Optional enriched 15-min bars.</param>
    /// <param name="bars1h">Optional enriched 1-hour bars.</param>
    /// <param name="bars1d">Optional enriched daily bars.</param>
    /// <param name="initialCapital">Starting capital for statistics.</param>
    public static BacktestResult RunBacktest(
        string symbol,
        IBacktestStrategy strategy,
        EnrichedBar[] bars1m,
        string triggerTf = "1m",
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null,
        double initialCapital = 25_000.0,
        RiskGovernorConfig? riskGovernorConfig = null,
        OpportunityScoringSettings? scoringSettings = null)
    {
        var governorConfig = riskGovernorConfig ?? RiskGovernorConfigFactory.CreateBacktest(initialCapital);
        var governor = new RiskGovernor(governorConfig);
        governor.ResetSession(bars1m.Length > 0 ? bars1m[0].Bar.Timestamp : DateTime.UtcNow, initialCapital);

        // Inject symbol into strategy for self-learning lookups
        if (strategy is BacktestStrategyBase bsb)
            bsb.Symbol = symbol;

        // 1. Generate signals
        var signals = BacktestStrategyBase.FilterExecutionReadySignals(
            strategy.GenerateSignals(bars1m, bars5m, bars15m, bars1h, bars1d),
            bars1m,
            minimumEntryScore: 0,
            settings: scoringSettings);

        // 2. Simulate trades (no overlapping â€” skip signals while in a trade)
        var trades = new List<BacktestTradeResult>();
        var acceptedEntryIntents = new List<BacktestSelectedEntryIntent>();
        var lifecycleTrades = new List<BacktestTradeLifecycleResult>();
        int nextAllowedBar = 0;
        int nextAllowedLongBar = 0;
        int nextAllowedShortBar = 0;
        double cumulativeRealizedPnl = 0.0;
        var recentClosedTrades = new List<BacktestTradeResult>();
        var bucketRealizedPnl = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var sig in signals)
        {
            var bucket = NormalizeGovernorBucket(sig.SubStrategy);
            var entryCheck = governor.EvaluateEntry(bars1m[Math.Clamp(sig.BarIndex, 0, bars1m.Length - 1)].Bar.Timestamp, bucket);
            if (!entryCheck.EntryAllowed)
            {
                if (entryCheck.PortfolioBlocked)
                {
                    break;
                }

                continue;
            }

            if (sig.BarIndex < nextAllowedBar)
                continue;

            if (sig.Side == TradeSide.Long && sig.BarIndex < nextAllowedLongBar)
                continue;

            if (sig.Side == TradeSide.Short && sig.BarIndex < nextAllowedShortBar)
                continue;

            var effectiveSignal = ApplyAdaptiveEntrySizing(sig, entryCheck, recentClosedTrades);
            if (effectiveSignal.PositionSize <= 0)
                continue;

            BacktestTradeResult? result;
            BacktestTradeLifecycleResult? lifecycleResult = null;
            if (strategy is IBacktestLifecycleStrategy lifecycleStrategy)
            {
                var selectedEntryIntent = lifecycleStrategy.CreateSelectedEntryIntent(effectiveSignal, triggerTf);
                acceptedEntryIntents.Add(selectedEntryIntent);

                lifecycleResult = lifecycleStrategy.SimulateAcceptedEntryIntent(selectedEntryIntent, bars1m);
                result = lifecycleResult.Trade;
            }
            else
            {
                result = strategy.SimulateTrade(effectiveSignal, bars1m);
                if (result?.SelectedEntryIntent is not null)
                {
                    acceptedEntryIntents.Add(result.SelectedEntryIntent);
                }

            }

            if (result != null)
            {
                var trade = result with
                {
                    GovernorBucket = bucket
                };
                nextAllowedBar = result.ExitBar + 1;

                if (strategy is IBacktestPostTradeSignalGate signalGate)
                {
                    var retryLockout = signalGate.GetRetryLockout(effectiveSignal, trade);
                    if (retryLockout is not null)
                    {
                        if (retryLockout.Side == TradeSide.Long)
                        {
                            nextAllowedLongBar = Math.Max(nextAllowedLongBar, retryLockout.NextAllowedBarIndex);
                        }
                        else
                        {
                            nextAllowedShortBar = Math.Max(nextAllowedShortBar, retryLockout.NextAllowedBarIndex);
                        }
                    }
                }

                cumulativeRealizedPnl += result.Pnl;
                bucketRealizedPnl[bucket] = bucketRealizedPnl.GetValueOrDefault(bucket) + result.Pnl;

                var bucketStates = bucketRealizedPnl
                    .Select(kvp => new RiskGovernorBucketStateInput(
                        Bucket: kvp.Key,
                        RealizedPnlUsd: kvp.Value,
                        UnrealizedPnlUsd: 0.0,
                        OpenPositionCount: 0))
                    .ToArray();

                var update = governor.UpdatePortfolio(result.ExitTime, cumulativeRealizedPnl, 0.0, 0, bucketStates);
                var tradeStopReason = string.Empty;
                if (update.NewlyStoppedBuckets.Count > 0)
                {
                    tradeStopReason = string.Join(", ", update.NewlyStoppedBuckets.Select(x => $"{x.Bucket}:{x.ActiveReason}"));
                }
                else if (!update.EntryAllowed)
                {
                    tradeStopReason = update.Reason;
                }

                if (!string.IsNullOrWhiteSpace(tradeStopReason))
                {
                    trade = trade with
                    {
                        GovernorTriggeredStop = true,
                        GovernorStopReason = tradeStopReason
                    };
                }

                trades.Add(trade);
                recentClosedTrades.Add(trade);
                if (recentClosedTrades.Count > 8)
                {
                    recentClosedTrades.RemoveAt(0);
                }

                if (lifecycleResult is not null)
                {
                    lifecycleTrades.Add(lifecycleResult with
                    {
                        Trade = trade,
                    });
                }
                else if (trade.LifecycleFinalState is not null
                    && trade.LifecycleEvents is not null)
                {
                    lifecycleTrades.Add(new BacktestTradeLifecycleResult(
                        effectiveSignal,
                        trade,
                        trade.LifecycleFinalState,
                        trade.LifecycleEvents));
                }

                if (!update.EntryAllowed)
                {
                    break;
                }
            }
        }

        // 3. Compute stats & equity curve
        var governorReport = BuildGovernorReport(governor.Snapshot);
        var stats = ComputeStatistics(trades, initialCapital, governorReport);
        var equityCurve = BuildEquityCurve(trades, initialCapital);

        return new BacktestResult(
            Symbol: symbol,
            TriggerTf: triggerTf,
            Trades: trades,
            EquityCurve: equityCurve,
            Stats: stats,
            AcceptedEntryIntents: acceptedEntryIntents,
            LifecycleTrades: lifecycleTrades);
    }

    private static BacktestSignal ApplyAdaptiveEntrySizing(
        BacktestSignal signal,
        RiskGovernorEntryCheckResult entryCheck,
        IReadOnlyList<BacktestTradeResult> recentClosedTrades)
    {
        var multiplier = Math.Clamp(entryCheck.SuggestedEntrySizeMultiplier, 0.35, 1.0);
        multiplier *= ResolveLossClusterSizingMultiplier(signal.Side, recentClosedTrades);

        if (multiplier >= 0.999)
        {
            return signal;
        }

        var scaledSize = Math.Max(1, (int)Math.Floor(signal.PositionSize * Math.Clamp(multiplier, 0.25, 1.0)));
        return signal with
        {
            PositionSize = scaledSize
        };
    }

    private static double ResolveLossClusterSizingMultiplier(TradeSide side, IReadOnlyList<BacktestTradeResult> recentClosedTrades)
    {
        if (recentClosedTrades.Count == 0)
        {
            return 1.0;
        }

        var consecutiveLosses = 0;
        var consecutiveSameSideLosses = 0;
        var recentLosses = 0;

        for (var i = recentClosedTrades.Count - 1; i >= 0; i--)
        {
            var trade = recentClosedTrades[i];
            if (trade.Pnl < 0)
            {
                recentLosses++;
                if (consecutiveLosses == recentClosedTrades.Count - 1 - i)
                {
                    consecutiveLosses++;
                }

                if (trade.Side == side)
                {
                    consecutiveSameSideLosses++;
                }
                else if (consecutiveSameSideLosses > 0)
                {
                    break;
                }
            }
            else if (consecutiveLosses > 0)
            {
                break;
            }
        }

        var multiplier = 1.0;
        if (consecutiveLosses >= 2)
        {
            multiplier *= consecutiveLosses >= 4
                ? 0.55
                : consecutiveLosses == 3
                    ? 0.68
                    : 0.82;
        }

        if (consecutiveSameSideLosses >= 2)
        {
            multiplier *= consecutiveSameSideLosses >= 3 ? 0.72 : 0.86;
        }

        if (recentLosses >= 3)
        {
            multiplier *= 0.92;
        }

        return Math.Clamp(multiplier, 0.35, 1.0);
    }

    private static BacktestGovernorReport BuildGovernorReport(RiskGovernorSnapshot snapshot)
    {
        var stopReasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(snapshot.ActiveReason))
        {
            stopReasonCounts[snapshot.ActiveReason] = 1;
        }

        var haltedBuckets = snapshot.Buckets
            .Where(x => x.EntryBlocked)
            .OrderBy(x => x.Bucket, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var bucket in haltedBuckets)
        {
            if (string.IsNullOrWhiteSpace(bucket.ActiveReason))
            {
                continue;
            }

            if (!stopReasonCounts.TryAdd(bucket.ActiveReason, 1))
            {
                stopReasonCounts[bucket.ActiveReason]++;
            }
        }

        return new BacktestGovernorReport(
            SessionStopped: snapshot.EntriesBlocked,
            StopReason: snapshot.ActiveReason,
            HaltedBucketCount: haltedBuckets.Length,
            HaltedBucketSummary: haltedBuckets.Length == 0
                ? string.Empty
                : string.Join(", ", haltedBuckets.Select(x => $"{x.Bucket}:{x.ActiveReason}")),
            StopReasonCounts: stopReasonCounts);
    }

    private static string NormalizeGovernorBucket(string bucket)
        => string.IsNullOrWhiteSpace(bucket) ? "DEFAULT" : bucket.Trim().ToUpperInvariant();
}

