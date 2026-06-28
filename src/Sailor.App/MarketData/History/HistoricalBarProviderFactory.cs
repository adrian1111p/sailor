using Sailor.App.Broker.Ibkr;

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
        return new Sailor.App.Broker.Ibkr.History.IbkrApiHistoricalBarProvider(connectionOptions);
#else
        return new DisabledIbkrHistoricalBarProvider(fallback);
#endif
    }
}
