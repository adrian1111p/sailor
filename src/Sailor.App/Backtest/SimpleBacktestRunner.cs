using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Strategies;

namespace Sailor.App.Backtest;

public static class SimpleBacktestRunner
{
    public static async Task RunAsync(string symbol, string timeframe = "1m")
    {
        BacktestOptions options = BacktestOptions.CreateDefault(symbol, timeframe);

        string logDirectory = GetBacktestLogDirectory();
        Directory.CreateDirectory(logDirectory);

        string logFilePath = Path.Combine(
            logDirectory,
            $"backtest_{options.Symbol}_{options.Timeframe}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

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

        var dataProvider = new CsvBacktestDataProvider();
        var strategy = new SimpleMomentumBacktestStrategy();

        BacktestDataSet dataSet = dataProvider.LoadBars(options.Symbol, options.Timeframe);
        IReadOnlyList<BacktestBar> bars = dataSet.Bars;
        IReadOnlyList<BacktestIndicatorSnapshot> indicators = TechnicalIndicatorCalculator.Calculate(bars);

        decimal cash = options.InitialCash;

        BacktestBar? previousBar = null;
        List<BacktestTrade> trades = [];

        bool hasOpenPosition = false;
        int quantity = 0;
        int entryBarIndex = -1;
        decimal entryPrice = 0m;
        DateTimeOffset entryTime = default;
        string entryReason = string.Empty;

        Log("sailor backtest started");
        Log($"Symbol: {options.Symbol}");
        Log($"Timeframe: {options.Timeframe}");
        Log($"Strategy: {strategy.Name}");
        Log($"Bars: {bars.Count}");
        Log("Indicators: EMA9, SMA20, SMA200, VWAP, VolumeAverage20");
        Log($"Initial cash: {cash:F2}");
        Log($"Max position notional: {options.MaxPositionNotional:F2}");
        Log($"Stop loss: {options.StopLossPercent:F2}%");
        Log($"Take profit: {options.TakeProfitPercent:F2}%");
        Log($"Max hold bars: {options.MaxHoldBars}");
        Log($"Data source: {dataSet.SourcePath}");
        Log($"Log file: {logFilePath}");
        Log("");

        for (int barIndex = 0; barIndex < bars.Count; barIndex++)
        {
            BacktestBar bar = bars[barIndex];
            BacktestIndicatorSnapshot indicator = indicators[barIndex];

            if (barIndex == 0 || barIndex == 8 || barIndex == 19 || barIndex == 199)
            {
                Log($"{bar.Time:yyyy-MM-dd HH:mm} | indicators ready check | {indicator.ToCompactString()}");
            }

            if (hasOpenPosition)
            {
                bool closedByExitRule = TryCloseByExitRule(
                    bar,
                    barIndex,
                    options,
                    trades,
                    Log,
                    ref cash,
                    ref hasOpenPosition,
                    ref quantity,
                    ref entryPrice,
                    ref entryTime,
                    ref entryReason,
                    ref entryBarIndex);

                if (closedByExitRule)
                {
                    previousBar = bar;
                    continue;
                }
            }

            BacktestSignal signal = strategy.Evaluate(
                bar,
                previousBar,
                indicator,
                hasOpenPosition);

            if (signal.Type == BacktestSignalType.Buy && !hasOpenPosition)
            {
                quantity = CalculateQuantity(options.MaxPositionNotional, bar.Close);
                decimal cost = quantity * bar.Close;

                if (quantity <= 0)
                {
                    Log($"{bar.Time:yyyy-MM-dd HH:mm} | BUY skipped: invalid quantity at price {bar.Close:F2}");
                }
                else if (cash >= cost)
                {
                    cash -= cost;
                    hasOpenPosition = true;
                    entryPrice = bar.Close;
                    entryTime = bar.Time;
                    entryReason = signal.Reason;
                    entryBarIndex = barIndex;

                    Log($"{bar.Time:yyyy-MM-dd HH:mm} | BUY  {quantity} {options.Symbol} @ {bar.Close:F2} | Cash={cash:F2} | {signal.Reason}");
                }
                else
                {
                    Log($"{bar.Time:yyyy-MM-dd HH:mm} | BUY skipped: cost {cost:F2} > cash {cash:F2}");
                }
            }
            else if (signal.Type == BacktestSignalType.Sell && hasOpenPosition)
            {
                ClosePosition(
                    trades,
                    Log,
                    options.Symbol,
                    bar.Time,
                    bar.Close,
                    quantity,
                    entryTime,
                    entryPrice,
                    entryReason,
                    signal.Reason,
                    ref cash,
                    ref hasOpenPosition,
                    ref quantity,
                    ref entryPrice,
                    ref entryReason,
                    ref entryBarIndex);
            }

            previousBar = bar;
        }

        if (hasOpenPosition && bars.Count > 0)
        {
            BacktestBar finalBar = bars[^1];

            ClosePosition(
                trades,
                Log,
                options.Symbol,
                finalBar.Time,
                finalBar.Close,
                quantity,
                entryTime,
                entryPrice,
                entryReason,
                "Forced close at end of backtest.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref entryPrice,
                ref entryReason,
                ref entryBarIndex);
        }

        int winners = trades.Count(t => t.Pnl > 0);
        int losers = trades.Count(t => t.Pnl < 0);
        decimal totalPnl = trades.Sum(t => t.Pnl);

        var summary = new BacktestSummary(
            Symbol: options.Symbol,
            TotalTrades: trades.Count,
            Winners: winners,
            Losers: losers,
            TotalPnl: totalPnl,
            FinalCash: cash);

        Log("");
        Log("Backtest summary");
        Log("----------------");
        Log($"Symbol:       {summary.Symbol}");
        Log($"Timeframe:    {options.Timeframe}");
        Log($"Bars:         {bars.Count}");
        Log($"Last EMA9:    {FormatIndicator(indicators[^1].Ema9)}");
        Log($"Last SMA20:   {FormatIndicator(indicators[^1].Sma20)}");
        Log($"Last SMA200:  {FormatIndicator(indicators[^1].Sma200)}");
        Log($"Last VWAP:    {FormatIndicator(indicators[^1].Vwap)}");
        Log($"Last VolAvg:  {FormatIndicator(indicators[^1].VolumeAverage20)}");
        Log($"Trades:       {summary.TotalTrades}");
        Log($"Winners:      {summary.Winners}");
        Log($"Losers:       {summary.Losers}");
        Log($"Win rate:     {summary.WinRatePercent:F2}%");
        Log($"Total PnL:    {summary.TotalPnl:F2}");
        Log($"Final cash:   {summary.FinalCash:F2}");
        Log("");
        Log("sailor backtest finished");

        Console.WriteLine();
        Console.WriteLine("Backtest log created:");
        Console.WriteLine(logFilePath);
    }

    private static bool TryCloseByExitRule(
        BacktestBar bar,
        int barIndex,
        BacktestOptions options,
        List<BacktestTrade> trades,
        Action<string> log,
        ref decimal cash,
        ref bool hasOpenPosition,
        ref int quantity,
        ref decimal entryPrice,
        ref DateTimeOffset entryTime,
        ref string entryReason,
        ref int entryBarIndex)
    {
        decimal stopPrice = entryPrice * (1m - options.StopLossPercent / 100m);
        decimal takeProfitPrice = entryPrice * (1m + options.TakeProfitPercent / 100m);
        int barsHeld = barIndex - entryBarIndex;

        if (bar.Low <= stopPrice)
        {
            ClosePosition(
                trades,
                log,
                options.Symbol,
                bar.Time,
                stopPrice,
                quantity,
                entryTime,
                entryPrice,
                entryReason,
                $"Stop loss hit at {stopPrice:F2}.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref entryPrice,
                ref entryReason,
                ref entryBarIndex);

            return true;
        }

        if (bar.High >= takeProfitPrice)
        {
            ClosePosition(
                trades,
                log,
                options.Symbol,
                bar.Time,
                takeProfitPrice,
                quantity,
                entryTime,
                entryPrice,
                entryReason,
                $"Take profit hit at {takeProfitPrice:F2}.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref entryPrice,
                ref entryReason,
                ref entryBarIndex);

            return true;
        }

        if (barsHeld >= options.MaxHoldBars)
        {
            ClosePosition(
                trades,
                log,
                options.Symbol,
                bar.Time,
                bar.Close,
                quantity,
                entryTime,
                entryPrice,
                entryReason,
                $"Max hold reached after {barsHeld} bars.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref entryPrice,
                ref entryReason,
                ref entryBarIndex);

            return true;
        }

        return false;
    }

    private static void ClosePosition(
        List<BacktestTrade> trades,
        Action<string> log,
        string symbol,
        DateTimeOffset exitTime,
        decimal exitPrice,
        int quantity,
        DateTimeOffset entryTime,
        decimal entryPrice,
        string entryReason,
        string exitReason,
        ref decimal cash,
        ref bool hasOpenPosition,
        ref int openQuantity,
        ref decimal openEntryPrice,
        ref string openEntryReason,
        ref int entryBarIndex)
    {
        decimal proceeds = quantity * exitPrice;
        cash += proceeds;

        var trade = new BacktestTrade(
            Symbol: symbol,
            EntryTime: entryTime,
            ExitTime: exitTime,
            EntryPrice: entryPrice,
            ExitPrice: exitPrice,
            Quantity: quantity,
            EntryReason: entryReason,
            ExitReason: exitReason);

        trades.Add(trade);

        log($"{exitTime:yyyy-MM-dd HH:mm} | SELL {quantity} {symbol} @ {exitPrice:F2} | PnL={trade.Pnl:F2} ({trade.PnlPercent:F2}%) | Cash={cash:F2} | {exitReason}");

        hasOpenPosition = false;
        openQuantity = 0;
        openEntryPrice = 0m;
        openEntryReason = string.Empty;
        entryBarIndex = -1;
    }

    private static string FormatIndicator(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "n/a";
    }

    private static int CalculateQuantity(decimal maxPositionNotional, decimal price)
    {
        if (price <= 0m)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Floor(maxPositionNotional / price));
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
