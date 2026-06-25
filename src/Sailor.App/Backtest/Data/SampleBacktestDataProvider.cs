using Sailor.App.Backtest.Models;

namespace Sailor.App.Backtest.Data;

public sealed class SampleBacktestDataProvider
{
    public IReadOnlyList<BacktestBar> LoadBars(string symbol)
    {
        var bars = new List<BacktestBar>();
        var random = new Random(42);

        decimal price = 100.00m;
        DateTimeOffset start = DateTimeOffset.Now.Date.AddHours(9).AddMinutes(30);

        for (int i = 0; i < 120; i++)
        {
            decimal open = price;
            decimal change = (decimal)(random.NextDouble() - 0.48) * 0.90m;

            price = Math.Max(1.00m, price + change);

            decimal close = price;
            decimal high = Math.Max(open, close) + 0.15m;
            decimal low = Math.Min(open, close) - 0.15m;

            bars.Add(new BacktestBar(
                Time: start.AddMinutes(i),
                Symbol: symbol,
                Open: decimal.Round(open, 2),
                High: decimal.Round(high, 2),
                Low: decimal.Round(low, 2),
                Close: decimal.Round(close, 2),
                Volume: random.Next(20_000, 150_000)));
        }

        return bars;
    }
}
