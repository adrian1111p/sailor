using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Scanner;

namespace Sailor.App.Backtest.Runner;

public sealed record BacktestRankingRow(
    int ScannerRank,
    ScannerCandidate Candidate,
    BacktestRunResult Result)
{
    public decimal RankingScore =>
        Result.TotalPnl * 10m +
        Result.WinRatePercent +
        Candidate.Score +
        Math.Min(Candidate.VolumeRatio, 5m) * 2m -
        Math.Max(0, Result.Losers - Result.Winners) * 5m;

    public string ToMarkdownRow(int rank)
    {
        return $"| {rank} | {Candidate.Symbol} | {Candidate.Side} | {ScannerRank} | {Result.TotalPnl:F2} | {Result.TotalTrades} | {Result.WinRatePercent:F2}% | {Result.Winners} | {Result.Losers} | {Candidate.Score:F2} | {Candidate.MomentumPercent:F2}% | {Candidate.VolumeRatio:F2} | {Candidate.Close:F2} |";
    }
}
