using Sailor.App.Models;
using Sailor.App.Services;
using Sailor.App.Strategies;

namespace Sailor.App.Engine;

public sealed class TradingEngine
{
    private readonly SampleMarketDataProvider _marketDataProvider;
    private readonly SimpleMomentumStrategy _strategy;

    public TradingEngine(
        SampleMarketDataProvider marketDataProvider,
        SimpleMomentumStrategy strategy)
    {
        _marketDataProvider = marketDataProvider;
        _strategy = strategy;
    }

    public async Task RunAsync(string symbol)
    {
        decimal cash = 10_000.00m;
        int orderQuantity = 10;
        Position? position = null;

        await foreach (MarketBar bar in _marketDataProvider.GetBarsAsync(symbol))
        {
            TradeSignal signal = _strategy.Evaluate(bar);

            Console.WriteLine(
                $"{bar.Time:HH:mm:ss} | {bar.Symbol} | Close={bar.Close,8:C2} | Volume={bar.Volume,7} | Signal={signal.Type}");

            switch (signal.Type)
            {
                case SignalType.Buy:
                    if (position is null)
                    {
                        decimal cost = orderQuantity * bar.Close;

                        if (cash >= cost)
                        {
                            cash -= cost;
                            position = new Position(bar.Symbol, orderQuantity, bar.Close, bar.Time);

                            Console.WriteLine(
                                $"  BUY  {orderQuantity} shares @ {bar.Close:C2} | Cash={cash:C2} | Reason={signal.Reason}");
                        }
                        else
                        {
                            Console.WriteLine("  BUY skipped: not enough simulated cash.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  BUY skipped: position already open.");
                    }

                    break;

                case SignalType.Sell:
                    if (position is not null)
                    {
                        decimal proceeds = position.Quantity * bar.Close;
                        decimal pnl = position.UnrealizedPnL(bar.Close);

                        cash += proceeds;

                        Console.WriteLine(
                            $"  SELL {position.Quantity} shares @ {bar.Close:C2} | PnL={pnl:C2} | Cash={cash:C2} | Reason={signal.Reason}");

                        position = null;
                    }
                    else
                    {
                        Console.WriteLine("  SELL skipped: no open position.");
                    }

                    break;

                case SignalType.Hold:
                    if (position is not null)
                    {
                        decimal unrealizedPnL = position.UnrealizedPnL(bar.Close);
                        Console.WriteLine($"  HOLD open position | Unrealized PnL={unrealizedPnL:C2}");
                    }

                    break;
            }
        }

        Console.WriteLine();
        Console.WriteLine("Simulation finished.");
        Console.WriteLine($"Final cash: {cash:C2}");

        if (position is not null)
        {
            Console.WriteLine(
                $"Open position remains: {position.Quantity} {position.Symbol} @ entry {position.EntryPrice:C2}");
        }
    }
}
