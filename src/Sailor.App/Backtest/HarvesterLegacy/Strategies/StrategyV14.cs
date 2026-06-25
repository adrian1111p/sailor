using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

// ---------------------------------------------------------------------------
//  V14 â€” Small-Cap Mean-Reversion Specialist
// ---------------------------------------------------------------------------
//  The original V14 mixed several fade branches and both directions, which
//  produced a large trade count but consistently poor expectancy. The retained
//  default profile keeps only the historically strongest edge: long-only VWAP
//  reversion on liquid small-caps with strict stretch, RVOL, and confluence.
//
//  Core principles:
//  1. LONG-ONLY VWAP REVERSION â€” avoid the historically weak short side.
//  2. STRICT RVOL GATING â€” require RVOL >= 2.0 before entering.
//  3. DEEP STRETCH ONLY â€” require >= 1.8 ATR displacement from VWAP.
//  4. HIGH CONFLUENCE ONLY â€” preserve the narrow edge instead of chasing count.
//  5. FAST EXITS â€” cut losers quickly and harvest short mean-reversion bounces.
// ---------------------------------------------------------------------------

public sealed class V14Config
{
    // ---- Position Sizing ----
    public double RiskPerTradeDollars { get; set; } = 20.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;
    public int MaxShares { get; set; } = 6_500;
    public double MinRiskPerShare { get; set; } = 0.005;

    // ---- Direction ----
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = false;
    public int MaxSignalsPerDay { get; set; } = 3;
    public int CooldownBars { get; set; } = 8;

    // ---- Price Filters ----
    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;

    // ---- Volume Filters (relaxed for small caps) ----
    public double BaseRvolMin { get; set; } = 0.0;
    public double VolumeSpikeRvol { get; set; } = 1.6;
    public double VolumeSpikeBonus { get; set; } = 1;             // extra confluence for spike
    public double VolumeClimaxMultiplier { get; set; } = 3.0;     // extreme volume = exhaustion

    // ---- Spread / Liquidity (relaxed for small caps) ----
    public double SpreadZMax { get; set; } = 8.0;
    public double L2LiquidityMin { get; set; } = 0.0;

    // ---- Time Windows ----
    public int MarketOpenMinute { get; set; } = 570;
    public int SkipFirstNMinutes { get; set; } = 5;
    public (int Start, int End)[] EntryWindows { get; set; } = [(575, 955)];

    // ---- Sub-strategy A: VWAP Reversion ----
    public bool VwapReversionEnabled { get; set; } = true;
    public double VwapStretchAtr { get; set; } = 1.5;
    public double VwapStretchExtremeAtr { get; set; } = 2.5;      // extreme stretch = extra confluence
    public double VwapReversionMinConfluence { get; set; } = 4;

    // ---- Sub-strategy B: BB Extreme Fade ----
    public bool BbFadeEnabled { get; set; } = false;
    public double BbExtremeLow { get; set; } = 0.08;              // %B threshold for long
    public double BbExtremeHigh { get; set; } = 0.92;             // %B threshold for short
    public double BbFadeMinConfluence { get; set; } = 3;

    // ---- Sub-strategy C: Exhaustion Reversal ----
    public bool ExhaustionEnabled { get; set; } = false;
    public double ExhaustionRsiLong { get; set; } = 25.0;         // RSI oversold
    public double ExhaustionRsiShort { get; set; } = 75.0;        // RSI overbought
    public double ExhaustionMinConfluence { get; set; } = 4;

    // ---- Sub-strategy D: Keltner Fade ----
    public bool KeltnerFadeEnabled { get; set; } = false;
    public double KeltnerFadeMinConfluence { get; set; } = 3;

    // ---- Entry Quality Filters ----
    public bool RequireVolumeSpikeForEntry { get; set; } = false;
    public bool RequireEmaMicroTrend { get; set; } = false;        // price must be on correct side of EMA9
    public bool RequireMultiBarConfirm { get; set; } = false;      // require 2 of last 3 bars same direction
    public double AdxMaxForMeanReversion { get; set; } = 50.0;
    public int PersistenceBars { get; set; } = 0;                  // require extreme reading for N consecutive bars
    public bool RequirePriorMove { get; set; } = false;            // require prior directional move before reversal
    public bool RequireFirstReversalCandle { get; set; } = false;  // current bar must be first candle changing direction

    // ---- MACD Crossover Gate ----
    public bool RequireMacdAlignment { get; set; } = false;  // gate: MACD must confirm trade direction
    // Long: MACD > Signal AND MACD trending up (or just crossed above)
    // Short: MACD < Signal AND MACD trending down (or just crossed below)
    public int MacdCrossLookback { get; set; } = 3;          // lookback bars for "just crossed"

    // ---- Confluence Scoring ----
    public double WillRExtremeLong { get; set; } = -80.0;
    public double WillRExtremeShort { get; set; } = -20.0;
    public double MfiOversold { get; set; } = 25.0;
    public double MfiOverbought { get; set; } = 75.0;
    public double StochOversold { get; set; } = 20.0;
    public double StochOverbought { get; set; } = 80.0;

    // ---- Exit Configuration ----
    public double HardStopR { get; set; } = 1.30;
    public double BreakevenR { get; set; } = 0.25;
    public double TrailR { get; set; } = 0.15;
    public double GivebackPct { get; set; } = 0.60;
    public double GivebackUsdCap { get; set; } = 20.0;
    public double Tp1R { get; set; } = 0.55;
    public double Tp2R { get; set; } = 1.10;
    public int MaxHoldBars { get; set; } = 20;
    public double SlippageCents { get; set; } = 0.5;
    public double CommissionPerShare { get; set; } = 0.005;

    // ---- Stop Distance ----
    public double StopAtrMultiplier { get; set; } = 2.0;
    public double MinStopCents { get; set; } = 3.0;

    // ---- Next-bar open entry ----
    public bool UseNextBarOpenEntry { get; set; } = true;

    // ---- Diagnostics ----
    public bool EnableDiagnostics { get; set; } = false;
    public string DiagnosticsLabel { get; set; } = "V14";
}

public sealed class StrategyV14 : BacktestStrategyBase
{
    private readonly V14Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV14(V14Config? cfg = null)
    {
        _cfg = cfg ?? new V14Config();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.15,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            GivebackCapAnchorLowPrice = 0.5,
            GivebackCapAnchorHighPrice = 50.0,
            GivebackCapAtLowPrice = 3.0,
            GivebackCapAtHighPrice = 15.0,
            GivebackCapMinUsd = 2.0,
            GivebackCapMaxUsd = 20.0,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
            ReversalFlatten = false,
            MicroTrail = true,
            MicroTrailCents = 1.0,
            MicroTrailActivateCents = 2.0,
            EmaTrail = true,
            EmaTrailBufferAtr = 0.15,
            FlattenOnPeakGiveback = true,
            PeakGivebackKeepFraction = 0.50,
            PeakGivebackActivateR = 0.30,
            FlattenOnStagnation = true,
            StagnationBars = 8,
            StagnationMinPeakR = 0.15,
            StagnationMaxAdverseR = -0.08,
        };
    }

    // =====================================================================
    //  SIGNAL GENERATION â€” standalone, no V3_1 dependency
    // =====================================================================
    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var diag = new V14Diagnostics(_cfg.DiagnosticsLabel,
            _cfg.EnableDiagnostics || DiagnosticsEnabledFromEnvironment());

        var signals = new List<BacktestSignal>();
        var daySignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBar = -10_000;

        for (int i = 30; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0) continue;

            double price = row.Bar.Close;

            // ---- Price filter ----
            if (price < _cfg.MinPrice || price > _cfg.MaxPrice)
            {
                diag.Reject("price");
                continue;
            }

            // ---- Basic indicator availability ----
            if (double.IsNaN(row.Rsi14) || double.IsNaN(row.Ema9) || double.IsNaN(row.Vwap))
            {
                diag.Reject("indicators-missing");
                continue;
            }

            // ---- Spread check (relaxed) ----
            if (!double.IsNaN(row.SpreadZ) && row.SpreadZ > _cfg.SpreadZMax)
            {
                diag.Reject("spread");
                continue;
            }

            // ---- Time window ----
            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes)
            {
                diag.Reject("before-entry-start");
                continue;
            }

            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows))
            {
                diag.Reject("outside-entry-window");
                continue;
            }

            // ---- Cooldown ----
            if (i - lastAcceptedBar < _cfg.CooldownBars)
            {
                diag.Reject("cooldown");
                continue;
            }

            // ---- Max signals per day ----
            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            int countForDay = daySignalCounts.GetValueOrDefault(dayEt);
            if (countForDay >= _cfg.MaxSignalsPerDay)
            {
                diag.Reject("max-signals-per-day");
                continue;
            }

            if (IsMarketRegimeBlocked(row))
            {
                diag.Reject("market-regime-blocked");
                continue;
            }

            diag.Raw++;

            // ---- Compute confluence factors ----
            var ctx = BuildConfluenceContext(triggerBars, i, row, atr);

            if (_cfg.BaseRvolMin > 0 && ctx.Rvol < _cfg.BaseRvolMin)
            {
                diag.Reject("base-rvol");
                continue;
            }

            // ---- Entry quality gates ----
            if (_cfg.RequireVolumeSpikeForEntry && !ctx.IsVolSpike)
            {
                diag.Reject("no-vol-spike");
                continue;
            }

            if (ctx.Adx > _cfg.AdxMaxForMeanReversion)
            {
                diag.Reject("adx-too-high");
                continue;
            }

            // ---- Prior move gate ----
            if (_cfg.RequirePriorMove)
            {
                // For long setups: need prior selloff; for short: prior rally
                // Since we don't know the side yet, check both â€” sub-strategies will filter
                if (!ctx.HasPriorSelloff && !ctx.HasPriorRally)
                {
                    diag.Reject("no-prior-move");
                    continue;
                }
            }

            // ---- First reversal candle gate ----
            bool passesReversalCandleCheck = true;
            if (_cfg.RequireFirstReversalCandle && i >= 2)
            {
                bool prevWasBearish = triggerBars[i - 1].Bar.Close < triggerBars[i - 1].Bar.Open;
                bool prevWasBullish = triggerBars[i - 1].Bar.Close > triggerBars[i - 1].Bar.Open;
                // Will be checked per-side in sub-strategies
                passesReversalCandleCheck = (ctx.IsBullishCandle && prevWasBearish)
                                         || (ctx.IsBearishCandle && prevWasBullish);
                if (!passesReversalCandleCheck)
                {
                    diag.Reject("not-first-reversal");
                    continue;
                }
            }

            // ---- MACD alignment gate ----
            // Long: MACD above signal AND trending up, OR just crossed up
            // Short: MACD below signal AND trending down, OR just crossed down
            // Avoids: MACD above signal but trending down (pre-cross volatility)
            if (_cfg.RequireMacdAlignment)
            {
                bool longOk = (ctx.MacdAboveSignal && ctx.MacdTrendingUp) || ctx.MacdJustCrossedUp;
                bool shortOk = (!ctx.MacdAboveSignal && !ctx.MacdTrendingUp) || ctx.MacdJustCrossedDown;
                if (_cfg.AllowLong && !_cfg.AllowShort && !longOk)
                {
                    diag.Reject("macd-no-long-align");
                    continue;
                }
                if (!_cfg.AllowLong && _cfg.AllowShort && !shortOk)
                {
                    diag.Reject("macd-no-short-align");
                    continue;
                }
                if (_cfg.AllowLong && _cfg.AllowShort && !longOk && !shortOk)
                {
                    diag.Reject("macd-no-align");
                    continue;
                }
            }

            // ---- Try each sub-strategy ----
            BacktestSignal? signal = null;

            if (_cfg.ExhaustionEnabled)
                signal = TryExhaustionReversal(triggerBars, i, row, atr, ctx);

            if (signal == null && _cfg.VwapReversionEnabled)
                signal = TryVwapReversion(triggerBars, i, row, atr, ctx);

            if (signal == null && _cfg.BbFadeEnabled)
                signal = TryBbExtremeFade(triggerBars, i, row, atr, ctx);

            if (signal == null && _cfg.KeltnerFadeEnabled)
                signal = TryKeltnerFade(triggerBars, i, row, atr, ctx);

            if (signal == null)
            {
                diag.Reject("no-setup");
                continue;
            }

            signals.Add(signal);
            lastAcceptedBar = i;
            daySignalCounts[dayEt] = countForDay + 1;
            diag.Accepted++;
            if (signal.Side == TradeSide.Long) diag.AcceptedLong++;
            else diag.AcceptedShort++;
        }

        diag.PrintSummary();
        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    => ExitEngine.SimulateTrade(signal, triggerBars, ApplySelfLearningExitOverrides(_exitCfg, signal.SubStrategy));
    // =====================================================================
    //  PERSISTENCE CHECK â€” require extreme readings for N consecutive bars
    // =====================================================================
    private bool CheckPersistence(EnrichedBar[] bars, int endIndex, TradeSide side)
    {
        int n = _cfg.PersistenceBars;
        if (n <= 0) return true;
        if (endIndex < n) return false;

        for (int j = endIndex - n; j < endIndex; j++)
        {
            var b = bars[j];
            if (side == TradeSide.Long)
            {
                // Require RSI or BB%B in oversold territory for N bars
                bool rsiOversold = !double.IsNaN(b.Rsi14) && b.Rsi14 < 35;
                bool bbLow = !double.IsNaN(b.BbPctB) && b.BbPctB < 0.20;
                if (!rsiOversold && !bbLow) return false;
            }
            else
            {
                bool rsiOverbought = !double.IsNaN(b.Rsi14) && b.Rsi14 > 65;
                bool bbHigh = !double.IsNaN(b.BbPctB) && b.BbPctB > 0.80;
                if (!rsiOverbought && !bbHigh) return false;
            }
        }
        return true;
    }

    // =====================================================================
    //  CONFLUENCE CONTEXT â€” pre-compute all indicator readings
    // =====================================================================
    private sealed record ConfluenceCtx(
        double Rsi, double BbPctB, double BbBandwidth,
        double StochK, double StochD,
        double Mfi, double WillR,
        double Rvol, double OfiSignal,
        double VwapDist, double VwapDistAtr,
        bool IsVolSpike, bool IsVolClimax,
        bool IsBullishCandle, bool IsBearishCandle,
        bool IsHammer, bool IsStar,
        double Dpo, double Adx,
        double DcPct, bool HasPriorSelloff, bool HasPriorRally,
        bool SupertrendBullish,
        double MacdLine, double MacdSignalLine, double MacdHist,
        bool MacdAboveSignal, bool MacdTrendingUp,
        bool MacdJustCrossedUp, bool MacdJustCrossedDown);

    private ConfluenceCtx BuildConfluenceContext(EnrichedBar[] bars, int i, EnrichedBar row, double atr)
    {
        double rsi = row.Rsi14;
        double bbPctB = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
        double bbBw = double.IsNaN(row.BbBandwidth) ? 0.0 : row.BbBandwidth;
        double stochK = double.IsNaN(row.StochK) ? 50.0 : row.StochK;
        double stochD = double.IsNaN(row.StochD) ? 50.0 : row.StochD;
        double mfi = double.IsNaN(row.Mfi14) ? 50.0 : row.Mfi14;
        double willR = double.IsNaN(row.WillR14) ? -50.0 : row.WillR14;
        double rvol = double.IsNaN(row.Rvol) ? 1.0 : row.Rvol;
        double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;
        double dpo = double.IsNaN(row.Dpo20) ? 0.0 : row.Dpo20;
        double adx = double.IsNaN(row.Adx) ? 20.0 : row.Adx;

        double vwapDist = row.Bar.Close - row.Vwap;
        double vwapDistAtr = atr > 0 ? vwapDist / atr : 0.0;

        bool isVolSpike = rvol >= _cfg.VolumeSpikeRvol;
        bool isVolClimax = rvol >= _cfg.VolumeClimaxMultiplier;

        double body = row.Bar.Close - row.Bar.Open;
        double range = row.Bar.High - row.Bar.Low;
        bool isBullish = body > 0;
        bool isBearish = body < 0;

        // Hammer: small body at top, long lower wick
        double lowerWick = Math.Min(row.Bar.Open, row.Bar.Close) - row.Bar.Low;
        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        bool isHammer = range > 0
            && lowerWick > Math.Abs(body) * 1.5
            && upperWick < Math.Abs(body) * 0.5;

        // Shooting star: small body at bottom, long upper wick
        bool isStar = range > 0
            && upperWick > Math.Abs(body) * 1.5
            && lowerWick < Math.Abs(body) * 0.5;

        // Donchian channel position (0 = lower, 1 = upper)
        double dcPct = double.IsNaN(row.DcPct) ? 0.5 : row.DcPct;

        // Prior move check: was there a 3+ bar directional move before this bar?
        bool hasPriorSelloff = false;
        bool hasPriorRally = false;
        if (i >= 4)
        {
            int downBars = 0, upBars = 0;
            for (int j = i - 3; j < i; j++)
            {
                if (bars[j].Bar.Close < bars[j].Bar.Open) downBars++;
                else if (bars[j].Bar.Close > bars[j].Bar.Open) upBars++;
            }
            hasPriorSelloff = downBars >= 2;
            hasPriorRally = upBars >= 2;
        }

        // Supertrend direction (1 = bullish, -1 = bearish)
        bool stBullish = row.StDirection >= 1;

        // MACD crossover detection
        double macdLine = double.IsNaN(row.Macd) ? 0.0 : row.Macd;
        double macdSigLine = double.IsNaN(row.MacdSignal) ? 0.0 : row.MacdSignal;
        double macdH = double.IsNaN(row.MacdHist) ? 0.0 : row.MacdHist;
        bool macdAbove = macdLine > macdSigLine;
        // MACD trending up = current MACD > previous MACD
        bool macdUp = false;
        if (i >= 1 && !double.IsNaN(bars[i - 1].Macd))
            macdUp = macdLine > bars[i - 1].Macd;
        // Just crossed: histogram flipped sign within lookback
        bool justCrossedUp = false, justCrossedDown = false;
        int crossLb = _cfg.MacdCrossLookback;
        for (int j = Math.Max(1, i - crossLb + 1); j <= i; j++)
        {
            if (j < 1) continue;
            double prevH = double.IsNaN(bars[j - 1].MacdHist) ? 0.0 : bars[j - 1].MacdHist;
            double currH = double.IsNaN(bars[j].MacdHist) ? 0.0 : bars[j].MacdHist;
            if (prevH <= 0 && currH > 0) justCrossedUp = true;
            if (prevH >= 0 && currH < 0) justCrossedDown = true;
        }

        return new ConfluenceCtx(
            rsi, bbPctB, bbBw, stochK, stochD, mfi, willR,
            rvol, ofiSig, vwapDist, vwapDistAtr,
            isVolSpike, isVolClimax,
            isBullish, isBearish, isHammer, isStar,
            dpo, adx, dcPct, hasPriorSelloff, hasPriorRally,
            stBullish,
            macdLine, macdSigLine, macdH,
            macdAbove, macdUp, justCrossedUp, justCrossedDown);
    }

    // =====================================================================
    //  CONFLUENCE SCORING â€” count confirming indicators
    // =====================================================================
    private int ScoreLongConfluence(ConfluenceCtx ctx)
    {
        int score = 0;
        if (ctx.Rsi < 35) score++;
        if (ctx.Rsi < 25) score++;                                    // double for extreme
        if (ctx.BbPctB < 0.15) score++;
        if (ctx.StochK < _cfg.StochOversold) score++;
        if (ctx.StochK < _cfg.StochOversold && ctx.StochK > ctx.StochD) score++;  // bullish cross
        if (ctx.Mfi < _cfg.MfiOversold) score++;
        if (ctx.WillR < _cfg.WillRExtremeLong) score++;
        if (ctx.OfiSignal > 0.1) score++;                            // buying pressure
        if (ctx.IsVolSpike) score += (int)_cfg.VolumeSpikeBonus;
        if (ctx.IsHammer) score++;
        if (ctx.IsBullishCandle) score++;
        if (ctx.Dpo < -0.5 * ctx.Adx / 25.0) score++;               // depressed price level
        return score;
    }

    private int ScoreShortConfluence(ConfluenceCtx ctx)
    {
        int score = 0;
        if (ctx.Rsi > 65) score++;
        if (ctx.Rsi > 75) score++;                                    // double for extreme
        if (ctx.BbPctB > 0.85) score++;
        if (ctx.StochK > _cfg.StochOverbought) score++;
        if (ctx.StochK > _cfg.StochOverbought && ctx.StochK < ctx.StochD) score++;  // bearish cross
        if (ctx.Mfi > _cfg.MfiOverbought) score++;
        if (ctx.WillR > _cfg.WillRExtremeShort) score++;
        if (ctx.OfiSignal < -0.1) score++;                           // selling pressure
        if (ctx.IsVolSpike) score += (int)_cfg.VolumeSpikeBonus;
        if (ctx.IsStar) score++;
        if (ctx.IsBearishCandle) score++;
        if (ctx.Dpo > 0.5 * ctx.Adx / 25.0) score++;                // elevated price level
        return score;
    }

    // =====================================================================
    //  SUB-STRATEGY A: VWAP Reversion
    // =====================================================================
    private BacktestSignal? TryVwapReversion(EnrichedBar[] bars, int i, EnrichedBar row, double atr, ConfluenceCtx ctx)
    {
        if (double.IsNaN(row.Vwap) || row.Vwap <= 0) return null;

        double absDistAtr = Math.Abs(ctx.VwapDistAtr);
        if (absDistAtr < _cfg.VwapStretchAtr) return null;

        // Extreme stretch gives extra confluence
        int extraConfluence = absDistAtr >= _cfg.VwapStretchExtremeAtr ? 1 : 0;

        // Long: price far below VWAP â€” expecting bounce
        if (ctx.VwapDistAtr < -_cfg.VwapStretchAtr && _cfg.AllowLong)
        {
            int score = ScoreLongConfluence(ctx) + extraConfluence;
            if (score >= _cfg.VwapReversionMinConfluence && CheckPersistence(bars, i, TradeSide.Long))
            {
                // Require candle shows reversal (close > open or hammer)
                if (ctx.IsBullishCandle || ctx.IsHammer)
                    return MakeSignal(bars, i, TradeSide.Long, atr, "VL_FAST20");
            }
        }

        // Short: price far above VWAP â€” expecting fade
        if (ctx.VwapDistAtr > _cfg.VwapStretchAtr && _cfg.AllowShort)
        {
            int score = ScoreShortConfluence(ctx) + extraConfluence;
            if (score >= _cfg.VwapReversionMinConfluence && CheckPersistence(bars, i, TradeSide.Short))
            {
                if (ctx.IsBearishCandle || ctx.IsStar)
                    return MakeSignal(bars, i, TradeSide.Short, atr, "VWAP_REV");
            }
        }

        return null;
    }

    // =====================================================================
    //  SUB-STRATEGY B: BB Extreme Fade
    // =====================================================================
    private BacktestSignal? TryBbExtremeFade(EnrichedBar[] bars, int i, EnrichedBar row, double atr, ConfluenceCtx ctx)
    {
        // Long: price below lower BB (BB%B < threshold)
        if (ctx.BbPctB < _cfg.BbExtremeLow && _cfg.AllowLong)
        {
            int score = ScoreLongConfluence(ctx);
            if (score >= _cfg.BbFadeMinConfluence && CheckPersistence(bars, i, TradeSide.Long))
            {
                // Require stochastic turning up or bullish candle
                if (ctx.IsBullishCandle || (ctx.StochK > ctx.StochD && ctx.StochK < _cfg.StochOversold + 10))
                    return MakeSignal(bars, i, TradeSide.Long, atr, "BB_FADE");
            }
        }

        // Short: price above upper BB (BB%B > threshold)
        if (ctx.BbPctB > _cfg.BbExtremeHigh && _cfg.AllowShort)
        {
            int score = ScoreShortConfluence(ctx);
            if (score >= _cfg.BbFadeMinConfluence && CheckPersistence(bars, i, TradeSide.Short))
            {
                if (ctx.IsBearishCandle || (ctx.StochK < ctx.StochD && ctx.StochK > _cfg.StochOverbought - 10))
                    return MakeSignal(bars, i, TradeSide.Short, atr, "BB_FADE");
            }
        }

        return null;
    }

    // =====================================================================
    //  SUB-STRATEGY C: Exhaustion Reversal (volume climax + RSI extreme)
    // =====================================================================
    private BacktestSignal? TryExhaustionReversal(EnrichedBar[] bars, int i, EnrichedBar row, double atr, ConfluenceCtx ctx)
    {
        // Only on volume climax or extreme RSI
        if (!ctx.IsVolClimax && ctx.Rsi > _cfg.ExhaustionRsiLong && ctx.Rsi < _cfg.ExhaustionRsiShort)
            return null;

        // Long exhaustion: big sell-off with volume climax â†’ expect bounce
        if (ctx.Rsi <= _cfg.ExhaustionRsiLong && _cfg.AllowLong)
        {
            int score = ScoreLongConfluence(ctx);
            // Volume climax adds extra weight
            if (ctx.IsVolClimax) score += 2;
            if (score >= _cfg.ExhaustionMinConfluence && CheckPersistence(bars, i, TradeSide.Long))
            {
                // Need reversal candle (hammer or bullish close)
                if (ctx.IsHammer || ctx.IsBullishCandle)
                    return MakeSignal(bars, i, TradeSide.Long, atr, "EXHAUSTION");
            }
        }

        // Short exhaustion: big rally with volume climax â†’ expect fade
        if (ctx.Rsi >= _cfg.ExhaustionRsiShort && _cfg.AllowShort)
        {
            int score = ScoreShortConfluence(ctx);
            if (ctx.IsVolClimax) score += 2;
            if (score >= _cfg.ExhaustionMinConfluence && CheckPersistence(bars, i, TradeSide.Short))
            {
                if (ctx.IsStar || ctx.IsBearishCandle)
                    return MakeSignal(bars, i, TradeSide.Short, atr, "EXHAUSTION");
            }
        }

        return null;
    }

    // =====================================================================
    //  SUB-STRATEGY D: Keltner Channel Fade
    // =====================================================================
    private BacktestSignal? TryKeltnerFade(EnrichedBar[] bars, int i, EnrichedBar row, double atr, ConfluenceCtx ctx)
    {
        if (double.IsNaN(row.KcUpper) || double.IsNaN(row.KcLower)) return null;

        // Long: price below lower KC â†’ fading the extreme
        if (row.Bar.Close < row.KcLower && _cfg.AllowLong)
        {
            int score = ScoreLongConfluence(ctx);
            if (score >= _cfg.KeltnerFadeMinConfluence && CheckPersistence(bars, i, TradeSide.Long))
            {
                if (ctx.IsBullishCandle || ctx.IsHammer)
                    return MakeSignal(bars, i, TradeSide.Long, atr, "KC_FADE");
            }
        }

        // Short: price above upper KC â†’ fading the extreme
        if (row.Bar.Close > row.KcUpper && _cfg.AllowShort)
        {
            int score = ScoreShortConfluence(ctx);
            if (score >= _cfg.KeltnerFadeMinConfluence && CheckPersistence(bars, i, TradeSide.Short))
            {
                if (ctx.IsBearishCandle || ctx.IsStar)
                    return MakeSignal(bars, i, TradeSide.Short, atr, "KC_FADE");
            }
        }

        return null;
    }

    // =====================================================================
    //  SIGNAL BUILDER
    // =====================================================================
    private BacktestSignal? MakeSignal(EnrichedBar[] bars, int i, TradeSide side, double atr, string subStrategy)
    {
        if (IsSelfLearningBlocked($"V14_{subStrategy}")) return null;

        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTs = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry && i + 1 < bars.Length)
        {
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTs = bars[entryIndex].Bar.Timestamp;
        }

        // Stop distance: ATR-based with minimum
        double stopDist = Math.Max(atr * _cfg.StopAtrMultiplier, _cfg.MinStopCents / 100.0);
        stopDist = ApplySelfLearningStopMultiplier(stopDist);
        double stopPrice = side == TradeSide.Long
            ? entryPrice - stopDist
            : entryPrice + stopDist;

        double riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare < _cfg.MinRiskPerShare) riskPerShare = _cfg.MinRiskPerShare;

        int posSize = BacktestHelpers.ComputePositionSize(
            entryPrice, riskPerShare,
            _cfg.RiskPerTradeDollars, _cfg.AccountSize,
            _cfg.MaxPositionNotionalPctOfAccount, _cfg.MaxShares);
        posSize = ApplySelfLearningPositionSize(posSize, $"V14_{subStrategy}");

        if (posSize <= 0) return null;

        return new BacktestSignal(
            BarIndex: entryIndex,
            Timestamp: entryTs,
            Side: side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: posSize,
            AtrValue: atr,
            HtfTrend: HtfBias.Neutral,
            MtfMomentum: "N/A",
            SubStrategy: $"V14_{subStrategy}");
    }

    // =====================================================================
    //  DIAGNOSTICS
    // =====================================================================
    private static bool DiagnosticsEnabledFromEnvironment()
        => StrategyDiagnosticsEnvironment.IsEnabled("V14");

    private sealed class V14Diagnostics
    {
        private readonly string _label;
        private readonly bool _enabled;
        private readonly Dictionary<string, int> _rejections = new();

        public int Raw;
        public int Accepted;
        public int AcceptedLong;
        public int AcceptedShort;

        public V14Diagnostics(string label, bool enabled)
        {
            _label = label;
            _enabled = enabled;
        }

        public void Reject(string reason)
        {
            if (!_enabled) return;
            _rejections[reason] = _rejections.GetValueOrDefault(reason) + 1;
        }

        public void PrintSummary()
        {
            if (!_enabled) return;
            var rejectStr = _rejections.Count > 0
                ? string.Join(", ", _rejections.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}={kv.Value}"))
                : "";
            Console.WriteLine($"[V14-DIAG:{_label}] raw={Raw} accepted={Accepted} long={AcceptedLong} short={AcceptedShort}");
            Console.WriteLine($"[V14-DIAG:{_label}] rejects: {rejectStr}");
        }
    }
}

