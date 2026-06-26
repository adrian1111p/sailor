using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Strategies.HarvesterConduct;

public interface ISailorConductEntryStrategy
{
    string ProfileName { get; }

    string StrategyName { get; }

    string VariantName { get; }

    BacktestSignal EvaluateEntry(
        BacktestBar currentBar,
        BacktestBar previousBar,
        BacktestIndicatorSnapshot indicators,
        IReadOnlyList<BacktestBar> recentBars,
        SailorStrategyProfile profile);
}
