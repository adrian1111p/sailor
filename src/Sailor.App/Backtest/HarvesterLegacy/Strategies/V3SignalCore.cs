using Sailor.App.Backtest.Engine;

namespace Sailor.App.Backtest.Strategies;

internal sealed class V3SignalCoreConfig
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.18;
    public int MaxShares { get; set; } = 6_500;
    public double MinRiskPerShare { get; set; } = 0.01;
    public double MinPrice { get; set; } = 0.3;
    public double MaxPrice { get; set; } = 700.0;
    public bool UseNextBarOpenEntry { get; set; } = true;
    public double VwapStretchAtr { get; set; } = 1.5;
    public bool VwapEnabled { get; set; } = true;
    public double BbEntryPctbLow { get; set; } = 0.05;
    public double BbEntryPctbHigh { get; set; } = 0.95;
    public bool BbEnabled { get; set; } = true;
    public bool SqueezeEnabled { get; set; } = true;
    public int SqueezeBars { get; set; } = 10;
    public double L2LiquidityMin { get; set; } = 25.0;
    public double SpreadZMax { get; set; } = 2.0;
    public double VolAccelMin { get; set; } = -0.3;
    public double RvolMin { get; set; } = 0.5;
    public double RsiOversold { get; set; } = 35.0;
    public double RsiOverbought { get; set; } = 65.0;
    public bool RequireVolumeConfirm { get; set; } = true;
    public double HardStopR { get; set; } = 1.5;
    public double TrailR { get; set; } = 1.0;
    public double GivebackPct { get; set; } = 0.60;
    public double Tp1R { get; set; } = 1.0;
    public double Tp2R { get; set; } = 2.5;
    public double BreakevenR { get; set; } = 0.8;
    public int MaxHoldBars { get; set; } = 90;
    public double SlippageCents { get; set; } = 1.5;
    public double CommissionPerShare { get; set; } = 0.005;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
}

internal sealed class V3SignalCore
{
    private readonly V3SignalCoreConfig _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    internal V3SignalCore(V3SignalCoreConfig? cfg = null)
    {
        _cfg = cfg ?? new V3SignalCoreConfig();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.5,
            UseFixedGivebackUsdCap = true,
            GivebackUsdCap = 30.0,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
        };
    }

    internal IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var signals = new List<BacktestSignal>();
        int squeezeCount = 0;

        for (int i = 7; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0) continue;

            string htfBias = HtfGuardAtTime(row.Bar.Timestamp, bars1h, bars1d);
            double evalPrice = row.Bar.Close;

            if (evalPrice < _cfg.MinPrice || evalPrice > _cfg.MaxPrice) continue;
            if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin) continue;
            if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax) continue;

            if (_cfg.RequireVolumeConfirm)
            {
                if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin) continue;
                if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.VolAccelMin) continue;
            }
            else
            {
                if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin) continue;
                if (!double.IsNaN(row.VolAccel) && row.VolAccel < _cfg.VolAccelMin) continue;
            }

            double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;
            double vwapVal = row.Vwap;
            double bbPctb = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
            double rsiVal = double.IsNaN(row.Rsi14) ? 50.0 : row.Rsi14;
            double stochK = double.IsNaN(row.StochK) ? 50.0 : row.StochK;

            if (!double.IsNaN(row.BbUpper) && !double.IsNaN(row.KcUpper) &&
                row.BbUpper < row.KcUpper && row.BbLower > row.KcLower)
            {
                squeezeCount++;
            }
            else
            {
                bool wasSqueezed = squeezeCount >= _cfg.SqueezeBars;
                squeezeCount = 0;

                if (_cfg.SqueezeEnabled && wasSqueezed && !double.IsNaN(row.KcMid))
                {
                    if (evalPrice > row.KcMid && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                    {
                        var squeezeScore = ComputeSqueezeScore(row, TradeSide.Long, ofiSig, htfBias);
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "SQUEEZE", squeezeScore);
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                    else if (evalPrice < row.KcMid && _cfg.AllowShort && htfBias != "STRONG_BULL")
                    {
                        var squeezeScore = ComputeSqueezeScore(row, TradeSide.Short, ofiSig, htfBias);
                        var sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "SQUEEZE", squeezeScore);
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }
            }

            if (_cfg.VwapEnabled && !double.IsNaN(vwapVal) && vwapVal > 0)
            {
                double distFromVwap = (evalPrice - vwapVal) / atrVal;

                if (distFromVwap < -_cfg.VwapStretchAtr && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                {
                    if (rsiVal < _cfg.RsiOversold && ofiSig > 0)
                    {
                        var vwapScore = ComputeVwapScore(row, TradeSide.Long, distFromVwap, ofiSig);
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "VWAP", vwapScore);
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }

                if (distFromVwap > _cfg.VwapStretchAtr && _cfg.AllowShort && htfBias != "STRONG_BULL")
                {
                    if (rsiVal > _cfg.RsiOverbought && ofiSig < 0)
                    {
                        var vwapScore = ComputeVwapScore(row, TradeSide.Short, distFromVwap, ofiSig);
                        var sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "VWAP", vwapScore);
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }
            }

            if (_cfg.BbEnabled)
            {
                if (bbPctb < _cfg.BbEntryPctbLow && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                {
                    bool confirm = evalPrice > row.Bar.Open
                        || (stochK < 25 && !double.IsNaN(row.StochD) && stochK > row.StochD);
                    if (confirm)
                    {
                        var bbScore = ComputeBbScore(row, TradeSide.Long, ofiSig);
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "BB", bbScore);
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }

                if (bbPctb > _cfg.BbEntryPctbHigh && _cfg.AllowShort && htfBias != "STRONG_BULL")
                {
                    bool confirm = evalPrice < row.Bar.Open
                        || (stochK > 75 && !double.IsNaN(row.StochD) && stochK < row.StochD);
                    if (confirm)
                    {
                        var bbScore = ComputeBbScore(row, TradeSide.Short, ofiSig);
                        var sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "BB", bbScore);
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }
            }
        }

        return signals;
    }

    internal BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private BacktestSignal? MakeSignal(int i, EnrichedBar[] bars, TradeSide side, double atrVal, string subStrategy, int entryScore)
    {
        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTs = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry)
        {
            if (i + 1 >= bars.Length) return null;
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTs = bars[entryIndex].Bar.Timestamp;
        }

        double stopDist = _cfg.HardStopR * atrVal;
        double stopPrice = side == TradeSide.Long ? entryPrice - stopDist : entryPrice + stopDist;
        double riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare <= 0 || riskPerShare < _cfg.MinRiskPerShare) return null;

        int posSize = BacktestHelpers.ComputePositionSize(
            entryPrice,
            riskPerShare,
            _cfg.RiskPerTradeDollars,
            _cfg.AccountSize,
            _cfg.MaxPositionNotionalPctOfAccount,
            _cfg.MaxShares);
        if (posSize <= 0) return null;

        return new BacktestSignal(
            BarIndex: entryIndex,
            Timestamp: entryTs,
            Side: side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: posSize,
            AtrValue: atrVal,
            HtfTrend: HtfBias.Neutral,
            MtfMomentum: "N/A",
            SubStrategy: subStrategy,
            EntryScore: entryScore);
    }

    private int ComputeSqueezeScore(EnrichedBar row, TradeSide side, double ofiSig, string htfBias)
    {
        var score = 1;
        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin + 0.25)
            score++;
        if (!double.IsNaN(row.VolAccel) && row.VolAccel >= 0)
            score++;
        if (side == TradeSide.Long ? ofiSig > 0 : ofiSig < 0)
            score++;
        if (side == TradeSide.Long
            ? htfBias is "BULL" or "STRONG_BULL"
            : htfBias is "BEAR" or "STRONG_BEAR")
        {
            score++;
        }

        return score;
    }

    private int ComputeVwapScore(EnrichedBar row, TradeSide side, double distFromVwap, double ofiSig)
    {
        var score = 1;
        if (Math.Abs(distFromVwap) >= _cfg.VwapStretchAtr + 0.30)
            score++;
        if (side == TradeSide.Long ? row.Rsi14 <= _cfg.RsiOversold - 5.0 : row.Rsi14 >= _cfg.RsiOverbought + 5.0)
            score++;
        if (side == TradeSide.Long ? ofiSig > 0.10 : ofiSig < -0.10)
            score++;
        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin + 0.35)
            score++;
        if (side == TradeSide.Long ? row.Bar.Close > row.Bar.Open : row.Bar.Close < row.Bar.Open)
            score++;

        return score;
    }

    private int ComputeBbScore(EnrichedBar row, TradeSide side, double ofiSig)
    {
        var score = 1;
        if (side == TradeSide.Long ? row.BbPctB <= _cfg.BbEntryPctbLow + 0.05 : row.BbPctB >= _cfg.BbEntryPctbHigh - 0.05)
            score++;
        if (side == TradeSide.Long
            ? !double.IsNaN(row.StochK) && !double.IsNaN(row.StochD) && row.StochK > row.StochD
            : !double.IsNaN(row.StochK) && !double.IsNaN(row.StochD) && row.StochK < row.StochD)
        {
            score++;
        }
        if (side == TradeSide.Long ? row.Bar.Close > row.Bar.Open : row.Bar.Close < row.Bar.Open)
            score++;
        if (side == TradeSide.Long ? ofiSig > 0 : ofiSig < 0)
            score++;
        if (!double.IsNaN(row.Rvol) && row.Rvol >= _cfg.RvolMin + 0.20)
            score++;

        return score;
    }

    private static string HtfGuardAtTime(DateTime ts, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        int scoreSum = 0;
        int scoreCount = 0;
        ScoreHtf(bars1h, ts, ref scoreSum, ref scoreCount);
        ScoreHtf(bars1d, ts, ref scoreSum, ref scoreCount);
        if (scoreCount == 0) return "NEUTRAL";
        double avg = (double)scoreSum / scoreCount;
        if (avg >= 2) return "STRONG_BULL";
        if (avg <= -2) return "STRONG_BEAR";
        return "NEUTRAL";

        static void ScoreHtf(EnrichedBar[]? bars, DateTime ts, ref int scoreSum, ref int scoreCount)
        {
            if (bars == null || bars.Length < 2) return;
            int idx = BacktestHelpers.FindBarAtOrBefore(bars, ts);
            if (idx < 1) return;
            var last = bars[idx];
            var prev = bars[idx - 1];
            int slope = last.Ema21 > prev.Ema21 ? 1 : -1;
            if (!double.IsNaN(last.Rsi14))
            {
                if (last.Rsi14 > 70) slope++;
                else if (last.Rsi14 < 30) slope--;
            }
            scoreSum += slope;
            scoreCount++;
        }
    }
}
