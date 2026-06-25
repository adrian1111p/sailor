using Sailor.App.Backtest;

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
        string symbol = args.Length >= 2
            ? args[1].Trim().ToUpperInvariant()
            : "AAPL";

        await SimpleBacktestRunner.RunAsync(symbol);
        break;
    }

    default:
        Console.WriteLine($"Unknown command: {command}");
        PrintHelp();
        break;
}

static void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  sailor backtest");
    Console.WriteLine("  sailor backtest AAPL");
    Console.WriteLine("  sailor backtest TSLA");
}