using Sailor.App.Engine;
using Sailor.App.Services;
using Sailor.App.Strategies;

string symbol = args.Length > 0
    ? args[0].Trim().ToUpperInvariant()
    : "AAPL";

Console.WriteLine("001_sailor - simple C# day trading simulator");
Console.WriteLine($"Symbol: {symbol}");
Console.WriteLine("Mode: PAPER SIMULATION ONLY");
Console.WriteLine();

var marketDataProvider = new SampleMarketDataProvider();
var strategy = new SimpleMomentumStrategy();
var engine = new TradingEngine(marketDataProvider, strategy);

await engine.RunAsync(symbol);
