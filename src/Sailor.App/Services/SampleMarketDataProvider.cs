using Sailor.App.Models;

namespace Sailor.App.Services;

public sealed class SampleMarketDataProvider
{
    public async IAsyncEnumerable<MarketBar> GetBarsAsync(string symbol)
    {
        var random = new Random(42);
        decimal price = 100.00m;

        for (int i = 0; i < 40; i++)
        {
            decimal open = price;
            decimal change = (decimal)(random.NextDouble() - 0.48) * 1.20m;

            price = Math.Max(1.00m, price + change);

            decimal close = price;
            decimal high = Math.Max(open, close) + 0.20m;
            decimal low = Math.Min(open, close) - 0.20m;
            long volume = random.Next(10_000, 80_000);

            yield return new MarketBar(
                Time: DateTimeOffset.Now.AddMinutes(i),
                Symbol: symbol,
                Open: decimal.Round(open, 2),
                High: decimal.Round(high, 2),
                Low: decimal.Round(low, 2),
                Close: decimal.Round(close, 2),
                Volume: volume);

            await Task.Delay(50);
        }
    }
}
