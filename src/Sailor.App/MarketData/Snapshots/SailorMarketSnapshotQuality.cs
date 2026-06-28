namespace Sailor.App.MarketData.Snapshots;

public enum SailorMarketSnapshotQuality
{
    Unknown = 0,
    SyntheticBacktest = 1,
    Delayed = 2,
    Live = 3
}
