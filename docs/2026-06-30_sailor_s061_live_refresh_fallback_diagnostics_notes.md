# SAILOR-061 — Live refresh fallback and diagnostics

Status: implemented.

## Problem

SAILOR-060 correctly moved live paper runtime data requests onto one shared IBKR data session, but a paper send-orders run could still move immediately to `CloseOnly` when the first per-iteration refresh returned zero bars for every active symbol. In the observed run, the startup conduct sessions already had current same-day bars, but the refresh layer failed before those bars aged out.

## Implemented behavior

1. Every SAILOR-059 refresh request now emits SAILOR-061 diagnostics containing the exact IBKR historical request parameters:
   - symbol;
   - duration;
   - bar size;
   - what-to-show;
   - RTH flag;
   - request id;
   - primary exchange;
   - end UTC;
   - provider/client id.
2. When a refresh returns zero bars or fails, the runtime may reuse the current in-memory decision bar as a fallback.
3. Fallback is allowed only while the in-memory bar passes the SAILOR-058 live current-candle age gate.
4. The runtime no longer moves to `CloseOnly` on the first failed refresh if a current fallback bar is still usable.
5. The runtime still moves to `CloseOnly` once all active symbols have no successful refresh and no current fallback bar remains usable.
6. A new direct diagnostic command tests the shared-data refresh path outside the full paper conduct loop:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper history-refresh-test TSLA --client-id 222 --lookback-minutes 60
```

## New settings

```json
"LiveCandleRefreshFallbackEnabled": true,
"LiveCandleRefreshDiagnosticsEnabled": true,
"LiveRefreshCloseOnlyAfterStale": true
```

## Self-test

The SAILOR-057 self-test suite now includes:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario live-refresh-fallback-diagnostics
```

## Expected runtime evidence

When IBKR refresh fails but the current in-memory bar is still fresh:

```text
SAILOR-061 live refresh fallback reused current in-memory bar ... after refresh failure
```

When the fallback bar becomes stale:

```text
SAILOR-061 live refresh fallback refused stale in-memory bar after refresh failure
SAILOR-061 live data refresh failed for all active symbol(s) and no current fallback bar remains usable. Runtime moved to CloseOnly
```

## Safety note

SAILOR-061 does not force trades. It only avoids premature CloseOnly while a previously loaded live bar is still current. Strategy filters, reconciliation gates, lifecycle policies, stale-data checks, and force-flat logic remain active.
