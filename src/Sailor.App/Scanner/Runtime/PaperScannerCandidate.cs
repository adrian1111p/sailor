using Sailor.App.Backtest.Scanner;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Scanner.Runtime;

public sealed record PaperScannerCandidate(
    int Rank,
    ScannerCandidate Candidate,
    SailorMarketSnapshot? Snapshot,
    string SnapshotMessage,
    IReadOnlyList<string> SnapshotWarnings)
{
    public string Symbol => Candidate.Symbol;

    public bool HasL1 => Snapshot?.HasL1 == true;

    public bool HasL2 => Snapshot?.HasL2 == true;

    public decimal SpreadBps => Snapshot?.SpreadBps ?? 0m;

    public decimal BookImbalance => Snapshot?.BookImbalance ?? 0m;

    public decimal LiquidityScore => Snapshot?.LiquidityScore ?? 0m;

    public string ToDisplayLine()
    {
        string l1 = HasL1 ? $"L1 spread={SpreadBps:F1}bps" : "L1=n/a";
        string l2 = HasL2 ? $"L2 imb={BookImbalance:F2}" : "L2=n/a";
        return Candidate.ToDisplayLine(Rank) + $" | {l1} | {l2} | liq={LiquidityScore:F1}";
    }
}
