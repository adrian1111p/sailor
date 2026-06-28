namespace Sailor.App.Strategy.Runtime;

public interface ISailorRuntimeStrategy
{
    string Name { get; }

    Task<SailorStrategyDecision> EvaluateAsync(
        SailorStrategyFrame frame,
        CancellationToken cancellationToken = default);
}
