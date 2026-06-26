using Sailor.App.Backtest.Conduct;
using Sailor.App.Backtest.Data;
using Sailor.App.Backtest.Indicators;
using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;
using Sailor.App.Backtest.Strategies;
using Sailor.App.Backtest.Strategies.HarvesterConduct;
using Sailor.App.Configuration;
using Sailor.App.Logging;

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
        ConductExitSettings conductSettings = ResolveConductSettings(settings, profile);
        var conductExitEngine = new SailorConductExitEngine(conductSettings);

        string logDirectory = SailorLogPaths.Backtest;
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
        int positionSide = 0;
        int entryBarIndex = -1;
        int lastExitBarIndex = -10_000;
        decimal entryPrice = 0m;
        DateTimeOffset entryTime = default;
        string entryReason = string.Empty;
        SailorConductExitState? conductState = null;
        BacktestSignal? pendingNextBarEntry = null;
        DateTimeOffset pendingSignalTime = default;

        Log("sailor backtest started");
        Log($"Symbol: {options.Symbol}");
        Log($"Timeframe: {options.Timeframe}");
        Log($"Strategy profile: {profile.Name}");
        Log($"Strategy: {strategy.Name}");
        Log($"Side mode: {profile.SideMode}");
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
        Log($"Market hours enabled: {profile.UseMarketHours}");

        if (profile.UseMarketHours)
        {
            Log($"Market window ET minutes: open={profile.MarketOpenMinute}, skipFirst={profile.SkipFirstMinutes}, lastEntry={profile.LastEntryMinute}, forceFlat={profile.ForceFlatMinute}");
            Log($"Next-bar-open entries: {profile.UseNextBarOpenEntry}");
            Log($"Minimum bars between entries: {profile.MinimumBarsBetweenEntries}");
        }

        if (profile.UseConductExits)
        {
            Log($"Conduct profile: {profile.ConductProfileName}");
            Log($"Conduct hard stop: {conductSettings.HardStopPercent:F2}%");
            Log($"Conduct breakeven after: {conductSettings.MoveStopToBreakevenAfterPercent:F2}% with buffer {conductSettings.BreakevenBufferPercent:F2}%");
            Log($"Conduct trailing after: {conductSettings.StartTrailingAfterPercent:F2}% with giveback {conductSettings.GivebackPercent:F2}% and cap {conductSettings.GivebackNotionalCap:F2}");
            Log($"Conduct micro trail: {conductSettings.UseMicroTrail}, activate {conductSettings.MicroTrailActivatePercent:F2}%, trail {conductSettings.MicroTrailPercent:F2}%");
            Log($"Conduct indicator exits after bars: {conductSettings.MinimumBarsBeforeIndicatorExit}");
            Log($"Conduct exit filters: EMA9={conductSettings.UseEma9Exit}, VWAP={conductSettings.UseVwapExit}, Trend={conductSettings.UseTrendExit}, OppositeMomentum={conductSettings.UseOppositeMomentumExit}");
            Log($"Conduct max hold bars: {conductSettings.MaxHoldBars}");
            Log($"Conduct fixed take profit enabled: {conductSettings.UseTakeProfitExit}");
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

            if (hasOpenPosition && ShouldForceFlat(profile, bar))
            {
                ClosePosition(
                    trades,
                    Log,
                    options.Symbol,
                    bar.Time,
                    bar.Close,
                    quantity,
                    positionSide,
                    entryTime,
                    entryPrice,
                    entryReason,
                    $"conduct session flat: ET minute {MarketTime.GetEasternMinuteOfDay(bar.Time)} >= force flat {profile.ForceFlatMinute}.",
                    ref cash,
                    ref hasOpenPosition,
                    ref quantity,
                    ref positionSide,
                    ref entryPrice,
                    ref entryReason,
                    ref entryBarIndex);

                conductState = null;
                lastExitBarIndex = barIndex;
            }

            if (!hasOpenPosition && pendingNextBarEntry is not null)
            {
                TryOpenPosition(
                    bar,
                    bar.Open,
                    barIndex,
                    ResolveEntrySide(pendingNextBarEntry, strategy, profile),
                    options,
                    profile,
                    pendingNextBarEntry with
                    {
                        Reason = $"next-bar-open entry from {pendingSignalTime:yyyy-MM-dd HH:mm}: {pendingNextBarEntry.Reason}"
                    },
                    Log,
                    ref cash,
                    ref hasOpenPosition,
                    ref quantity,
                    ref positionSide,
                    ref entryPrice,
                    ref entryTime,
                    ref entryReason,
                    ref entryBarIndex,
                    ref conductState,
                    lastExitBarIndex);

                pendingNextBarEntry = null;
            }

            if (hasOpenPosition)
            {
                bool closedByExitRule = profile.UseConductExits
                    ? TryCloseByConductExit(
                        bar,
                        previousBar,
                        indicator,
                        barIndex,
                        options,
                        profile,
                        conductExitEngine,
                        trades,
                        Log,
                        ref cash,
                        ref hasOpenPosition,
                        ref quantity,
                        ref positionSide,
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
                        ref positionSide,
                        ref entryPrice,
                        ref entryTime,
                        ref entryReason,
                        ref entryBarIndex);

                if (closedByExitRule)
                {
                    conductState = null;
                    lastExitBarIndex = barIndex;
                    previousBar = bar;
                    continue;
                }
            }

            BacktestSignal signal = strategy.Evaluate(
                bar,
                previousBar,
                indicator,
                hasOpenPosition,
                positionSide);

            int requestedEntrySide = ResolveEntrySide(signal, strategy, profile);

            if (!hasOpenPosition && requestedEntrySide != 0)
            {
                if (profile.UseNextBarOpenEntry)
                {
                    pendingNextBarEntry = signal;
                    pendingSignalTime = bar.Time;
                    string scheduledAction = requestedEntrySide < 0 ? "SELL_SHORT" : "BUY";
                    Log($"{bar.Time:yyyy-MM-dd HH:mm} | {scheduledAction} signal scheduled for next bar open | {signal.Reason}");
                }
                else
                {
                    TryOpenPosition(
                        bar,
                        bar.Close,
                        barIndex,
                        requestedEntrySide,
                        options,
                        profile,
                        signal,
                        Log,
                        ref cash,
                        ref hasOpenPosition,
                        ref quantity,
                        ref positionSide,
                        ref entryPrice,
                        ref entryTime,
                        ref entryReason,
                        ref entryBarIndex,
                        ref conductState,
                        lastExitBarIndex);
                }
            }
            else if (hasOpenPosition && IsExitSignal(signal, positionSide))
            {
                ClosePosition(
                    trades,
                    Log,
                    options.Symbol,
                    bar.Time,
                    bar.Close,
                    quantity,
                    positionSide,
                    entryTime,
                    entryPrice,
                    entryReason,
                    signal.Reason,
                    ref cash,
                    ref hasOpenPosition,
                    ref quantity,
                    ref positionSide,
                    ref entryPrice,
                    ref entryReason,
                    ref entryBarIndex);

                conductState = null;
                lastExitBarIndex = barIndex;
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
                positionSide,
                entryTime,
                entryPrice,
                entryReason,
                "Forced close at end of backtest.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref positionSide,
                ref entryPrice,
                ref entryReason,
                ref entryBarIndex);

            conductState = null;
        }

        int winners = trades.Count(t => t.Pnl > 0);
        int losers = trades.Count(t => t.Pnl < 0);
        decimal totalPnl = trades.Sum(t => t.Pnl);
        int longTrades = trades.Count(t => t.PositionSide > 0);
        int shortTrades = trades.Count(t => t.PositionSide < 0);
        decimal longPnl = trades.Where(t => t.PositionSide > 0).Sum(t => t.Pnl);
        decimal shortPnl = trades.Where(t => t.PositionSide < 0).Sum(t => t.Pnl);

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
        Log($"Long trades:  {longTrades}");
        Log($"Short trades: {shortTrades}");
        Log($"Long PnL:     {longPnl:F2}");
        Log($"Short PnL:    {shortPnl:F2}");
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
            LogFilePath: logFilePath,
            Trades: trades.ToArray());
    }

    private static IBacktestStrategy CreateStrategy(SailorStrategyProfile profile)
    {
        if (profile.UseConductExits ||
            profile.Name.Contains("conduct", StringComparison.OrdinalIgnoreCase) ||
            SailorConductStrategyRegistry.IsSupported(profile.Name))
        {
            return new SailorConductBacktestStrategy(profile);
        }

        return profile.Name.Equals("simple-momentum", StringComparison.OrdinalIgnoreCase)
            ? new SimpleMomentumBacktestStrategy(profile)
            : new SailorTrendVolumeBacktestStrategy(profile);
    }

    private static ConductExitSettings ResolveConductSettings(
        SailorAppSettings settings,
        SailorStrategyProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ConductProfileName) &&
            settings.ConductProfiles.TryGetValue(profile.ConductProfileName, out ConductExitSettings? conductProfile))
        {
            return conductProfile;
        }

        if (settings.ConductProfiles.TryGetValue(profile.Name, out ConductExitSettings? profileByName))
        {
            return profileByName;
        }

        if (SailorConductStrategyRegistry.TryCreateDefaultExitSettings(profile.Name, out ConductExitSettings builtInConductSettings))
        {
            return builtInConductSettings;
        }

        return settings.Conduct;
    }

    private static bool TryOpenPosition(
        BacktestBar bar,
        decimal entryExecutionPrice,
        int barIndex,
        int newPositionSide,
        BacktestOptions options,
        SailorStrategyProfile profile,
        BacktestSignal signal,
        Action<string> log,
        ref decimal cash,
        ref bool hasOpenPosition,
        ref int quantity,
        ref int positionSide,
        ref decimal entryPrice,
        ref DateTimeOffset entryTime,
        ref string entryReason,
        ref int entryBarIndex,
        ref SailorConductExitState? conductState,
        int lastExitBarIndex)
    {
        if (!CanOpenNewPosition(profile, bar, barIndex, lastExitBarIndex, out string rejectReason))
        {
            log($"{bar.Time:yyyy-MM-dd HH:mm} | entry skipped: {rejectReason}");
            return false;
        }

        if (newPositionSide == 0)
        {
            log($"{bar.Time:yyyy-MM-dd HH:mm} | entry skipped: signal is not an enabled entry side.");
            return false;
        }

        quantity = CalculateQuantity(options.MaxPositionNotional, entryExecutionPrice);
        decimal reservedNotional = quantity * entryExecutionPrice;
        string action = newPositionSide < 0 ? "SELL_SHORT" : "BUY";

        if (quantity <= 0)
        {
            log($"{bar.Time:yyyy-MM-dd HH:mm} | {action} skipped: invalid quantity at price {entryExecutionPrice:F2}");
            return false;
        }

        if (cash < reservedNotional)
        {
            log($"{bar.Time:yyyy-MM-dd HH:mm} | {action} skipped: reserved notional {reservedNotional:F2} > cash {cash:F2}");
            return false;
        }

        cash -= reservedNotional;
        hasOpenPosition = true;
        positionSide = newPositionSide;
        entryPrice = entryExecutionPrice;
        entryTime = bar.Time;
        entryReason = signal.Reason;
        entryBarIndex = barIndex;
        conductState = profile.UseConductExits
            ? new SailorConductExitState(entryTime, entryBarIndex, entryPrice, quantity, newPositionSide)
            : null;

        log($"{bar.Time:yyyy-MM-dd HH:mm} | {action} {quantity} {options.Symbol} @ {entryExecutionPrice:F2} | Reserved={reservedNotional:F2} | Cash={cash:F2} | {signal.Reason}");
        return true;
    }

    private static bool TryCloseByConductExit(
        BacktestBar bar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot indicator,
        int barIndex,
        BacktestOptions options,
        SailorStrategyProfile profile,
        SailorConductExitEngine conductExitEngine,
        List<BacktestTrade> trades,
        Action<string> log,
        ref decimal cash,
        ref bool hasOpenPosition,
        ref int quantity,
        ref int positionSide,
        ref decimal entryPrice,
        ref DateTimeOffset entryTime,
        ref string entryReason,
        ref int entryBarIndex,
        ref SailorConductExitState? conductState)
    {
        if (conductState is null)
        {
            conductState = new SailorConductExitState(entryTime, entryBarIndex, entryPrice, quantity, positionSide);
        }

        SailorConductExitDecision decision = conductExitEngine.EvaluateExit(
            positionSide,
            bar,
            previousBar,
            indicator,
            options,
            profile,
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
            positionSide,
            entryTime,
            entryPrice,
            entryReason,
            decision.Reason,
            ref cash,
            ref hasOpenPosition,
            ref quantity,
            ref positionSide,
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
        ref int positionSide,
        ref decimal entryPrice,
        ref DateTimeOffset entryTime,
        ref string entryReason,
        ref int entryBarIndex)
    {
        decimal stopPrice = positionSide < 0
            ? entryPrice * (1m + options.StopLossPercent / 100m)
            : entryPrice * (1m - options.StopLossPercent / 100m);
        decimal takeProfitPrice = positionSide < 0
            ? entryPrice * (1m - options.TakeProfitPercent / 100m)
            : entryPrice * (1m + options.TakeProfitPercent / 100m);
        int barsHeld = barIndex - entryBarIndex;

        bool stopHit = positionSide < 0 ? bar.High >= stopPrice : bar.Low <= stopPrice;
        bool takeProfitHit = positionSide < 0 ? bar.Low <= takeProfitPrice : bar.High >= takeProfitPrice;

        if (stopHit)
        {
            ClosePosition(
                trades,
                log,
                options.Symbol,
                bar.Time,
                stopPrice,
                quantity,
                positionSide,
                entryTime,
                entryPrice,
                entryReason,
                $"Stop loss hit at {stopPrice:F2}.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref positionSide,
                ref entryPrice,
                ref entryReason,
                ref entryBarIndex);

            return true;
        }

        if (takeProfitHit)
        {
            ClosePosition(
                trades,
                log,
                options.Symbol,
                bar.Time,
                takeProfitPrice,
                quantity,
                positionSide,
                entryTime,
                entryPrice,
                entryReason,
                $"Take profit hit at {takeProfitPrice:F2}.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref positionSide,
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
                positionSide,
                entryTime,
                entryPrice,
                entryReason,
                $"Max hold reached after {barsHeld} bars.",
                ref cash,
                ref hasOpenPosition,
                ref quantity,
                ref positionSide,
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
        int positionSide,
        DateTimeOffset entryTime,
        decimal entryPrice,
        string entryReason,
        string exitReason,
        ref decimal cash,
        ref bool hasOpenPosition,
        ref int openQuantity,
        ref int openPositionSide,
        ref decimal openEntryPrice,
        ref string openEntryReason,
        ref int entryBarIndex)
    {
        var trade = new BacktestTrade(
            Symbol: symbol,
            EntryTime: entryTime,
            ExitTime: exitTime,
            EntryPrice: entryPrice,
            ExitPrice: exitPrice,
            Quantity: quantity,
            EntryReason: entryReason,
            ExitReason: exitReason,
            PositionSide: positionSide);

        decimal cashRelease = positionSide < 0
            ? (quantity * entryPrice) + trade.Pnl
            : quantity * exitPrice;
        cash += cashRelease;

        trades.Add(trade);

        string action = positionSide < 0 ? "BUY_TO_COVER" : "SELL";
        log($"{exitTime:yyyy-MM-dd HH:mm} | {action} {quantity} {symbol} @ {exitPrice:F2} | Side={trade.SideName} | PnL={trade.Pnl:F2} ({trade.PnlPercent:F2}%) | Cash={cash:F2} | {exitReason}");

        hasOpenPosition = false;
        openQuantity = 0;
        openPositionSide = 0;
        openEntryPrice = 0m;
        openEntryReason = string.Empty;
        entryBarIndex = -1;
    }

    private static int ResolveEntrySide(
        BacktestSignal signal,
        IBacktestStrategy strategy,
        SailorStrategyProfile profile)
    {
        return signal.Type switch
        {
            BacktestSignalType.Buy when profile.SideMode.AllowsLong() => 1,
            BacktestSignalType.Sell when strategy.AllowsShortEntries && profile.SideMode.AllowsShort() => -1,
            _ => 0
        };
    }

    private static bool IsExitSignal(BacktestSignal signal, int positionSide)
    {
        return (positionSide > 0 && signal.Type == BacktestSignalType.Sell) ||
               (positionSide < 0 && signal.Type == BacktestSignalType.Buy);
    }

    private static bool CanOpenNewPosition(
        SailorStrategyProfile profile,
        BacktestBar bar,
        int barIndex,
        int lastExitBarIndex,
        out string rejectReason)
    {
        if (profile.MinimumBarsBetweenEntries > 0 &&
            lastExitBarIndex > -10_000 &&
            barIndex - lastExitBarIndex < profile.MinimumBarsBetweenEntries)
        {
            rejectReason = $"cooldown active, {barIndex - lastExitBarIndex} bars since last exit < required {profile.MinimumBarsBetweenEntries}.";
            return false;
        }

        if (profile.UseMarketHours)
        {
            int minute = MarketTime.GetEasternMinuteOfDay(bar.Time);
            int firstAllowedMinute = profile.MarketOpenMinute + profile.SkipFirstMinutes;

            if (minute < firstAllowedMinute)
            {
                rejectReason = $"before entry window, ET minute {minute} < first allowed {firstAllowedMinute}.";
                return false;
            }

            if (minute > profile.LastEntryMinute)
            {
                rejectReason = $"after last entry window, ET minute {minute} > last entry {profile.LastEntryMinute}.";
                return false;
            }

            if (minute >= profile.ForceFlatMinute)
            {
                rejectReason = $"inside force-flat window, ET minute {minute} >= force flat {profile.ForceFlatMinute}.";
                return false;
            }
        }

        rejectReason = string.Empty;
        return true;
    }

    private static bool ShouldForceFlat(SailorStrategyProfile profile, BacktestBar bar)
    {
        return profile.UseMarketHours && MarketTime.GetEasternMinuteOfDay(bar.Time) >= profile.ForceFlatMinute;
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
}
