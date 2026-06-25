namespace Sailor.App.Backtest;

/// <summary>
/// Shared helpers shared across backtest strategies: trading-day grouping,
/// opening-range computation, binary search over enriched bar arrays,
/// entry-window checks, and position-size computation.
/// </summary>
internal static class BacktestHelpers
{
    /// <summary>Day group produced by <see cref="GroupByTradingDayEt"/>.</summary>
    internal sealed record DayGroup(DateOnly DateEt, int StartIdx, int EndIdx);

    /// <summary>
    /// Group enriched bars by exchange trading day (Eastern Time).
    /// Returns day groups with inclusive-start, exclusive-end indices.
    /// </summary>
    internal static List<DayGroup> GroupByTradingDayEt(EnrichedBar[] bars)
    {
        var groups = new List<DayGroup>();
        if (bars.Length == 0) return groups;

        int start = 0;
        DateOnly currentDay = TradingTime.GetDateEt(bars[0].Bar.Timestamp);

        for (int i = 1; i < bars.Length; i++)
        {
            var day = TradingTime.GetDateEt(bars[i].Bar.Timestamp);
            if (day != currentDay)
            {
                groups.Add(new DayGroup(currentDay, start, i));
                start = i;
                currentDay = day;
            }
        }
        groups.Add(new DayGroup(currentDay, start, bars.Length));
        return groups;
    }

    /// <summary>
    /// Compute the opening range (high, low, end-index) for an ET trading day.
    /// Ignores premarket bars before <paramref name="marketOpenMinute"/>.
    /// </summary>
    internal static (double OrHigh, double OrLow, int OrEndIdx) ComputeOpeningRangeEt(
        int dayStartIdx, int dayEndIdx, EnrichedBar[] allBars,
        int marketOpenMinute, int orMinutes)
    {
        int orEndMinute = marketOpenMinute + orMinutes;

        double orHigh = double.MinValue;
        double orLow = double.MaxValue;
        int orEndIdx = -1;

        for (int i = dayStartIdx; i < dayEndIdx && i < allBars.Length; i++)
        {
            int minute = TradingTime.GetMinuteOfDayEt(allBars[i].Bar.Timestamp);

            if (minute < marketOpenMinute) continue;

            if (minute >= orEndMinute)
            {
                orEndIdx = i;
                break;
            }

            orHigh = Math.Max(orHigh, allBars[i].Bar.High);
            orLow = Math.Min(orLow, allBars[i].Bar.Low);
        }

        if (orEndIdx < 0) return (double.NaN, double.NaN, -1);
        if (orHigh == double.MinValue || orLow == double.MaxValue) return (double.NaN, double.NaN, -1);

        return (orHigh, orLow, orEndIdx);
    }

    /// <summary>
    /// Binary-search for the last bar whose timestamp is â‰¤ <paramref name="ts"/>.
    /// Returns -1 if no such bar exists or if <paramref name="bars"/> is null/empty.
    /// </summary>
    internal static int FindBarAtOrBefore(EnrichedBar[]? bars, DateTime ts)
    {
        if (bars is null || bars.Length == 0) return -1;
        int lo = 0, hi = bars.Length - 1, best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (bars[mid].Bar.Timestamp <= ts) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best;
    }

    /// <summary>
    /// Check whether <paramref name="minuteEt"/> falls inside any of the entry windows.
    /// </summary>
    internal static bool InEntryWindow(int minuteEt, IReadOnlyList<(int Start, int End)> windows)
    {
        foreach (var (start, end) in windows)
        {
            if (minuteEt >= start && minuteEt <= end) return true;
        }
        return false;
    }

    /// <summary>
    /// Compute position size from risk budget and notional caps.
    /// </summary>
    internal static int ComputePositionSize(
        double entryPrice,
        double riskPerShare,
        double riskPerTradeDollars,
        double accountSize,
        double maxPositionNotionalPctOfAccount,
        int maxShares)
    {
        int qtyByRisk = Math.Max(1, (int)(riskPerTradeDollars / riskPerShare));

        double maxNotional = Math.Max(0.0, accountSize * maxPositionNotionalPctOfAccount);
        int qtyByNotional = maxNotional > 0 && entryPrice > 0
            ? Math.Max(1, (int)(maxNotional / entryPrice))
            : maxShares;

        int qty = Math.Min(qtyByRisk, qtyByNotional);
        qty = Math.Min(qty, maxShares);
        return Math.Max(1, qty);
    }
}

