#if SAILOR_IBAPI
using IBApi;
using Sailor.App.Broker.Orders;

namespace Sailor.App.Broker.Ibkr.Orders;

public static class IbkrOrderTranslator
{
    public static Contract BuildStockContract(string symbol, string primaryExchange)
        => new()
        {
            Symbol = symbol.Trim().ToUpperInvariant(),
            SecType = "STK",
            Exchange = "SMART",
            PrimaryExch = string.IsNullOrWhiteSpace(primaryExchange) ? "NASDAQ" : primaryExchange.Trim().ToUpperInvariant(),
            Currency = "USD"
        };

    public static Order BuildOrder(SailorOrderIntent intent)
    {
        if (intent.Quantity <= 0)
        {
            throw new ArgumentException("Order quantity must be > 0 for SAILOR-028 manual paper orders.", nameof(intent));
        }

        string action = intent.Side switch
        {
            SailorOrderSide.Buy => "BUY",
            SailorOrderSide.BuyToCover => "BUY",
            SailorOrderSide.Sell => "SELL",
            SailorOrderSide.SellShort => "SELL",
            _ => throw new ArgumentException($"Unsupported SAILOR-028 order side: {intent.Side}.")
        };

        string orderType = intent.OrderType switch
        {
            SailorOrderType.Market => "MKT",
            SailorOrderType.Limit => "LMT",
            SailorOrderType.Stop => "STP",
            SailorOrderType.StopLimit => "STP LMT",
            _ => throw new ArgumentException($"Unsupported SAILOR-028 order type: {intent.OrderType}.")
        };

        if (intent.OrderType is SailorOrderType.Limit or SailorOrderType.StopLimit && (!intent.LimitPrice.HasValue || intent.LimitPrice <= 0m))
        {
            throw new ArgumentException("Limit price is required and must be > 0 for LMT/STP LMT orders.", nameof(intent));
        }

        var order = new Order
        {
            Action = action,
            OrderType = orderType,
            TotalQuantity = intent.Quantity,
            Tif = intent.NormalizedTimeInForce,
            Transmit = true,
            OrderRef = intent.NormalizedIntentId
        };

        if (intent.LimitPrice.HasValue)
        {
            order.LmtPrice = Convert.ToDouble(intent.LimitPrice.Value);
        }

        if (!string.IsNullOrWhiteSpace(intent.Account))
        {
            order.Account = intent.Account.Trim();
        }

        return order;
    }
}
#endif
