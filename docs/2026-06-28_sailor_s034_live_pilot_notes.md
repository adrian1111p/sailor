# SAILOR-034 — Live pilot implementation notes

## Goal

SAILOR-034 adds the first guarded live-pilot path for one-symbol live trading. The implementation is intentionally conservative: live entry routing remains impossible unless the SAILOR-033 live-readiness gate passes and the live-pilot restrictions also pass.

## Implemented scope

- `live run 1m v21-15minutes 1 TSLA --confirm-live --operator-watching-tws`
- One explicit symbol only; universes/files/multiple symbols are blocked.
- One profile per run.
- Long-only by default. Short pilot requires both `Runtime.Live.AllowShort=true` and `--allow-short-live`.
- Max-notional enforcement before live entry intents are routed.
- Live pilot consumes the SAILOR-033 gate:
  - `Runtime.Live.AllowLiveTrading=true`
  - `--confirm-live`
  - recent promotable paper certification report
  - matching account
  - requested max notional within configured maximum
- Pre-run live reconciliation is required before entry routing.
- Final live reconciliation is required after the pilot.
- End exposure must be zero to produce a promotable live-pilot report.
- `live flatten SYMBOL` now exists as a close-only command.
- Live pilot artifacts are written under `logs/Live/Pilot`.

## Commands

Blocked/safe default smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live run 1m v21-15minutes 1 TSLA --confirm-live --operator-watching-tws --account DUN559573 --max-notional 100 --iterations 5
```

Expected with default config:

```text
Runtime.Live.AllowLiveTrading=false
live pilot blocked
no live order sent
live_pilot_latest.json produced
```

Readiness/pilot evidence:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live readiness --account DUN559573 --max-notional 100 --confirm-live
```

Close-only flatten dry run:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live flatten TSLA --account DUN559573 --confirm-live --dry-run
```

Close-only live flatten, only after deliberate operator approval and config enablement:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live flatten TSLA --account DUN559573 --send-orders --confirm-live
```

## Live-pilot safety rules

The pilot blocks before any live order route when any of these are false:

1. Paper certification is promotable.
2. Paper certification is recent enough.
3. Live account matches the paper certification account.
4. Requested max notional is small enough.
5. Live config explicitly enables live trading.
6. Operator passes `--confirm-live`.
7. Operator passes `--operator-watching-tws`.
8. Command uses exactly one explicit symbol.
9. Pre-run live broker reconciliation is clean.

## Artifacts

```text
logs/Live/Readiness/live_readiness_latest.json
logs/Live/Readiness/live_readiness_YYYYMMDD.csv
logs/Live/Pilot/live_pilot_latest.json
logs/Live/Pilot/live_pilot_YYYYMMDD.csv
logs/Live/Runtime/live_run_YYYYMMDD_HHMMSS.log
logs/Live/Orders/orders_YYYYMMDD.csv
state/live/order-ledger.jsonl
state/live/reconciliation.json
```

## Important current limitation

The first pilot uses the same runtime strategy and order-intent path as paper. It is intentionally small and must remain operator-supervised in TWS. Do not enable `Runtime.Live.AllowLiveTrading` until the paper report is promotable, live account is correct, max notional is suitable, and old paper/live open orders are cleared.

## v4 cleanup

- Corrected live flatten runtime display so the command summary shows the configured default timeframe instead of parsing `--account` as a positional timeframe.
- Corrected disabled broker-state provider wording so live-mode flatten refers to live broker positions/open orders/executions, not paper positions.
