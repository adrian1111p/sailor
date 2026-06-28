using Sailor.App.Broker.Orders;

namespace Sailor.App.Broker.Ibkr.Orders;

public static class IbkrOrderRouterFactory
{
    public static IOrderRouter Create(
        bool sendOrders,
        IbkrConnectionOptions connectionOptions,
        string primaryExchange,
        int waitSeconds)
    {
        if (!sendOrders)
        {
            return new DryRunOrderRouter();
        }

        if (connectionOptions.Mode == Sailor.App.Runtime.Common.SailorRuntimeMode.Live)
        {
            return new DisabledBrokerOrderRouter("SAILOR-028 blocks live order submission. Use paper mode only.");
        }

#if SAILOR_IBAPI
        return new IbkrPaperOrderRouter(connectionOptions, primaryExchange, waitSeconds);
#else
        return new DisabledBrokerOrderRouter("IBKR paper order router requires the optional IBApi build. Re-run with -p:EnableIbkrApi=true, or use dry-run mode.");
#endif
    }
}
