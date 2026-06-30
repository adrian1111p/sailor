# 2026-06-30 SAILOR-052 — Broker state mirror and manual trade detector

## Scope completed

SAILOR-052 adds the second foundation layer for full order/trade management after SAILOR-051.

The milestone is evidence-first and safe:

- it requests broker positions, open orders, and recent executions through the existing reconciliation provider;
- it writes a persistent broker mirror snapshot;
- it compares broker state with the SAILOR-051 trade lifecycle registry;
- it classifies broker/manual/external ownership events;
- it updates the lifecycle registry when broker state proves a position exists or has disappeared;
- it does not send orders;
- it does not change strategy entry/exit behavior;
- it does not yet replenish scanner slots.

## New commands

Broker mirror via the trade-management command group:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trades mirror --account DUN559573 --wait-seconds 15
```

Equivalent broker command group:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper broker mirror --account DUN559573 --wait-seconds 15
```

Manual intraday classification mode, used while a strategy is already running or after an operator intentionally wants unknown broker positions classified as intraday manual activity:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper broker mirror --account DUN559573 --intraday --wait-seconds 15
```

Local-only smoke test without requesting TWS broker state:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper broker mirror --local-only
```

## New persistence paths

Broker mirror snapshots:

```text
state/paper/broker-mirror/broker_state_mirror_latest.json
state/paper/broker-mirror/broker_state_mirror_yyyyMMdd.jsonl
state/live/broker-mirror/broker_state_mirror_latest.json
state/live/broker-mirror/broker_state_mirror_yyyyMMdd.jsonl
```

The existing SAILOR-051 registry remains the lifecycle source of truth:

```text
state/paper/trades/trade_registry_latest.json
state/paper/trades/trade_registry_yyyyMMdd.jsonl
state/live/trades/trade_registry_latest.json
state/live/trades/trade_registry_yyyyMMdd.jsonl
```

## Classification model

SAILOR-052 emits detections with these meanings:

```text
sailor-owned-position-synchronized
manual-pre-start-position-registered
manual-intraday-position-registered
manual-close-detected
external-open-order-detected
external-execution-detected
broker-flat-confirmed
broker-mirror-warning
```

### Pre-start broker/manual positions

If broker mirror sees a non-flat broker position and no matching lifecycle exists, the default classification is:

```text
ManualPreStart
```

This covers TWS positions that already existed before Sailor started.

### Intraday manual positions

If the command is run with `--intraday`, unknown broker positions are classified as:

```text
ManualIntraday
```

This is the ownership marker for manual trades opened in TWS after Sailor was already running.

### Manual close / stop-for-day

If the lifecycle registry has an active non-flat trade but the broker mirror no longer reports that symbol as non-flat, the detector marks the lifecycle as:

```text
ClosedManually
ManualStoppedForDay = true
```

This supports the rule:

```text
If I close one trade manually in TWS, stop the trade completely for the rest of the day.
```

Future SAILOR-053/054 work must consume this registry flag and block re-entry for that lifecycle unless the scanner explicitly opens a new scanner-owned trade under the permitted re-entry rule.

## Runtime integration

`paper run` / live pilot host now invoke SAILOR-052 automatically when broker-verified reconciliation evidence is available before creating strategy sessions.

The integration is intentionally conservative:

- in dry-run/local mode with no broker evidence, mirror detection is skipped;
- in send-orders / broker-verified mode, broker positions are mirrored before sessions are created;
- pre-run unknown broker positions are treated as `ManualPreStart`;
- missing active lifecycle positions can be marked as manual close only when broker state is verified.

## What this does not implement yet

SAILOR-052 does not yet:

- run an always-on broker poller during every conduct iteration;
- create strategy sessions dynamically for newly detected manual intraday symbols;
- merge newly detected manual symbols into the candle/history pipeline;
- replenish scanner slots back to the configured minimum;
- enforce the stop-for-day flag inside every conduct strategy;
- perform severe-disconnect full reconstruction.

Those are the next milestones:

```text
SAILOR-053 — Dynamic async trade session manager
SAILOR-054 — Strategy lifecycle policy and manual-stop enforcement
SAILOR-055 — Scanner minimum-target replenishment
SAILOR-056 — Severe disconnect rebuild
```

## Files added/changed

```text
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
src/Sailor.App/Runtime/Paper/PaperRuntimeHost.cs
src/Sailor.App/Runtime/TradeManagement/BrokerMirrorDetection.cs
src/Sailor.App/Runtime/TradeManagement/BrokerMirrorDetectionType.cs
src/Sailor.App/Runtime/TradeManagement/BrokerStateManualTradeDetector.cs
src/Sailor.App/Runtime/TradeManagement/BrokerStateMirrorRows.cs
src/Sailor.App/Runtime/TradeManagement/BrokerStateMirrorSnapshot.cs
src/Sailor.App/Runtime/TradeManagement/BrokerStateMirrorStore.cs
src/Sailor.App/Runtime/TradeManagement/TradeLifecycleRegistryStore.cs
docs/2026-06-30_sailor_s052_broker_state_mirror_manual_trade_detector_notes.md
docs/2026-06-30_sailor_order_trade_management_audit.md
```
