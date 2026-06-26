using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct;

public interface ISailorConductPositionStrategy : ISailorConductEntryStrategy
{
    bool AllowsShortEntries { get; }

    BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile,
        bool hasOpenPosition);
}
