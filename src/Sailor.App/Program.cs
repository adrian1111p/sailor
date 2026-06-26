using Sailor.App.Backtest;
using Sailor.App.Backtest.Data;

Console.WriteLine("sailor - C# day trading application");
Console.WriteLine("Mode: development / paper / backtest only");
Console.WriteLine();

if (args.Length == 0)
{
    PrintHelp();
    return;
}

string command = args[0].Trim().ToLowerInvariant();

switch (command)
{
    case "backtest":
    {
        if (args.Length >= 2 && args[1].Equals("--list", StringComparison.OrdinalIgnoreCase))
        {
            PrintAvailableBacktestData(args);
            break;
        }

        string symbol = args.Length >= 2
            ? args[1].Trim().ToUpperInvariant()
            : "AAPL";

        string timeframe = args.Length >= 3
            ? args[2].Trim()
            : "1m";

        await SimpleBacktestRunner.RunAsync(symbol, timeframe);
        break;
    }

    default:
        Console.WriteLine($"Unknown command: {command}");
        PrintHelp();
        break;
}

static void PrintAvailableBacktestData(string[] args)
{
    var provider = new CsvBacktestDataProvider();

    if (args.Length >= 3)
    {
        string symbol = args[2].Trim().ToUpperInvariant();
        IReadOnlyList<string> timeframes = provider.ListTimeframes(symbol);

        Console.WriteLine($"Available timeframes for {symbol}:");
        foreach (string timeframe in timeframes)
        {
            Console.WriteLine($"  {timeframe}");
        }

        return;
    }

    IReadOnlyList<string> symbols = provider.ListSymbols();

    Console.WriteLine($"Available symbols: {symbols.Count}");
    foreach (string symbol in symbols.Take(80))
    {
        Console.WriteLine($"  {symbol}");
    }

    if (symbols.Count > 80)
    {
        Console.WriteLine($"  ... {symbols.Count - 80} more");
    }
}

static void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  sailor backtest");
    Console.WriteLine("  sailor backtest AAPL");
    Console.WriteLine("  sailor backtest TSLA 1m");
    Console.WriteLine("  sailor backtest --list");
    Console.WriteLine("  sailor backtest --list AAPL");
}
