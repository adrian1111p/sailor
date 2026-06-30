# SAILOR-058 — Live paper current-candle guard

Implemented on 2026-06-30 after the first paper send-orders run showed the conduct loop evaluating stale `2026-06-29` bars during the 2026-06-30 live session.

## What changed

- Paper send-orders sessions now prefer the latest current same-day candle as the conduct anchor.
- If no current same-day candle exists, the session still starts safely but the conduct loop blocks entries/evaluation with a clear stale-bar reason.
- Runtime force-flat checks in paper send-orders mode now use the live runtime clock instead of the historical frame timestamp.
- Active-session logs now print bar count, first/last loaded bar time, and the selected start reason.
- The deterministic self-test suite includes `live-current-candle-guard`.

## Safety behavior

The gate is active only when `paper run` is in send-orders mode and `Runtime.Safety.BlockStaleHistoricalReplay=true`. Dry-run/local replay keeps historical behavior for smoke testing.

A frame is blocked when one of these is true:

- the frame's Eastern trading date differs from the current Eastern trading date;
- the frame is older than `LiveBarMaxAgeMinutes`;
- the frame is ahead of the runtime clock by more than `LiveBarFutureToleranceMinutes`.

## Operational effect

If IBKR/history refresh does not deliver current 1m bars, Sailor now fails safe. The log will show:

```text
SAILOR-058 live-paper current-candle gate blocked stale historical replay
```

This means no automatic entry should be sent until the data layer provides a current live candle.

## Next possible improvement

Build and append live 1m bars from streaming quote/trade snapshots during the conduct loop, so paper send-orders can continue even when historical refresh lags.
