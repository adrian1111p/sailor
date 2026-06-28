using Sailor.App.Broker.Orders;
using Sailor.App.Runtime.Common;

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

        if (connectionOptions.Mode == SailorRuntimeMode.Live && !connectionOptions.SendOrders)
        {
            return new DisabledBrokerOrderRouter("Live order submission was requested, but the live connection options are not marked send-orders. The live-readiness gate must pass first.");
        }

#if SAILOR_IBAPI
        return new IbkrPaperOrderRouter(connectionOptions, primaryExchange, waitSeconds);
#else
        string target = connectionOptions.Mode == SailorRuntimeMode.Live ? "live" : "paper";
        return new DisabledBrokerOrderRouter($"IBKR {target} order router requires the optional IBApi build. Re-run with -p:EnableIbkrApi=true, or use dry-run mode.");
#endif
    }
}
