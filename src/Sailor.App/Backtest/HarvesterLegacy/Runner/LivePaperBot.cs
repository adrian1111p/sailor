// LivePaperBot.cs â€” Legacy live paper trading bot adapter.
// Port of backtest/live_paper.py
//
// Runs a persistent polling loop during market hours:
//   - Connects to IBKR TWS paper trading (port 7497)
//   - Assigns best strategy per symbol (from sweep optimization)
//   - Simulates entries and delegates post-fill conduct through the shared engine
//   - Flattens all positions at EOD (15:55 ET)
//
// IMPORTANT: This class is designed to work with the existing SnapshotRuntime
// IBKR infrastructure. For live execution, it needs an active EClientSocket.
// This implementation provides the complete trading logic; the IBKR connection
// layer is handled by the existing broker adapter.

using Sailor.App.Backtest.DataFetcher;
using Sailor.App.Backtest.Engine;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Strategies;
using Harvester.App.Strategy;
using Harvester.App.Strategy.Conduct;

namespace Sailor.App.Backtest.Runner;

/// <summary>
/// Strategy assignment type for per-symbol strategy routing.
/// </summary>
public enum LiveStrategyType
{
    TrendV13,
}

/// <summary>
/// Tracks a live open position with all state needed for exit management.
/// </summary>
public sealed class LivePosition
{
    public required string Symbol { get; init; }
    public required TradeSide Side { get; init; }
    public required double EntryPrice { get; init; }
    public double StopPrice { get; set; }
    public required int Shares { get; init; }
    public required DateTime EntryTimeEt { get; init; }
    public required string Strategy { get; init; }
    public required double AtrAtEntry { get; init; }
    public required double RiskPerShare { get; init; }
    public bool BreakevenActivated { get; set; }
    public double PeakPrice { get; set; }
    public double TrailingStop { get; set; }
    public bool Tp1Hit { get; set; }
}

/// <summary>
/// Entry in the daily trade log.
/// </summary>
public sealed record TradeLogEntry
{
    public required string Time { get; init; }
    public required string Symbol { get; init; }
    public required TradeSide Side { get; init; }
    public required double Entry { get; init; }
    public required double Stop { get; init; }
    public required string Strategy { get; init; }
    public string Status { get; set; } = "OPEN";
    public double ExitPrice { get; set; }
    public double Pnl { get; set; }
    public double PnlR { get; set; }
    public string ExitReason { get; set; } = "";
}

/// <summary>
/// Legacy live paper trading bot adapter.
/// Entry discovery stays here for historical/offline use, but post-fill conduct
/// now delegates into <see cref="PostFillConductExecutor"/> instead of carrying
/// a parallel stop, trail, and target implementation.
/// </summary>
public sealed class LivePaperBot
{
    // â”€â”€ Constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public const int PaperPort = 7497;
    public const int ClientId = 90;
    public const int PositionSize = 2;
    public const int MaxDailyTrades = 10;
    public const int EodFlattenMinute = 955; // 15:55 ET
    public const int CheckIntervalSec = 60;

    // â”€â”€ Per-symbol strategy assignment â”€â”€
    public static readonly Dictionary<string, LiveStrategyType> SymbolStrategy = new()
    {
        ["AAPL"] = LiveStrategyType.TrendV13,
        ["TSLA"] = LiveStrategyType.TrendV13,
        ["NVDA"] = LiveStrategyType.TrendV13,
        ["AMD"] = LiveStrategyType.TrendV13,
        ["META"] = LiveStrategyType.TrendV13,
    };

    // â”€â”€ Strategy configs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private static readonly StrategyConfig CfgTrend = new()
    {
        RiskPerTradeDollars = 50.0,
        TrailR = 1.5, GivebackPct = 0.70, Tp1R = 2.0, Tp2R = 4.0,
        HardStopR = 1.5, BreakevenR = 1.2, RvolMin = 1.3, AdxThreshold = 20.0,
    };

    // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public Dictionary<string, LivePosition> Positions { get; } = new();
    public int DailyTrades { get; private set; }
    public double DailyPnl { get; private set; }
    public List<TradeLogEntry> TradeLog { get; } = [];

    private readonly Action<string> _log;

    public LivePaperBot(Action<string>? log = null)
    {
        _log = log ?? Console.WriteLine;
    }

    /// <summary>
    /// Check if the assigned strategy generates a signal for a symbol using cached data.
    /// In live mode, data would come from IBKR reqHistoricalData.
    /// </summary>
    public BacktestSignal? CheckForSignal(string symbol, EnrichedBar[] bars1m,
        EnrichedBar[]? bars5m = null, EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null, EnrichedBar[]? bars1d = null)
    {
        if (!SymbolStrategy.TryGetValue(symbol, out var stratType))
            return null;

        if (bars1m.Length < 60)
            return null;

        var strategy = CreateStrategy(stratType);
        var signals = BacktestStrategyBase.FilterExecutionReadySignals(
            strategy.GenerateSignals(bars1m, bars5m, bars15m, bars1h, bars1d),
            bars1m);

        if (signals.Count == 0)
            return null;

        // Only use the most recent signal (within last 2 bars)
        var lastSig = signals[^1];
        if (lastSig.BarIndex < bars1m.Length - 2)
            return null;

        return lastSig;
    }

    /// <summary>
    /// Record a trade entry (would be paired with IBKR order execution in live mode).
    /// </summary>
    public void RecordEntry(string symbol, BacktestSignal signal, double fillPrice, DateTime entryTimeEt)
    {
        var side = signal.Side;
        Positions[symbol] = new LivePosition
        {
            Symbol = symbol,
            Side = side,
            EntryPrice = fillPrice,
            StopPrice = signal.StopPrice,
            Shares = PositionSize,
            EntryTimeEt = entryTimeEt,
            Strategy = GetStrategyName(symbol),
            AtrAtEntry = signal.AtrValue,
            RiskPerShare = signal.RiskPerShare,
            PeakPrice = fillPrice,
            TrailingStop = signal.StopPrice,
        };

        DailyTrades++;
        TradeLog.Add(new TradeLogEntry
        {
            Time = entryTimeEt.ToString("HH:mm:ss"),
            Symbol = symbol,
            Side = side,
            Entry = fillPrice,
            Stop = signal.StopPrice,
            Strategy = GetStrategyName(symbol),
        });

        _log($">>> ENTERING {side} {symbol} {PositionSize} shares @ ${fillPrice:F2} " +
             $"(stop: ${signal.StopPrice:F2}) [{GetStrategyName(symbol)}]");
    }

    /// <summary>
    /// Evaluate post-fill conduct for an open position given the current price.
    /// Returns the delegated conduct-engine reason if the position should be reduced or closed.
    /// </summary>
    public string? ManagePosition(string symbol, double currentPrice, DateTime nowEt,
        BacktestBar? lastBar = null, BacktestBar? prevBar = null)
    {
        if (!Positions.TryGetValue(symbol, out var pos))
            return null;

        if (!SymbolStrategy.TryGetValue(symbol, out var stratType))
            return null;

        var rps = pos.RiskPerShare;
        if (rps <= 0) return null;

        var cascadeParams = GetCascadeParams(stratType);
        var evaluation = EvaluateConduct(pos, symbol, currentPrice, nowEt, lastBar, prevBar, cascadeParams);

        pos.PeakPrice = evaluation.StatePatch.PeakPrice;
        pos.TrailingStop = evaluation.StatePatch.TrailingStopPrice;
        pos.StopPrice = evaluation.StatePatch.StopPrice;
        pos.BreakevenActivated = evaluation.StatePatch.BreakevenActivated;
        pos.Tp1Hit = evaluation.StatePatch.Tp1Activated;

        var isLong = pos.Side == TradeSide.Long;

        // Log position state
        double unrealizedR = isLong
            ? (currentPrice - pos.EntryPrice) / rps
            : (pos.EntryPrice - currentPrice) / rps;
        double peakR = isLong
            ? (pos.PeakPrice - pos.EntryPrice) / rps
            : (pos.EntryPrice - pos.PeakPrice) / rps;
        _log($"  [{symbol}] Price=${currentPrice:F2} UnR={unrealizedR:F2}R PeakR={peakR:F2}R");

        if (evaluation.Decision.ShouldExit && !evaluation.Decision.IsPartialExit)
        {
            _log($"    Exit: {evaluation.Decision.Reason} â€” {evaluation.Decision.Detail}");
            return evaluation.Decision.Reason;
        }

        if (evaluation.Decision.IsPartialExit)
        {
            _log($"    Partial: {evaluation.Decision.Reason} â€” {evaluation.Decision.Detail}");
            return evaluation.Decision.Reason;
        }

        return null;
    }

    /// <summary>
    /// Record a trade exit and update PnL.
    /// </summary>
    public double RecordExit(string symbol, double exitPrice, string reason)
    {
        if (!Positions.TryGetValue(symbol, out var pos))
            return 0;

        double pnl = pos.Side == TradeSide.Long
            ? (exitPrice - pos.EntryPrice) * pos.Shares
            : (pos.EntryPrice - exitPrice) * pos.Shares;

        double pnlR = pos.RiskPerShare > 0
            ? pnl / (pos.RiskPerShare * pos.Shares)
            : 0;

        DailyPnl += pnl;

        _log($"<<< EXITED {pos.Side} {symbol} @ ${exitPrice:F2} ({reason}) " +
             $"PnL=${pnl:F2} ({pnlR:F2}R) [{pos.Strategy}]");

        // Update trade log
        for (int i = TradeLog.Count - 1; i >= 0; i--)
        {
            if (TradeLog[i].Symbol == symbol && TradeLog[i].Status == "OPEN")
            {
                TradeLog[i].ExitPrice = exitPrice;
                TradeLog[i].Pnl = pnl;
                TradeLog[i].PnlR = pnlR;
                TradeLog[i].ExitReason = reason;
                TradeLog[i].Status = "CLOSED";
                break;
            }
        }

        Positions.Remove(symbol);
        return pnl;
    }

    /// <summary>
    /// Flatten all open positions.
    /// </summary>
    public void FlattenAll(Func<string, double> getCurrentPrice, string reason = "EOD")
    {
        foreach (var symbol in Positions.Keys.ToArray())
        {
            _log($"Flattening {symbol} ({reason})");
            var price = getCurrentPrice(symbol);
            RecordExit(symbol, price, reason);
        }
    }

    /// <summary>
    /// Print end-of-day summary.
    /// </summary>
    public void PrintSummary()
    {
        _log($"\n{new string('=', 60)}");
        _log("  END-OF-DAY SUMMARY");
        _log(new string('=', 60));
        _log($"  Total trades: {DailyTrades}");
        _log($"  Daily PnL: ${DailyPnl:F2}");

        if (TradeLog.Count > 0)
        {
            var closed = TradeLog.Where(t => t.Status == "CLOSED").ToList();
            var wins = closed.Count(t => t.Pnl > 0);
            if (closed.Count > 0)
            {
                _log($"  Win rate: {100.0 * wins / closed.Count:F0}% ({wins}/{closed.Count})");
            }

            foreach (var t in TradeLog)
            {
                var pnlStr = t.Status == "CLOSED" ? $"${t.Pnl:F2}" : "OPEN";
                _log($"    {t.Time} {t.Side} {t.Symbol} Entry=${t.Entry:F2} {pnlStr} [{t.Strategy}]");
            }
        }
        _log(new string('=', 60));
    }

    /// <summary>
    /// Simulate a full trading day using cached data (offline backtesting of the live bot logic).
    /// </summary>
    public void SimulateFromCached(string[]? symbols = null)
    {
        var syms = symbols ?? SymbolStrategy.Keys.ToArray();
        _log(new string('=', 60));
        _log("  LIVE PAPER BOT SIMULATION (cached data)");
        _log($"  Symbols: {string.Join(", ", syms)}");
        _log($"  Position size: {PositionSize} shares");
        _log("  Strategy assignments:");
        foreach (var (sym, stype) in SymbolStrategy)
        {
            if (syms.Contains(sym))
                _log($"    {sym} -> {stype}");
        }
        _log(new string('=', 60));

        // Load all data
        var allData = new Dictionary<string, (EnrichedBar[] M1, EnrichedBar[]? M5, EnrichedBar[]? M15, EnrichedBar[]? H1, EnrichedBar[]? D1)>();
        foreach (var sym in syms)
        {
            if (!CsvBarStorage.Exists(sym, "1m")) continue;
            var bars1m = TechnicalIndicators.EnrichWithIndicators(CsvBarStorage.LoadBars(sym, "1m"));
            L1L2Enrichment.Enrich(sym, bars1m);

            EnrichedBar[]? ctx5m = null, ctx15m = null, ctx1h = null, ctx1d = null;
            foreach (var tf in new[] { "5m", "15m", "1h", "1D" })
            {
                if (!CsvBarStorage.Exists(sym, tf)) continue;
                var b = CsvBarStorage.LoadBars(sym, tf);
                if (b.Length == 0) continue;
                var e = TechnicalIndicators.EnrichWithIndicators(b);
                switch (tf)
                {
                    case "5m": ctx5m = e; break;
                    case "15m": ctx15m = e; break;
                    case "1h": ctx1h = e; break;
                    case "1D": ctx1d = e; break;
                }
            }

            allData[sym] = (bars1m, ctx5m, ctx15m, ctx1h, ctx1d);
        }

        // Walk through bars for each symbol checking signals
        foreach (var (sym, (bars1m, ctx5m, ctx15m, ctx1h, ctx1d)) in allData)
        {
            if (!SymbolStrategy.ContainsKey(sym)) continue;
            if (DailyTrades >= MaxDailyTrades) break;

            var signal = CheckForSignal(sym, bars1m, ctx5m, ctx15m, ctx1h, ctx1d);
            if (signal != null && !Positions.ContainsKey(sym))
            {
                var now = bars1m[^1].Bar.Timestamp;
                RecordEntry(sym, signal, signal.EntryPrice, now);

                // Simulate exit using the last bar price
                var lastPrice = bars1m[^1].Bar.Close;
                BacktestBar? lastBarData = bars1m.Length >= 1 ? bars1m[^1].Bar : null;
                BacktestBar? prevBarData = bars1m.Length >= 2 ? bars1m[^2].Bar : null;
                var exitReason = ManagePosition(sym, lastPrice, now, lastBarData, prevBarData);
                if (exitReason != null)
                {
                    RecordExit(sym, lastPrice, exitReason);
                }
            }
        }

        PrintSummary();
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string GetStrategyName(string symbol) =>
        SymbolStrategy.TryGetValue(symbol, out var st) ? st.ToString() : "Unknown";

    private static IBacktestStrategy CreateStrategy(LiveStrategyType stratType)
    {
        IBacktestStrategy strategy = stratType switch
        {
            LiveStrategyType.TrendV13 => new ConductStrategyV3(CfgTrend),
            _ => new ConductStrategyV3(CfgTrend),
        };
        BacktestStrategyBase.InjectSelfLearning(strategy);
        return strategy;
    }

    private static ExitCascadeParams GetCascadeParams(LiveStrategyType stratType) => stratType switch
    {
        LiveStrategyType.TrendV13 => new ExitCascadeParams
        {
            HardStopR = CfgTrend.HardStopR, TrailR = CfgTrend.TrailR,
            Tp1R = CfgTrend.Tp1R, Tp2R = CfgTrend.Tp2R,
            BreakevenR = CfgTrend.BreakevenR, GivebackPct = CfgTrend.GivebackPct,
            MaxHoldSeconds = 90 * 60,
        },
        _ => new ExitCascadeParams
        {
            HardStopR = CfgTrend.HardStopR, TrailR = CfgTrend.TrailR,
            Tp1R = CfgTrend.Tp1R, Tp2R = CfgTrend.Tp2R,
            BreakevenR = CfgTrend.BreakevenR, GivebackPct = CfgTrend.GivebackPct,
            MaxHoldSeconds = 90 * 60,
        },
    };

    internal static PostFillConductExecutionResult EvaluateConduct(
        LivePosition position,
        string symbol,
        double currentPrice,
        DateTime nowEt,
        BacktestBar? lastBar,
        BacktestBar? prevBar,
        ExitCascadeParams cascadeParams)
    {
        var isLong = position.Side == TradeSide.Long;
        var peakPrice = position.PeakPrice;
        if (lastBar is not null)
        {
            peakPrice = isLong
                ? Math.Max(position.PeakPrice, lastBar.High)
                : Math.Min(position.PeakPrice, lastBar.Low);
        }

        var unrealizedPnlPeak = isLong
            ? (peakPrice - position.EntryPrice) * position.Shares
            : (position.EntryPrice - peakPrice) * position.Shares;

        var state = new FilledTradeState(
            IntentId: $"LP::{symbol}::{position.EntryTimeEt:yyyyMMddHHmmss}",
            Account: string.Empty,
            Symbol: symbol,
            Profile: LiveStrategyProfile.Default,
            Side: isLong ? PositionSide.Long : PositionSide.Short,
            FilledQuantity: position.Shares,
            OpenQuantity: position.Shares,
            AverageFillPrice: position.EntryPrice,
            EntryUtc: position.EntryTimeEt,
            EntryAtr14: position.AtrAtEntry,
            RiskPerShare: position.RiskPerShare,
            StopPrice: position.StopPrice,
            TakeProfitPrice: isLong
                ? position.EntryPrice + cascadeParams.Tp2R * position.RiskPerShare
                : position.EntryPrice - cascadeParams.Tp2R * position.RiskPerShare,
            UnrealizedPnlUsd: isLong
                ? (currentPrice - position.EntryPrice) * position.Shares
                : (position.EntryPrice - currentPrice) * position.Shares,
            UnrealizedPnlPeakUsd: Math.Max(0, unrealizedPnlPeak),
            MostFavorablePrice: peakPrice,
            MostAdversePrice: position.EntryPrice,
            ManagedTrailingStopPrice: position.TrailingStop,
            BreakevenActivated: position.BreakevenActivated,
            Tp1Activated: position.Tp1Hit,
            ProfitExtensionArmed: false,
            StrategyBucket: position.Strategy);

        var frame = CreateConductFrame(currentPrice, nowEt, lastBar, prevBar, position.AtrAtEntry);
        var policy = ConductPolicy.FromExitCascadeParams(cascadeParams);

        return PostFillConductExecutor.Execute(
            state,
            frame,
            policy,
            new DailyTradeConductOptions(
                ContinuationConfirmedOverride: false,
                SourcePolicy: "LivePaperBotLegacy"));
    }

    private static ConductMarketFrame CreateConductFrame(
        double currentPrice,
        DateTime nowEt,
        BacktestBar? lastBar,
        BacktestBar? prevBar,
        double atr14)
    {
        var timestamp = lastBar?.Timestamp ?? nowEt;
        var currentBar = lastBar ?? new BacktestBar(timestamp, currentPrice, currentPrice, currentPrice, currentPrice, 0);
        var features = new V3LiveFeatureSnapshot(
            TimestampUtc: timestamp,
            L1: new V3LiveL1Snapshot(timestamp, currentPrice, currentPrice, currentPrice, 0, 0, 0.0, true),
            L2: new V3LiveL2Snapshot(0, 0, 0.0, 0.0, false),
            IsReady: true,
            Price: currentPrice,
            Atr14: atr14,
            Rsi14: double.NaN,
            Vwap: currentPrice,
            DistFromVwapAtr: 0.0,
            BbPctB: double.NaN,
            KcMid: currentPrice,
            StochK: double.NaN,
            StochD: double.NaN,
            Adx14: double.NaN,
            Rvol: double.NaN,
            VolAccel: double.NaN,
            OfiSignal: 0.0,
            SqueezeOn: false,
            BbBandwidth: double.NaN,
            AtrRatio: 1.0,
            RejectReason: string.Empty,
            PrevClose: prevBar?.Close ?? double.NaN,
            PrevOpen: prevBar?.Open ?? double.NaN,
            PrevHigh: prevBar?.High ?? double.NaN,
            PrevLow: prevBar?.Low ?? double.NaN,
            PrevVolume: prevBar?.Volume ?? double.NaN,
            CurrentOpen: currentBar.Open,
            CurrentHigh: currentBar.High,
            CurrentLow: currentBar.Low);

        var completedCandles = prevBar is null
            ? Array.Empty<LiveCandle>()
            :
            [
                new LiveCandle(prevBar.Timestamp, prevBar.Open, prevBar.High, prevBar.Low, prevBar.Close, prevBar.Volume, 1)
            ];

        var candles = new V3LiveCandleSnapshot(
            string.Empty,
            timestamp,
            new Dictionary<int, V3LiveTimeframeCandleData>
            {
                [60] = new(
                    60,
                    completedCandles,
                    new LiveCandle(currentBar.Timestamp, currentBar.Open, currentBar.High, currentBar.Low, currentBar.Close, currentBar.Volume, 1))
            });

        return ConductMarketFrame.FromLiveInputs(features, candles, timestampUtc: timestamp);
    }
}

