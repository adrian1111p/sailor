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

        if (!PassesCommonFilters(currentBar, indicators, prefix, out string commonRejectReason, out decimal volumeRatio))
        {
            return BacktestSignal.Hold(commonRejectReason);
        }

        if (profile.SideMode.AllowsLong())
        {
            BacktestSignal longSignal = EvaluateLongEntry(currentBar, previousBar, indicators, recentBars, profile, prefix, volumeRatio);
            if (longSignal.Type == BacktestSignalType.Buy)
            {
                return longSignal;
            }
        }

        if (profile.SideMode.AllowsShort())
        {
            BacktestSignal shortSignal = EvaluateShortEntry(currentBar, previousBar, indicators, recentBars, profile, prefix, volumeRatio);
            if (shortSignal.Type == BacktestSignalType.Sell)
            {
                return shortSignal;
            }
        }

        return BacktestSignal.Hold(
            $"{prefix}: no {profile.SideMode} conduct setup passed. Close={currentBar.Close:F2}, PrevClose={previousBar.Close:F2}, EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, VWAP={Format(indicators.Vwap)}, VolRatio={volumeRatio:F2}.");
    }

    private bool PassesCommonFilters(
        BacktestBar currentBar,
        BacktestIndicatorSnapshot indicators,
        string prefix,
        out string rejectReason,
        out decimal volumeRatio)
    {
        volumeRatio = 0m;

        if (currentBar.Close < _rules.MinimumPrice || currentBar.Close > _rules.MaximumPrice)
        {
            rejectReason = $"{prefix}: price filter failed, close {currentBar.Close:F2} outside {_rules.MinimumPrice:F2}-{_rules.MaximumPrice:F2}.";
            return false;
        }

        if (currentBar.Volume < _rules.MinimumVolume)
        {
            rejectReason = $"{prefix}: volume filter failed, volume {currentBar.Volume} < {_rules.MinimumVolume}.";
            return false;
        }

        if (indicators.VolumeAverage20.HasValue && indicators.VolumeAverage20.Value > 0m)
        {
            volumeRatio = currentBar.Volume / indicators.VolumeAverage20.Value;
        }

        if (_rules.MinimumVolumeRatio > 0m)
        {
            if (!indicators.VolumeAverage20.HasValue || indicators.VolumeAverage20.Value <= 0m)
            {
                rejectReason = $"{prefix}: waiting for VolumeAverage20.";
                return false;
            }

            if (volumeRatio < _rules.MinimumVolumeRatio)
            {
                rejectReason = $"{prefix}: volume ratio failed, ratio {volumeRatio:F2} < {_rules.MinimumVolumeRatio:F2}.";
                return false;
            }
        }

        rejectReason = string.Empty;
        return true;
    }

    private BacktestSignal EvaluateLongEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile,
        string prefix,
        decimal volumeRatio)
    {
        if (_rules.RequireGreenBar && currentBar.Close <= currentBar.Open)
        {
            return BacktestSignal.Hold($"{prefix}: long green-bar filter failed, close {currentBar.Close:F2} <= open {currentBar.Open:F2}.");
        }

        if (_rules.RequireCloseAbovePreviousHigh && currentBar.Close <= previousBar.High)
        {
            return BacktestSignal.Hold($"{prefix}: long previous-high filter failed, close {currentBar.Close:F2} <= previous high {previousBar.High:F2}.");
        }

        if (!PassesLongTrendFilters(currentBar, indicators, prefix, out string rejectReason))
        {
            return BacktestSignal.Hold(rejectReason);
        }

        List<string> passedSetups = [];

        if (HasPattern(SailorConductEntryPattern.Momentum) && PassesLongMomentum(currentBar, previousBar, profile))
        {
            passedSetups.Add("momentum");
        }

        if (HasPattern(SailorConductEntryPattern.Pullback) && PassesLongPullback(currentBar, previousBar, indicators))
        {
            passedSetups.Add("pullback");
        }

        if (HasPattern(SailorConductEntryPattern.Breakout) && PassesLongBreakout(currentBar, recentBars))
        {
            passedSetups.Add("breakout");
        }

        if (HasPattern(SailorConductEntryPattern.VwapReversion) && PassesLongVwapReversion(currentBar, previousBar, indicators))
        {
            passedSetups.Add("vwap-reversion");
        }

        if (HasPattern(SailorConductEntryPattern.ChoppyShield) && PassesLongChoppyShield(currentBar, previousBar, indicators))
        {
            passedSetups.Add("choppy-shield");
        }

        if (passedSetups.Count == 0)
        {
            return BacktestSignal.Hold($"{prefix}: no long setup passed.");
        }

        return BacktestSignal.Buy(
            $"{prefix} LONG conduct entry: setups={string.Join('+', passedSetups)}, close={currentBar.Close:F2}, prev={previousBar.Close:F2}, " +
            $"EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, SMA200={Format(indicators.Sma200)}, " +
            $"VWAP={Format(indicators.Vwap)}, VolRatio={volumeRatio:F2}.");
    }

    private BacktestSignal EvaluateShortEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile,
        string prefix,
        decimal volumeRatio)
    {
        if (_rules.RequireGreenBar && currentBar.Close >= currentBar.Open)
        {
            return BacktestSignal.Hold($"{prefix}: short red-bar filter failed, close {currentBar.Close:F2} >= open {currentBar.Open:F2}.");
        }

        if (_rules.RequireCloseAbovePreviousHigh && currentBar.Close >= previousBar.Low)
        {
            return BacktestSignal.Hold($"{prefix}: short previous-low filter failed, close {currentBar.Close:F2} >= previous low {previousBar.Low:F2}.");
        }

        if (!PassesShortTrendFilters(currentBar, indicators, prefix, out string rejectReason))
        {
            return BacktestSignal.Hold(rejectReason);
        }

        List<string> passedSetups = [];

        if (HasPattern(SailorConductEntryPattern.Momentum) && PassesShortMomentum(currentBar, previousBar, profile))
        {
            passedSetups.Add("momentum");
        }

        if (HasPattern(SailorConductEntryPattern.Pullback) && PassesShortPullback(currentBar, previousBar, indicators))
        {
            passedSetups.Add("pullback");
        }

        if (HasPattern(SailorConductEntryPattern.Breakout) && PassesShortBreakout(currentBar, recentBars))
        {
            passedSetups.Add("breakdown");
        }

        if (HasPattern(SailorConductEntryPattern.VwapReversion) && PassesShortVwapReversion(currentBar, previousBar, indicators))
        {
            passedSetups.Add("vwap-reversion");
        }

        if (HasPattern(SailorConductEntryPattern.ChoppyShield) && PassesShortChoppyShield(currentBar, previousBar, indicators))
        {
            passedSetups.Add("choppy-shield");
        }

        if (passedSetups.Count == 0)
        {
            return BacktestSignal.Hold($"{prefix}: no short setup passed.");
        }

        return BacktestSignal.Sell(
            $"{prefix} SHORT conduct entry: setups={string.Join('+', passedSetups)}, close={currentBar.Close:F2}, prev={previousBar.Close:F2}, " +
            $"EMA9={Format(indicators.Ema9)}, SMA20={Format(indicators.Sma20)}, SMA200={Format(indicators.Sma200)}, " +
            $"VWAP={Format(indicators.Vwap)}, VolRatio={volumeRatio:F2}.");
    }

    private bool PassesLongTrendFilters(
        BacktestBar currentBar,
        BacktestIndicatorSnapshot indicators,
        string prefix,
        out string rejectReason)
    {
        if (_rules.RequireEma9AboveSma20)
        {
            if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
            {
                rejectReason = $"{prefix}: waiting for EMA9/SMA20.";
                return false;
            }

            if (indicators.Ema9.Value <= indicators.Sma20.Value)
            {
                rejectReason = $"{prefix}: long trend filter failed, EMA9 {indicators.Ema9.Value:F2} <= SMA20 {indicators.Sma20.Value:F2}.";
                return false;
            }

            decimal emaSpreadPercent = PercentSpread(indicators.Ema9.Value, indicators.Sma20.Value);
            if (_rules.MinimumEmaSpreadPercent > 0m && emaSpreadPercent < _rules.MinimumEmaSpreadPercent)
            {
                rejectReason = $"{prefix}: long EMA spread failed, spread {emaSpreadPercent:F2}% < {_rules.MinimumEmaSpreadPercent:F2}%.";
                return false;
            }
        }

        if (_rules.RequireCloseAboveVwap)
        {
            if (!indicators.Vwap.HasValue)
            {
                rejectReason = $"{prefix}: waiting for VWAP.";
                return false;
            }

            if (currentBar.Close <= indicators.Vwap.Value)
            {
                rejectReason = $"{prefix}: long VWAP filter failed, close {currentBar.Close:F2} <= VWAP {indicators.Vwap.Value:F2}.";
                return false;
            }
        }

        if (_rules.RequireCloseAboveSma200WhenAvailable && indicators.Sma200.HasValue && currentBar.Close <= indicators.Sma200.Value)
        {
            rejectReason = $"{prefix}: long SMA200 filter failed, close {currentBar.Close:F2} <= SMA200 {indicators.Sma200.Value:F2}.";
            return false;
        }

        if (_rules.MaximumVwapExtensionPercent > 0m && indicators.Vwap.HasValue)
        {
            decimal vwapExtensionPercent = PercentSpread(currentBar.Close, indicators.Vwap.Value);
            if (vwapExtensionPercent > _rules.MaximumVwapExtensionPercent)
            {
                rejectReason = $"{prefix}: long VWAP extension too high, close is {vwapExtensionPercent:F2}% above VWAP.";
                return false;
            }
        }

        rejectReason = string.Empty;
        return true;
    }

    private bool PassesShortTrendFilters(
        BacktestBar currentBar,
        BacktestIndicatorSnapshot indicators,
        string prefix,
        out string rejectReason)
    {
        if (_rules.RequireEma9AboveSma20)
        {
            if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
            {
                rejectReason = $"{prefix}: waiting for EMA9/SMA20.";
                return false;
            }

            if (indicators.Ema9.Value >= indicators.Sma20.Value)
            {
                rejectReason = $"{prefix}: short trend filter failed, EMA9 {indicators.Ema9.Value:F2} >= SMA20 {indicators.Sma20.Value:F2}.";
                return false;
            }

            decimal emaSpreadPercent = PercentSpread(indicators.Sma20.Value, indicators.Ema9.Value);
            if (_rules.MinimumEmaSpreadPercent > 0m && emaSpreadPercent < _rules.MinimumEmaSpreadPercent)
            {
                rejectReason = $"{prefix}: short EMA spread failed, spread {emaSpreadPercent:F2}% < {_rules.MinimumEmaSpreadPercent:F2}%.";
                return false;
            }
        }

        if (_rules.RequireCloseAboveVwap)
        {
            if (!indicators.Vwap.HasValue)
            {
                rejectReason = $"{prefix}: waiting for VWAP.";
                return false;
            }

            if (currentBar.Close >= indicators.Vwap.Value)
            {
                rejectReason = $"{prefix}: short VWAP filter failed, close {currentBar.Close:F2} >= VWAP {indicators.Vwap.Value:F2}.";
                return false;
            }
        }

        if (_rules.RequireCloseAboveSma200WhenAvailable && indicators.Sma200.HasValue && currentBar.Close >= indicators.Sma200.Value)
        {
            rejectReason = $"{prefix}: short SMA200 filter failed, close {currentBar.Close:F2} >= SMA200 {indicators.Sma200.Value:F2}.";
            return false;
        }

        if (_rules.MaximumVwapExtensionPercent > 0m && indicators.Vwap.HasValue)
        {
            decimal vwapExtensionPercent = PercentSpread(indicators.Vwap.Value, currentBar.Close);
            if (vwapExtensionPercent > _rules.MaximumVwapExtensionPercent)
            {
                rejectReason = $"{prefix}: short VWAP extension too high, close is {vwapExtensionPercent:F2}% below VWAP.";
                return false;
            }
        }

        rejectReason = string.Empty;
        return true;
    }

    private bool HasPattern(SailorConductEntryPattern pattern)
    {
        return (_rules.Patterns & pattern) == pattern;
    }

    private bool PassesLongMomentum(BacktestBar currentBar, BacktestBar previousBar, SailorStrategyProfile profile)
    {
        decimal requiredMomentum = _rules.EntryMomentumPercent > 0m ? _rules.EntryMomentumPercent : profile.EntryMomentumPercent;
        decimal entryThreshold = previousBar.Close * (1m + requiredMomentum / 100m);
        return currentBar.Close >= entryThreshold;
    }

    private bool PassesShortMomentum(BacktestBar currentBar, BacktestBar previousBar, SailorStrategyProfile profile)
    {
        decimal requiredMomentum = _rules.EntryMomentumPercent > 0m ? _rules.EntryMomentumPercent : profile.EntryMomentumPercent;
        decimal entryThreshold = previousBar.Close * (1m - requiredMomentum / 100m);
        return currentBar.Close <= entryThreshold;
    }

    private bool PassesLongPullback(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Ema9.HasValue || currentBar.Close < indicators.Ema9.Value)
        {
            return false;
        }

        decimal distanceFromEma = Math.Abs(PercentSpread(currentBar.Close, indicators.Ema9.Value));
        bool recoveredAbovePrevious = currentBar.Close > previousBar.Close;
        return recoveredAbovePrevious && distanceFromEma <= _rules.PullbackMaximumDistanceFromEmaPercent;
    }

    private bool PassesShortPullback(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Ema9.HasValue || currentBar.Close > indicators.Ema9.Value)
        {
            return false;
        }

        decimal distanceFromEma = Math.Abs(PercentSpread(currentBar.Close, indicators.Ema9.Value));
        bool recoveredBelowPrevious = currentBar.Close < previousBar.Close;
        return recoveredBelowPrevious && distanceFromEma <= _rules.PullbackMaximumDistanceFromEmaPercent;
    }

    private bool PassesLongBreakout(BacktestBar currentBar, IReadOnlyList<BacktestBar> recentBars)
    {
        if (recentBars.Count <= _rules.BreakoutLookbackBars)
        {
            return false;
        }

        IEnumerable<BacktestBar> previousWindow = recentBars.Take(Math.Max(0, recentBars.Count - 1)).TakeLast(_rules.BreakoutLookbackBars);
        if (!previousWindow.Any())
        {
            return false;
        }

        decimal previousHigh = previousWindow.Max(bar => bar.High);
        decimal breakoutPrice = previousHigh * (1m + _rules.BreakoutBufferPercent / 100m);
        return currentBar.Close >= breakoutPrice;
    }

    private bool PassesShortBreakout(BacktestBar currentBar, IReadOnlyList<BacktestBar> recentBars)
    {
        if (recentBars.Count <= _rules.BreakoutLookbackBars)
        {
            return false;
        }

        IEnumerable<BacktestBar> previousWindow = recentBars.Take(Math.Max(0, recentBars.Count - 1)).TakeLast(_rules.BreakoutLookbackBars);
        if (!previousWindow.Any())
        {
            return false;
        }

        decimal previousLow = previousWindow.Min(bar => bar.Low);
        decimal breakdownPrice = previousLow * (1m - _rules.BreakoutBufferPercent / 100m);
        return currentBar.Close <= breakdownPrice;
    }

    private bool PassesLongVwapReversion(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Vwap.HasValue)
        {
            return false;
        }

        decimal previousDistanceBelowVwap = PercentSpread(indicators.Vwap.Value, previousBar.Close);
        bool wasBelowVwapButNotBroken = previousBar.Close < indicators.Vwap.Value && previousDistanceBelowVwap <= _rules.VwapReversionMaximumDistancePercent;
        bool isRecovering = currentBar.Close > previousBar.Close && currentBar.Close >= currentBar.Open;
        return wasBelowVwapButNotBroken && isRecovering;
    }

    private bool PassesShortVwapReversion(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Vwap.HasValue)
        {
            return false;
        }

        decimal previousDistanceAboveVwap = PercentSpread(previousBar.Close, indicators.Vwap.Value);
        bool wasAboveVwapButNotBroken = previousBar.Close > indicators.Vwap.Value && previousDistanceAboveVwap <= _rules.VwapReversionMaximumDistancePercent;
        bool isRecoveringDown = currentBar.Close < previousBar.Close && currentBar.Close <= currentBar.Open;
        return wasAboveVwapButNotBroken && isRecoveringDown;
    }

    private bool PassesLongChoppyShield(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
        {
            return false;
        }

        decimal oneBarMomentumPercent = PercentSpread(currentBar.Close, previousBar.Close);
        decimal emaSpreadPercent = Math.Abs(PercentSpread(indicators.Ema9.Value, indicators.Sma20.Value));
        return oneBarMomentumPercent > 0m && oneBarMomentumPercent <= _rules.ChoppyMaximumMomentumPercent && emaSpreadPercent >= 0.02m;
    }

    private bool PassesShortChoppyShield(BacktestBar currentBar, BacktestBar previousBar, BacktestIndicatorSnapshot indicators)
    {
        if (!indicators.Ema9.HasValue || !indicators.Sma20.HasValue)
        {
            return false;
        }

        decimal oneBarMomentumPercent = PercentSpread(currentBar.Close, previousBar.Close);
        decimal emaSpreadPercent = Math.Abs(PercentSpread(indicators.Ema9.Value, indicators.Sma20.Value));
        return oneBarMomentumPercent < 0m && Math.Abs(oneBarMomentumPercent) <= _rules.ChoppyMaximumMomentumPercent && emaSpreadPercent >= 0.02m;
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
