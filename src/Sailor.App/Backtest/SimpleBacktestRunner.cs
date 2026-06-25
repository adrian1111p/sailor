using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Strategies;

namespace Sailor.App.Backtest;

public static class SimpleBacktestRunner
{
    public static async Task RunAsync(string symbol)
    {
        string logDirectory = GetBacktestLogDirectory();
        Directory.CreateDirectory(logDirectory);

        string logFilePath = Path.Combine(
            logDirectory,
            $"backtest_{symbol}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        await using var fileStream = new FileStream(
            logFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);

        await using var writer = new StreamWriter(fileStream);

        void Log(string message)
        {
            Console.WriteLine(message);
            writer.WriteLine(message);
            writer.Flush();
        }

        var dataProvider = new SampleBacktestDataProvider();
        var strategy = new SimpleMomentumBacktestStrategy();

        IReadOnlyList<BacktestBar> bars = dataProvider.LoadBars(symbol);

        decimal cash = 10_000.00m;
        int quantity = 10;

        BacktestBar? previousBar = null;
        BacktestTrade? lastTrade = null;
        List<BacktestTrade> trades = [];

        bool hasOpenPosition = false;
        decimal entryPrice = 0m;
        DateTimeOffset entryTime = default;
        string entryReason = string.Empty;

        Log("sailor backtest started");
        Log($"Symbol: {symbol}");
        Log($"Strategy: {strategy.Name}");
        Log($"Bars: {bars.Count}");
        Log($"Initial cash: {cash:F2}");
        Log($"Log file: {logFilePath}");
        Log("");

        foreach (BacktestBar bar in bars)
        {
            BacktestSignal signal = strategy.Evaluate(
                bar,
                previousBar,
                hasOpenPosition);

            Log($"{bar.Time:HH:mm} | {bar.Symbol} | Close={bar.Close,8:F2} | Volume={bar.Volume,8} | Signal={signal.Type}");

            if (signal.Type == BacktestSignalType.Buy && !hasOpenPosition)
            {
                decimal cost = quantity * bar.Close;

                if (cash >= cost)
                {
                    cash -= cost;
                    hasOpenPosition = true;
                    entryPrice = bar.Close;
                    entryTime = bar.Time;
                    entryReason = signal.Reason;

                    Log($"  BUY  {quantity} @ {bar.Close:F2} | Cash={cash:F2} | {signal.Reason}");
                }
                else
                {
                    Log("  BUY skipped: not enough cash.");
                }
            }
            else if (signal.Type == BacktestSignalType.Sell && hasOpenPosition)
            {
                decimal proceeds = quantity * bar.Close;
                cash += proceeds;

                lastTrade = new BacktestTrade(
                    Symbol: symbol,
                    EntryTime: entryTime,
                    ExitTime: bar.Time,
                    EntryPrice: entryPrice,
                    ExitPrice: bar.Close,
                    Quantity: quantity,
                    EntryReason: entryReason,
                    ExitReason: signal.Reason);

                trades.Add(lastTrade);

                Log($"  SELL {quantity} @ {bar.Close:F2} | PnL={lastTrade.Pnl:F2} | Cash={cash:F2} | {signal.Reason}");

                hasOpenPosition = false;
                entryPrice = 0m;
                entryReason = string.Empty;
            }

            previousBar = bar;
        }

        if (hasOpenPosition && bars.Count > 0)
        {
            BacktestBar finalBar = bars[^1];
            decimal proceeds = quantity * finalBar.Close;
            cash += proceeds;

            lastTrade = new BacktestTrade(
                Symbol: symbol,
                EntryTime: entryTime,
                ExitTime: finalBar.Time,
                EntryPrice: entryPrice,
                ExitPrice: finalBar.Close,
                Quantity: quantity,
                EntryReason: entryReason,
                ExitReason: "Forced close at end of backtest.");

            trades.Add(lastTrade);

            Log("");
            Log($"  FORCE SELL {quantity} @ {finalBar.Close:F2} | PnL={lastTrade.Pnl:F2} | Cash={cash:F2}");

            hasOpenPosition = false;
        }

        int winners = trades.Count(t => t.Pnl > 0);
        int losers = trades.Count(t => t.Pnl < 0);
        decimal totalPnl = trades.Sum(t => t.Pnl);

        var summary = new BacktestSummary(
            Symbol: symbol,
            TotalTrades: trades.Count,
            Winners: winners,
            Losers: losers,
            TotalPnl: totalPnl,
            FinalCash: cash);

        Log("");
        Log("Backtest summary");
        Log("----------------");
        Log($"Symbol:       {summary.Symbol}");
        Log($"Trades:       {summary.TotalTrades}");
        Log($"Winners:      {summary.Winners}");
        Log($"Losers:       {summary.Losers}");
        Log($"Win rate:     {summary.WinRatePercent:F2}%");
        Log($"Total PnL:    {summary.TotalPnl:F2}");
        Log($"Final cash:   {summary.FinalCash:F2}");
        Log("");
        Log("sailor backtest finished");

        Console.WriteLine();
        Console.WriteLine($"Backtest log created:");
        Console.WriteLine(logFilePath);
    }

    private static string GetBacktestLogDirectory()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Logs",
            "Backtest"));
    }
}
