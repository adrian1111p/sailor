using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Strategies;

public interface IBacktestStrategy
{
    string Name { get; }

    BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar? previousBar,
        bool hasOpenPosition);
}
