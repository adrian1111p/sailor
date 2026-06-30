# SAILOR-051 — Trade Lifecycle Registry and Ownership Model

Date: 2026-06-30  
Scope: first implementation step after the order/trade-management audit.  
Status: source implementation milestone.

---

## 1. Purpose

SAILOR-051 adds the first persistent trade-management foundation:

```text
state/{paper|live}/trades/trade_registry_latest.json
state/{paper|live}/trades/trade_registry_yyyyMMdd.jsonl
```

The registry records which runtime/trade lifecycle owns a symbol, where the lifecycle came from, and what its latest lifecycle status is. This is the base required before later milestones can safely attach manual TWS trades, pre-existing broker orders, async workers, scanner-slot replenishment, and severe-disconnect recovery.

SAILOR-051 is intentionally non-invasive:

```text
- no order-routing behavior is changed;
- no broker discovery loop is added yet;
- no async per-symbol worker is added yet;
- no scanner target replenishment is added yet;
- no manual TWS trade detector is added yet.
```

---

## 2. Added model

New namespace:

```text
Sailor.App.Runtime.TradeManagement
```

New core concepts:

```text
TradeLifecycle
TradeLifecycleStatus
SailorTradeOrigin
TradeLifecycleRegistrySnapshot
TradeLifecycleRegistryStore
TradeLifecycleEvent
```

### 2.1 Origins

The registry now supports these ownership origins:

```text
ScannerOwned        -> scanner-selected trade/session; counts toward future scanner target
SailorPreExisting   -> broker/local position already existed when selected session was created
ManualPreStart      -> reserved for broker mirror milestone
ManualIntraday      -> reserved for broker mirror/manual detection milestone
UnknownBroker       -> broker-origin lifecycle that cannot yet be classified safely
ExplicitRuntime     -> explicit/fallback runtime symbol not selected by scanner ranking
SailorManualCommand -> created by the sailor paper/live order command path
```

The important SAILOR-051 rule is:

```text
Only ScannerOwned active lifecycles count toward the future scanner minimum target.
Manual/unknown/explicit lifecycles are managed separately in later milestones.
```

### 2.2 Statuses

The registry now supports:

```text
PendingEntry
EntrySubmitted
Open
ExitSubmitted
ClosedByStrategy
ClosedManually
StoppedForDay
Recovered
UnknownBroker
Error
```

The current implementation writes `PendingEntry`, `EntrySubmitted`, `Open`, `ExitSubmitted`, `ClosedByStrategy`, and `Error`. The remaining statuses are reserved for the broker-state mirror and manual-close detector milestones.

---

## 3. Runtime integration

### 3.1 Paper/live conduct session activation

When `paper run` or the shared paper/live host activates symbol sessions, SAILOR now creates/updates registry rows:

```text
scanner candidate session -> Origin=ScannerOwned
fallback/explicit session -> Origin=ExplicitRuntime
selected session with existing broker/local seed -> Origin=SailorPreExisting
```

The command log prints:

```text
Trade registry latest JSON: state/paper/trades/trade_registry_latest.json
Trade registry event JSONL: state/paper/trades/trade_registry_yyyyMMdd.jsonl
```

### 3.2 Order receipt updates

When the conduct loop submits an intent and receives a receipt, SAILOR now updates the matching trade lifecycle after the local session position update.

Examples:

```text
EnterLong / EnterShort filled or dry-run-filled -> Open
EnterLong / EnterShort submitted but not filled   -> EntrySubmitted
ExitLong / ExitShort submitted                    -> ExitSubmitted
ExitLong / ExitShort filled or dry-run-filled     -> ClosedByStrategy
Rejected/Failed receipt                           -> Error
```

### 3.3 Manual command path

The `paper order` command now writes registry evidence with:

```text
Origin=SailorManualCommand
```

The live close-only flatten path writes lifecycle evidence as:

```text
Origin=UnknownBroker
```

because the position came from broker truth and may be manual/pre-existing until the broker-state mirror classifies it in SAILOR-052.

---

## 4. New operator command

New command:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trades status
```

Show all historical/closed rows too:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trades status --all
```

Filter one symbol:

```powershell
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trades status --all --symbol TSLA
```

The command is local-file only:

```text
- does not connect to TWS;
- does not reconcile broker state;
- does not conduct strategy logic;
- does not send orders.
```

---

## 5. What this milestone does not solve yet

SAILOR-051 is the ownership registry only. These requirements remain for later milestones:

```text
SAILOR-052 broker-state mirror and manual trade detector
SAILOR-053 dynamic async trade session manager
SAILOR-054 strategy lifecycle policies/manual close stop-for-day
SAILOR-055 scanner minimum-target replenishment every 5 minutes
SAILOR-056 severe-disconnect recovery and full session rebuild
SAILOR-057 trade-management regression/self-tests
```

---

## 6. Suggested validation

Build:

```powershell
dotnet clean
dotnet build
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

Dry-run conduct registry evidence:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 3
```

Read registry:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trades status --all
```

Expected:

```text
- build succeeds;
- paper run prints trade registry paths;
- state/paper/trades/trade_registry_latest.json exists;
- state/paper/trades/trade_registry_yyyyMMdd.jsonl exists;
- trades status prints active or historical lifecycles;
- no broker behavior changes compared with previous milestone.
```
