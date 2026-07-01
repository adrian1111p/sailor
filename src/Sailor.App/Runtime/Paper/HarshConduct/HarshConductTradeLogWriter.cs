using System.Globalization;
using Sailor.App.Broker.Orders;
using Sailor.App.Logging;
using Sailor.App.Runtime.Common;
using Sailor.App.Strategy.Runtime;

namespace Sailor.App.Runtime.Paper;

public sealed record HarshConductTradeEvent(
    DateTimeOffset TimeUtc,
    string Mode,
    string Strategy,
    string Variant,
    string Style,
    int Iteration,
    string Symbol,
    string ScannerSide,
    SailorStrategyDecisionType Decision,
    SailorOrderSide OrderSide,
    SailorOrderType OrderType,
    int Quantity,
    decimal ReferencePrice,
    decimal AverageFillPrice,
    int FilledQuantity,
    SailorOrderStatus Status,
    bool SentToBroker,
    int PositionBefore,
    int PositionAfter,
    decimal RealizedPnl,
    string Reason);

public sealed record HarshConductSummary(
    DateTimeOffset TimeUtc,
    string Strategy,
    string Variant,
    string Style,
    string Symbols,
    int Trades,
    bool AtLeast50,
    decimal WinRate,
    decimal ProfitFactor,
    decimal Sharpe,
    decimal EquitySharpe,
    decimal EquitySortino,
    decimal EquityDownsideDeviation,
    decimal TotalPnl,
    decimal MaxDrawdown,
    decimal AverageWin,
    decimal AverageLoss,
    decimal Expectancy,
    int GovernanceStops,
    string GovernanceReason);

public sealed class HarshConductTradeTracker
{
    private readonly List<decimal> _closedPnls = new();
    private readonly List<decimal> _equityCurve = new();
    private decimal _equity;
    private decimal _peakEquity;
    private decimal _maxDrawdown;

    public int EntryTrades { get; private set; }

    public decimal Record(
        SailorOrderIntent intent,
        SailorOrderReceipt receipt,
        SailorStrategyPositionContext positionBefore,
        int positionAfter,
        decimal fallbackFillPrice,
        bool assumeDryRunFill)
    {
        int fillQuantity = receipt.FilledQuantity;
        decimal fillPrice = receipt.AverageFillPrice > 0m ? receipt.AverageFillPrice : fallbackFillPrice;
        if (assumeDryRunFill && receipt.Status == SailorOrderStatus.DryRun)
        {
            fillQuantity = intent.Quantity;
            fillPrice = fallbackFillPrice;
        }

        if (fillQuantity <= 0)
        {
            return 0m;
        }

        if (!positionBefore.HasOpenPosition && (intent.Side is SailorOrderSide.Buy or SailorOrderSide.SellShort))
        {
            EntryTrades++;
            return 0m;
        }

        if (!positionBefore.HasOpenPosition)
        {
            return 0m;
        }

        decimal realized = 0m;
        int closingQuantity = Math.Min(Math.Abs(positionBefore.Quantity), fillQuantity);
        if (closingQuantity <= 0)
        {
            return 0m;
        }

        if (positionBefore.Quantity > 0 && intent.Side == SailorOrderSide.Sell)
        {
            realized = (fillPrice - positionBefore.AveragePrice) * closingQuantity;
        }
        else if (positionBefore.Quantity < 0 && intent.Side == SailorOrderSide.BuyToCover)
        {
            realized = (positionBefore.AveragePrice - fillPrice) * closingQuantity;
        }

        if (realized != 0m || positionAfter == 0)
        {
            _closedPnls.Add(realized);
            _equity += realized;
            _equityCurve.Add(_equity);
            _peakEquity = Math.Max(_peakEquity, _equity);
            _maxDrawdown = Math.Min(_maxDrawdown, _equity - _peakEquity);
        }

        return realized;
    }

    public HarshConductSummary BuildSummary(
        string strategy,
        string variant,
        string style,
        IEnumerable<string> activeSymbols,
        int governanceStops,
        string governanceReason)
    {
        decimal wins = _closedPnls.Count(pnl => pnl > 0m);
        decimal losses = _closedPnls.Count(pnl => pnl < 0m);
        decimal totalClosed = _closedPnls.Count;
        decimal grossWin = _closedPnls.Where(pnl => pnl > 0m).Sum();
        decimal grossLoss = Math.Abs(_closedPnls.Where(pnl => pnl < 0m).Sum());
        decimal totalPnl = _closedPnls.Sum();
        decimal avgWin = wins > 0 ? grossWin / wins : 0m;
        decimal avgLoss = losses > 0 ? -grossLoss / losses : 0m;
        decimal expectancy = totalClosed > 0 ? totalPnl / totalClosed : 0m;

        return new HarshConductSummary(
            DateTimeOffset.UtcNow,
            strategy,
            variant,
            style,
            string.Join(" ", activeSymbols.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)),
            EntryTrades,
            EntryTrades >= 50,
            totalClosed > 0 ? wins / totalClosed : 0m,
            grossLoss > 0m ? grossWin / grossLoss : grossWin > 0m ? 999m : 0m,
            CalculateSharpe(_closedPnls),
            CalculateSharpe(_equityCurve),
            CalculateSortino(_equityCurve),
            CalculateDownsideDeviation(_equityCurve),
            totalPnl,
            Math.Abs(_maxDrawdown),
            avgWin,
            avgLoss,
            expectancy,
            governanceStops,
            string.IsNullOrWhiteSpace(governanceReason) ? "none" : governanceReason.Trim());
    }

    private static decimal CalculateSharpe(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        decimal avg = values.Average();
        decimal variance = values.Select(value => (value - avg) * (value - avg)).Average();
        double stdDev = Math.Sqrt((double)variance);
        return stdDev <= 0d ? 0m : avg / (decimal)stdDev;
    }

    private static decimal CalculateSortino(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        decimal avg = values.Average();
        decimal downsideDeviation = CalculateDownsideDeviation(values);
        return downsideDeviation <= 0m ? 0m : avg / downsideDeviation;
    }

    private static decimal CalculateDownsideDeviation(IReadOnlyList<decimal> values)
    {
        decimal[] downside = values.Where(value => value < 0m).ToArray();
        if (downside.Length == 0)
        {
            return 0m;
        }

        decimal variance = downside.Select(value => value * value).Average();
        return (decimal)Math.Sqrt((double)variance);
    }
}

public sealed class HarshConductTradeLogWriter
{
    private static readonly string TradeHeader = "TimeUtc,Mode,Strategy,Variant,Style,Iteration,Symbol,ScannerSide,Decision,OrderSide,OrderType,Quantity,ReferencePrice,AverageFillPrice,FilledQuantity,Status,SentToBroker,PositionBefore,PositionAfter,RealizedPnL,Reason";
    private static readonly string SummaryHeader = "Strategy,Variant,Style,Symbols,Trades,>=50,WinRate,PF,Sharpe,EqSharpe,EqSortino,EqDownDev,TotalPnL$,MaxDD$,AvgWin$,AvgLoss$,Expectancy,GovStops,GovReason";

    public HarshConductTradeLogWriter(SailorRuntimeMode mode)
    {
        string root = mode == SailorRuntimeMode.Live ? SailorLogPaths.Live : SailorLogPaths.Paper;
        DirectoryPath = Path.Combine(root, "HarshConduct");
        Directory.CreateDirectory(DirectoryPath);
        string date = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        TradeCsvPath = Path.Combine(DirectoryPath, $"harsh_conduct_trades_{date}.csv");
        LatestTradeCsvPath = Path.Combine(DirectoryPath, "harsh_conduct_trades_latest.csv");
        SummaryCsvPath = Path.Combine(DirectoryPath, $"harsh_conduct_summary_{date}.csv");
        LatestSummaryCsvPath = Path.Combine(DirectoryPath, "harsh_conduct_summary_latest.csv");
        EnsureHeader(TradeCsvPath, TradeHeader);
        File.WriteAllText(LatestTradeCsvPath, TradeHeader + Environment.NewLine);
        EnsureHeader(SummaryCsvPath, SummaryHeader);
    }

    public string DirectoryPath { get; }

    public string TradeCsvPath { get; }

    public string LatestTradeCsvPath { get; }

    public string SummaryCsvPath { get; }

    public string LatestSummaryCsvPath { get; }

    public void AppendTrade(HarshConductTradeEvent trade)
    {
        string row = string.Join(',',
            Csv(trade.TimeUtc.ToString("O", CultureInfo.InvariantCulture)),
            Csv(trade.Mode),
            Csv(trade.Strategy),
            Csv(trade.Variant),
            Csv(trade.Style),
            trade.Iteration.ToString(CultureInfo.InvariantCulture),
            Csv(trade.Symbol),
            Csv(trade.ScannerSide),
            Csv(trade.Decision.ToString()),
            Csv(trade.OrderSide.ToString()),
            Csv(trade.OrderType.ToString()),
            trade.Quantity.ToString(CultureInfo.InvariantCulture),
            trade.ReferencePrice.ToString("0.####", CultureInfo.InvariantCulture),
            trade.AverageFillPrice.ToString("0.####", CultureInfo.InvariantCulture),
            trade.FilledQuantity.ToString(CultureInfo.InvariantCulture),
            Csv(trade.Status.ToString()),
            trade.SentToBroker ? "true" : "false",
            trade.PositionBefore.ToString(CultureInfo.InvariantCulture),
            trade.PositionAfter.ToString(CultureInfo.InvariantCulture),
            trade.RealizedPnl.ToString("0.####", CultureInfo.InvariantCulture),
            Csv(trade.Reason));

        File.AppendAllText(TradeCsvPath, row + Environment.NewLine);
        File.AppendAllText(LatestTradeCsvPath, row + Environment.NewLine);
    }

    public void WriteSummary(HarshConductSummary summary)
    {
        string row = string.Join(',',
            Csv(summary.Strategy),
            Csv(summary.Variant),
            Csv(summary.Style),
            Csv(summary.Symbols),
            summary.Trades.ToString(CultureInfo.InvariantCulture),
            summary.AtLeast50 ? "true" : "false",
            summary.WinRate.ToString("0.####", CultureInfo.InvariantCulture),
            summary.ProfitFactor.ToString("0.####", CultureInfo.InvariantCulture),
            summary.Sharpe.ToString("0.####", CultureInfo.InvariantCulture),
            summary.EquitySharpe.ToString("0.####", CultureInfo.InvariantCulture),
            summary.EquitySortino.ToString("0.####", CultureInfo.InvariantCulture),
            summary.EquityDownsideDeviation.ToString("0.####", CultureInfo.InvariantCulture),
            summary.TotalPnl.ToString("0.####", CultureInfo.InvariantCulture),
            summary.MaxDrawdown.ToString("0.####", CultureInfo.InvariantCulture),
            summary.AverageWin.ToString("0.####", CultureInfo.InvariantCulture),
            summary.AverageLoss.ToString("0.####", CultureInfo.InvariantCulture),
            summary.Expectancy.ToString("0.####", CultureInfo.InvariantCulture),
            summary.GovernanceStops.ToString(CultureInfo.InvariantCulture),
            Csv(summary.GovernanceReason));

        File.AppendAllText(SummaryCsvPath, row + Environment.NewLine);
        File.WriteAllText(LatestSummaryCsvPath, SummaryHeader + Environment.NewLine + row + Environment.NewLine);
    }

    private static void EnsureHeader(string path, string header)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            File.WriteAllText(path, header + Environment.NewLine);
        }
    }

    private static string Csv(string value)
    {
        string safe = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        if (safe.Contains(',') || safe.Contains('"'))
        {
            return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return safe;
    }
}
