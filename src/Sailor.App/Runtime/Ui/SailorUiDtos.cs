namespace Sailor.App.Runtime.Ui;

public sealed record SailorUiSnapshot(
    string Mode,
    DateTimeOffset ObservedUtc,
    string Status,
    SailorUiPnlSection Pnl,
    IReadOnlyList<SailorUiTradeRow> ActiveRows,
    IReadOnlyList<SailorUiScannerRow> ScannerRows,
    IReadOnlyList<SailorUiStrategyOption> Strategies,
    int MaxActiveStrategies,
    int RefreshMilliseconds,
    bool ControlsEnabled,
    string ControlMode,
    IReadOnlyList<string> ActiveDesiredStrategies,
    string DesiredStateUpdatedUtc,
    string SourceSummary,
    IReadOnlyList<string> Warnings);

public sealed record SailorUiPnlSection(
    decimal DailyPnl,
    decimal Unrealized,
    decimal Realized,
    string Currency,
    bool Stale,
    string StaleReason);

public sealed record SailorUiTradeRow(
    decimal DailyPnl,
    int? ScanRanking,
    string Symbol,
    int Position,
    decimal MarketValue,
    decimal BuyValue,
    decimal Open,
    decimal Price,
    bool PriceStale,
    string PriceSource,
    bool TradeEnabled,
    string Strategy,
    IReadOnlyList<SailorUiStrategyOption> StrategyOptions,
    long Volume,
    string Status,
    string Reason);

public sealed record SailorUiScannerRow(
    int ScanRanking,
    string Symbol,
    bool TradeEnabled,
    string Strategy,
    IReadOnlyList<SailorUiStrategyOption> StrategyOptions,
    long Volume,
    decimal Price,
    bool PriceStale,
    string SelectedSide,
    decimal FinalScore,
    string Status,
    string Reason);

public sealed record SailorUiStrategyOption(
    string Strategy,
    string ProfileName,
    string Variant,
    string Style,
    decimal TotalPnl,
    int Trades,
    decimal WinRate,
    decimal ProfitFactor);

internal sealed record SailorUiScannerCandidate(
    int Rank,
    string Symbol,
    string Status,
    string SelectedSide,
    decimal FinalScore,
    decimal Price,
    long Volume,
    bool PriceStale,
    string Reason);
