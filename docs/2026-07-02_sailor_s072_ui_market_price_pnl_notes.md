# SAILOR-072 — SailorUI market price and DLY P&L correction

Date: 2026-07-02

## Purpose

SAILOR-072 fixes the SailorUI active/today trade rows where broker/open positions could show:

- `DLY P&L = 0.00` even while TWS showed a daily unrealized gain/loss; and
- `Price = Open` or `0.00` instead of the actual current/exit price.

The issue was caused by SailorUI using the broker mirror average cost as the final fallback price when no scanner candidate price existed for an active broker/manual position.

## Scope

SailorUI remains read-only for broker actions. This milestone does not send orders and does not change paper/live routing. It only improves how SailorUI resolves display prices and P&L from files already written by Sailor runtime/market-data capture.

## Price source order

For each Section 2 active/today row, SailorUI now resolves `Price` in this order:

1. latest L1 market snapshot CSV from `logs/{Paper|Live}/Snapshots/snapshot_*.csv`, using `Last` when available;
2. bid/ask midpoint from the same snapshot when `Last` is unavailable;
3. scanner current 1-minute decision close;
4. broker open/average price fallback.

The row `PriceSource` identifies which source was used.

## DLY P&L formula

For active rows with position quantity and open price:

```text
DLY P&L = (Price - Open) × signed quantity
```

Examples:

```text
LONG  +100, Open=10.00, Price=10.50  => +50.00
SHORT -750, Open=1.3448, Price=1.2100 => +101.10
```

This means short rows become green/winning when actual price is below open and red/losing when actual price is above open.

## Stale marking

Market snapshot prices are marked stale when older than the configured UI market snapshot fresh window. Stale prices still show with the `*` marker.

## Self-test

Added scenario:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario sailor-ui-market-price-pnl
```

Expected after SAILOR-072:

```text
PASS passed=22/22
```
