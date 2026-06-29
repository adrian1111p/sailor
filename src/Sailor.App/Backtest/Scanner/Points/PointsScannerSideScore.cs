namespace Sailor.App.Backtest.Scanner.Points;

public sealed record PointsScannerSideScore(
    string Side,
    decimal Score,
    bool SideEnabled,
    IReadOnlyList<PointsScannerFactor> Factors,
    IReadOnlyList<string> LegacyBlockReasons)
{
    public decimal PositivePoints => Factors.Where(factor => factor.Points > 0m).Sum(factor => factor.Points);

    public decimal NegativePoints => Factors.Where(factor => factor.Points < 0m).Sum(factor => factor.Points);

    public IReadOnlyList<PointsScannerFactor> PositiveFactors => Factors
        .Where(factor => factor.Points > 0m)
        .OrderByDescending(factor => factor.Points)
        .ToArray();

    public IReadOnlyList<PointsScannerFactor> NegativeFactors => Factors
        .Where(factor => factor.Points < 0m)
        .OrderBy(factor => factor.Points)
        .ToArray();

    public string TopPositiveFactors(int take = 5)
        => PositiveFactors.Count == 0
            ? "none"
            : string.Join("; ", PositiveFactors.Take(Math.Max(1, take)).Select(factor => factor.ToDisplayString()));

    public string TopNegativeFactors(int take = 5)
        => NegativeFactors.Count == 0
            ? "none"
            : string.Join("; ", NegativeFactors.Take(Math.Max(1, take)).Select(factor => factor.ToDisplayString()));
}
