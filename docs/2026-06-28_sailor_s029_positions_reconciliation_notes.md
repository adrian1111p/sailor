# 2026-06-28 SAILOR-029 — Positions and Reconciliation

## Scope completed

SAILOR-029 adds the first Sailor-native position and reconciliation layer after the SAILOR-028 paper order router.

Implemented capabilities:

- Local order ledger store under `state/{mode}/order-ledger.jsonl`.
- Daily order CSV report under `logs/{Paper|Live}/Orders`.
- Local position store under `state/{mode}/positions.json`.
- Ledger-derived Sailor position reconstruction from filled order rows.
- Broker position provider contract.
- Optional IBKR position provider when the app is built with `-p:EnableIbkrApi=true`.
- Broker state request for:
  - positions,
  - open orders,
  - recent executions.
- Reconciliation service comparing:
  - Sailor ledger-derived positions,
  - IBKR broker positions,
  - IBKR open orders,
  - Sailor order ledger references.
- External/manual broker positions are marked as critical mismatches.
- Broker open orders not mapped to a Sailor intent id or broker order id are marked as external open orders.
- New runtime commands:
  - `paper status`
  - `paper reconcile`

## Commands

Local status only:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper status
```

Broker reconciliation with IBKR paper:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DUN559573 --wait-seconds 15
```

Local-only reconciliation without IBApi/TWS:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper reconcile --local-only
```

## Safety behavior

SAILOR-029 intentionally blocks future strategy entries unless broker reconciliation succeeds with status `Matched`.

Entries remain blocked when:

- IBKR broker state is not available,
- a broker position exists but Sailor has no ledger-derived position,
- Sailor has a ledger-derived position but IBKR does not report it,
- Sailor and broker quantities differ,
- an unmapped broker open order exists.

## Files added/changed

```text
src/Sailor.App/Broker/State/SailorStatePaths.cs
src/Sailor.App/Broker/State/OrderLedgerStore.cs
src/Sailor.App/Broker/State/PositionStore.cs
src/Sailor.App/Broker/State/IPositionProvider.cs
src/Sailor.App/Broker/State/ReconciliationService.cs
src/Sailor.App/Broker/Ibkr/IbkrPositionProvider.cs
src/Sailor.App/Broker/Ibkr/Orders/IbkrPaperOrderRouter.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
docs/2026-06-28_sailor_s029_positions_reconciliation_notes.md
```

## Not included yet

The following remains for the next milestones:

- Continuous paper conduct loop.
- Automatic flatten order generation.
- Reconnect loop with close-only/halted safety modes.
- Live order enablement.

