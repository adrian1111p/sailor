namespace Sailor.App.Backtest.Scanner.Points;

public enum PointsScannerMode
{
    LegacyBlocks = 0,
    PointsOnly = 1,
    HybridCompare = 2
}

public static class PointsScannerModeExtensions
{
    public static string ToConfigValue(this PointsScannerMode mode)
        => mode switch
        {
            PointsScannerMode.PointsOnly => "points-only",
            PointsScannerMode.HybridCompare => "hybrid-compare",
            _ => "legacy-blocks"
        };

    public static PointsScannerMode ParseOrDefault(string? value, PointsScannerMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string normalized = value.Trim()
            .Replace("_", "-", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        return normalized switch
        {
            "legacy" or "legacy-block" or "legacy-blocks" or "blocks" => PointsScannerMode.LegacyBlocks,
            "points" or "points-only" or "pointsonly" or "no-blocks" or "noblocks" => PointsScannerMode.PointsOnly,
            "hybrid" or "hybrid-compare" or "hybridcompare" or "compare" => PointsScannerMode.HybridCompare,
            _ => fallback
        };
    }
}
