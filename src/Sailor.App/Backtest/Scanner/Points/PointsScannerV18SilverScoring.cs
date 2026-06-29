using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Scanner.Points;

public static class PointsScannerV18SilverScoring
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
        if (!profile.Name.Equals("v18-silver", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<PointsScannerFactor>();
        }

        var factors = new List<PointsScannerFactor>();

        bool candleColorOk = isShort ? latestBar.Close < latestBar.Open : latestBar.Close > latestBar.Open;
        Add(factors,
            candleColorOk ? "V18_CANDLE_COLOR" : "V18_CANDLE_COLOR_ADVERSE",
            candleColorOk ? "V18 candle color supports side" : "V18 candle color is opposite side",
            candleColorOk ? settings.CandleColorPoints : settings.OppositeCandleColorPenalty,
            $"open={latestBar.Open:F4} close={latestBar.Close:F4}",
            "v18");

        if (previousBar is not null && previousBar.Close > 0m)
        {
            decimal barMomentum = (latestBar.Close - previousBar.Close) / previousBar.Close * 100m;
            decimal directionalBarMomentum = isShort ? -barMomentum : barMomentum;
            decimal entryThreshold = Math.Max(0m, profile.EntryMomentumPercent);
            decimal points = entryThreshold <= 0m
                ? 0m
                : Math.Clamp(directionalBarMomentum / entryThreshold, -1m, 1m) * settings.BarToBarMomentumPoints;
            Add(factors,
                "V18_BAR_MOMENTUM",
                "V18 bar-to-bar directional momentum",
                points,
                $"directional={directionalBarMomentum:F3}% threshold={entryThreshold:F3}%",
                "v18");
        }
        else
        {
            Add(factors,
                "V18_BAR_MOMENTUM_MISSING",
                "V18 bar-to-bar momentum is missing because previous bar is unavailable",
                -2m,
                "previousBar=n/a",
                "v18");
        }

        if (latestIndicators.Vwap.HasValue && latestIndicators.Vwap.Value > 0m)
        {
            decimal distanceFromVwap = Math.Abs((latestBar.Close - latestIndicators.Vwap.Value) / latestIndicators.Vwap.Value * 100m);
            if (distanceFromVwap <= 1m)
            {
                Add(factors,
                    "V18_VWAP_REVERSION",
                    "V18 close is within VWAP reversion band",
                    settings.VwapReversionPoints,
                    $"distance={distanceFromVwap:F2}%",
                    "v18");
            }

            if (distanceFromVwap <= 2m)
            {
                Add(factors,
                    "V18_VWAP_EXTENSION_OK",
                    "V18 VWAP extension is inside limit",
                    settings.VwapExtensionWithinLimitPoints,
                    $"distance={distanceFromVwap:F2}%",
                    "v18");
            }
            else
            {
                decimal penalty = Math.Max(settings.VwapExtensionMaximumPenalty, -(distanceFromVwap - 2m) * 3m);
                Add(factors,
                    "V18_VWAP_EXTENSION_HIGH",
                    "V18 VWAP extension is high",
                    penalty,
                    $"distance={distanceFromVwap:F2}%",
                    "v18");
            }
        }
        else
        {
            Add(factors,
                "V18_VWAP_MISSING",
                "V18 VWAP reversion/extension cannot be scored",
                settings.MissingVwapPenalty,
                "VWAP=n/a",
                "v18");
        }

        decimal bodyPercent = latestBar.Open > 0m
            ? Math.Abs((latestBar.Close - latestBar.Open) / latestBar.Open * 100m)
            : 0m;
        if (bodyPercent <= 0.65m)
        {
            Add(factors,
                "V18_CHOPPY_SHIELD",
                "V18 choppy-shield body size is controlled",
                settings.ChoppyShieldPoints,
                $"body={bodyPercent:F2}%",
                "v18");
        }
        else
        {
            decimal penalty = Math.Max(-settings.ChoppyShieldPoints, -bodyPercent);
            Add(factors,
                "V18_CHOPPY_SHIELD_BODY_HIGH",
                "V18 choppy-shield body size is extended",
                penalty,
                $"body={bodyPercent:F2}%",
                "v18");
        }

        if (volumeRatio >= profile.MinimumVolumeRatio)
        {
            Add(factors,
                "V18_VOL_RATIO_PROFILE",
                "V18 volume ratio meets selective-short profile",
                settings.V18VolumeRatioProfilePoints,
                $"ratio={volumeRatio:F2} min={profile.MinimumVolumeRatio:F2}",
                "v18");
        }
        else
        {
            decimal penalty = -settings.V18VolumeRatioProfilePoints * Math.Clamp(profile.MinimumVolumeRatio - volumeRatio, 0m, profile.MinimumVolumeRatio) / Math.Max(profile.MinimumVolumeRatio, 0.01m);
            Add(factors,
                "V18_VOL_RATIO_PROFILE_LOW",
                "V18 volume ratio is below selective-short profile",
                penalty,
                $"ratio={volumeRatio:F2} min={profile.MinimumVolumeRatio:F2}",
                "v18");
        }

        return factors;
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
