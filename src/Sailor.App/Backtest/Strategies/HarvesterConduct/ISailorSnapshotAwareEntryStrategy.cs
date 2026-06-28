using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Configuration;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct;

public interface ISailorSnapshotAwareEntryStrategy : ISailorConductEntryStrategy
{
    BacktestSignal EvaluateEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile,
        SailorMarketSnapshot? snapshot,
        L1L2SnapshotSettings snapshotSettings);
}
