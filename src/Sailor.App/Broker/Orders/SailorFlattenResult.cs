namespace Sailor.App.Broker.Orders;

public sealed record SailorFlattenResult(
    string Symbol,
    bool Success,
    string Message,
    DateTimeOffset Time,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Warnings);
