using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Strategies;

public interface IBacktestStrategy
{
    string Name { get; }

    bool AllowsShortEntries => false;

    BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        bool hasOpenPosition,
        int positionSide);
}
