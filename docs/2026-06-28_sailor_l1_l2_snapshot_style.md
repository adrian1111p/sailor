# 2026-06-28 Sailor L1/L2 Snapshot Layer

## Purpose

This update introduces a Sailor-native L1/L2/live snapshot model without importing Harvester live/paper runtime classes.

The goal is to keep the strategy code independent from IBKR/TWS and still allow the same strategy logic to consume market context in three modes:

1. Backtest synthetic snapshot derived from 1m bars.
2. Future paper runtime snapshot from IBKR/TWS.
3. Future live runtime snapshot from IBKR/TWS.

## What was intentionally not imported from Harvester

Harvester has live/paper runtime structures such as live feature snapshots, candle snapshots, runtime order-book state, and IBKR-specific market data reducers.

Sailor does not import those classes. Instead, Sailor now has neutral market-data records:

- `L1QuoteSnapshot`
- `L2OrderBookLevel`
- `L2OrderBookSnapshot`
- `SailorMarketSnapshot`
- `SailorMarketSnapshotQuality`

These are located in:

```text
src/Sailor.App/MarketData/Snapshots
```

## Backtest synthetic snapshots

Backtest now creates synthetic L1/L2 snapshots from the loaded 1m bars:

```text
src/Sailor.App/Backtest/Snapshots/SyntheticBacktestMarketSnapshotProvider.cs
```

The synthetic snapshot estimates:

- bid
- ask
- spread bps
- bid size
- ask size
- L2 depth levels
- book imbalance
- liquidity score

Important: this is not real historical L2. It is an advisory replay approximation designed to exercise the same strategy API that future paper/live snapshots will use.

## Suitable strategies

The first L1/L2 integration is applied to Sailor conduct-style profiles that are suitable for microstructure filtering:

- `sailor-trend-volume`
- `sailor-conduct-v3`
- `conduct-v3`
- `harvester-conduct-v3`
- `harvester-conduct-v9`
- `v16-sqzbreakout`
- `v13`
- `v10-hybrid`
- `v17-hybridflow`
- `v2-conduct`
- `v18-silver`
- `v1-first`
- `v19-purplecloud`
- `v15-shortcap`
- `v14-smallcap`
- `v20-gen001-choppyshield`
- `v12`

The angle strategies `v21-15minutes`, `v22-15minutes`, `v23-5minutes`, and `v24-5minutes` remain primarily higher-timeframe EMA-angle conduct strategies. They are not filtered by L1/L2 by default because their core decision is 15m/5m candle based.

## Entry guard behavior

Sailor conduct strategies now support snapshot-aware entry evaluation through:

```text
ISailorSnapshotAwareEntryStrategy
SailorSnapshotEntryGuard
```

The guard can check:

- spread bps
- liquidity score
- L1 size imbalance
- L2 book imbalance
- profile suitability

Default behavior is safe for research:

```json
"SyntheticSnapshotsAreAdvisoryOnly": true
```

That means the synthetic backtest L1/L2 information is appended to entry reasons and logs, but does not reject trades. This avoids changing the historical backtest too aggressively with fake L2 data.

For paper/live later, set:

```json
"SyntheticSnapshotsAreAdvisoryOnly": false
```

or feed real snapshots. Then the same guard can reject entries with bad spread, weak liquidity, or adverse book imbalance.

## Exit guard behavior

The conduct exit engine also accepts snapshots. Exit guards are disabled by default:

```json
"EnableExitGuards": false
```

When enabled with real snapshots, they can flatten a position if book imbalance becomes strongly adverse.

## Why this style is better

This style keeps Sailor clean:

- no IBKR dependency inside strategies
- no Harvester runtime classes imported
- backtest/paper/live can use the same neutral snapshot contract
- L1/L2 can be advisory in research and strict in paper/live
- strategies stay scanner-independent

## Validation commands

```powershell
cd D:\Site\sailor

dotnet clean
dotnet build

dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m v16-sqzbreakout
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m v21-15minutes
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps
```

Expected log entries include:

```text
L1/L2 snapshots: syntheticBacktest=True, entryGuards=True, exitGuards=False, advisorySynthetic=True
L1/L2 snapshot check | SyntheticBacktest | L1 bid=... ask=... spread=... | L2 imbalance=...
```
