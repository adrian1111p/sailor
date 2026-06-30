using Sailor.App.Backtest.Models;
using Sailor.App.Backtest.Profiles;

namespace Sailor.App.Backtest.Scanner.Points;

/// <summary>
/// Compatibility shim for the original SAILOR-046 V18-Silver scorer.
///
/// The scoring factors are now shared by all strategy profiles through
/// PointsScannerCommonStrategyScoring. Keep this class so older docs, tests,
/// or future refactors that reference the old type name still receive the same
/// common profile scoring instead of a V18-only implementation.
/// </summary>
public static class PointsScannerV18SilverScoring
{
    public static IReadOnlyList<PointsScannerFactor> Score(
        BacktestBar latestBar,
        BacktestBar? previousBar,
        BacktestIndicatorSnapshot latestIndicators,
        SailorStrategyProfile profile,
        PointsScannerSettings settings,
        bool isShort,
        decimal volumeRatio)
        => PointsScannerCommonStrategyScoring.Score(
            latestBar,
            previousBar,
            latestIndicators,
            profile,
            settings,
            isShort,
            volumeRatio);
}
