using Sailor.App.Backtest;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct.AngleEma;

public abstract class AngleEmaConductStrategyBase : ISailorConductPositionStrategy
{
    private readonly AngleEmaConductSettings _settings;
    private AngleConductSide _side = AngleConductSide.Flat;
    private DateTimeOffset? _lastEntrySignalCandleStart;
    private DateTimeOffset? _lastExitSignalCandleStart;

    protected AngleEmaConductStrategyBase(AngleEmaConductSettings settings)
    {
        _settings = settings;
    }

    public string ProfileName => _settings.ProfileName;

    public string StrategyName => _settings.StrategyName;

    public string VariantName => _settings.VariantName;

    public bool AllowsShortEntries => _settings.AllowShort;

    public BacktestSignal EvaluateEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile)
    {
        return Evaluate(
            currentBar,
            previousBar,
            indicators,
            recentBars,
            profile,
            hasOpenPosition: false,
            positionSide: 0);
    }

    public BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile,
        bool hasOpenPosition,
        int positionSide)
    {
        if (!hasOpenPosition && _side != AngleConductSide.Flat)
        {
            _side = AngleConductSide.Flat;
        }

        string prefix = $"{StrategyName}/{VariantName}";
        AngleEmaState state = CalculateState(recentBars, currentBar.Symbol);

        if (!state.IsReady || state.SignalCandle is null)
        {
            return BacktestSignal.Hold(
                $"{prefix}: waiting for completed {_settings.CandleMinutes}m EMA9/ATR angle readiness.");
        }

        AngleConductCandle candle = state.SignalCandle;
        decimal ema9 = state.Ema9!.Value;
        decimal angle = state.AngleDegrees;
        decimal atr = state.Atr!.Value;
        decimal volumeRatio = CalculateVolumeRatio(currentBar, indicators);

        if (!hasOpenPosition)
        {
            if (_lastEntrySignalCandleStart == candle.StartTime)
            {
                return BacktestSignal.Hold(
                    $"{prefix}: completed {_settings.CandleMinutes}m candle {candle.StartTime:yyyy-MM-dd HH:mm} already emitted an entry signal.");
            }

            if (!PassesBasicFilters(currentBar, indicators, volumeRatio, prefix, out string filterRejectReason))
            {
                return BacktestSignal.Hold(filterRejectReason);
            }

            if (profile.SideMode.AllowsLong() && angle >= _settings.AngleThresholdDegrees && PassesLongReEntry(candle, state, ema9))
            {
                _side = AngleConductSide.Long;
                _lastEntrySignalCandleStart = candle.StartTime;
                return BacktestSignal.Buy(
                    $"{prefix}: LONG completed {_settings.CandleMinutes}m EMA9 angle setup, angle {angle:F2}° >= {_settings.AngleThresholdDegrees:F2}°, " +
                    $"EMA9={ema9:F2}, ATR={atr:F2}, candle O/H/L/C={candle.Open:F2}/{candle.High:F2}/{candle.Low:F2}/{candle.Close:F2}, VolRatio={volumeRatio:F2}.");
            }

            if (_settings.AllowShort && profile.SideMode.AllowsShort() && angle <= -_settings.AngleThresholdDegrees && PassesShortReEntry(candle, state, ema9))
            {
                _side = AngleConductSide.Short;
                _lastEntrySignalCandleStart = candle.StartTime;
                return BacktestSignal.Sell(
                    $"{prefix}: SHORT completed {_settings.CandleMinutes}m EMA9 angle setup, angle {angle:F2}° <= -{_settings.AngleThresholdDegrees:F2}°, " +
                    $"EMA9={ema9:F2}, ATR={atr:F2}, candle O/H/L/C={candle.Open:F2}/{candle.High:F2}/{candle.Low:F2}/{candle.Close:F2}, VolRatio={volumeRatio:F2}.");
            }

            return BacktestSignal.Hold(
                $"{prefix}: no completed-candle angle entry, {_settings.CandleMinutes}m EMA9 angle={angle:F2}°, EMA9={ema9:F2}, ATR={atr:F2}, candleClose={candle.Close:F2}.");
        }

        // Harvester V21/V23 do not evaluate the same higher-timeframe candle for exit immediately after entry.
        // The original code starts exit resolution at source candle + 1 and then fills at the next 1m open.
        if (_lastEntrySignalCandleStart == candle.StartTime)
        {
            return BacktestSignal.Hold(
                $"{prefix}: hold through entry signal candle {_settings.CandleMinutes}m {candle.StartTime:yyyy-MM-dd HH:mm}; waiting for next completed candle.");
        }

        if (_lastExitSignalCandleStart == candle.StartTime)
        {
            return BacktestSignal.Hold(
                $"{prefix}: completed {_settings.CandleMinutes}m candle {candle.StartTime:yyyy-MM-dd HH:mm} already emitted an exit signal.");
        }

        if (_side == AngleConductSide.Long)
        {
            if (ShouldFlattenLong(candle, state, ema9, angle, out string exitReason))
            {
                _side = AngleConductSide.Flat;
                _lastExitSignalCandleStart = candle.StartTime;
                return BacktestSignal.Sell($"{prefix}: FLATTEN LONG, {exitReason}");
            }

            return BacktestSignal.Hold(
                $"{prefix}: hold long, completed {_settings.CandleMinutes}m EMA9 angle={angle:F2}°, EMA9={ema9:F2}, ATR={atr:F2}, close={candle.Close:F2}.");
        }

        if (_side == AngleConductSide.Short)
        {
            if (ShouldFlattenShort(candle, state, ema9, angle, out string exitReason))
            {
                _side = AngleConductSide.Flat;
                _lastExitSignalCandleStart = candle.StartTime;
                return BacktestSignal.Buy($"{prefix}: FLATTEN SHORT, {exitReason}");
            }

            return BacktestSignal.Hold(
                $"{prefix}: hold short, completed {_settings.CandleMinutes}m EMA9 angle={angle:F2}°, EMA9={ema9:F2}, ATR={atr:F2}, close={candle.Close:F2}.");
        }

        return BacktestSignal.Hold(
            $"{prefix}: open position exists, but internal angle side is flat; waiting for runner synchronization.");
    }

    private bool PassesBasicFilters(
        BacktestBar currentBar,
        BacktestIndicatorSnapshot indicators,
        decimal volumeRatio,
        string prefix,
        out string rejectReason)
    {
        if (currentBar.Close < _settings.MinimumPrice || currentBar.Close > _settings.MaximumPrice)
        {
            rejectReason = $"{prefix}: price filter failed, close {currentBar.Close:F2} outside {_settings.MinimumPrice:F2}-{_settings.MaximumPrice:F2}.";
            return false;
        }

        if (currentBar.Volume < _settings.MinimumVolume)
        {
            rejectReason = $"{prefix}: volume filter failed, volume {currentBar.Volume} < {_settings.MinimumVolume}.";
            return false;
        }

        if (_settings.MinimumVolumeRatio > 0m)
        {
            if (!indicators.VolumeAverage20.HasValue || indicators.VolumeAverage20.Value <= 0m)
            {
                rejectReason = $"{prefix}: waiting for VolumeAverage20.";
                return false;
            }

            if (volumeRatio < _settings.MinimumVolumeRatio)
            {
                rejectReason = $"{prefix}: volume ratio failed, ratio {volumeRatio:F2} < {_settings.MinimumVolumeRatio:F2}.";
                return false;
            }
        }

        rejectReason = string.Empty;
        return true;
    }

    private bool ShouldFlattenLong(
        AngleConductCandle candle,
        AngleEmaState state,
        decimal ema9,
        decimal angle,
        out string reason)
    {
        if (angle < _settings.NeutralAngleDegrees)
        {
            reason = $"{_settings.CandleMinutes}m EMA9 angle neutral/bearish: {angle:F2}° < {_settings.NeutralAngleDegrees:F2}°.";
            return true;
        }

        if (candle.IsRed && candle.Crosses(ema9))
        {
            reason = $"red completed {_settings.CandleMinutes}m candle crosses rising EMA9 {ema9:F2}.";
            return true;
        }

        if (candle.IsRed && state.LastGreenCandle is not null)
        {
            decimal support = state.LastGreenCandle.LongSupport;
            if (candle.Low <= support || candle.Close <= support)
            {
                reason = $"red candle broke last green support {support:F2}; low={candle.Low:F2}, close={candle.Close:F2}.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private bool ShouldFlattenShort(
        AngleConductCandle candle,
        AngleEmaState state,
        decimal ema9,
        decimal angle,
        out string reason)
    {
        if (angle > -_settings.NeutralAngleDegrees)
        {
            reason = $"{_settings.CandleMinutes}m EMA9 angle neutral/bullish: {angle:F2}° > -{_settings.NeutralAngleDegrees:F2}°.";
            return true;
        }

        if (candle.IsGreen && candle.Crosses(ema9))
        {
            reason = $"green completed {_settings.CandleMinutes}m candle crosses bearish EMA9 {ema9:F2}.";
            return true;
        }

        if (candle.IsGreen && state.LastRedCandle is not null)
        {
            decimal resistance = state.LastRedCandle.ShortResistance;
            if (candle.High >= resistance || candle.Close >= resistance)
            {
                reason = $"green candle broke last red resistance {resistance:F2}; high={candle.High:F2}, close={candle.Close:F2}.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private bool PassesLongReEntry(
        AngleConductCandle candle,
        AngleEmaState state,
        decimal ema9)
    {
        bool greenSetup = candle.IsGreen && (candle.Crosses(ema9) || candle.Close >= ema9 || candle.Low >= ema9);

        if (!greenSetup)
        {
            return false;
        }

        if (state.LastRedCandle is null)
        {
            return true;
        }

        return candle.Close > state.LastRedCandle.BodyHigh || candle.High > state.LastRedCandle.High;
    }

    private bool PassesShortReEntry(
        AngleConductCandle candle,
        AngleEmaState state,
        decimal ema9)
    {
        bool redSetup = candle.IsRed && (candle.Crosses(ema9) || candle.Close <= ema9 || candle.High <= ema9);

        if (!redSetup)
        {
            return false;
        }

        if (state.LastGreenCandle is null)
        {
            return true;
        }

        return candle.Close < state.LastGreenCandle.BodyLow || candle.Low < state.LastGreenCandle.Low;
    }

    private AngleEmaState CalculateState(
        IReadOnlyList<BacktestBar> recentBars,
        string symbol)
    {
        IReadOnlyList<BacktestBar> rawBars = recentBars.Count > _settings.MaxRecentRawBars
            ? recentBars.Skip(recentBars.Count - _settings.MaxRecentRawBars).ToArray()
            : recentBars;

        List<AngleConductCandle> candles = BuildCandles(rawBars, symbol);

        // Use only fully completed higher-timeframe candles for backtest signals.
        // This matches Harvester StrategyV21/V23, which generates a signal on a completed 15m/5m bar
        // and enters on the first following 1m bar. It also avoids repainting the unfinished candle.
        List<AngleConductCandle> closedCandles = candles.Count > 1
            ? candles.Take(candles.Count - 1).ToList()
            : [];

        if (closedCandles.Count < _settings.EmaPeriod + 1)
        {
            return new AngleEmaState(
                null,
                null,
                null,
                0m,
                closedCandles.LastOrDefault(),
                closedCandles.Count > 1 ? closedCandles[^2] : null,
                LastGreen(closedCandles),
                LastRed(closedCandles));
        }

        IReadOnlyList<decimal> closes = closedCandles.Select(candle => candle.Close).ToArray();
        List<decimal> emaValues = CalculateEma(closes, _settings.EmaPeriod);

        if (emaValues.Count < 2)
        {
            return new AngleEmaState(
                null,
                null,
                null,
                0m,
                closedCandles.LastOrDefault(),
                closedCandles.Count > 1 ? closedCandles[^2] : null,
                LastGreen(closedCandles),
                LastRed(closedCandles));
        }

        List<AngleConductCandle> candlesWithAtr = ApplyAtr(closedCandles);
        AngleConductCandle signalCandle = candlesWithAtr[^1];
        AngleConductCandle? previousSignalCandle = candlesWithAtr.Count > 1 ? candlesWithAtr[^2] : null;
        decimal ema9 = emaValues[^1];
        decimal previousEma9 = emaValues[^2];
        decimal? atr = signalCandle.Atr14 ?? previousSignalCandle?.Atr14;

        if (!atr.HasValue || atr.Value <= 0m)
        {
            return new AngleEmaState(
                null,
                null,
                null,
                0m,
                signalCandle,
                previousSignalCandle,
                LastGreen(candlesWithAtr.Take(Math.Max(0, candlesWithAtr.Count - 1))),
                LastRed(candlesWithAtr.Take(Math.Max(0, candlesWithAtr.Count - 1))));
        }

        decimal angle = CalculateAngleDegrees(previousEma9, ema9, atr.Value);

        return new AngleEmaState(
            Ema9: decimal.Round(ema9, 4),
            PreviousEma9: decimal.Round(previousEma9, 4),
            Atr: decimal.Round(atr.Value, 4),
            AngleDegrees: decimal.Round(angle, 4),
            SignalCandle: signalCandle,
            PreviousSignalCandle: previousSignalCandle,
            LastGreenCandle: LastGreen(candlesWithAtr.Take(Math.Max(0, candlesWithAtr.Count - 1))),
            LastRedCandle: LastRed(candlesWithAtr.Take(Math.Max(0, candlesWithAtr.Count - 1))));
    }

    private List<AngleConductCandle> BuildCandles(
        IReadOnlyList<BacktestBar> bars,
        string symbol)
    {
        var result = new List<AngleConductCandle>();

        foreach (BacktestBar bar in bars)
        {
            DateTimeOffset bucketStart = GetBucketStart(bar.Time);
            AngleConductCandle? current = result.Count > 0 ? result[^1] : null;

            if (current is null || current.StartTime != bucketStart)
            {
                result.Add(new AngleConductCandle(
                    StartTime: bucketStart,
                    EndTime: bar.Time,
                    Symbol: symbol,
                    Open: bar.Open,
                    High: bar.High,
                    Low: bar.Low,
                    Close: bar.Close,
                    Volume: bar.Volume));
            }
            else
            {
                result[^1] = current with
                {
                    EndTime = bar.Time,
                    High = Math.Max(current.High, bar.High),
                    Low = Math.Min(current.Low, bar.Low),
                    Close = bar.Close,
                    Volume = current.Volume + bar.Volume
                };
            }
        }

        return result;
    }

    private DateTimeOffset GetBucketStart(DateTimeOffset time)
    {
        int minuteOfDay = MarketTime.GetEasternMinuteOfDay(time);
        int marketOpenMinute = 570;
        int offsetFromOpen = Math.Max(0, minuteOfDay - marketOpenMinute);
        int bucketOffset = offsetFromOpen / _settings.CandleMinutes * _settings.CandleMinutes;
        int bucketMinuteOfDay = marketOpenMinute + bucketOffset;

        return new DateTimeOffset(time.Date.AddMinutes(bucketMinuteOfDay), time.Offset);
    }

    private static List<decimal> CalculateEma(
        IReadOnlyList<decimal> values,
        int period)
    {
        var result = new List<decimal>();

        if (values.Count < period)
        {
            return result;
        }

        decimal seed = 0m;
        for (int i = 0; i < period; i++)
        {
            seed += values[i];
        }

        decimal ema = seed / period;
        result.Add(ema);

        decimal multiplier = 2m / (period + 1m);

        for (int i = period; i < values.Count; i++)
        {
            ema = ((values[i] - ema) * multiplier) + ema;
            result.Add(ema);
        }

        return result;
    }

    private static List<AngleConductCandle> ApplyAtr(IReadOnlyList<AngleConductCandle> candles)
    {
        var result = new List<AngleConductCandle>(candles.Count);
        var trueRanges = new List<decimal>(candles.Count);

        for (int i = 0; i < candles.Count; i++)
        {
            AngleConductCandle candle = candles[i];
            decimal trueRange;

            if (i == 0)
            {
                trueRange = candle.High - candle.Low;
            }
            else
            {
                decimal previousClose = candles[i - 1].Close;
                decimal highLow = candle.High - candle.Low;
                decimal highPreviousClose = Math.Abs(candle.High - previousClose);
                decimal lowPreviousClose = Math.Abs(candle.Low - previousClose);
                trueRange = Math.Max(highLow, Math.Max(highPreviousClose, lowPreviousClose));
            }

            trueRanges.Add(Math.Max(0m, trueRange));
            int atrPeriod = Math.Min(14, trueRanges.Count);
            decimal atr = trueRanges.TakeLast(atrPeriod).Average();
            result.Add(candle with { Atr14 = atr });
        }

        return result;
    }

    private static decimal CalculateAngleDegrees(
        decimal previousEma,
        decimal currentEma,
        decimal atr)
    {
        if (atr <= 0m)
        {
            return 0m;
        }

        // Harvester StrategyV21/V23 normalize EMA slope by ATR, not by percent price change.
        // This keeps the degree threshold comparable across high- and low-priced symbols.
        double normalizedSlope = (double)((currentEma - previousEma) / atr);
        return (decimal)(Math.Atan(normalizedSlope) * 180.0 / Math.PI);
    }

    private static decimal CalculateVolumeRatio(
        BacktestBar currentBar,
        BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.VolumeAverage20.HasValue || indicators.VolumeAverage20.Value <= 0m)
        {
            return 0m;
        }

        return currentBar.Volume / indicators.VolumeAverage20.Value;
    }

    private static AngleConductCandle? LastGreen(IEnumerable<AngleConductCandle> candles)
    {
        return candles.LastOrDefault(candle => candle.IsGreen);
    }

    private static AngleConductCandle? LastRed(IEnumerable<AngleConductCandle> candles)
    {
        return candles.LastOrDefault(candle => candle.IsRed);
    }
}
