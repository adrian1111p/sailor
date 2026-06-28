using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Strategies.HarvesterConduct;
using Sailor.App.Configuration;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Backtest.Strategies;

public sealed class SailorConductBacktestStrategy : IBacktestStrategy, ISailorMarketSnapshotConsumer
{
    private readonly SailorStrategyProfile _profile;
    private readonly ISailorConductEntryStrategy _entryStrategy;
    private readonly List<BacktestBar> _recentBars = [];
    private readonly L1L2SnapshotSettings _snapshotSettings;
    private SailorMarketSnapshot? _currentSnapshot;

    public SailorConductBacktestStrategy(
        SailorStrategyProfile profile,
        L1L2SnapshotSettings? snapshotSettings = null)
    {
        _profile = profile;
        _snapshotSettings = snapshotSettings ?? new L1L2SnapshotSettings();
        _entryStrategy = SailorConductStrategyRegistry.CreateEntryStrategy(profile);
    }

    public void UpdateMarketSnapshot(SailorMarketSnapshot? snapshot)
    {
        _currentSnapshot = snapshot;
    }

    public string Name => _entryStrategy.StrategyName;

    public bool AllowsShortEntries => _profile.SideMode.AllowsShort();

    public BacktestSignal Evaluate(
        BacktestBar currentBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicators,
        bool hasOpenPosition,
        int positionSide)
    {
        Remember(currentBar);

        if (previousBar is null)
        {
            return BacktestSignal.Hold($"{_entryStrategy.StrategyName}: waiting for previous bar.");
        }

        if (_entryStrategy is ISailorSnapshotAwarePositionStrategy snapshotAwarePositionStrategy)
        {
            return snapshotAwarePositionStrategy.Evaluate(
                currentBar,
                previousBar,
                indicators,
                _recentBars,
                _profile,
                hasOpenPosition,
                positionSide,
                _currentSnapshot,
                _snapshotSettings);
        }

        if (_entryStrategy is ISailorConductPositionStrategy positionStrategy)
        {
            return positionStrategy.Evaluate(
                currentBar,
                previousBar,
                indicators,
                _recentBars,
                _profile,
                hasOpenPosition,
                positionSide);
        }

        if (hasOpenPosition)
        {
            return BacktestSignal.Hold(
                $"{_entryStrategy.StrategyName}: open position is managed by Sailor conduct exit engine.");
        }

        if (_entryStrategy is ISailorSnapshotAwareEntryStrategy snapshotAwareEntryStrategy)
        {
            return snapshotAwareEntryStrategy.EvaluateEntry(
                currentBar,
                previousBar,
                indicators,
                _recentBars,
                _profile,
                _currentSnapshot,
                _snapshotSettings);
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

        if (_recentBars.Count > 2500)
        {
            _recentBars.RemoveAt(0);
        }
    }
}
