namespace Sailor.App.Backtest.Scanner.Points;

public sealed class PointsScannerSettings
{
    public decimal MinimumBarsFullPoints { get; init; } = 10m;

    public decimal MinimumBarsPartialPoints { get; init; } = 5m;

    public decimal CriticalBarsPenalty { get; init; } = -20m;

    public decimal PriceRangePoints { get; init; } = 8m;

    public decimal PriceBelowMinimumPenalty { get; init; } = -15m;

    public decimal PriceAboveMaximumPenalty { get; init; } = -10m;

    public decimal VolumeFullPoints { get; init; } = 10m;

    public decimal VolumeMaximumPenalty { get; init; } = -20m;

    public decimal VolumeRatioStrongPoints { get; init; } = 15m;

    public decimal VolumeRatioBasePoints { get; init; } = 5m;

    public decimal VolumeRatioWeakPenalty { get; init; } = -15m;

    public decimal MissingVolumeAveragePenalty { get; init; } = -8m;

    public decimal MomentumWeight { get; init; } = 20m;

    public decimal MaximumMomentumPoints { get; init; } = 20m;

    public decimal EmaTrendPoints { get; init; } = 8m;

    public decimal MissingEmaPenalty { get; init; } = -4m;

    public decimal VwapPositionPoints { get; init; } = 8m;

    public decimal MissingVwapPenalty { get; init; } = -4m;

    public decimal Sma200Points { get; init; } = 4m;

    public decimal MissingSma200Penalty { get; init; } = -2m;

    public decimal CandleColorPoints { get; init; } = 10m;

    public decimal OppositeCandleColorPenalty { get; init; } = -6m;

    public decimal BarToBarMomentumPoints { get; init; } = 12m;

    public decimal VwapReversionPoints { get; init; } = 8m;

    public decimal ChoppyShieldPoints { get; init; } = 6m;

    public decimal V18VolumeRatioProfilePoints { get; init; } = 8m;

    public decimal VwapExtensionWithinLimitPoints { get; init; } = 6m;

    public decimal VwapExtensionMaximumPenalty { get; init; } = -15m;

    public decimal ReadyThreshold { get; init; } = 60m;

    public decimal WeakReadyThreshold { get; init; } = 45m;

    public decimal WatchOnlyThreshold { get; init; } = 25m;

    public static PointsScannerSettings Default { get; } = new();
}
