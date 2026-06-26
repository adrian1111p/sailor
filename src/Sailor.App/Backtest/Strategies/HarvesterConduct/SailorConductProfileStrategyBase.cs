using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct;

public abstract class SailorConductProfileStrategyBase : ISailorConductEntryStrategy
{
    private readonly SailorConductEntryRules _rules;

    protected SailorConductProfileStrategyBase(SailorConductEntryRules rules)
    {
        _rules = rules;
    }

    public string ProfileName => _rules.ProfileName;

    public string StrategyName => _rules.StrategyName;

    public string VariantName => _rules.VariantName;

    public BacktestSignal EvaluateEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile)
    {
        string prefix = $"{StrategyName}/{VariantName}";

        if (currentBar.Close < _rules.MinimumPrice || currentBar.Close > _rules.MaximumPrice)
        {
            return BacktestSignal.Hold(
                $"{prefix}: price filter failed, close {currentBar.Close:F2} outside {_rules.MinimumPrice:F2}-{_rules.MaximumPrice:F2}.");
        }

        if (currentBar.Volume < _rules.MinimumVolume)
        {
            return BacktestSignal.Hold(
                $"{prefix}: volume filter failed, volume {currentBar.Volume} < {_rules.MinimumVolume}.");
        }

        decimal volumeRatio = 0m;
        if (indicators.VolumeAverage20.HasValue && indicators.VolumeAverage20.Value > 0m)
        {
            volumeRatio = currentBar.Volume / indicators.VolumeAverage20.Value;
        }

        if (_rules.MinimumVolumeRatio > 0m)
        {
            if (!indicators.VolumeAverage20.HasValue || indicators.VolumeAverage20.Value <= 0m)
            {
                return BacktestSignal.Hold($"{prefix}: waiting for VolumeAverage20.");
            }

            if (volumeRatio < _rules.MinimumVolumeRatio)
            {
                return BacktestSignal.Hold(
                    $"{prefix}: volume ratio failed, ratio {volumeRatio:F2} < {_rules.MinimumVolumeRatio:F2}.");
            }
        }

        if (_rules.RequireGreenBar && currentBar.Close <= currentBar.Open)
        {
            return BacktestSignal.Hold(
                $"{prefix}: green-bar filter failed, close {currentBar.Close:F2} <= open {currentBar.Open:F2}.");
        }

        if (_rules.RequireCloseAbovePreviousHigh && currentBar.Close <= previousBar.High)
        {
            return BacktestSignal.Hold(
                $"{prefix}: previous-high filter failed, close {currentBar.Close:F2} <= previous high {previousBar.High:F2}.");
        }

        if (_rules.RequireEma9AboveSma20)
        {
            if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
            {
                return BacktestSignal.Hold($"{prefix}: waiting for EMA9/SMA20.");
            }

            if (indicators.Ema9.Value <= indicators.Sma20.Value)
            {
                return BacktestSignal.Hold(
                    $"{prefix}: trend filter failed, EMA9 {indicators.Ema9.Value:F2} <= SMA20 {indicators.Sma20.Value:F2}.");
            }

            decimal emaSpreadPercent = PercentSpread(indicators.Ema9.Value, indicators.Sma20.Value);
            if (_rules.MinimumEmaSpreadPercent > 0m && emaSpreadPercent < _rules.MinimumEmaSpreadPercent)
            {
                return BacktestSignal.Hold(
                    $"{prefix}: EMA spread failed, spread {emaSpreadPercent:F2}% < {_rules.MinimumEmaSpreadPercent:F2}%.");
            }
        }

        if (_rules.RequireCloseAboveVwap)
        {
            if (!indicators.Vwap.HasValue)
            {
                return BacktestSignal.Hold($"{prefix}: waiting for VWAP.");
            }

            if (currentBar.Close <= indicators.Vwap.Value)
            {
                return BacktestSignal.Hold(
                    $"{prefix}: VWAP filter failed, close {currentBar.Close:F2} <= VWAP {indicators.Vwap.Value:F2}.");
            }
        }

        if (_rules.RequireCloseAboveSma200WhenAvailable &&
            indicators.Sma200.HasValue &&
            currentBar.Close <= indicators.Sma200.Value)
        {
            return BacktestSignal.Hold(
                $"{prefix}: SMA200 filter failed, close {currentBar.Close:F2} <= SMA200 {indicators.Sma200.Value:F2}.");
        }

        if (_rules.MaximumVwapExtensionPercent > 0m && indicators.Vwap.HasValue)
        {
            decimal vwapExtensionPercent = PercentSpread(currentBar.Close, indicators.Vwap.Value);
            if (vwapExtensionPercent > _rules.MaximumVwapExtensionPercent)
            {
                return BacktestSignal.Hold(
                    $"{prefix}: VWAP extension too high, close is {vwapExtensionPercent:F2}% above VWAP.");
            }
        }

        List<string> passedSetups = [];

        if (HasPattern(SailorConductEntryPattern.Momentum) && PassesMomentum(currentBar, previousBar, profile))
        {
            passedSetups.Add("momentum");
        }

        if (HasPattern(SailorConductEntryPattern.Pullback) && PassesPullback(currentBar, previousBar, indicators))
        {
            passedSetups.Add("pullback");
        }

        if (HasPattern(SailorConductEntryPattern.Breakout) && PassesBreakout(currentBar, recentBars))
        {
            passedSetups.Add("breakout");
        }

        if (HasPattern(SailorConductEntryPattern.VwapReversion) && PassesVwapReversion(currentBar, previousBar, indicators))
        {
            passedSetups.Add("vwap-reversion");
        }

        if (HasPattern(SailorConductEntryPattern.ChoppyShield) && PassesChoppyShield(currentBar, previousBar, indicators))
        {
            passedSetups.Add("choppy-shield");
        }

        if (passedSetups.Count == 0)
        {
            return BacktestSignal.Hold(
                $"{prefix}: no conduct setup passed. Close={currentBar.Close:F2}, PrevClose={previousBar.Close:F2}, EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, VWAP={Format(indicators.Vwap)}, VolRatio={volumeRatio:F2}.");
        }

        return BacktestSignal.Buy(
            $"{prefix} conduct entry: setups={string.Join('+', passedSetups)}, close={currentBar.Close:F2}, prev={previousBar.Close:F2}, " +
            $"EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, SMA200={Format(indicators.Sma200)}, " +
            $"VWAP={Format(indicators.Vwap)}, VolRatio={volumeRatio:F2}.");
    }

    private bool HasPattern(SailorConductEntryPattern pattern)
    {
        return (_rules.Patterns & pattern) == pattern;
    }

    private bool PassesMomentum(
        BacktestBar currentBar,
        BacktestBar previousBar,
        SailorStrategyProfile profile)
    {
        decimal requiredMomentum = _rules.EntryMomentumPercent > 0m
            ? _rules.EntryMomentumPercent
            : profile.EntryMomentumPercent;

        decimal entryThreshold = previousBar.Close * (1m + requiredMomentum / 100m);
        return currentBar.Close >= entryThreshold;
    }

    private bool PassesPullback(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Ema9.HasValue)
        {
            return false;
        }

        if (currentBar.Close < indicators.Ema9.Value)
        {
            return false;
        }

        decimal distanceFromEma = Math.Abs(PercentSpread(currentBar.Close, indicators.Ema9.Value));
        bool recoveredAbovePrevious = currentBar.Close > previousBar.Close;
        return recoveredAbovePrevious && distanceFromEma <= _rules.PullbackMaximumDistanceFromEmaPercent;
    }

    private bool PassesBreakout(
        BacktestBar currentBar,
        IReadOnlyList<BacktestBar> recentBars)
    {
        if (recentBars.Count <= _rules.BreakoutLookbackBars)
        {
            return false;
        }

        IEnumerable<BacktestBar> previousWindow = recentBars
            .Take(Math.Max(0, recentBars.Count - 1))
            .TakeLast(_rules.BreakoutLookbackBars);

        if (!previousWindow.Any())
        {
            return false;
        }

        decimal previousHigh = previousWindow.Max(bar => bar.High);
        decimal breakoutPrice = previousHigh * (1m + _rules.BreakoutBufferPercent / 100m);
        return currentBar.Close >= breakoutPrice;
    }

    private bool PassesVwapReversion(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Vwap.HasValue)
        {
            return false;
        }

        decimal previousDistanceBelowVwap = PercentSpread(indicators.Vwap.Value, previousBar.Close);
        bool wasBelowVwapButNotBroken = previousBar.Close < indicators.Vwap.Value &&
            previousDistanceBelowVwap <= _rules.VwapReversionMaximumDistancePercent;
        bool isRecovering = currentBar.Close > previousBar.Close && currentBar.Close >= currentBar.Open;
        return wasBelowVwapButNotBroken && isRecovering;
    }

    private bool PassesChoppyShield(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
        {
            return false;
        }

        decimal oneBarMomentumPercent = PercentSpread(currentBar.Close, previousBar.Close);
        decimal emaSpreadPercent = Math.Abs(PercentSpread(indicators.Ema9.Value, indicators.Sma20.Value));
        return oneBarMomentumPercent > 0m &&
               oneBarMomentumPercent <= _rules.ChoppyMaximumMomentumPercent &&
               emaSpreadPercent >= 0.02m;
    }

    private static decimal PercentSpread(decimal left, decimal right)
    {
        return right == 0m ? 0m : (left - right) / right * 100m;
    }

    private static string Format(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }
}
