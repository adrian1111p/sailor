using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Scanner.Points;

public sealed class PointsScanner
{
    private readonly CsvBacktestDataProvider _dataProvider;
    private readonly PointsScannerSettings _settings;

    public PointsScanner(CsvBacktestDataProvider dataProvider, PointsScannerSettings? settings = null)
    {
        _dataProvider = dataProvider;
        _settings = settings ?? PointsScannerSettings.Default;
    }

    public IReadOnlyList<PointsScannerCandidate> Scan(
        string timeframe,
        SailorStrategyProfile profile,
        int? topCount = null,
        IEnumerable<string>? symbols = null)
    {
        string normalizedTimeframe = string.IsNullOrWhiteSpace(timeframe)
            ? "1m"
            : timeframe.Trim();

        int effectiveTopCount = Math.Max(1, topCount.GetValueOrDefault(profile.ScannerTopCount));
        IEnumerable<string> symbolsToScan = symbols ?? _dataProvider.ListSymbols();
        var candidates = new List<PointsScannerCandidate>();

        foreach (string symbol in symbolsToScan
                     .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                     .Select(symbol => symbol.Trim().ToUpperInvariant())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase))
        {
            PointsScannerCandidate? candidate = TryCreateCandidate(symbol, normalizedTimeframe, profile);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates
            .OrderBy(candidate => StatusSort(candidate.Status))
            .ThenByDescending(candidate => candidate.FinalScore)
            .ThenBy(candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(effectiveTopCount)
            .ToArray();
    }

    private PointsScannerCandidate? TryCreateCandidate(
        string symbol,
        string timeframe,
        SailorStrategyProfile profile)
    {
        BacktestDataSet dataSet;
        try
        {
            dataSet = _dataProvider.LoadBars(symbol, timeframe);
        }
        catch
        {
            return null;
        }

        IReadOnlyList<BacktestBar> bars = dataSet.Bars;
        if (bars.Count == 0)
        {
            return null;
        }

        IReadOnlyList<BacktestIndicatorSnapshot> indicators = TechnicalIndicatorCalculator.Calculate(bars);
        BacktestBar latestBar = bars[^1];
        BacktestBar? previousBar = bars.Count >= 2 ? bars[^2] : null;
        BacktestIndicatorSnapshot latestIndicators = indicators[^1];

        decimal volumeRatio = CalculateVolumeRatio(latestBar, latestIndicators);
        int lookbackIndex = Math.Max(0, bars.Count - 1 - profile.ScannerLookbackBars);
        decimal lookbackClose = bars[lookbackIndex].Close;
        decimal momentumPercent = lookbackClose > 0m
            ? (latestBar.Close - lookbackClose) / lookbackClose * 100m
            : 0m;

        PointsScannerSideScore longScore = ScoreSide(
            dataSet,
            latestBar,
            previousBar,
            latestIndicators,
            profile,
            bars.Count,
            volumeRatio,
            momentumPercent,
            side: "LONG",
            sideEnabled: profile.SideMode.AllowsLong());

        PointsScannerSideScore shortScore = ScoreSide(
            dataSet,
            latestBar,
            previousBar,
            latestIndicators,
            profile,
            bars.Count,
            volumeRatio,
            momentumPercent,
            side: "SHORT",
            sideEnabled: profile.SideMode.AllowsShort());

        PointsScannerSideScore selectedScore = SelectEnabledSide(longScore, shortScore, profile.SideMode);
        PointsScannerStatus status = DetermineStatus(bars.Count, selectedScore.Score, selectedScore.SideEnabled);

        return new PointsScannerCandidate(
            Symbol: dataSet.Symbol,
            Timeframe: dataSet.Timeframe,
            Status: status,
            SelectedSide: selectedScore.Side.ToUpperInvariant(),
            Close: latestBar.Close,
            Volume: latestBar.Volume,
            Ema9: latestIndicators.Ema9,
            Sma20: latestIndicators.Sma20,
            Sma200: latestIndicators.Sma200,
            Vwap: latestIndicators.Vwap,
            VolumeAverage20: latestIndicators.VolumeAverage20,
            VolumeRatio: decimal.Round(volumeRatio, 4),
            MomentumPercent: decimal.Round(momentumPercent, 4),
            LongScore: longScore,
            ShortScore: shortScore,
            SelectedScore: selectedScore);
    }

    private PointsScannerSideScore ScoreSide(
        BacktestDataSet dataSet,
        BacktestBar latestBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot latestIndicators,
        SailorStrategyProfile profile,
        int barCount,
        decimal volumeRatio,
        decimal momentumPercent,
        string side,
        bool sideEnabled)
    {
        bool isShort = side.Equals("SHORT", StringComparison.OrdinalIgnoreCase);
        var factors = new List<PointsScannerFactor>();
        var legacyBlocks = new List<string>();

        if (!sideEnabled)
        {
            Add(factors, "SIDE_DISABLED", "Side disabled by profile", -100m, profile.SideMode.ToString(), "profile");
            legacyBlocks.Add($"{side} disabled by profile SideMode={profile.SideMode}");
        }

        ScoreDataAvailability(factors, legacyBlocks, barCount, profile);
        ScorePrice(factors, legacyBlocks, latestBar.Close, profile);
        ScoreVolume(factors, legacyBlocks, latestBar, latestIndicators, volumeRatio, profile);
        ScoreDirectionalTrend(factors, legacyBlocks, latestBar, latestIndicators, profile, isShort, momentumPercent);
        factors.AddRange(PointsScannerCommonStrategyScoring.Score(
            latestBar,
            previousBar,
            latestIndicators,
            profile,
            _settings,
            isShort,
            volumeRatio));

        decimal score = factors.Sum(factor => factor.Points);
        return new PointsScannerSideScore(
            Side: side.ToUpperInvariant(),
            Score: decimal.Round(score, 2),
            SideEnabled: sideEnabled,
            Factors: factors,
            LegacyBlockReasons: legacyBlocks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void ScoreDataAvailability(
        List<PointsScannerFactor> factors,
        List<string> legacyBlocks,
        int barCount,
        SailorStrategyProfile profile)
    {
        if (barCount >= profile.ScannerMinimumBars)
        {
            Add(factors, "BARS_FULL", "Enough bars for scanner profile", _settings.MinimumBarsFullPoints, $"bars={barCount} min={profile.ScannerMinimumBars}", "data");
        }
        else if (barCount >= 20)
        {
            Add(factors, "BARS_PARTIAL", "Partial but usable bar history", _settings.MinimumBarsPartialPoints, $"bars={barCount} min={profile.ScannerMinimumBars}", "data");
            legacyBlocks.Add($"bars {barCount} < ScannerMinimumBars {profile.ScannerMinimumBars}");
        }
        else
        {
            Add(factors, "BARS_CRITICAL", "Too few bars for normal scanner calculations", _settings.CriticalBarsPenalty, $"bars={barCount}", "data");
            legacyBlocks.Add($"bars {barCount} < ScannerMinimumBars {profile.ScannerMinimumBars}");
        }
    }

    private void ScorePrice(
        List<PointsScannerFactor> factors,
        List<string> legacyBlocks,
        decimal close,
        SailorStrategyProfile profile)
    {
        if (close < profile.MinimumPrice)
        {
            Add(factors, "PRICE_BELOW_MIN", "Close is below preferred minimum price", _settings.PriceBelowMinimumPenalty, $"close={close:F4} min={profile.MinimumPrice:F4}", "price");
            legacyBlocks.Add($"close {close:F4} < MinimumPrice {profile.MinimumPrice:F4}");
            return;
        }

        if (close > profile.MaximumPrice)
        {
            Add(factors, "PRICE_ABOVE_MAX", "Close is above preferred maximum price", _settings.PriceAboveMaximumPenalty, $"close={close:F4} max={profile.MaximumPrice:F4}", "price");
            legacyBlocks.Add($"close {close:F4} > MaximumPrice {profile.MaximumPrice:F4}");
            return;
        }

        Add(factors, "PRICE_RANGE", "Close is inside preferred price range", _settings.PriceRangePoints, $"close={close:F4}", "price");
    }

    private void ScoreVolume(
        List<PointsScannerFactor> factors,
        List<string> legacyBlocks,
        BacktestBar latestBar,
        BacktestIndicatorSnapshot latestIndicators,
        decimal volumeRatio,
        SailorStrategyProfile profile)
    {
        if (profile.MinimumVolume <= 0 || latestBar.Volume >= profile.MinimumVolume)
        {
            Add(factors, "VOLUME_OK", "Latest volume meets profile minimum", _settings.VolumeFullPoints, $"volume={latestBar.Volume} min={profile.MinimumVolume}", "volume");
        }
        else
        {
            decimal volumeShare = profile.MinimumVolume <= 0
                ? 1m
                : Math.Clamp((decimal)latestBar.Volume / profile.MinimumVolume, 0m, 1m);
            decimal penalty = _settings.VolumeMaximumPenalty * (1m - volumeShare);
            Add(factors, "VOLUME_LOW", "Latest volume is below profile minimum", penalty, $"volume={latestBar.Volume} min={profile.MinimumVolume}", "volume");
            legacyBlocks.Add($"volume {latestBar.Volume} < MinimumVolume {profile.MinimumVolume}");
        }

        if (!latestIndicators.VolumeAverage20.HasValue || latestIndicators.VolumeAverage20.Value <= 0m)
        {
            Add(factors, "VOL_AVG_MISSING", "VolumeAverage20 is missing", _settings.MissingVolumeAveragePenalty, "VolAvg20=n/a", "volume");
            if (profile.MinimumVolumeRatio > 0m)
            {
                legacyBlocks.Add("VolumeAverage20 missing while MinimumVolumeRatio is required");
            }
            return;
        }

        if (volumeRatio >= 2m)
        {
            Add(factors, "VOL_RATIO_STRONG", "Volume ratio is strong", _settings.VolumeRatioStrongPoints, $"ratio={volumeRatio:F2}", "volume");
        }
        else if (volumeRatio >= 1m)
        {
            decimal points = _settings.VolumeRatioBasePoints + ((volumeRatio - 1m) * 10m);
            Add(factors, "VOL_RATIO_OK", "Volume ratio is above average", points, $"ratio={volumeRatio:F2}", "volume");
        }
        else if (volumeRatio >= 0.5m)
        {
            decimal points = -5m + ((volumeRatio - 0.5m) * 20m);
            Add(factors, "VOL_RATIO_WEAK", "Volume ratio is weak but usable", points, $"ratio={volumeRatio:F2}", "volume");
        }
        else
        {
            Add(factors, "VOL_RATIO_LOW", "Volume ratio is very low", _settings.VolumeRatioWeakPenalty, $"ratio={volumeRatio:F2}", "volume");
        }

        if (profile.MinimumVolumeRatio > 0m && volumeRatio < profile.MinimumVolumeRatio)
        {
            legacyBlocks.Add($"volumeRatio {volumeRatio:F2} < MinimumVolumeRatio {profile.MinimumVolumeRatio:F2}");
        }
    }

    private void ScoreDirectionalTrend(
        List<PointsScannerFactor> factors,
        List<string> legacyBlocks,
        BacktestBar latestBar,
        BacktestIndicatorSnapshot latestIndicators,
        SailorStrategyProfile profile,
        bool isShort,
        decimal momentumPercent)
    {
        decimal directionalMomentum = isShort ? -momentumPercent : momentumPercent;
        decimal momentumPoints = Math.Clamp(directionalMomentum * _settings.MomentumWeight, -_settings.MaximumMomentumPoints, _settings.MaximumMomentumPoints);
        Add(factors, "MOMENTUM_LOOKBACK", isShort ? "Short lookback momentum" : "Long lookback momentum", momentumPoints, $"momentum={directionalMomentum:F2}%", "momentum");

        if (latestIndicators.Ema9.HasValue && latestIndicators.Sma20.HasValue)
        {
            bool trendOk = isShort
                ? latestIndicators.Ema9.Value < latestIndicators.Sma20.Value
                : latestIndicators.Ema9.Value > latestIndicators.Sma20.Value;
            Add(factors, trendOk ? "EMA_TREND_OK" : "EMA_TREND_ADVERSE", trendOk ? "EMA9/SMA20 trend supports side" : "EMA9/SMA20 trend is adverse", trendOk ? _settings.EmaTrendPoints : -_settings.EmaTrendPoints, $"EMA9={latestIndicators.Ema9.Value:F4} SMA20={latestIndicators.Sma20.Value:F4}", "trend");
            if (profile.RequireEma9AboveSma20 && !trendOk)
            {
                legacyBlocks.Add(isShort ? "EMA9 is not below SMA20 for SHORT" : "EMA9 is not above SMA20 for LONG");
            }
        }
        else
        {
            Add(factors, "EMA_MISSING", "EMA9/SMA20 trend data is missing", _settings.MissingEmaPenalty, "EMA9/SMA20=n/a", "trend");
            if (profile.RequireEma9AboveSma20)
            {
                legacyBlocks.Add("EMA9/SMA20 missing while EMA trend is required");
            }
        }

        if (latestIndicators.Vwap.HasValue)
        {
            bool vwapOk = isShort
                ? latestBar.Close < latestIndicators.Vwap.Value
                : latestBar.Close > latestIndicators.Vwap.Value;
            Add(factors, vwapOk ? "VWAP_SIDE_OK" : "VWAP_SIDE_ADVERSE", vwapOk ? "Close/VWAP position supports side" : "Close/VWAP position is adverse", vwapOk ? _settings.VwapPositionPoints : -_settings.VwapPositionPoints, $"close={latestBar.Close:F4} vwap={latestIndicators.Vwap.Value:F4}", "vwap");
            if (profile.RequirePriceAboveVwap && !vwapOk)
            {
                legacyBlocks.Add(isShort ? "close is not below VWAP for SHORT" : "close is not above VWAP for LONG");
            }
        }
        else
        {
            Add(factors, "VWAP_MISSING", "VWAP is missing", _settings.MissingVwapPenalty, "VWAP=n/a", "vwap");
            if (profile.RequirePriceAboveVwap)
            {
                legacyBlocks.Add("VWAP missing while VWAP filter is required");
            }
        }

        if (latestIndicators.Sma200.HasValue)
        {
            bool sma200Ok = isShort
                ? latestBar.Close < latestIndicators.Sma200.Value
                : latestBar.Close > latestIndicators.Sma200.Value;
            Add(factors, sma200Ok ? "SMA200_SIDE_OK" : "SMA200_SIDE_ADVERSE", sma200Ok ? "Close/SMA200 position supports side" : "Close/SMA200 position is adverse", sma200Ok ? _settings.Sma200Points : -_settings.Sma200Points, $"close={latestBar.Close:F4} sma200={latestIndicators.Sma200.Value:F4}", "trend");
            if (profile.RequirePriceAboveSma200WhenAvailable && !sma200Ok)
            {
                legacyBlocks.Add(isShort ? "close is not below SMA200 for SHORT" : "close is not above SMA200 for LONG");
            }
        }
        else
        {
            Add(factors, "SMA200_MISSING", "SMA200 is missing", _settings.MissingSma200Penalty, "SMA200=n/a", "trend");
        }
    }

    private PointsScannerStatus DetermineStatus(int barCount, decimal selectedScore, bool sideEnabled)
    {
        if (!sideEnabled || barCount < 20)
        {
            return PointsScannerStatus.NotReady;
        }

        if (selectedScore >= _settings.ReadyThreshold)
        {
            return PointsScannerStatus.Ready;
        }

        if (selectedScore >= _settings.WeakReadyThreshold)
        {
            return PointsScannerStatus.WeakReady;
        }

        return selectedScore >= _settings.WatchOnlyThreshold
            ? PointsScannerStatus.WatchOnly
            : PointsScannerStatus.WatchOnly;
    }

    private static PointsScannerSideScore SelectEnabledSide(
        PointsScannerSideScore longScore,
        PointsScannerSideScore shortScore,
        BacktestSideMode sideMode)
    {
        if (sideMode.AllowsLong() && !sideMode.AllowsShort())
        {
            return longScore;
        }

        if (sideMode.AllowsShort() && !sideMode.AllowsLong())
        {
            return shortScore;
        }

        if (!sideMode.AllowsLong() && !sideMode.AllowsShort())
        {
            return longScore.Score >= shortScore.Score ? longScore : shortScore;
        }

        return longScore.Score >= shortScore.Score ? longScore : shortScore;
    }

    private static decimal CalculateVolumeRatio(BacktestBar latestBar, BacktestIndicatorSnapshot latestIndicators)
    {
        if (!latestIndicators.VolumeAverage20.HasValue || latestIndicators.VolumeAverage20.Value <= 0m)
        {
            return 0m;
        }

        return latestBar.Volume / latestIndicators.VolumeAverage20.Value;
    }

    private static int StatusSort(PointsScannerStatus status)
        => status switch
        {
            PointsScannerStatus.Ready => 0,
            PointsScannerStatus.WeakReady => 1,
            PointsScannerStatus.WatchOnly => 2,
            PointsScannerStatus.NotReady => 3,
            _ => 4
        };

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
