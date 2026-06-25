using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Strategies;

public sealed class SimpleMomentumBacktestStrategy : IBacktestStrategy
{
    public string Name => "SimpleMomentum";

    public BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar? previousBar,
        bool hasOpenPosition)
    {
        if (previousBar is null)
        {
            return BacktestSignal.Hold("Waiting for previous bar.");
        }

        decimal buyThreshold = previousBar.Close * 1.002m;
        decimal sellThreshold = previousBar.Close * 0.998m;

        if (!hasOpenPosition && currentBar.Close >= buyThreshold)
        {
            return BacktestSignal.Buy(
                $"Momentum up: close {currentBar.Close:F2} >= {buyThreshold:F2}");
        }

        if (hasOpenPosition && currentBar.Close <= sellThreshold)
        {
            return BacktestSignal.Sell(
                $"Momentum down: close {currentBar.Close:F2} <= {sellThreshold:F2}");
        }

        return BacktestSignal.Hold("No action.");
    }
}
