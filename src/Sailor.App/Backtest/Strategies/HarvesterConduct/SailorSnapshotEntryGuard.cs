using Sailor.App.Configuration;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct;

public sealed record SailorSnapshotGuardDecision(
    bool Passed,
    string Reason)
{
    public static SailorSnapshotGuardDecision Accept(string reason) => new(true, reason);

    public static SailorSnapshotGuardDecision Reject(string reason) => new(false, reason);
}

public static class SailorSnapshotEntryGuard
{
    public static SailorSnapshotGuardDecision Evaluate(
        string profileName,
        int intendedSide,
        SailorMarketSnapshot? snapshot,
        L1L2SnapshotSettings settings)
    {
        if (!settings.EnableEntryGuards)
        {
            return SailorSnapshotGuardDecision.Accept("L1/L2 guard disabled.");
        }

        if (!settings.IsProfileSuitable(profileName))
        {
            return SailorSnapshotGuardDecision.Accept($"L1/L2 guard skipped: profile {profileName} is not marked snapshot-suitable.");
        }

        if (snapshot is null)
        {
            return settings.RequireSnapshotForEntry
                ? SailorSnapshotGuardDecision.Reject("L1/L2 guard rejected: no market snapshot available.")
                : SailorSnapshotGuardDecision.Accept("L1/L2 guard advisory: no market snapshot available.");
        }

        string prefix = snapshot.Quality == SailorMarketSnapshotQuality.SyntheticBacktest && settings.SyntheticSnapshotsAreAdvisoryOnly
            ? "L1/L2 synthetic advisory"
            : "L1/L2 guard";

        if (snapshot.Quality == SailorMarketSnapshotQuality.SyntheticBacktest && settings.SyntheticSnapshotsAreAdvisoryOnly)
        {
            return SailorSnapshotGuardDecision.Accept(
                $"{prefix}: {snapshot.ToCompactString()}.");
        }

        if (snapshot.L1 is not null && snapshot.L1.SpreadBps > settings.MaxSpreadBps)
        {
            return SailorSnapshotGuardDecision.Reject(
                $"{prefix} rejected: spread {snapshot.L1.SpreadBps:F1}bps > max {settings.MaxSpreadBps:F1}bps.");
        }

        if (snapshot.LiquidityScore < settings.MinimumLiquidityScore)
        {
            return SailorSnapshotGuardDecision.Reject(
                $"{prefix} rejected: liquidity score {snapshot.LiquidityScore:F1} < minimum {settings.MinimumLiquidityScore:F1}.");
        }

        decimal bookImbalance = snapshot.BookImbalance;

        if (intendedSide > 0 && bookImbalance < -settings.MinimumBookImbalanceForMomentum)
        {
            return SailorSnapshotGuardDecision.Reject(
                $"{prefix} rejected: long setup has adverse book imbalance {bookImbalance:F2}.");
        }

        if (intendedSide < 0 && bookImbalance > settings.MinimumBookImbalanceForMomentum)
        {
            return SailorSnapshotGuardDecision.Reject(
                $"{prefix} rejected: short setup has adverse book imbalance {bookImbalance:F2}.");
        }

        return SailorSnapshotGuardDecision.Accept(
            $"{prefix} passed: {snapshot.ToCompactString()}.");
    }
}
