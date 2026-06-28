namespace Sailor.App.MarketData.Snapshots;

public interface ISailorMarketSnapshotConsumer
{
    void UpdateMarketSnapshot(SailorMarketSnapshot? snapshot);
}
