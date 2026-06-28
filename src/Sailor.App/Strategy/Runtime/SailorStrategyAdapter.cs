using Sailor.App.Backtest.Conduct;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Strategies;
using Sailor.App.Configuration;
using Sailor.App.MarketData.Snapshots;

namespace Sailor.App.Strategy.Runtime;

public sealed class SailorStrategyAdapter
{
    private readonly SailorAppSettings _settings;
    private readonly SailorStrategyProfile _profile;
    private readonly IBacktestStrategy _strategy;
    private readonly SailorConductExitEngine? _conductExitEngine;
    private readonly BacktestOptions _backtestOptions;
    private readonly Dictionary<string, SailorConductExitState> _conductStates = new(StringComparer.OrdinalIgnoreCase);

    public SailorStrategyAdapter(
        SailorAppSettings settings,
        SailorStrategyProfile profile,
        string symbol,
        string timeframe)
    {
        _settings = settings;
        _profile = profile;
        _strategy = CreateBacktestStrategy(settings, profile);
        _backtestOptions = BacktestOptions.CreateDefault(symbol, timeframe, profile.Name, settings);

        if (profile.UseConductExits)
        {
            _conductExitEngine = new SailorConductExitEngine(ResolveConductSettings(settings, profile), settings.L1L2);
        }
    }

    public string Name => _strategy.Name;

    public bool AllowsShortEntries => _strategy.AllowsShortEntries;

    public Task<SailorStrategyDecision> EvaluateAsync(
        SailorStrategyFrame frame,
        SailorStrategyPositionContext position,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BacktestBar? currentBar = frame.LatestBar;
        BacktestIndicatorSnapshot? indicators = frame.LatestIndicators;
        if (currentBar is null || indicators is null)
        {
            return Task.FromResult(SailorStrategyDecision.Hold(frame.Symbol, "Waiting for bar and indicator data."));
        }

        BacktestBar? previousBar = frame.Bars.Count >= 2 ? frame.Bars[^2] : null;

        if (position.HasOpenPosition && _conductExitEngine is not null)
        {
            SailorConductExitState conductState = GetOrCreateConductState(frame, position, currentBar, indicators);
            SailorConductExitDecision exitDecision = _conductExitEngine.EvaluateExit(
                position.PositionSide,
                currentBar,
                previousBar,
                indicators,
                _backtestOptions,
                _profile,
                conductState,
                indicators.BarIndex,
                frame.MarketSnapshot);

            if (exitDecision.ShouldExit)
            {
                SailorStrategyDecisionType type = position.PositionSide < 0
                    ? SailorStrategyDecisionType.ExitShort
                    : SailorStrategyDecisionType.ExitLong;

                return Task.FromResult(new SailorStrategyDecision(
                    type,
                    frame.Symbol,
                    position.AbsoluteQuantity,
                    Sailor.App.Broker.Orders.SailorOrderType.Market,
                    null,
                    exitDecision.Reason));
            }

            return Task.FromResult(SailorStrategyDecision.Hold(frame.Symbol, exitDecision.Reason));
        }

        if (!position.HasOpenPosition)
        {
            _conductStates.Remove(frame.Symbol);
        }

        if (_strategy is ISailorMarketSnapshotConsumer snapshotConsumer)
        {
            snapshotConsumer.UpdateMarketSnapshot(frame.MarketSnapshot);
        }

        BacktestSignal signal = _strategy.Evaluate(
            currentBar,
            previousBar,
            indicators,
            position.HasOpenPosition,
            position.PositionSide);

        SailorStrategyDecision decision = MapSignal(frame.Symbol, signal, position);
        return Task.FromResult(decision);
    }

    private SailorConductExitState GetOrCreateConductState(
        SailorStrategyFrame frame,
        SailorStrategyPositionContext position,
        BacktestBar currentBar,
        BacktestIndicatorSnapshot indicators)
    {
        if (_conductStates.TryGetValue(frame.Symbol, out SailorConductExitState? state) &&
            state.PositionSide == position.PositionSide &&
            state.Quantity == position.AbsoluteQuantity)
        {
            return state;
        }

        decimal entryPrice = position.AveragePrice > 0m ? position.AveragePrice : currentBar.Close;
        int entryBarIndex = position.EntryBarIndex >= 0
            ? position.EntryBarIndex
            : Math.Max(0, indicators.BarIndex);

        state = new SailorConductExitState(
            currentBar.Time,
            entryBarIndex,
            entryPrice,
            position.AbsoluteQuantity,
            position.PositionSide);

        _conductStates[frame.Symbol] = state;
        return state;
    }

    private SailorStrategyDecision MapSignal(
        string symbol,
        BacktestSignal signal,
        SailorStrategyPositionContext position)
    {
        if (signal.Type == BacktestSignalType.Hold)
        {
            return SailorStrategyDecision.Hold(symbol, signal.Reason);
        }

        if (signal.Type == BacktestSignalType.Buy)
        {
            if (position.PositionSide < 0)
            {
                return new SailorStrategyDecision(
                    SailorStrategyDecisionType.ExitShort,
                    symbol,
                    position.AbsoluteQuantity,
                    Sailor.App.Broker.Orders.SailorOrderType.Market,
                    null,
                    signal.Reason);
            }

            if (!position.HasOpenPosition)
            {
                return new SailorStrategyDecision(
                    SailorStrategyDecisionType.EnterLong,
                    symbol,
                    0,
                    Sailor.App.Broker.Orders.SailorOrderType.Market,
                    null,
                    signal.Reason);
            }
        }

        if (signal.Type == BacktestSignalType.Sell)
        {
            if (position.PositionSide > 0)
            {
                return new SailorStrategyDecision(
                    SailorStrategyDecisionType.ExitLong,
                    symbol,
                    position.AbsoluteQuantity,
                    Sailor.App.Broker.Orders.SailorOrderType.Market,
                    null,
                    signal.Reason);
            }

            if (!position.HasOpenPosition && AllowsShortEntries)
            {
                return new SailorStrategyDecision(
                    SailorStrategyDecisionType.EnterShort,
                    symbol,
                    0,
                    Sailor.App.Broker.Orders.SailorOrderType.Market,
                    null,
                    signal.Reason);
            }
        }

        return SailorStrategyDecision.Hold(symbol, $"Signal {signal.Type} ignored for current position side {position.PositionSide}: {signal.Reason}");
    }

    private static IBacktestStrategy CreateBacktestStrategy(
        SailorAppSettings settings,
        SailorStrategyProfile profile)
    {
        if (profile.Name.Equals("simple-momentum", StringComparison.OrdinalIgnoreCase) ||
            profile.Name.Equals("simple", StringComparison.OrdinalIgnoreCase))
        {
            return new SimpleMomentumBacktestStrategy(profile);
        }

        if (profile.UseConductExits)
        {
            return new SailorConductBacktestStrategy(profile, settings.L1L2);
        }

        return new SailorTrendVolumeBacktestStrategy(profile, settings.L1L2);
    }

    private static ConductExitSettings ResolveConductSettings(
        SailorAppSettings settings,
        SailorStrategyProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ConductProfileName) &&
            settings.ConductProfiles.TryGetValue(profile.ConductProfileName, out ConductExitSettings? namedByConductProfile))
        {
            return namedByConductProfile;
        }

        if (settings.ConductProfiles.TryGetValue(profile.Name, out ConductExitSettings? namedByProfile))
        {
            return namedByProfile;
        }

        return settings.Conduct;
    }
}
