using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Scanner.Points;

/// <summary>
/// Common points-scanner add-on factors shared by every strategy profile.
///
/// These factors were introduced from the original V18-Silver/selective-short
/// work, but they are intentionally profile-neutral now. They use the active
/// SailorStrategyProfile values (side mode, entry momentum, minimum volume
/// ratio, price/VWAP/EMA/SMA preferences) and therefore apply to V3/V6/V9,
/// V16, V18, V21, configured profiles, and future strategies without adding
/// a dedicated scanner module for each strategy.
/// </summary>
public static class PointsScannerCommonStrategyScoring
{
    public static IReadOnlyList<PointsScannerFactor> Score(
        BacktestBar latestBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot latestIndicators,
        SailorStrategyProfile profile,
        PointsScannerSettings settings,
        bool isShort,
        decimal volumeRatio)
    {
        var factors = new List<PointsScannerFactor>();

        ScoreCandleDirection(factors, latestBar, settings, isShort);
        ScoreBarMomentum(factors, latestBar, previousBar, profile, settings, isShort);
        ScoreVwapReversionAndExtension(factors, latestBar, latestIndicators, settings);
        ScoreCandleBodyControl(factors, latestBar, settings);
        ScoreProfileVolumeRatio(factors, profile, settings, volumeRatio);

        return factors;
    }

    private static void ScoreCandleDirection(
        List<PointsScannerFactor> factors,
        BacktestBar latestBar,
        PointsScannerSettings settings,
        bool isShort)
    {
        bool candleColorOk = isShort
            ? latestBar.Close < latestBar.Open
            : latestBar.Close > latestBar.Open;

        Add(factors,
            candleColorOk ? "PROFILE_CANDLE_COLOR" : "PROFILE_CANDLE_COLOR_ADVERSE",
            candleColorOk ? "Candle color supports selected side" : "Candle color is opposite selected side",
            candleColorOk ? settings.CandleColorPoints : settings.OppositeCandleColorPenalty,
            $"open={latestBar.Open:F4} close={latestBar.Close:F4}",
            "profile-common");
    }

    private static void ScoreBarMomentum(
        List<PointsScannerFactor> factors,
        BacktestBar latestBar,
        BacktestBar? previousBar,
        SailorStrategyProfile profile,
        PointsScannerSettings settings,
        bool isShort)
    {
        if (previousBar is not null && previousBar.Close > 0m)
        {
            decimal barMomentum = (latestBar.Close - previousBar.Close) / previousBar.Close * 100m;
            decimal directionalBarMomentum = isShort ? -barMomentum : barMomentum;
            decimal entryThreshold = Math.Max(0m, profile.EntryMomentumPercent);
            decimal points = entryThreshold <= 0m
                ? Math.Clamp(directionalBarMomentum, -1m, 1m) * settings.BarToBarMomentumPoints
                : Math.Clamp(directionalBarMomentum / entryThreshold, -1m, 1m) * settings.BarToBarMomentumPoints;

            Add(factors,
                "PROFILE_BAR_MOMENTUM",
                "Bar-to-bar directional momentum versus profile entry threshold",
                points,
                $"directional={directionalBarMomentum:F3}% threshold={entryThreshold:F3}%",
                "profile-common");
            return;
        }

        Add(factors,
            "PROFILE_BAR_MOMENTUM_MISSING",
            "Bar-to-bar momentum is missing because previous bar is unavailable",
            -2m,
            "previousBar=n/a",
            "profile-common");
    }

    private static void ScoreVwapReversionAndExtension(
        List<PointsScannerFactor> factors,
        BacktestBar latestBar,
        BacktestIndicatorSnapshot latestIndicators,
        PointsScannerSettings settings)
    {
        if (latestIndicators.Vwap.HasValue && latestIndicators.Vwap.Value > 0m)
        {
            decimal distanceFromVwap = Math.Abs((latestBar.Close - latestIndicators.Vwap.Value) / latestIndicators.Vwap.Value * 100m);
            if (distanceFromVwap <= 1m)
            {
                Add(factors,
                    "PROFILE_VWAP_REVERSION",
                    "Close is within the shared VWAP reversion band",
                    settings.VwapReversionPoints,
                    $"distance={distanceFromVwap:F2}%",
                    "profile-common");
            }

            if (distanceFromVwap <= 2m)
            {
                Add(factors,
                    "PROFILE_VWAP_EXTENSION_OK",
                    "VWAP extension is inside the shared profile limit",
                    settings.VwapExtensionWithinLimitPoints,
                    $"distance={distanceFromVwap:F2}%",
                    "profile-common");
            }
            else
            {
                decimal penalty = Math.Max(settings.VwapExtensionMaximumPenalty, -(distanceFromVwap - 2m) * 3m);
                Add(factors,
                    "PROFILE_VWAP_EXTENSION_HIGH",
                    "VWAP extension is high for the selected side",
                    penalty,
                    $"distance={distanceFromVwap:F2}%",
                    "profile-common");
            }
            return;
        }

        Add(factors,
            "PROFILE_VWAP_MISSING",
            "VWAP reversion/extension cannot be scored",
            settings.MissingVwapPenalty,
            "VWAP=n/a",
            "profile-common");
    }

    private static void ScoreCandleBodyControl(
        List<PointsScannerFactor> factors,
        BacktestBar latestBar,
        PointsScannerSettings settings)
    {
        decimal bodyPercent = latestBar.Open > 0m
            ? Math.Abs((latestBar.Close - latestBar.Open) / latestBar.Open * 100m)
            : 0m;

        if (bodyPercent <= 0.65m)
        {
            Add(factors,
                "PROFILE_BODY_CONTROLLED",
                "Candle body size is controlled for shared choppy-shield scoring",
                settings.ChoppyShieldPoints,
                $"body={bodyPercent:F2}%",
                "profile-common");
            return;
        }

        decimal penalty = Math.Max(-settings.ChoppyShieldPoints, -bodyPercent);
        Add(factors,
            "PROFILE_BODY_EXTENDED",
            "Candle body size is extended for shared choppy-shield scoring",
            penalty,
            $"body={bodyPercent:F2}%",
            "profile-common");
    }

    private static void ScoreProfileVolumeRatio(
        List<PointsScannerFactor> factors,
        SailorStrategyProfile profile,
        PointsScannerSettings settings,
        decimal volumeRatio)
    {
        if (profile.MinimumVolumeRatio <= 0m)
        {
            Add(factors,
                "PROFILE_VOL_RATIO_NOT_REQUIRED",
                "Profile does not require a minimum volume ratio",
                settings.ProfileVolumeRatioPoints,
                $"ratio={volumeRatio:F2} min=0.00",
                "profile-common");
            return;
        }

        if (volumeRatio >= profile.MinimumVolumeRatio)
        {
            Add(factors,
                "PROFILE_VOL_RATIO_OK",
                "Volume ratio meets active strategy profile",
                settings.ProfileVolumeRatioPoints,
                $"ratio={volumeRatio:F2} min={profile.MinimumVolumeRatio:F2}",
                "profile-common");
            return;
        }

        decimal penalty = -settings.ProfileVolumeRatioPoints
            * Math.Clamp(profile.MinimumVolumeRatio - volumeRatio, 0m, profile.MinimumVolumeRatio)
            / Math.Max(profile.MinimumVolumeRatio, 0.01m);

        Add(factors,
            "PROFILE_VOL_RATIO_LOW",
            "Volume ratio is below active strategy profile",
            penalty,
            $"ratio={volumeRatio:F2} min={profile.MinimumVolumeRatio:F2}",
            "profile-common");
    }

    private static void Add(
        List<PointsScannerFactor> factors,
        string code,
        string description,
        decimal points,
        string rawValue,
        string category)
    {
        factors.Add(new PointsScannerFactor(code, description, decimal.Round(points, 2), rawValue, category));
    }
}
