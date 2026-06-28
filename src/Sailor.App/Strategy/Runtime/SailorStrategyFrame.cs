using Sailor.App.Backtest.Models;
using Sailor.App.MarketData.Snapshots;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Strategy.Runtime;

public sealed record SailorStrategyFrame(
    SailorRuntimeMode Mode,
    DateTimeOffset Time,
    string Symbol,
    string Timeframe,
    IReadOnlyList<BacktestBar> Bars,
    IReadOnlyList<BacktestIndicatorSnapshot> Indicators,
    SailorMarketSnapshot? MarketSnapshot,
    SailorRuntimeState RuntimeState)
{
    public BacktestBar? LatestBar => Bars.Count == 0 ? null : Bars[^1];

    public BacktestIndicatorSnapshot? LatestIndicators => Indicators.Count == 0 ? null : Indicators[^1];

    public bool HasL1 => MarketSnapshot?.HasL1 == true;

    public bool HasL2 => MarketSnapshot?.HasL2 == true;
}
