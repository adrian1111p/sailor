using Harvester.App.Strategy;

namespace Sailor.App.Backtest.Strategies.Default;

internal static class DefaultStrategyModule
{
    internal const string StrategyBucket = "CONDUCT_V3";
    internal const string SetupPrefix = "CONDUCT_V3[BT]";

    internal static ConductStrategyV3 Create(StrategyConfig? config = null)
        => new(config);

    internal static ConductStrategyV3 CreateConfigured(
        StrategyConfig? config,
        string? symbol,
        SelfLearningSignalAdapter? selfLearning = null)
    {
        var strategy = new ConductStrategyV3(config);
        strategy.Symbol = NormalizeSymbol(symbol);
        strategy.SelfLearning = selfLearning ?? BacktestStrategyBase.LoadSharedSelfLearning();
        return strategy;
    }

    private static string? NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol)
            ? null
            : symbol.Trim().ToUpperInvariant();
}
