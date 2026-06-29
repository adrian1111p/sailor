using System.Globalization;

namespace Sailor.App.Backtest.Scanner.Points;

public sealed record PointsScannerFactor(
    string Code,
    string Description,
    decimal Points,
    string RawValue,
    string Category)
{
    public bool IsPositive => Points > 0m;

    public bool IsPenalty => Points < 0m;

    public string ToDisplayString()
    {
        string sign = Points > 0m ? "+" : string.Empty;
        string raw = string.IsNullOrWhiteSpace(RawValue) ? string.Empty : $" [{RawValue}]";
        return $"{sign}{Points.ToString("0.##", CultureInfo.InvariantCulture)} {Code}: {Description}{raw}";
    }
}
