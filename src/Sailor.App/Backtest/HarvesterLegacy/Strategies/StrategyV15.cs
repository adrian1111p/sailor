using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

// ---------------------------------------------------------------------------
//  V15 â€” Small-Cap Retained Short Profile
// ---------------------------------------------------------------------------
//  RETAINED DEFAULT:
//  - Short-only by default on the active 52-symbol small-cap basket
//  - Keeps breakdown continuation as the retained short profile
//  - Disables divergence, HOD rejection, and parabolic exhaustion by default
//    because they either degrade results or are already blocked/de-prioritized
//    by the current self-learning layer
//  - Preserves long VWAP reversion as an opt-in path, but routes it through an
//    exact retained setup key instead of the generic VWAP family bucket
// ---------------------------------------------------------------------------

public sealed class V15Config
{
    // ---- Position Sizing ----
    public double RiskPerTradeDollars { get; set; } = 20.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;
    public int MaxShares { get; set; } = 6_500;
    public double MinRiskPerShare { get; set; } = 0.005;

    // ---- Direction & Gating ----
    public bool AllowLong { get; set; } = false;
    public bool AllowShort { get; set; } = true;
    public int MaxSignalsPerDay { get; set; } = 2;
    public int CooldownBars { get; set; } = 10;

    // ---- Price Filters ----
    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;

    // ---- Volume Filters ----
    public double VolumeSpikeRvol { get; set; } = 1.8;
    public double VolumeClimaxMultiplier { get; set; } = 3.0;

    // ---- Spread / Liquidity ----
    public double SpreadZMax { get; set; } = 6.5;

    // ---- Time Windows ----
    public int MarketOpenMinute { get; set; } = 570;        // 9:30 AM ET
    public int SkipFirstNMinutes { get; set; } = 5;
    public (int Start, int End)[] EntryWindows { get; set; } = [(575, 955)];
    public int ShortEarliestMinuteEt { get; set; } = 620;  // 10:20 AM â€” avoid opening squeezes

    // ---- Exit Configuration ----
    public double HardStopR { get; set; } = 0.95;
    public double BreakevenR { get; set; } = 0.30;
    public double TrailR { get; set; } = 0.12;
    public double GivebackPct { get; set; } = 0.50;
    public double GivebackUsdCap { get; set; } = 20.0;
    public double Tp1R { get; set; } = 0.60;
    public double Tp2R { get; set; } = 1.35;
    public int MaxHoldBars { get; set; } = 18;
    public double SlippageCents { get; set; } = 0.5;
    public double CommissionPerShare { get; set; } = 0.005;
    public double StopAtrMultiplier { get; set; } = 1.6;
    public double MinStopCents { get; set; } = 3.0;
    public bool UseNextBarOpenEntry { get; set; } = true;

    // ========= LONG L1: VWAP Reversion (V14 proven) =========
    public bool VwapRevLongEnabled { get; set; } = false;
    public double VwapStretchAtr { get; set; } = 1.8;
    public double VwapStretchExtremeAtr { get; set; } = 2.5;
    public int VwapRevLongMinConfluence { get; set; } = 6;
    public bool RequireVolumeSpikeForLong { get; set; } = true;
    public double LongVolumeSpikeRvol { get; set; } = 2.0;
    public double AdxMaxForMeanReversion { get; set; } = 30.0;
    public double VolumeSpikeBonus { get; set; } = 1;
    // Scoring thresholds (matching V14 winner defaults)
    public double StochOversold { get; set; } = 20.0;
    public double MfiOversold { get; set; } = 25.0;
    public double WillRExtremeLong { get; set; } = -80.0;

    // ========= SHORT S1: Parabolic Exhaustion =========
    public bool ParabolicEnabled { get; set; } = false;
    public double ParabolicPctThreshold { get; set; } = 0.12;  // 12% move from N-bar low
    public int ParabolicLookbackBars { get; set; } = 10;
    public double ParabolicRsiMin { get; set; } = 65.0;
    public double ParabolicRvolMin { get; set; } = 2.0;
    public int ParabolicMinConfirm { get; set; } = 4;

    // ========= SHORT S2: HOD Rejection =========
    public bool HodReversalEnabled { get; set; } = false;
    public double HodDropAtr { get; set; } = 1.0;
    public int HodMinBarsAfterHod { get; set; } = 3;
    public int HodMinConfirm { get; set; } = 4;

    // ========= SHORT S3: Breakdown Continuation =========
    public bool BreakdownEnabled { get; set; } = true;
    public int BreakdownMinConfirm { get; set; } = 4;
    public bool UseContinuationScoreOnBreakdown { get; set; } = false;
    public int BreakdownContinuationScoreFloor { get; set; } = 3;

    // ========= SHORT S4: Bearish Divergence =========
    public bool DivergenceEnabled { get; set; } = false;
    public int DivergenceLookback { get; set; } = 10;
    public double DivergenceRsiGap { get; set; } = 6.0;    // RSI must be >= 6 pts lower
    public int DivergenceMinConfirm { get; set; } = 4;

    // ========= L1/L2 Gates & Bonuses (mirrors V3LiveConfig) =========
    public bool UseL1L2 { get; set; } = true;

    // L2 Directional Gate
    public bool UseL2DirectionalGate { get; set; } = true;
    public double L2ImbalanceMinForLong { get; set; } = 1.05;
    public double L2ImbalanceMaxForShort { get; set; } = 0.95;

    // L2 DWMP Gate
    public bool UseL2DwmpGate { get; set; } = true;

    // L1 Size Ratio Gate
    public bool UseL1SizeRatioGate { get; set; } = true;
    public double L1SizeRatioMinForLong { get; set; } = 1.1;
    public double L1SizeRatioMaxForShort { get; set; } = 0.9;

    // L1 Last vs Mid Gate
    public bool UseL1LastVsMidGate { get; set; } = true;

    // L1 Spread Tightening Bonus
    public bool UseL1SpreadTighteningBonus { get; set; } = true;
    public double L1SpreadTighteningRatio { get; set; } = 0.80;

    // L1 Size Surge Bonus
    public bool UseL1SizeSurgeBonus { get; set; } = true;
    public double L1SizeSurgeMultiplier { get; set; } = 2.0;

    // L2 Delta OFI Bonus
    public bool UseL2DeltaOfiBonus { get; set; } = true;
    public double L2DeltaOfiMinShift { get; set; } = 0.15;

    // L2 Split Imbalance (Deep book) Bonus
    public bool UseL2SplitImbalance { get; set; } = true;
    public double L2DeepImbalanceMinForLong { get; set; } = 1.2;
    public double L2DeepImbalanceMaxForShort { get; set; } = 0.8;

    public bool UseCandidateScoring { get; set; } = false;
    public bool SqueezeRecoveryLongEnabled { get; set; } = false;
    public int SqueezeMinBars { get; set; } = 4;
    public int SqueezeRecoveryMinConfirm { get; set; } = 4;
}

public sealed class StrategyV15 : BacktestStrategyBase
{
    private readonly V15Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV15(V15Config? cfg = null)
    {
        _cfg = cfg ?? new V15Config();
        if (!_cfg.AllowLong && !_cfg.AllowShort)
            throw new InvalidOperationException("V15: at least one direction must be enabled.");
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
    //  SIGNAL GENERATION
    // =====================================================================
    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var signals = new List<BacktestSignal>();
        var daySignalCounts = new Dictionary<DateOnly, int>();
        int lastAcceptedBar = -10_000;
        int squeezeCount = 0;

        // HOD tracking state (per trading day)
        DateOnly currentDay = default;
        double dayHigh = 0;
        int dayHighIdx = 0;

        for (int i = 30; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0) continue;

            double price = row.Bar.Close;
            if (price < _cfg.MinPrice || price > _cfg.MaxPrice) continue;
            if (double.IsNaN(row.Rsi14) || double.IsNaN(row.Ema9) || double.IsNaN(row.Vwap))
                continue;
            if (!double.IsNaN(row.SpreadZ) && row.SpreadZ > _cfg.SpreadZMax) continue;

            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes) continue;
            if (!BacktestHelpers.InEntryWindow(minuteEt, _cfg.EntryWindows)) continue;

            // Update HOD tracking
            var dayEt = TradingTime.GetDateEt(row.Bar.Timestamp);
            if (dayEt != currentDay)
            {
                currentDay = dayEt;
                dayHigh = row.Bar.High;
                dayHighIdx = i;
            }
            else if (row.Bar.High > dayHigh)
            {
                dayHigh = row.Bar.High;
                dayHighIdx = i;
            }

            // Cooldown & daily cap
            if (i - lastAcceptedBar < _cfg.CooldownBars) continue;
            int countForDay = daySignalCounts.GetValueOrDefault(dayEt);
            if (countForDay >= _cfg.MaxSignalsPerDay) continue;

            if (IsMarketRegimeBlocked(row)) continue;

            bool inSqueeze = !double.IsNaN(row.BbUpper) && !double.IsNaN(row.KcUpper)
                && row.BbUpper < row.KcUpper && row.BbLower > row.KcLower;
            bool squeezeReleased = !inSqueeze && squeezeCount >= _cfg.SqueezeMinBars;
            squeezeCount = inSqueeze ? squeezeCount + 1 : 0;

            var candidates = new List<V15SignalCandidate>(6);

            // ---- LONG: VWAP Reversion (V14 proven) ----
            if (_cfg.VwapRevLongEnabled && _cfg.AllowLong)
            {
                var candidate = TryVwapReversionLong(triggerBars, i, row, atr);
                if (candidate is not null)
                    candidates.Add(candidate);
            }

            if (_cfg.SqueezeRecoveryLongEnabled && _cfg.AllowLong && squeezeReleased)
            {
                var candidate = TrySqueezeRecoveryLong(triggerBars, i, row, atr);
                if (candidate is not null)
                    candidates.Add(candidate);
            }

            // ---- SHORTS: only after earliest-minute gate ----
            if (_cfg.AllowShort && minuteEt >= _cfg.ShortEarliestMinuteEt)
            {
                if (_cfg.ParabolicEnabled)
                {
                    var candidate = TryParabolicExhaustion(triggerBars, i, row, atr);
                    if (candidate is not null)
                        candidates.Add(candidate);
                }

                if (_cfg.HodReversalEnabled)
                {
                    var candidate = TryHodReversal(triggerBars, i, row, atr, dayHigh, dayHighIdx);
                    if (candidate is not null)
                        candidates.Add(candidate);
                }

                if (_cfg.BreakdownEnabled)
                {
                    var candidate = TryBreakdownContinuation(triggerBars, i, row, atr);
                    if (candidate is not null)
                        candidates.Add(candidate);
                }

                if (_cfg.DivergenceEnabled)
                {
                    var candidate = TryBearishDivergence(triggerBars, i, row, atr);
                    if (candidate is not null)
                        candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0) continue;

            var selected = _cfg.UseCandidateScoring
                ? candidates
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Priority)
                    .First()
                : candidates
                    .OrderBy(candidate => candidate.Priority)
                    .First();

            signals.Add(selected.Signal);
            lastAcceptedBar = i;
            daySignalCounts[dayEt] = countForDay + 1;
        }

        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    => ExitEngine.SimulateTrade(signal, triggerBars, ApplySelfLearningExitOverrides(_exitCfg, signal.SubStrategy));
    // =====================================================================
    //  LONG L1: VWAP Reversion (V14's proven mean-reversion long)
    //  Price far below VWAP + oversold confluence + reversal candle
    // =====================================================================
    private V15SignalCandidate? TryVwapReversionLong(EnrichedBar[] bars, int i, EnrichedBar row, double atr)
    {
        if (double.IsNaN(row.Vwap) || row.Vwap <= 0) return null;

        double vwapDist = row.Bar.Close - row.Vwap;
        double vwapDistAtr = atr > 0 ? vwapDist / atr : 0.0;

        // Price must be far BELOW VWAP
        if (vwapDistAtr > -_cfg.VwapStretchAtr) return null;

        // ADX must be low enough for mean-reversion
        double adx = double.IsNaN(row.Adx) ? 20.0 : row.Adx;
        if (adx > _cfg.AdxMaxForMeanReversion) return null;

        // Volume spike gate
        double rvol = double.IsNaN(row.Rvol) ? 1.0 : row.Rvol;
        if (_cfg.RequireVolumeSpikeForLong && rvol < _cfg.LongVolumeSpikeRvol) return null;

        // Confluence scoring (identical to V14)
        int score = ScoreLongConfluence(row);
        double absDistAtr = Math.Abs(vwapDistAtr);
        if (absDistAtr >= _cfg.VwapStretchExtremeAtr) score++; // extreme stretch bonus

        if (score < _cfg.VwapRevLongMinConfluence) return null;

        // Require reversal candle (bullish or hammer)
        double body = row.Bar.Close - row.Bar.Open;
        double range = row.Bar.High - row.Bar.Low;
        double lowerWick = Math.Min(row.Bar.Open, row.Bar.Close) - row.Bar.Low;
        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        bool isBullish = body > 0;
        bool isHammer = range > 0
            && lowerWick > Math.Abs(body) * 1.5
            && upperWick < Math.Abs(body) * 0.5;

        if (!isBullish && !isHammer) return null;

        if (!PassLongL1L2Gates(row)) return null;

        return MakeCandidate(bars, i, TradeSide.Long, atr, "VL_FAST20", score, priority: 0);
    }

    // V14-identical long confluence scoring + L1/L2 bonuses
    private int ScoreLongConfluence(EnrichedBar row)
    {
        int score = 0;
        if (row.Rsi14 < 35) score++;
        if (row.Rsi14 < 25) score++;

        double bbPctB = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
        if (bbPctB < 0.15) score++;

        double stochK = double.IsNaN(row.StochK) ? 50.0 : row.StochK;
        double stochD = double.IsNaN(row.StochD) ? 50.0 : row.StochD;
        if (stochK < _cfg.StochOversold) score++;
        if (stochK < _cfg.StochOversold && stochK > stochD) score++;

        double mfi = double.IsNaN(row.Mfi14) ? 50.0 : row.Mfi14;
        if (mfi < _cfg.MfiOversold) score++;

        double willR = double.IsNaN(row.WillR14) ? -50.0 : row.WillR14;
        if (willR < _cfg.WillRExtremeLong) score++;

        double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;
        if (ofiSig > 0.1) score++;

        double rvol = double.IsNaN(row.Rvol) ? 1.0 : row.Rvol;
        if (rvol >= _cfg.VolumeSpikeRvol) score += (int)_cfg.VolumeSpikeBonus;

        double body = row.Bar.Close - row.Bar.Open;
        double range = row.Bar.High - row.Bar.Low;
        double lowerWick = Math.Min(row.Bar.Open, row.Bar.Close) - row.Bar.Low;
        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        bool isHammer = range > 0
            && lowerWick > Math.Abs(body) * 1.5
            && upperWick < Math.Abs(body) * 0.5;
        if (isHammer) score++;
        if (body > 0) score++;

        double dpo = double.IsNaN(row.Dpo20) ? 0.0 : row.Dpo20;
        double adxVal = double.IsNaN(row.Adx) ? 20.0 : row.Adx;
        if (dpo < -0.5 * adxVal / 25.0) score++;

        // â”€â”€ L1 Bonuses â”€â”€
        score += ScoreLongL1L2Bonuses(row);

        return score;
    }

    private V15SignalCandidate? TrySqueezeRecoveryLong(EnrichedBar[] bars, int i, EnrichedBar row, double atr)
    {
        if (double.IsNaN(row.KcMid) || row.Bar.Close <= row.KcMid)
            return null;

        var confirm = 0;
        if (row.Bar.Close > row.Bar.Open) confirm++;
        if (!double.IsNaN(row.Rsi14) && row.Rsi14 <= 48.0) confirm++;
        if (!double.IsNaN(row.StochK) && !double.IsNaN(row.StochD) && row.StochK > row.StochD) confirm++;
        if (!double.IsNaN(row.OfiSignal) && row.OfiSignal > 0) confirm++;
        if (!double.IsNaN(row.Rvol) && row.Rvol >= 1.2) confirm++;
        if (!double.IsNaN(row.Vwap) && row.Bar.Close >= row.Vwap) confirm++;

        confirm += ScoreLongL1L2Bonuses(row);
        if (confirm < _cfg.SqueezeRecoveryMinConfirm)
            return null;

        if (!PassLongL1L2Gates(row))
            return null;

        return MakeCandidate(bars, i, TradeSide.Long, atr, "V11_SQZ_REC", confirm, priority: 1);
    }

    // =====================================================================
    //  SHORT S1: Parabolic Exhaustion
    //  Stock rose > X% from N-bar low + volume climax + reversal candle
    //  Targets pump-and-dump / unsustainable parabolic moves
    // =====================================================================
    private V15SignalCandidate? TryParabolicExhaustion(EnrichedBar[] bars, int i, EnrichedBar row, double atr)
    {
        if (i < _cfg.ParabolicLookbackBars) return null;

        // Find lowest low in lookback window
        double lowestLow = double.MaxValue;
        for (int j = i - _cfg.ParabolicLookbackBars; j < i; j++)
            lowestLow = Math.Min(lowestLow, bars[j].Bar.Low);

        if (lowestLow <= 0) return null;

        // How much has price risen from that low?
        double pctFromLow = (row.Bar.High - lowestLow) / lowestLow;
        if (pctFromLow < _cfg.ParabolicPctThreshold) return null;

        // RSI must be elevated (confirms genuine overbought, not slow grind)
        if (row.Rsi14 < _cfg.ParabolicRsiMin) return null;

        // Volume must be elevated during this move
        double rvol = double.IsNaN(row.Rvol) ? 1.0 : row.Rvol;
        if (rvol < _cfg.ParabolicRvolMin) return null;

        // Current bar must show reversal: bearish candle or shooting star
        double body = row.Bar.Close - row.Bar.Open;
        double range = row.Bar.High - row.Bar.Low;
        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        bool isBearish = body < 0;
        bool isStar = range > 0 && upperWick > Math.Abs(body) * 1.5;
        if (!isBearish && !isStar) return null;

        // Targeted confirmation (overbought exhaustion signals)
        int confirm = 0;
        double stochK = double.IsNaN(row.StochK) ? 50.0 : row.StochK;
        if (stochK > 80) confirm++;                              // Stochastic overbought
        double bbPctB = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
        if (bbPctB > 0.90) confirm++;                            // At/above upper Bollinger
        double mfi = double.IsNaN(row.Mfi14) ? 50.0 : row.Mfi14;
        if (mfi > 75) confirm++;                                 // MFI overbought
        double willR = double.IsNaN(row.WillR14) ? -50.0 : row.WillR14;
        if (willR > -20) confirm++;                              // WilliamsR overbought
        if (isStar) confirm++;                                   // Shooting star = rejection wick
        double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;
        if (ofiSig < -0.1) confirm++;                            // Selling pressure emerging
        double dcPct = double.IsNaN(row.DcPct) ? 0.5 : row.DcPct;
        if (dcPct > 0.90) confirm++;                             // Near Donchian high
        // Volume climax on the prior bar (buying exhaustion)
        if (i >= 1)
        {
            double prevRvol = double.IsNaN(bars[i - 1].Rvol) ? 1.0 : bars[i - 1].Rvol;
            if (prevRvol >= _cfg.VolumeClimaxMultiplier) confirm++;
        }

        if (confirm < _cfg.ParabolicMinConfirm) return null;

        // â”€â”€ L1/L2 Short Bonuses â”€â”€
        confirm += ScoreShortL1L2Bonuses(row);
        // Re-check with possibly higher confirm (bonuses only help, never block)

        // â”€â”€ L1/L2 Short Gates â”€â”€
        if (!PassShortL1L2Gates(row)) return null;

        return MakeCandidate(bars, i, TradeSide.Short, atr, "SPARA_10", confirm, priority: 2);
    }

    // =====================================================================
    //  SHORT S2: HOD Rejection
    //  Stock made High-Of-Day, then dropped > 1 ATR from it
    //  Confirms the HOD is set; short the continuation lower
    // =====================================================================
    private V15SignalCandidate? TryHodReversal(EnrichedBar[] bars, int i, EnrichedBar row, double atr,
        double dayHigh, int dayHighIdx)
    {
        if (atr <= 0 || dayHigh <= 0) return null;

        // HOD must be at least N bars ago (not still making new highs)
        if (i - dayHighIdx < _cfg.HodMinBarsAfterHod) return null;

        // Price must have dropped > N ATR from HOD
        double dropAtr = (dayHigh - row.Bar.Close) / atr;
        if (dropAtr < _cfg.HodDropAtr) return null;

        // Current bar must be bearish (confirming weakness)
        double body = row.Bar.Close - row.Bar.Open;
        if (body >= 0) return null;

        // Targeted confirmation (trend turning bearish)
        int confirm = 0;

        if (!double.IsNaN(row.Vwap) && row.Bar.Close < row.Vwap) confirm++;   // Below VWAP
        if (!double.IsNaN(row.Ema21) && row.Ema9 < row.Ema21) confirm++;      // EMA bearish
        if (row.StDirection <= -1) confirm++;                                    // Supertrend bearish

        double macdH = double.IsNaN(row.MacdHist) ? 0.0 : row.MacdHist;
        if (macdH < 0) confirm++;                                               // MACD negative

        double plusDi = double.IsNaN(row.PlusDi) ? 15.0 : row.PlusDi;
        double minusDi = double.IsNaN(row.MinusDi) ? 15.0 : row.MinusDi;
        if (minusDi > plusDi) confirm++;                                        // Bears in control

        if (row.Rsi14 < 50) confirm++;                                          // Bearish momentum

        double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;
        if (ofiSig < -0.1) confirm++;                                           // Selling pressure

        if (confirm < _cfg.HodMinConfirm) return null;

        confirm += ScoreShortL1L2Bonuses(row);
        if (!PassShortL1L2Gates(row)) return null;

        return MakeCandidate(bars, i, TradeSide.Short, atr, "SHOD_FAIL", confirm, priority: 3);
    }

    // =====================================================================
    //  SHORT S3: Breakdown Continuation
    //  Previous bar above key level, current bar closes below it
    //  Confirmed breakdown = short with momentum
    // =====================================================================
    private V15SignalCandidate? TryBreakdownContinuation(EnrichedBar[] bars, int i, EnrichedBar row, double atr)
    {
        if (i < 1) return null;
        var prev = bars[i - 1];

        // Detect breakdown: previous bar closed above level, current bar closes below
        bool vwapBreak = !double.IsNaN(prev.Vwap) && prev.Bar.Close > prev.Vwap
                      && !double.IsNaN(row.Vwap) && row.Bar.Close < row.Vwap;
        bool ema21Break = !double.IsNaN(prev.Ema21) && prev.Bar.Close > prev.Ema21
                       && !double.IsNaN(row.Ema21) && row.Bar.Close < row.Ema21;
        bool bbMidBreak = !double.IsNaN(prev.BbMid) && prev.Bar.Close > prev.BbMid
                       && !double.IsNaN(row.BbMid) && row.Bar.Close < row.BbMid;

        if (!vwapBreak && !ema21Break && !bbMidBreak) return null;

        // Current bar must be bearish (confirms breakdown, not just a wick)
        double body = row.Bar.Close - row.Bar.Open;
        if (body >= 0) return null;

        // Targeted confirmation
        int confirm = 0;
        if (vwapBreak) confirm++;
        if (ema21Break) confirm++;
        if (bbMidBreak) confirm++;

        double rvol = double.IsNaN(row.Rvol) ? 1.0 : row.Rvol;
        if (rvol >= _cfg.VolumeSpikeRvol) confirm++;                           // Volume on breakdown

        double adx = double.IsNaN(row.Adx) ? 20.0 : row.Adx;
        if (adx > 15) confirm++;                                                // Trend present

        double macdH = double.IsNaN(row.MacdHist) ? 0.0 : row.MacdHist;
        if (macdH < 0) confirm++;                                               // MACD bearish

        if (prev.Bar.Close > prev.Bar.Open) confirm++;                          // Prior rally failed

        if (confirm < _cfg.BreakdownMinConfirm) return null;

        confirm += ScoreShortL1L2Bonuses(row);
        if (_cfg.UseContinuationScoreOnBreakdown)
        {
            var continuationScore = ComputeBreakdownContinuationScore(row, prev);
            if (continuationScore < _cfg.BreakdownContinuationScoreFloor)
                return null;

            confirm += continuationScore;
        }

        if (!PassShortL1L2Gates(row)) return null;

        return MakeCandidate(bars, i, TradeSide.Short, atr, "SB_CONT20", confirm, priority: 4);
    }

    // =====================================================================
    //  SHORT S4: Bearish Divergence
    //  Price makes higher-high vs lookback, but RSI or MACD makes lower reading
    //  Momentum dying while price pushes = leading reversal signal
    // =====================================================================
    private V15SignalCandidate? TryBearishDivergence(EnrichedBar[] bars, int i, EnrichedBar row, double atr)
    {
        int lookback = _cfg.DivergenceLookback;
        if (i < lookback + 1) return null;

        // Find bar with highest high in lookback window (excluding last bar)
        int refIdx = -1;
        double refHigh = double.MinValue;
        for (int j = i - lookback; j < i - 1; j++)
        {
            if (j < 0) continue;
            if (bars[j].Bar.High > refHigh)
            {
                refHigh = bars[j].Bar.High;
                refIdx = j;
            }
        }
        if (refIdx < 0) return null;

        // Current bar's high must be at or above the reference high (higher high)
        if (row.Bar.High < refHigh * 0.998) return null;

        // RSI divergence: current RSI meaningfully lower than at reference bar
        bool rsiDiv = !double.IsNaN(bars[refIdx].Rsi14) && !double.IsNaN(row.Rsi14)
            && row.Rsi14 < bars[refIdx].Rsi14 - _cfg.DivergenceRsiGap;

        // MACD histogram divergence: current hist lower than at reference bar
        double refMacdH = double.IsNaN(bars[refIdx].MacdHist) ? 0.0 : bars[refIdx].MacdHist;
        double curMacdH = double.IsNaN(row.MacdHist) ? 0.0 : row.MacdHist;
        bool macdDiv = curMacdH < refMacdH - 0.05;

        // Need at least one form of divergence
        if (!rsiDiv && !macdDiv) return null;

        // Current bar must be bearish (confirming divergence playing out)
        double body = row.Bar.Close - row.Bar.Open;
        if (body >= 0) return null;

        // Targeted confirmation
        int confirm = 0;
        if (rsiDiv) confirm++;
        if (macdDiv) confirm++;

        // Shooting star (upper wick rejection at the new high)
        double range = row.Bar.High - row.Bar.Low;
        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        bool isStar = range > 0 && upperWick > Math.Abs(body) * 1.5;
        if (isStar) confirm++;

        // Stochastic bearish cross from overbought
        double stochK = double.IsNaN(row.StochK) ? 50.0 : row.StochK;
        double stochD = double.IsNaN(row.StochD) ? 50.0 : row.StochD;
        if (stochK > 70 && stochK < stochD) confirm++;

        // Volume declining on the new high (less buying conviction)
        if (bars[refIdx].Bar.Volume > 0
            && row.Bar.Volume < bars[refIdx].Bar.Volume * 0.8) confirm++;

        // Price near upper Bollinger Band (extended)
        double bbPctB = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
        if (bbPctB > 0.80) confirm++;

        if (confirm < _cfg.DivergenceMinConfirm) return null;

        confirm += ScoreShortL1L2Bonuses(row);
        if (!PassShortL1L2Gates(row)) return null;

        return MakeCandidate(bars, i, TradeSide.Short, atr, "SDIV_10", confirm, priority: 5);
    }

    private int ComputeBreakdownContinuationScore(EnrichedBar row, EnrichedBar prev)
    {
        var score = 0;
        if (!double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21) && row.Ema9 < row.Ema21)
            score++;
        if (!double.IsNaN(row.Vwap) && row.Bar.Close <= row.Vwap)
            score++;
        if (!double.IsNaN(row.OfiSignal) && row.OfiSignal < 0)
            score++;
        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.VolumeSpikeRvol)
            score++;
        if (prev.Bar.Close > prev.Bar.Open)
            score++;

        return score;
    }

    private bool PassLongL1L2Gates(EnrichedBar row)
    {
        if (!_cfg.UseL1L2 || double.IsNaN(row.ImbalanceRatio)) return true;

        if (_cfg.UseL2DirectionalGate && row.ImbalanceRatio < _cfg.L2ImbalanceMinForLong)
            return false;

        if (_cfg.UseL2DwmpGate && !double.IsNaN(row.DepthWeightedMid))
        {
            var mid = (row.BidPrice + row.AskPrice) / 2.0;
            if (!double.IsNaN(mid) && mid > 0 && row.DepthWeightedMid < mid)
                return false;
        }

        if (_cfg.UseL1SizeRatioGate && row.AskSize > 0 && !double.IsNaN(row.BidSize))
        {
            if (row.BidSize / row.AskSize < _cfg.L1SizeRatioMinForLong)
                return false;
        }

        if (_cfg.UseL1LastVsMidGate && !double.IsNaN(row.LastPrice) && !double.IsNaN(row.BidPrice))
        {
            var midPrice = (row.BidPrice + row.AskPrice) / 2.0;
            if (midPrice > 0 && row.LastPrice < midPrice)
                return false;
        }

        return true;
    }

    private int ScoreLongL1L2Bonuses(EnrichedBar row)
    {
        var bonus = 0;
        if (_cfg.UseL1L2 && !double.IsNaN(row.SpreadPct))
        {
            if (_cfg.UseL1SpreadTighteningBonus && row.SpreadPct < _cfg.L1SpreadTighteningRatio)
                bonus++;

            if (_cfg.UseL1SizeSurgeBonus && row.BidSize > 0 && row.AskSize > 0
                && row.BidSize / row.AskSize >= _cfg.L1SizeSurgeMultiplier)
            {
                bonus++;
            }
        }

        if (_cfg.UseL1L2 && !double.IsNaN(row.ImbalanceRatio))
        {
            if (_cfg.UseL2DeltaOfiBonus && !double.IsNaN(row.OfiSignal)
                && row.OfiSignal > _cfg.L2DeltaOfiMinShift)
            {
                bonus++;
            }

            if (_cfg.UseL2SplitImbalance && !double.IsNaN(row.DeepImbalanceRatio)
                && row.DeepImbalanceRatio > _cfg.L2DeepImbalanceMinForLong)
            {
                bonus++;
            }
        }

        return bonus;
    }

    // =====================================================================
    //  L1/L2 SHORT GATES & BONUSES (shared by all short sub-strategies)
    //  Mirrors V3LiveSignalEngine short-side logic
    // =====================================================================

    /// <summary>
    /// L1/L2 directional gates for shorts. Returns false if gate blocks entry.
    /// Only applies when L1/L2 data is present and UseL1L2 is enabled.
    /// </summary>
    private bool PassShortL1L2Gates(EnrichedBar row)
    {
        if (!_cfg.UseL1L2 || double.IsNaN(row.ImbalanceRatio)) return true;

        // L2 directional gate: ask depth must dominate for shorts
        if (_cfg.UseL2DirectionalGate && row.ImbalanceRatio > _cfg.L2ImbalanceMaxForShort)
            return false;

        // L2 DWMP gate: weighted mid should lean sell-side
        if (_cfg.UseL2DwmpGate && !double.IsNaN(row.DepthWeightedMid))
        {
            var mid = (row.BidPrice + row.AskPrice) / 2.0;
            if (!double.IsNaN(mid) && mid > 0 && row.DepthWeightedMid > mid)
                return false;
        }

        // L1 size ratio gate: ask side must be heavier for shorts
        if (_cfg.UseL1SizeRatioGate && row.BidSize > 0 && !double.IsNaN(row.AskSize))
        {
            if (row.AskSize / row.BidSize < 1.0 / _cfg.L1SizeRatioMaxForShort)
                return false;
        }

        // L1 last vs mid gate: last price should be at/below mid for shorts
        if (_cfg.UseL1LastVsMidGate && !double.IsNaN(row.LastPrice) && !double.IsNaN(row.BidPrice))
        {
            var midPrice = (row.BidPrice + row.AskPrice) / 2.0;
            if (midPrice > 0 && row.LastPrice > midPrice)
                return false;
        }

        return true;
    }

    /// <summary>
    /// L1/L2 bonuses for short confluence scoring. Returns extra points (0-4).
    /// </summary>
    private int ScoreShortL1L2Bonuses(EnrichedBar row)
    {
        if (!_cfg.UseL1L2 || double.IsNaN(row.SpreadPct)) return 0;

        int bonus = 0;

        // Spread tightening bonus
        if (_cfg.UseL1SpreadTighteningBonus && row.SpreadPct < _cfg.L1SpreadTighteningRatio)
            bonus++;

        // Ask size surge bonus (selling pressure)
        if (_cfg.UseL1SizeSurgeBonus && row.AskSize > 0 && row.BidSize > 0
            && row.AskSize / row.BidSize >= _cfg.L1SizeSurgeMultiplier)
            bonus++;

        // OFI bearish shift bonus
        if (_cfg.UseL2DeltaOfiBonus && !double.IsNaN(row.OfiSignal)
            && row.OfiSignal < -_cfg.L2DeltaOfiMinShift)
            bonus++;

        // Deep book bearish conviction bonus
        if (_cfg.UseL2SplitImbalance && !double.IsNaN(row.DeepImbalanceRatio)
            && row.DeepImbalanceRatio < _cfg.L2DeepImbalanceMaxForShort)
            bonus++;

        return bonus;
    }

    // =====================================================================
    //  SIGNAL BUILDER
    // =====================================================================
    private BacktestSignal? MakeSignal(EnrichedBar[] bars, int i, TradeSide side, double atr,
        string subStrategy, int entryScore = 0)
    {
        if (IsSelfLearningBlocked($"V15_{subStrategy}")) return null;

        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTs = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry && i + 1 < bars.Length)
        {
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTs = bars[entryIndex].Bar.Timestamp;
        }

        double stopDist = Math.Max(atr * _cfg.StopAtrMultiplier, _cfg.MinStopCents / 100.0);
        stopDist = ApplySelfLearningStopMultiplier(stopDist);
        double stopPrice = side == TradeSide.Long
            ? entryPrice - stopDist
            : entryPrice + stopDist;

        double riskPerShare = stopDist;
        if (riskPerShare < _cfg.MinRiskPerShare) riskPerShare = _cfg.MinRiskPerShare;

        int posSize = BacktestHelpers.ComputePositionSize(
            entryPrice, riskPerShare,
            _cfg.RiskPerTradeDollars, _cfg.AccountSize,
            _cfg.MaxPositionNotionalPctOfAccount, _cfg.MaxShares);
        posSize = ApplySelfLearningPositionSize(posSize, $"V15_{subStrategy}");

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
            SubStrategy: $"V15_{subStrategy}",
            EntryScore: entryScore);
    }

    private V15SignalCandidate? MakeCandidate(
        EnrichedBar[] bars,
        int i,
        TradeSide side,
        double atr,
        string subStrategy,
        int score,
        int priority)
    {
        var signal = MakeSignal(bars, i, side, atr, subStrategy, score);
        return signal is null ? null : new V15SignalCandidate(signal, score, priority);
    }

    private sealed record V15SignalCandidate(BacktestSignal Signal, int Score, int Priority);
}

