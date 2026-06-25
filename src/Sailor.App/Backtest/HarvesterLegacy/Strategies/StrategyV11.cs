using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

public sealed class V11Config
{
    public double RiskPerTradeDollars { get; set; } = 22.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;
    public int MaxShares { get; set; } = 6500;
    public double MinRiskPerShare { get; set; } = 0.01;

    public bool UseNextBarOpenEntry { get; set; } = true;
    public int CooldownBars { get; set; } = 4;

    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;
    public double RvolMin { get; set; } = 0.85;
    public double L2LiquidityMin { get; set; } = 20.0;
    public double SpreadZMax { get; set; } = 2.1;
    public double VolAccelMin { get; set; } = -0.20;

    public double BbLongThreshold { get; set; } = 0.12;
    public double BbShortThreshold { get; set; } = 0.88;
    public double VwapDeviationAtr { get; set; } = 0.60;
    public double RsiLongMax { get; set; } = 38.0;
    public double RsiShortMin { get; set; } = 62.0;
    public double AdxMin { get; set; } = 12.0;
    public double AdxMax { get; set; } = 36.0;
    public int MinScore { get; set; } = 5;
    public bool UseUnifiedEntryScore { get; set; } = false;
    public bool EnableDiagnostics { get; set; } = false;
    public string DiagnosticsLabel { get; set; } = string.Empty;

    public int SwingLookback { get; set; } = 4;
    public bool RequireHtfBiasFilter { get; set; } = true;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;

    public double HardStopR { get; set; } = 0.90;
    public double BreakevenR { get; set; } = 0.55;
    public double TrailR { get; set; } = 0.35;
    public double GivebackPct { get; set; } = 0.30;
    public bool UseFixedGivebackUsdCap { get; set; } = true;
    public bool UseVariableGivebackUsdCap { get; set; } = true;
    public double GivebackUsdCap { get; set; } = 38.0;
    public double Tp1R { get; set; } = 0.80;
    public double Tp2R { get; set; } = 1.45;
    public int MaxHoldBars { get; set; } = 30;
    public bool UseEmaTrail { get; set; } = false;
    public double EmaTrailBufferAtr { get; set; } = 0.15;
    public bool FlattenOnPeakGiveback { get; set; } = false;
    public double PeakGivebackKeepFraction { get; set; } = 0.50;
    public double PeakGivebackActivateR { get; set; } = 0.30;
    public bool FlattenOnStagnation { get; set; } = false;
    public int StagnationBars { get; set; } = 8;
    public double StagnationMinPeakR { get; set; } = 0.15;
    public double StagnationMaxAdverseR { get; set; } = -0.08;
    public bool UseL1L2DecisionOnOppositeBarsFlatten { get; set; } = false;

    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

/// <summary>
/// Phase 6.15 â€” FROZEN strategy. Retained for historical/regression comparison and explicit selection only;
/// excluded from the default/active comparison plans. Superseded by Conduct-V3. Trade conduct is unchanged.
/// </summary>
[FrozenStrategy(supersededBy: "Conduct-V3", reason: "Silver/pink-pearl lineage superseded by Conduct-V3 conduct.")]
public sealed class StrategyV11 : BacktestStrategyBase, IBacktestDiagnosticsProvider
{
    private readonly V11Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;
    private readonly V3SignalCore _signalCore;
    private readonly V11Diagnostics _diagnostics = new();

    private bool DiagnosticsEnabled => _cfg.EnableDiagnostics || StrategyDiagnosticsEnvironment.IsEnabled("V11");

    public StrategyV11(V11Config? cfg = null)
    {
        _cfg = cfg ?? new V11Config();
        _signalCore = new V3SignalCore(new V3SignalCoreConfig
        {
            RiskPerTradeDollars = _cfg.RiskPerTradeDollars,
            AccountSize = _cfg.AccountSize,
            MaxPositionNotionalPctOfAccount = _cfg.MaxPositionNotionalPctOfAccount,
            MaxShares = _cfg.MaxShares,
            MinRiskPerShare = _cfg.MinRiskPerShare,
            MinPrice = _cfg.MinPrice,
            MaxPrice = _cfg.MaxPrice,
            UseNextBarOpenEntry = _cfg.UseNextBarOpenEntry,
            VwapStretchAtr = Math.Max(1.2, _cfg.VwapDeviationAtr * 2.0),
            VwapEnabled = true,
            BbEntryPctbLow = _cfg.BbLongThreshold,
            BbEntryPctbHigh = _cfg.BbShortThreshold,
            BbEnabled = true,
            SqueezeEnabled = true,
            SqueezeBars = 8,
            L2LiquidityMin = _cfg.L2LiquidityMin,
            SpreadZMax = _cfg.SpreadZMax,
            VolAccelMin = _cfg.VolAccelMin,
            RvolMin = _cfg.RvolMin,
            RsiOversold = _cfg.RsiLongMax,
            RsiOverbought = _cfg.RsiShortMin,
            RequireVolumeConfirm = true,
            // Phase 1 parity fix: use config values directly â€” no Math.Max() clamping.
            // This ensures backtest and live use identical exit parameters.
            HardStopR = _cfg.HardStopR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            BreakevenR = _cfg.BreakevenR,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            AllowLong = _cfg.AllowLong,
            AllowShort = _cfg.AllowShort,
        });
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.20,
            UseFixedGivebackUsdCap = _cfg.UseFixedGivebackUsdCap,
            UseVariableGivebackUsdCap = _cfg.UseVariableGivebackUsdCap,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
            ReversalFlatten = true,
            MicroTrail = true,
            MicroTrailCents = 2.0,
            MicroTrailActivateCents = 3.0,
            EmaTrail = _cfg.UseEmaTrail,
            EmaTrailBufferAtr = _cfg.EmaTrailBufferAtr,
            FlattenOnPeakGiveback = _cfg.FlattenOnPeakGiveback,
            PeakGivebackKeepFraction = _cfg.PeakGivebackKeepFraction,
            PeakGivebackActivateR = _cfg.PeakGivebackActivateR,
            FlattenOnStagnation = _cfg.FlattenOnStagnation,
            StagnationBars = _cfg.StagnationBars,
            StagnationMinPeakR = _cfg.StagnationMinPeakR,
            StagnationMaxAdverseR = _cfg.StagnationMaxAdverseR,
            UseL1L2DecisionOnOppositeBarsFlatten = _cfg.UseL1L2DecisionOnOppositeBarsFlatten,
        };
    }

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var rawSignals = _signalCore.GenerateSignals(triggerBars, bars5m, bars15m, bars1h, bars1d);
        if (rawSignals.Count == 0) return rawSignals;

        var filtered = new List<BacktestSignal>(rawSignals.Count);
        int lastBar = -10_000;

        foreach (var signal in rawSignals.OrderBy(s => s.BarIndex))
        {
            if (DiagnosticsEnabled)
                _diagnostics.ObserveGenerated(signal.SubStrategy);

            if (signal.BarIndex - lastBar < _cfg.CooldownBars) continue;

            int evalIdx = Math.Max(0, signal.BarIndex - 1);
            if (evalIdx >= triggerBars.Length) continue;
            var row = triggerBars[evalIdx];

            if (!double.IsNaN(row.Adx) && (row.Adx < _cfg.AdxMin || row.Adx > _cfg.AdxMax))
            {
                if (DiagnosticsEnabled)
                    _diagnostics.ObserveReject(signal.SubStrategy, "adx-band");
                continue;
            }

            if (signal.Side == TradeSide.Long && !double.IsNaN(row.BbPctB) && row.BbPctB > 0.45)
            {
                if (DiagnosticsEnabled)
                    _diagnostics.ObserveReject(signal.SubStrategy, "bb-location");
                continue;
            }

            if (signal.Side == TradeSide.Short && !double.IsNaN(row.BbPctB) && row.BbPctB < 0.55)
            {
                if (DiagnosticsEnabled)
                    _diagnostics.ObserveReject(signal.SubStrategy, "bb-location");
                continue;
            }

            var entryScore = signal.EntryScore;
            if (_cfg.UseUnifiedEntryScore)
            {
                entryScore += ComputeUnifiedEntryScore(row, signal.Side);
                if (_cfg.MinScore > 0 && entryScore < _cfg.MinScore)
                {
                    if (DiagnosticsEnabled)
                        _diagnostics.ObserveReject(signal.SubStrategy, "entry-score");
                    continue;
                }
            }

            filtered.Add(signal with { EntryScore = entryScore });
            if (DiagnosticsEnabled)
                _diagnostics.ObserveAccepted(signal.SubStrategy, entryScore);
            lastBar = signal.BarIndex;
        }

        return filtered;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var result = ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);
        if (DiagnosticsEnabled && result is not null)
            _diagnostics.ObserveTrade(result);

        return result;
    }

    public void ResetDiagnostics()
    {
        _diagnostics.Reset();
    }

    public IReadOnlyList<string> GetDiagnosticsSummaryLines()
    {
        return DiagnosticsEnabled
            ? _diagnostics.BuildSummaryLines(string.IsNullOrWhiteSpace(_cfg.DiagnosticsLabel) ? "V11" : _cfg.DiagnosticsLabel)
            : [];
    }

    private int ComputeUnifiedEntryScore(EnrichedBar row, TradeSide side)
    {
        var score = 0;

        if (!double.IsNaN(row.Adx) && row.Adx >= _cfg.AdxMin && row.Adx <= _cfg.AdxMax)
            score++;

        if (side == TradeSide.Long)
        {
            if (!double.IsNaN(row.BbPctB) && row.BbPctB <= 0.30)
                score++;
            if (!double.IsNaN(row.OfiSignal) && row.OfiSignal > 0)
                score++;
        }
        else
        {
            if (!double.IsNaN(row.BbPctB) && row.BbPctB >= 0.70)
                score++;
            if (!double.IsNaN(row.OfiSignal) && row.OfiSignal < 0)
                score++;
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin + 0.20)
            score++;

        if (!double.IsNaN(row.VolAccel) && row.VolAccel >= 0)
            score++;

        if (HasDirectionalBookConfirmation(row, side))
            score++;

        return score;
    }

    private static bool HasDirectionalBookConfirmation(EnrichedBar row, TradeSide side)
    {
        var confirmations = 0;
        if (!double.IsNaN(row.ImbalanceRatio) && row.ImbalanceRatio > 0)
        {
            if (side == TradeSide.Long ? row.ImbalanceRatio > 1.0 : row.ImbalanceRatio < 1.0)
                confirmations++;
        }

        if (!double.IsNaN(row.DeepImbalanceRatio) && row.DeepImbalanceRatio > 0)
        {
            if (side == TradeSide.Long ? row.DeepImbalanceRatio > 1.0 : row.DeepImbalanceRatio < 1.0)
                confirmations++;
        }

        if (!double.IsNaN(row.BidSize) && !double.IsNaN(row.AskSize) && row.BidSize > 0 && row.AskSize > 0)
        {
            if (side == TradeSide.Long ? row.BidSize > row.AskSize : row.AskSize > row.BidSize)
                confirmations++;
        }

        return confirmations > 0;
    }

    private sealed class V11Diagnostics
    {
        private readonly Dictionary<string, int> _generatedBySource = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _acceptedBySource = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _rejectionsBySourceReason = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (int Trades, int Winners, double GrossProfit, double GrossLoss, double TotalPnl)> _tradeStatsBySource = new(StringComparer.OrdinalIgnoreCase);
        private int _acceptedSignals;

        public void Reset()
        {
            _generatedBySource.Clear();
            _acceptedBySource.Clear();
            _rejectionsBySourceReason.Clear();
            _tradeStatsBySource.Clear();
            _acceptedSignals = 0;
        }

        public void ObserveGenerated(string source)
        {
            var normalizedSource = NormalizeSource(source);
            _generatedBySource[normalizedSource] = _generatedBySource.GetValueOrDefault(normalizedSource) + 1;
        }

        public void ObserveReject(string source, string reason)
        {
            var key = $"{NormalizeSource(source)}::{reason}";
            _rejectionsBySourceReason[key] = _rejectionsBySourceReason.GetValueOrDefault(key) + 1;
        }

        public void ObserveAccepted(string source, int entryScore)
        {
            var normalizedSource = NormalizeSource(source);
            _acceptedSignals++;
            _acceptedBySource[normalizedSource] = _acceptedBySource.GetValueOrDefault(normalizedSource) + 1;
        }

        public void ObserveTrade(BacktestTradeResult trade)
        {
            var normalizedSource = NormalizeSource(trade.SubStrategy);
            var stats = _tradeStatsBySource.GetValueOrDefault(normalizedSource);
            stats.Trades++;
            stats.TotalPnl += trade.Pnl;
            if (trade.Pnl > 0)
            {
                stats.Winners++;
                stats.GrossProfit += trade.Pnl;
            }
            else if (trade.Pnl < 0)
            {
                stats.GrossLoss += Math.Abs(trade.Pnl);
            }

            _tradeStatsBySource[normalizedSource] = stats;
        }

        public IReadOnlyList<string> BuildSummaryLines(string label)
        {
            if (_generatedBySource.Count == 0 && _tradeStatsBySource.Count == 0)
                return [];

            var lines = new List<string>
            {
                $"diagnostics[{label}] generated={_generatedBySource.Values.Sum()} accepted={_acceptedSignals}"
            };

            foreach (var source in _generatedBySource.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            {
                var generated = _generatedBySource.GetValueOrDefault(source);
                var accepted = _acceptedBySource.GetValueOrDefault(source);
                var stats = _tradeStatsBySource.GetValueOrDefault(source);
                var profitFactor = stats.GrossLoss > 0 ? stats.GrossProfit / stats.GrossLoss : stats.GrossProfit > 0 ? double.PositiveInfinity : 0.0;
                lines.Add($"diag-source {source}: generated={generated} accepted={accepted} trades={stats.Trades} pnl={stats.TotalPnl:F2} pf={(double.IsPositiveInfinity(profitFactor) ? "inf" : profitFactor.ToString("F2"))}");
            }

            foreach (var rejection in _rejectionsBySourceReason.OrderByDescending(entry => entry.Value).Take(6))
            {
                lines.Add($"diag-reject {rejection.Key.Replace("::", ":", StringComparison.Ordinal)}={rejection.Value}");
            }

            return lines;
        }

        private static string NormalizeSource(string source)
            => string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
    }
}
