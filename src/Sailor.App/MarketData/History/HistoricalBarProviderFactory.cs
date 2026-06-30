using Sailor.App.Broker.Ibkr;
#if SAILOR_IBAPI
using Sailor.App.Broker.Ibkr.Shared;
#endif

namespace Sailor.App.MarketData.History;

public static class HistoricalBarProviderFactory
{
    public static IHistoricalBarProvider Create(
        bool requestIbkr,
        IbkrConnectionOptions connectionOptions)
    {
        var fallback = new LocalCsvHistoricalBarProvider();

        if (!requestIbkr)
        {
            return fallback;
        }

#if SAILOR_IBAPI
        return new IbkrSharedMarketDataHistoryProvider(connectionOptions);
#else
        return new DisabledIbkrHistoricalBarProvider(fallback);
#endif
    }
}
