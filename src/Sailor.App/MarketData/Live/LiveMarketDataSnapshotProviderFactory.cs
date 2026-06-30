using Sailor.App.Broker.Ibkr;
#if SAILOR_IBAPI
using Sailor.App.Broker.Ibkr.Shared;
#endif

namespace Sailor.App.MarketData.Live;

public static class LiveMarketDataSnapshotProviderFactory
{
    public static ILiveMarketDataSnapshotProvider Create(
        bool requestIbkr,
        IbkrConnectionOptions connectionOptions)
    {
        var fallback = new LocalCachedMarketDataSnapshotProvider();
        if (!requestIbkr)
        {
            return fallback;
        }

#if SAILOR_IBAPI
        return new IbkrSharedMarketDataHistoryProvider(connectionOptions);
#else
        return new DisabledIbkrMarketDataSnapshotProvider(fallback);
#endif
    }
}
