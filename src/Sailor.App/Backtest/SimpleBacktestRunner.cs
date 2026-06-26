using Sailor.App.Backtest.Conduct;
using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Strategies;
using Sailor.App.Configuration;

namespace Sailor.App.Backtest;

public static class SimpleBacktestRunner
{
    public static async Task<BacktestRunResult> RunAsync(
        string symbol,
        string? timeframe = null,
        string? profileName = null,
        bool echoToConsole = true,
        SailorAppSettings? settings = null)
    {
        settings ??= new SailorAppSettings();
        BacktestOptions options = BacktestOptions.CreateDefault(symbol, timeframe, profileName, settings);
        SailorStrategyProfile profile = SailorStrategyProfile.FromName(options.ProfileName, settings);
        var conductExitEngine = new SailorConductExitEngine(settings.Conduct);

        string logDirectory = GetBacktestLogDirectory();
        Directory.CreateDirectory(logDirectory);

        string logFilePath = Path.Combine(
            logDirectory,
            $"backtest_{options.Symbol}_{options.Timeframe}_{options.ProfileName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        await using var fileStream = new FileStream(
            logFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);

        await using var writer = new StreamWriter(fileStream);

        void Log(string message)
        {
            if (echoToConsole)
            {
                Console.WriteLine(message);
            }

            writer.WriteLine(message);
            writer.Flush();
        }

        var dataProvider = new CsvBacktestDataProvider();
        IBacktestStrategy strategy = CreateStrategy(profile);

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
        SailorConductExitState? conductState = null;

        Log("sailor backtest started");
        Log($"Symbol: {options.Symbol}");
        Log($"Timeframe: {options.Timeframe}");
        Log($"Strategy profile: {profile.Name}");
        Log($"Strategy: {strategy.Name}");
        Log($"Bars: {bars.Count}");
        Log("Indicators: EMA9, SMA20, SMA200, VWAP, VolumeAverage20");
        Log($"Initial cash: {cash:F2}");
        Log($"Max position notional: {options.MaxPositionNotional:F2}");
        Log($"Stop loss: {options.StopLossPercent:F2}%");
        Log($"Take profit: {options.TakeProfitPercent:F2}%");
        Log($"Max hold bars: {options.MaxHoldBars}");
        Log("Settings source: appsettings.json with built-in defaults fallback");
        Log($"Entry momentum: {profile.EntryMomentumPercent:F2}%");
        Log($"Exit momentum: {profile.ExitMomentumPercent:F2}%");
        Log($"Price filter: {profile.MinimumPrice:F2}-{profile.MaximumPrice:F2}");
        Log($"Minimum volume: {profile.MinimumVolume}");
        Log($"Minimum volume ratio: {profile.MinimumVolumeRatio:F2}");
        Log($"Profile filters: EMA9>SMA20={profile.RequireEma9AboveSma20}, Close>VWAP={profile.RequirePriceAboveVwap}, Close>SMA200 when available={profile.RequirePriceAboveSma200WhenAvailable}");
        Log($"Conduct exits enabled: {profile.UseConductExits}");

        if (profile.UseConductExits)
        {
            Log($"Conduct hard stop: {settings.Conduct.HardStopPercent:F2}%");
            Log($"Conduct breakeven after: {settings.Conduct.MoveStopToBreakevenAfterPercent:F2}% with buffer {settings.Conduct.BreakevenBufferPercent:F2}%");
            Log($"Conduct trailing after: {settings.Conduct.StartTrailingAfterPercent:F2}% with giveback {settings.Conduct.GivebackPercent:F2}% and cap {settings.Conduct.GivebackNotionalCap:F2}");
            Log($"Conduct indicator exits after bars: {settings.Conduct.MinimumBarsBeforeIndicatorExit}");
            Log($"Conduct exit filters: EMA9={settings.Conduct.UseEma9Exit}, VWAP={settings.Conduct.UseVwapExit}, Trend={settings.Conduct.UseTrendExit}");
            Log($"Conduct max hold bars: {settings.Conduct.MaxHoldBars}");
            Log($"Conduct fixed take profit enabled: {settings.Conduct.UseTakeProfitExit}");
        }

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
                bool closedByExitRule = profile.UseConductExits
                    ? TryCloseByConductExit(
                        bar,
                        indicator,
                        barIndex,
                        options,
                        conductExitEngine,
                        trades,
                        Log,
                        ref cash,
                        ref hasOpenPosition,
                        ref quantity,
                        ref entryPrice,
                        ref entryTime,
                        ref entryReason,
                        ref entryBarIndex,
                        ref conductState)
                    : TryCloseByExitRule(
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
                    conductState = null;
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
                    conductState = profile.UseConductExits
                        ? new SailorConductExitState(entryTime, entryBarIndex, entryPrice, quantity)
                        : null;

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

                conductState = null;
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

            conductState = null;
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
        Log($"Profile:      {profile.Name}");
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

        if (echoToConsole)
        {
            Console.WriteLine();
            Console.WriteLine("Backtest log created:");
            Console.WriteLine(logFilePath);
        }

        return new BacktestRunResult(
            Symbol: options.Symbol,
            Timeframe: options.Timeframe,
            ProfileName: profile.Name,
            StrategyName: strategy.Name,
            Bars: bars.Count,
            TotalTrades: summary.TotalTrades,
            Winners: summary.Winners,
            Losers: summary.Losers,
            TotalPnl: summary.TotalPnl,
            FinalCash: summary.FinalCash,
            LastEma9: indicators[^1].Ema9,
            LastSma20: indicators[^1].Sma20,
            LastSma200: indicators[^1].Sma200,
            LastVwap: indicators[^1].Vwap,
            LastVolumeAverage20: indicators[^1].VolumeAverage20,
            DataSourcePath: dataSet.SourcePath,
            LogFilePath: logFilePath);
    }

    private static IBacktestStrategy CreateStrategy(SailorStrategyProfile profile)
    {
        if (profile.UseConductExits || profile.Name.Equals("sailor-conduct-v3", StringComparison.OrdinalIgnoreCase))
        {
            return new SailorConductBacktestStrategy(profile);
        }

        return profile.Name.Equals("simple-momentum", StringComparison.OrdinalIgnoreCase)
            ? new SimpleMomentumBacktestStrategy()
            : new SailorTrendVolumeBacktestStrategy(profile);
    }

    private static bool TryCloseByConductExit(
        BacktestBar bar,
        BacktestIndicatorSnapshot indicator,
        int barIndex,
        BacktestOptions options,
        SailorConductExitEngine conductExitEngine,
        List<BacktestTrade> trades,
        Action<string> log,
        ref decimal cash,
        ref bool hasOpenPosition,
        ref int quantity,
        ref decimal entryPrice,
        ref DateTimeOffset entryTime,
        ref string entryReason,
        ref int entryBarIndex,
        ref SailorConductExitState? conductState)
    {
        if (conductState is null)
        {
            conductState = new SailorConductExitState(entryTime, entryBarIndex, entryPrice, quantity);
        }

        SailorConductExitDecision decision = conductExitEngine.EvaluateLongExit(
            bar,
            indicator,
            options,
            conductState,
            barIndex);

        if (!decision.ShouldExit)
        {
            return false;
        }

        ClosePosition(
            trades,
            log,
            options.Symbol,
            bar.Time,
            decision.ExitPrice,
            quantity,
            entryTime,
            entryPrice,
            entryReason,
            decision.Reason,
            ref cash,
            ref hasOpenPosition,
            ref quantity,
            ref entryPrice,
            ref entryReason,
            ref entryBarIndex);

        conductState = null;
        return true;
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
