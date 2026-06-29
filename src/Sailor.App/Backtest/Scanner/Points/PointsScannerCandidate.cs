using Sailor.App.Backtest.Scanner;
using System.Globalization;

namespace Sailor.App.Backtest.Scanner.Points;

public sealed record PointsScannerCandidate(
    string Symbol,
    string Timeframe,
    PointsScannerStatus Status,
    string SelectedSide,
    decimal Close,
    long Volume,
    decimal? Ema9,
    decimal? Sma20,
    decimal? Sma200,
    decimal? Vwap,
    decimal? VolumeAverage20,
    decimal VolumeRatio,
    decimal MomentumPercent,
    PointsScannerSideScore LongScore,
    PointsScannerSideScore ShortScore,
    PointsScannerSideScore SelectedScore)
{
    public decimal FinalScore => SelectedScore.Score;

    public decimal PositivePoints => SelectedScore.PositivePoints;

    public decimal NegativePoints => SelectedScore.NegativePoints;

    public IReadOnlyList<string> LegacyBlockReasons => SelectedScore.LegacyBlockReasons;

    public ScannerCandidate ToScannerCandidate()
        => new(
            Symbol: Symbol,
            Timeframe: Timeframe,
            Side: SelectedSide,
            Close: Close,
            Volume: Volume,
            Ema9: Ema9,
            Sma20: Sma20,
            Sma200: Sma200,
            Vwap: Vwap,
            VolumeAverage20: VolumeAverage20,
            VolumeRatio: decimal.Round(VolumeRatio, 2),
            MomentumPercent: decimal.Round(MomentumPercent, 2),
            Score: decimal.Round(FinalScore, 2),
            Reason: ToReasonString());

    public string ToReasonString()
    {
        string legacyBlocks = LegacyBlockReasons.Count == 0
            ? "legacyBlocks=none"
            : $"legacyBlocks={string.Join(" | ", LegacyBlockReasons)}";

        return $"POINTS status={Status.ToDisplayName()} long={LongScore.Score.ToString("0.##", CultureInfo.InvariantCulture)} " +
               $"short={ShortScore.Score.ToString("0.##", CultureInfo.InvariantCulture)} " +
               $"positive={PositivePoints.ToString("0.##", CultureInfo.InvariantCulture)} " +
               $"negative={NegativePoints.ToString("0.##", CultureInfo.InvariantCulture)}; " +
               $"+ {SelectedScore.TopPositiveFactors(4)}; - {SelectedScore.TopNegativeFactors(4)}; {legacyBlocks}";
    }
}
