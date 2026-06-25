namespace Sailor.App.Models;

public sealed record MarketBar(
    DateTimeOffset Time,
    string Symbol,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
