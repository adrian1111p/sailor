# SAILOR-059 — Live paper per-iteration candle refresh

## Status

Implemented as a safety/data-correctness continuation after SAILOR-058.

## Problem fixed

SAILOR-058 correctly anchored a paper `--send-orders` run to the latest same-day candle at startup, but the active `PaperSymbolSession` kept an in-memory bar list. During a 60-minute paper run the conduct loop could therefore keep evaluating the same startup candle, for example `15:47`, on later heartbeats such as `15:49` and `15:50`.

This is not valid live paper decisioning. A live paper session must refresh the active symbol candles before each strategy decision.

## Implementation

Added:

- `PaperLiveCandleRefreshService`
- `PaperLiveCandleRefreshResult`
- mutable live-refresh support in `PaperSymbolSession`
- per-iteration refresh call in `PaperConductLoop`
- SAILOR-057 self-test scenario `live-per-iteration-candle-refresh`
- runtime safety settings for the refresh client and lookback

## Runtime behavior

When `paper run ... --send-orders` is active and `Runtime.Safety.LiveCandleRefreshEnabled=true`:

1. The conduct loop creates a dedicated historical-bar refresh provider.
2. The refresh provider uses a separate IBKR client id: `orderRouterClientId + LiveCandleRefreshClientIdOffset`.
3. Before each strategy decision iteration, Sailor requests fresh 1-minute history for every active paper symbol.
4. Each `PaperSymbolSession` replaces/merges its in-memory bars with the refreshed bars and anchors to the latest current same-day usable candle.
5. Strategy evaluation uses the refreshed frame without advancing through historical replay bars.
6. If the refresh cannot produce a current candle, SAILOR-058 still blocks entries after the configured max age. Exits and force-flat remain allowed.

## Default settings

```json
"LiveCandleRefreshEnabled": true,
"LiveCandleRefreshLookbackMinutes": 60,
"LiveCandleRefreshClientIdOffset": 200,
"LiveCandleRefreshRequestIdBase": 31000
```

For the default paper client id `22`, the refresh client id is therefore `222`, avoiding the order-router client id collision.

## Expected log evidence

At conduct-loop startup:

```text
SAILOR-059 live paper per-iteration candle refresh is active: provider=ibkr-api clientId=222 lookbackMinutes=60.
```

Each iteration:

```text
SAILOR-059 candle-refresh iteration=2 requested=5 updated=5 unchanged=0 stale=0 failed=0
candle-refresh: BIYA: refresh success=True updated=True current=True previous=... refreshedLast=... applied=...
```

The important proof is that subsequent iterations show candle times advancing with the market, not remaining on the startup candle.

## Validation command

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

The self-test suite should include and pass:

```text
live-per-iteration-candle-refresh: PASS
```
