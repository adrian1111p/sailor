using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Strategies.HarvesterConduct;

namespace Sailor.App.Backtest.Strategies;

public sealed class SailorConductBacktestStrategy : IBacktestStrategy
{
    private readonly SailorStrategyProfile _profile;
    private readonly ISailorConductEntryStrategy _entryStrategy;
    private readonly List<BacktestBar> _recentBars = [];

    public SailorConductBacktestStrategy(SailorStrategyProfile profile)
    {
        _profile = profile;
        _entryStrategy = SailorConductStrategyRegistry.CreateEntryStrategy(profile);
    }

    public string Name => _entryStrategy.StrategyName;

    public BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        bool hasOpenPosition)
    {
        Remember(currentBar);

        if (previousBar is null)
        {
            return BacktestSignal.Hold($"{_entryStrategy.StrategyName}: waiting for previous bar.");
        }

        if (_entryStrategy is ISailorConductPositionStrategy positionStrategy)
        {
            return positionStrategy.Evaluate(
                currentBar,
                previousBar,
                indicators,
                _recentBars,
                _profile,
                hasOpenPosition);
        }

        if (hasOpenPosition)
        {
            return BacktestSignal.Hold(
                $"{_entryStrategy.StrategyName}: open position is managed by Sailor conduct exit engine.");
        }

        return _entryStrategy.EvaluateEntry(
            currentBar,
            previousBar,
            indicators,
            _recentBars,
            _profile);
    }

    private void Remember(BacktestBar bar)
    {
        _recentBars.Add(bar);

        if (_recentBars.Count > 500)
        {
            _recentBars.RemoveAt(0);
        }
    }
}
