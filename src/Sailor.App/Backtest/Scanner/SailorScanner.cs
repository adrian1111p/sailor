using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Scanner;

public sealed class SailorScanner
{
    private readonly CsvBacktestDataProvider _dataProvider;

    public SailorScanner(CsvBacktestDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public IReadOnlyList<ScannerCandidate> Scan(
        string timeframe,
        SailorStrategyProfile profile,
        int? topCount = null,
        IEnumerable<string>? symbols = null)
    {
        string normalizedTimeframe = string.IsNullOrWhiteSpace(timeframe)
            ? "1m"
            : timeframe.Trim();

        int effectiveTopCount = topCount.GetValueOrDefault(profile.ScannerTopCount);
        var candidates = new List<ScannerCandidate>();

        IEnumerable<string> symbolsToScan = symbols ?? _dataProvider.ListSymbols();

        foreach (string symbol in symbolsToScan
                     .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                     .Select(symbol => symbol.Trim().ToUpperInvariant())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase))
        {
            ScannerCandidate? candidate = TryCreateCandidate(symbol, normalizedTimeframe, profile);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, effectiveTopCount))
            .ToArray();
    }

    private ScannerCandidate? TryCreateCandidate(
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
        if (bars.Count < profile.ScannerMinimumBars)
        {
            return null;
        }

        IReadOnlyList<BacktestIndicatorSnapshot> indicators = TechnicalIndicatorCalculator.Calculate(bars);
        BacktestBar latestBar = bars[^1];
        BacktestIndicatorSnapshot latestIndicators = indicators[^1];

        if (latestBar.Close < profile.MinimumPrice || latestBar.Close > profile.MaximumPrice)
        {
            return null;
        }

        if (latestBar.Volume < profile.MinimumVolume)
        {
            return null;
        }

        decimal volumeRatio = 0m;
        if (latestIndicators.VolumeAverage20.HasValue && latestIndicators.VolumeAverage20.Value > 0m)
        {
            volumeRatio = latestBar.Volume / latestIndicators.VolumeAverage20.Value;
        }

        if (profile.MinimumVolumeRatio > 0m && volumeRatio < profile.MinimumVolumeRatio)
        {
            return null;
        }

        int lookbackIndex = Math.Max(0, bars.Count - 1 - profile.ScannerLookbackBars);
        decimal lookbackClose = bars[lookbackIndex].Close;
        decimal momentumPercent = lookbackClose > 0m
            ? (latestBar.Close - lookbackClose) / lookbackClose * 100m
            : 0m;

        ScannerCandidate? longCandidate = profile.SideMode.AllowsLong()
            ? TryCreateDirectionalCandidate(dataSet, latestBar, latestIndicators, profile, volumeRatio, momentumPercent, "LONG")
            : null;

        ScannerCandidate? shortCandidate = profile.SideMode.AllowsShort()
            ? TryCreateDirectionalCandidate(dataSet, latestBar, latestIndicators, profile, volumeRatio, momentumPercent, "SHORT")
            : null;

        return new[] { longCandidate, shortCandidate }
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
    }

    private static ScannerCandidate? TryCreateDirectionalCandidate(
        BacktestDataSet dataSet,
        BacktestBar latestBar,
        BacktestIndicatorSnapshot latestIndicators,
        SailorStrategyProfile profile,
        decimal volumeRatio,
        decimal momentumPercent,
        string side)
    {
        bool isShort = side.Equals("SHORT", StringComparison.OrdinalIgnoreCase);

        if (profile.RequireEma9AboveSma20)
        {
            if (!latestIndicators.Ema9.HasValue || !latestIndicators.Sma20.HasValue)
            {
                return null;
            }

            bool trendOk = isShort
                ? latestIndicators.Ema9.Value < latestIndicators.Sma20.Value
                : latestIndicators.Ema9.Value > latestIndicators.Sma20.Value;

            if (!trendOk)
            {
                return null;
            }
        }

        if (profile.RequirePriceAboveVwap)
        {
            if (!latestIndicators.Vwap.HasValue)
            {
                return null;
            }

            bool vwapOk = isShort
                ? latestBar.Close < latestIndicators.Vwap.Value
                : latestBar.Close > latestIndicators.Vwap.Value;

            if (!vwapOk)
            {
                return null;
            }
        }

        if (profile.RequirePriceAboveSma200WhenAvailable && latestIndicators.Sma200.HasValue)
        {
            bool sma200Ok = isShort
                ? latestBar.Close < latestIndicators.Sma200.Value
                : latestBar.Close > latestIndicators.Sma200.Value;

            if (!sma200Ok)
            {
                return null;
            }
        }

        decimal emaSpreadPercent = CalculatePercentSpread(latestIndicators.Ema9, latestIndicators.Sma20);
        decimal vwapSpreadPercent = CalculatePercentSpread(latestBar.Close, latestIndicators.Vwap);
        decimal sma200SpreadPercent = CalculatePercentSpread(latestBar.Close, latestIndicators.Sma200);

        decimal directionalMomentum = isShort ? -momentumPercent : momentumPercent;
        decimal directionalEmaSpread = isShort ? -emaSpreadPercent : emaSpreadPercent;
        decimal directionalVwapSpread = isShort ? -vwapSpreadPercent : vwapSpreadPercent;
        decimal directionalSma200Spread = isShort ? -sma200SpreadPercent : sma200SpreadPercent;

        decimal score =
            directionalMomentum * 2.0m +
            directionalEmaSpread * 2.0m +
            directionalVwapSpread * 1.5m +
            Math.Min(volumeRatio, 5m) * 10m +
            Math.Max(0m, directionalSma200Spread) * 0.5m;

        if (score <= 0m)
        {
            return null;
        }

        string reason = BuildReason(latestBar, latestIndicators, volumeRatio, side);

        return new ScannerCandidate(
            Symbol: dataSet.Symbol,
            Timeframe: dataSet.Timeframe,
            Side: side.ToUpperInvariant(),
            Close: latestBar.Close,
            Volume: latestBar.Volume,
            Ema9: latestIndicators.Ema9,
            Sma20: latestIndicators.Sma20,
            Sma200: latestIndicators.Sma200,
            Vwap: latestIndicators.Vwap,
            VolumeAverage20: latestIndicators.VolumeAverage20,
            VolumeRatio: decimal.Round(volumeRatio, 2),
            MomentumPercent: decimal.Round(momentumPercent, 2),
            Score: decimal.Round(score, 2),
            Reason: reason);
    }

    private static decimal CalculatePercentSpread(decimal? left, decimal? right)
    {
        if (!left.HasValue || !right.HasValue || right.Value == 0m)
        {
            return 0m;
        }

        return (left.Value - right.Value) / right.Value * 100m;
    }

    private static decimal CalculatePercentSpread(decimal left, decimal? right)
    {
        if (!right.HasValue || right.Value == 0m)
        {
            return 0m;
        }

        return (left - right.Value) / right.Value * 100m;
    }

    private static string BuildReason(
        BacktestBar latestBar,
        BacktestIndicatorSnapshot latestIndicators,
        decimal volumeRatio,
        string side)
    {
        var parts = new List<string> { side.ToUpperInvariant() };

        if (latestIndicators.Ema9.HasValue && latestIndicators.Sma20.HasValue)
        {
            parts.Add(latestIndicators.Ema9.Value > latestIndicators.Sma20.Value
                ? "EMA9>SMA20"
                : "EMA9<SMA20");
        }

        if (latestIndicators.Vwap.HasValue)
        {
            parts.Add(latestBar.Close > latestIndicators.Vwap.Value
                ? "Close>VWAP"
                : "Close<VWAP");
        }

        if (latestIndicators.Sma200.HasValue)
        {
            parts.Add(latestBar.Close > latestIndicators.Sma200.Value
                ? "Close>SMA200"
                : "Close<SMA200");
        }
        else
        {
            parts.Add("SMA200=n/a");
        }

        parts.Add($"VolRatio={volumeRatio:F2}");

        return string.Join(", ", parts);
    }
}
