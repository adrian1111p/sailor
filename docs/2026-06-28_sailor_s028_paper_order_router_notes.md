# 2026-06-28 SAILOR-028 Paper Order Intent and IBKR Paper Order Router Notes

## Purpose

SAILOR-028 adds the first Sailor-native order-routing layer for paper trading.

The goal is deliberately narrow:

- create normalized `SailorOrderIntent` objects from a manual command;
- validate side, quantity, order type, limit price, account, and TIF;
- write a persistent order ledger;
- keep the default build safe with a dry-run router;
- allow real IBKR paper order submission only when the optional `IBApi` build is enabled and `--send-orders` is explicitly supplied;
- keep live order submission blocked.

This milestone does **not** start the strategy conduct loop and does **not** implement full position reconciliation yet. Those remain SAILOR-029 and SAILOR-030 work.

---

## New command

Dry-run validation:

```powershell
cd D:\Site\sailor

dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper order TSLA BUY 1 LMT 350.00 --dry-run
```

Paper broker submission, optional IBApi build:

```powershell
cd D:\Site\sailor

dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true

dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper order TSLA BUY 1 LMT 350.00 --send-orders --account DUN559573 --wait-seconds 15
```

Market paper order smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper order TSLA BUY 1 MKT --send-orders --account DUN559573 --wait-seconds 15
```

Use only very small quantity in paper mode during SAILOR-028 validation.

---

## Supported manual order syntax

```text
paper order SYMBOL SIDE QTY TYPE [LIMIT_PRICE] [options]
```

Supported sides:

```text
BUY
SELL
SELL_SHORT
BUY_TO_COVER
```

Supported order types in this milestone:

```text
MKT
LMT
```

Recognized options:

```text
--dry-run
--send-orders
--account ACCOUNT_CODE
--tif DAY
--primary-exchange NASDAQ
--strategy manual-paper-order
--reason some_text
--wait-seconds 15
```

---

## Safety behavior

Default behavior is dry-run.

Real paper submission requires:

```text
mode = paper
--send-orders supplied
--dry-run not supplied
optional IBApi build enabled with -p:EnableIbkrApi=true
TWS/Gateway paper API reachable on configured host/port
```

Live submission is blocked in SAILOR-028 even if `--send-orders` is supplied.

---

## Ledger outputs

Every order command writes to both:

```text
state/paper/order-ledger.jsonl
logs/Paper/Orders/orders_yyyyMMdd.csv
```

The ledger stores:

```text
intent id
symbol
side
order type
quantity
limit price
TIF
strategy name
reason
account
broker order id
status
filled quantity
average fill price
whether the order was sent to broker
message
```

---

## Next milestone dependency

SAILOR-029 must add broker positions and reconciliation before Sailor can safely implement real flatten and continuous strategy conduct.

SAILOR-028 intentionally avoids automatic flatten because it does not yet know the authoritative broker position state.
