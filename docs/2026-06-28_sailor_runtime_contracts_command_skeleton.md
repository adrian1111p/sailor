# SAILOR Runtime Contracts and Command Skeleton

Date: 2026-06-28  
Updated through: SAILOR-034 — Live pilot

## Purpose

This document is the working command reference for the Sailor runtime after the paper/live milestones from SAILOR-022 through SAILOR-034.

It covers the commands needed for:

- backtest validation and HTML reports
- paper history, scanner, conduct loop, order routing, reconciliation, and certification reports
- live read-only checks, live-readiness gate evidence, and the guarded one-symbol live pilot

All commands are intended to be run from the repository root:

```powershell
cd D:\Site\sailor
```

The default safety rule remains:

```text
No live order may be sent unless the live-readiness gate and live-pilot restrictions pass.
```

## Common build commands

Run these before every milestone check:

```powershell
dotnet clean
dotnet build
```

Run these when a command must use the optional IBKR adapter:

```powershell
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

Use the optional IBKR build for commands that must request broker positions, open orders, executions, market data, or send broker orders.


## Command option reference

This chapter explains the most important command-line options used in the examples below. The exact command decides which options are valid, but these are the current working conventions through SAILOR-034.

### Positional command arguments

| Argument | Used by | Meaning | Example |
|---|---|---|---|
| `timeframe` | backtest, scan, rank, paper run, live run | Bar timeframe. Current normal value is `1m`. | `1m` |
| `profile` | backtest, scan, rank, paper run, live run | Strategy/profile name. | `v21-15minutes` |
| `top` | scan, rank, paper run, live run | Number of ranked/active symbols to use. Live pilot must use `1`. | `1`, `20` |
| `symbol` / `universe` | scan, rank, paper run, live run, order, flatten | A single ticker, comma-separated symbols, or a configured universe such as `smallcaps`. Live pilot requires one explicit symbol. | `TSLA`, `ALIT,BARK`, `smallcaps` |
| `side` | order | Order side. | `BUY`, `SELL` |
| `qty` | order / run quantity | Share quantity. In `paper run` / `live run`, this is the pilot quantity. | `1` |
| `orderType` | order | Manual order type. | `LMT`, `MKT` |
| `limitPrice` | limit order | Limit price for `LMT` orders. | `350.00` |

### Runtime length and cadence options

| Option | Meaning | Typical use |
|---|---|---|
| `--iterations N` | Limits how many conduct-loop cycles are executed. Use small values for smoke tests. | `--iterations 5` or `--iterations 10` |
| `--cadence-seconds N` | Delay between conduct-loop iterations. | `--cadence-seconds 1` |
| `--wait-seconds N` | Timeout/wait period for IBKR broker requests such as reconciliation, orders, positions, quotes, or depth. | `--wait-seconds 15` |
| `--seconds N` | Duration for quote/depth snapshot collection. | `--seconds 15` |
| `--levels N` | L2 depth levels to request. | `--levels 5` |

### Data and market-data options

| Option | Meaning | Typical use |
|---|---|---|
| `--local-cache` | Use local CSV/backtest cache instead of requesting IBKR history. Good for dry-run smoke tests. | `paper run ... --local-cache` |
| `--no-quotes` | Do not request live quote snapshots. Used when testing conduct logic without market data. | `--no-quotes` |
| `--no-depth` | Do not request L2/depth data. Used for scanner smoke tests. | `--no-depth` |
| `--days N` | Number of days of history to request/cache. | `--days 5` |
| `--market-data-type N` | IBKR market data type. `1` is live, `2` is frozen/delayed-frozen depending on entitlement/session. | `--market-data-type 2` |
| `--primary-exchange EXCH` | Primary exchange used for IBKR stock contract resolution. Default examples use NASDAQ. | `--primary-exchange NASDAQ` |
| `--smart-depth` | Requests SMART depth where supported. Only useful when the subscription and contract support it. | `--smart-depth` |

### Broker/account and IBKR options

| Option | Meaning | Typical use |
|---|---|---|
| `-p:EnableIbkrApi=true` | MSBuild property enabling the optional IBKR adapter. Required for real broker positions, open orders, executions, quotes/depth, and order routing. | `dotnet run ... -p:EnableIbkrApi=true -- paper reconcile ...` |
| `--account ACCOUNT` | Broker account to use or verify. Paper examples use the paper account; live examples must match the certification account. | `--account DU123456` |
| `--send-orders` | Requests broker order routing. Safe gates still decide whether an order may actually be sent. | `paper order ... --send-orders` |
| `--dry-run` | Validate/route locally without sending broker orders. In paper conduct dry-run, fills may be assumed locally to test conduct flow. | `--dry-run` |
| `--local-only` | Do not request broker state. Used for local reconciliation/status smoke tests. | `paper reconcile --local-only` |

### Paper trading and safety options

| Option | Meaning | Typical use |
|---|---|---|
| `--force-flat-now` | Forces the conduct loop into close/flatten behavior immediately. Useful for testing exit paths. | `paper run ... --force-flat-now` |
| `--simulate-disconnect-at N` | Simulates a disconnect/degraded state at conduct-loop iteration `N`. New entries must become blocked and runtime safety should move to `CloseOnly`. | `--simulate-disconnect-at 2` |
| `--reconnect-attempts N` | Number of recovery attempts after degraded state in broker-enabled runtime. | `--reconnect-attempts 3` |
| `--reconnect-backoff-seconds N` | Backoff between reconnect/recovery attempts. | `--reconnect-backoff-seconds 2` |

### Live-readiness and live-pilot options

| Option | Meaning | Required for |
|---|---|---|
| `--read-only` | Explicitly marks a live command as read-only. Required for `live connect`. | `live connect --read-only` |
| `--confirm-live` | Manual operator confirmation that live evidence was reviewed. This is required by the live-readiness and live-pilot gates, but does not bypass failed checks. | `live readiness`, `live run`, `live order`, `live flatten` |
| `--max-notional VALUE` | Maximum allowed notional for the requested live command. Must be less than or equal to `Runtime.Live.MaxOrderNotional`. | `--max-notional 100` |
| `--operator-watching-tws` | Confirms the operator is watching TWS during the live pilot. Required for `live run`. | `live run` |
| `--allow-short-live` | Allows short entries in the live pilot only when config also allows shorting. Long-only remains the default. | live pilot short tests only |

### Report and evidence commands

| Command / option | Meaning | Generated evidence |
|---|---|---|
| `paper report latest` | Builds the latest paper certification report from runtime logs, ledger, position store, reconciliation, and incidents. | `logs/Paper/Reports/*` |
| `live readiness` / `live gate` | Evaluates whether live trading is allowed based on config, manual confirmation, paper report, account match, max notional, and freshness. | `logs/Live/Readiness/*` |
| `live run ...` | When not blocked, runs the guarded one-symbol live pilot and writes pilot evidence. | `logs/Live/Pilot/*` |

### Git safety for generated option outputs

Many options generate local evidence files. These are useful for certification and debugging, but should not be committed:

```text
logs/
state/
src/**/bin/
src/**/obj/
```


## Generated files that must not be committed

The following folders contain generated runtime evidence, reports, local broker state, and build output. They should stay ignored by Git:

```text
logs/
state/
src/**/bin/
src/**/obj/
```

Recommended `.gitignore` entries:

```gitignore
logs/
state/
bin/
obj/
```

## Backtest commands

### List available backtest data

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest --list
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest --list TSLA
```

### Single-symbol backtest

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m sailor-trend-volume
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m v21-15minutes
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m v24-5minutes
dotnet run --project src\Sailor.App\Sailor.App.csproj -- backtest TSLA 1m v16-sqzbreakout
```

### Backtest scanner / ranking

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- scan 1m sailor-trend-volume 20
dotnet run --project src\Sailor.App\Sailor.App.csproj -- rank 1m sailor-trend-volume 20 smallcaps
dotnet run --project src\Sailor.App\Sailor.App.csproj -- rank 1m v21-15minutes 20 smallcaps
dotnet run --project src\Sailor.App\Sailor.App.csproj -- rank 1m simple-momentum 20 ALIT,BARK,SOFI,PLTR
```

### Backtest HTML report

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps 20
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps 0 v21-15minutes,v23-5minutes,v24-5minutes,v22-15minutes
```

### Backtest self-test

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- test-backtest quick
dotnet run --project src\Sailor.App\Sailor.App.csproj -- test-backtest full
```

## Paper commands

Paper mode uses TWS/Gateway paper port `7497` by default and client id `22`.

### Paper connection probe

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper connect
```

With optional IBKR build already built:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper connect
```

### Paper history cache

Use local cache only:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper history 1m TSLA --local-cache
```

Request IBKR history and mirror it to the backtest cache:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper history 1m TSLA --days 5 --account DU123456
```

Multiple symbols / universe example:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper history 1m smallcaps --top 10 --days 5 --account DU123456
```

### Paper scanner

Local cache smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan 1m sailor-trend-volume 3 smallcaps --local-cache --no-depth
```

IBKR-assisted scan with market data type 2:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan 1m v21-15minutes 1 TSLA --days 5 --market-data-type 2 --account DU123456
```

### Paper L1/L2 snapshot checks

L1 quote snapshot:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper quotes TSLA --seconds 15 --market-data-type 2 --account DU123456
```

L2 depth snapshot:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper depth TSLA --levels 5 --seconds 20 --market-data-type 2 --account DU123456
```

Local-cache quote smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper quotes TSLA --local-cache
```

### Paper status and local position view

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper status
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper positions
```

`paper status` reads local Sailor state only. It does not request broker state.

### Paper broker reconciliation

Local-only reconciliation smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper reconcile --local-only
```

Broker-verified reconciliation through TWS paper:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DU123456 --wait-seconds 15
```

Expected safety behavior:

```text
Entries remain blocked unless reconciliation status is Matched and there are no critical mismatches.
```

If TWS still has an old open order that is not mapped to the Sailor ledger, reconciliation should return `CriticalMismatch` and entries should remain blocked.

### Paper manual order router

Dry-run manual order:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper order TSLA BUY 1 LMT 350.00 --dry-run
```

Paper order through IBKR paper:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper order TSLA BUY 1 LMT 350.00 --send-orders --account DU123456 --wait-seconds 15
```

Market order dry-run:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper order TSLA BUY 1 MKT --dry-run
```

Paper live submission is allowed only in paper mode with `--send-orders`, the optional IBKR build, and TWS paper connection.

### Paper conduct loop dry-run

Local-cache/no-quotes smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1
```

Degraded-state simulation:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 5 --cadence-seconds 1 --simulate-disconnect-at 2
```

Expected result after the simulated disconnect:

```text
Runtime safety moves to CloseOnly.
New entries are blocked.
Exit/flatten routing remains allowed.
Incident evidence is written under logs/Paper/Incidents.
```

### Paper conduct loop with broker orders

First verify broker reconciliation:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DU123456 --wait-seconds 15
```

Then run paper conduct with send-orders:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --send-orders --account DU123456 --wait-seconds 15 --iterations 10 --reconnect-attempts 3 --reconnect-backoff-seconds 2
```

Important:

```text
SAILOR blocks entries before conduct loop when pre-run reconciliation is CriticalMismatch or NotBrokerVerified.
```

### Paper force-flat / close-only behavior

Paper flatten is strategy/runtime driven. Use the conduct loop force-flat switch:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 5 --force-flat-now
```

The old command still exists as a skeleton, but it does not send a broker order:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper flatten TSLA
```

### Paper certification report

Generate the latest paper certification report:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

Generated report files:

```text
logs/Paper/Reports/paper_certification_latest.json
logs/Paper/Reports/paper_certification_latest.md
logs/Paper/Reports/paper_certification_YYYYMMDD.csv
```

The report is promotable only when:

```text
end exposure is zero
latest broker reconciliation is clean
critical mismatches are absent
required runtime evidence is available
```

## Live commands

Live mode uses TWS/Gateway live port `7496` by default and client id `21`.

The default live config is intentionally safe:

```json
"Live": {
  "SendOrders": false,
  "AllowLiveTrading": false,
  "MaxOrderNotional": 100.0,
  "CertificationMaxAgeHours": 24,
  "AllowShort": false
}
```

### Live read-only connection

This command must be explicit read-only:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect --read-only
```

Without `--read-only`, the command prints the gate and does not open the live connection:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect
```

Expected result:

```text
No orders are sent.
Read-only live command is allowed only when explicitly requested.
```

### Live read-only scan

Local-cache live scan smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan 1m v21-15minutes 1 TSLA --local-cache --no-depth
```

Live scan through IBKR remains read-only and never creates an order router:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live scan 1m v21-15minutes 1 TSLA --days 5 --market-data-type 2 --account DU123456
```

### Live read-only L1/L2 checks

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live quotes TSLA --seconds 15 --market-data-type 2 --account DU123456
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live depth TSLA --levels 5 --seconds 20 --market-data-type 2 --account DU123456
```

These commands must not send orders.

### Live-readiness gate

Generate live-readiness evidence:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live readiness --account DU123456 --max-notional 100 --confirm-live
```

Alias:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live gate --account DU123456 --max-notional 100 --confirm-live
```

Generated readiness files:

```text
logs/Live/Readiness/live_readiness_latest.json
logs/Live/Readiness/live_readiness_YYYYMMDD.csv
```

The gate requires:

```text
Runtime.Live.AllowLiveTrading=true
--confirm-live
latest paper certification report exists
paper certification canPromote=true
paper certification age <= Runtime.Live.CertificationMaxAgeHours
live account matches paper certification account
requested --max-notional <= Runtime.Live.MaxOrderNotional
```

If any check fails, live trading remains blocked.

### Manual live order command

Manual live order sending remains blocked by design. This command evaluates the gate but does not create a live broker route:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live order TSLA BUY 1 LMT 350.00 --send-orders --account DU123456 --max-notional 100 --confirm-live
```

Expected result while the gate is not fully passed:

```text
SAILOR-033/034 blocks live order submission before any broker order route is created.
No broker order is sent.
```

### Live pilot run

Safe default smoke test. This should be blocked while `Runtime.Live.AllowLiveTrading=false`:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live run 1m v21-15minutes 1 TSLA --account DU123456 --max-notional 100 --confirm-live --operator-watching-tws --iterations 5
```

Actual live pilot requires the optional IBKR build and all live-readiness checks to pass:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run 1m v21-15minutes 1 TSLA --account DU123456 --max-notional 100 --confirm-live --operator-watching-tws --iterations 5 --wait-seconds 15
```

Live pilot restrictions:

```text
one explicit symbol only
top count must be 1
operator must pass --operator-watching-tws
Runtime.Live.AllowLiveTrading must be true
latest paper certification must be promotable
live account must match the paper certification account
pre-run live reconciliation must be clean
final live reconciliation must be clean
end exposure must be zero before promotion
long-only by default
short entries require both config and --allow-short-live
```

Live pilot report files:

```text
logs/Live/Pilot/live_pilot_latest.json
logs/Live/Pilot/live_pilot_YYYYMMDD.csv
```

### Live close-only flatten

Dry-run close-only flatten check:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live flatten TSLA --account DU123456 --confirm-live --dry-run
```

IBKR broker-position check without sending an order:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live flatten TSLA --account DU123456 --confirm-live --dry-run --wait-seconds 15
```

Close-only live flatten with broker order routing requires all three switches/config values:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live flatten TSLA --account DU123456 --send-orders --confirm-live --wait-seconds 15
```

Required for the final command:

```text
Runtime.Live.AllowLiveTrading=true
--send-orders
--confirm-live
non-flat live broker position exists for the symbol
IBKR provider enabled with -p:EnableIbkrApi=true
```

The command is close-only. It must never open a new live position.

## Recommended end-to-end workflow

### 1. Backtest/report validation

```powershell
dotnet clean
dotnet build
dotnet run --project src\Sailor.App\Sailor.App.csproj -- test-backtest quick
dotnet run --project src\Sailor.App\Sailor.App.csproj -- rank 1m v21-15minutes 20 smallcaps
dotnet run --project src\Sailor.App\Sailor.App.csproj -- html-report 1m smallcaps 20 v21-15minutes,v24-5minutes,v16-sqzbreakout
```

### 2. Paper dry-run conduct evidence

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 1 TSLA --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

### 3. Paper broker reconciliation and paper send-orders evidence

```powershell
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DU123456 --wait-seconds 15
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 1 TSLA --send-orders --account DU123456 --wait-seconds 15 --iterations 10
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper reconcile --account DU123456 --wait-seconds 15
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

### 4. Live read-only evidence

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect --read-only
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan 1m v21-15minutes 1 TSLA --local-cache --no-depth
```

### 5. Live-readiness evidence

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live readiness --account DU123456 --max-notional 100 --confirm-live
```

### 6. Live pilot only after all gates pass

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run 1m v21-15minutes 1 TSLA --account DU123456 --max-notional 100 --confirm-live --operator-watching-tws --iterations 5 --wait-seconds 15
```

## Current runtime contract summary

The runtime strategy receives one neutral frame:

```text
SailorStrategyFrame
- runtime mode
- symbol
- timeframe
- current bars
- current indicators
- optional L1/L2 market snapshot
- runtime state
```

The strategy returns one neutral decision:

```text
SailorStrategyDecision
- hold
- enter long
- enter short
- exit long
- exit short
- flatten
- cancel orders
```

The order router converts the decision into:

```text
SailorOrderIntent
```

This keeps strategy logic independent from IBKR and allows the same conduct strategy adapter to be used by backtest, paper, and guarded live pilot mode.

---

## SAILOR-039 scan-list runtime cycles and memory options

SAILOR-039 extends `paper scan-list` and `live scan-list` from a one-cycle scanner check into a non-trading dynamic scan-list runtime host. It still sends no orders.

### Paper scan-list runtime

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-quotes --max-symbols 45
```

### Live read-only scan-list runtime

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth --max-symbols 45
```

### Multi-cycle smoke test without waiting

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-quotes --max-symbols 45 --scan-cycles 2 --scan-refresh-seconds 1 --no-scan-cycle-wait
```

### Options

| Option | Meaning |
|---|---|
| `--scan-cycles N` | Run N scan-list cycles. Default is `1`. |
| `--cycles N` | Alias for `--scan-cycles`. |
| `--no-scan-cycle-wait` | Do not sleep between scan cycles. Use for local smoke tests. |
| `--scan-refresh-seconds N` | Workbook reload / selection refresh interval. Default is `300`. |
| `--history-batch-size N` | Maximum symbols in one history batch. Default is `45`. |
| `--history-batch-interval-minutes N` | Planned spacing between history batches. Default is `10`. |
| `--trade-top N` | Retain at least the best N scanner-rated symbols for later paper/live trade eligibility. The runtime keeps minimum `10`. |
| `--keep-trade-top N` | Alias for `--trade-top`. |

Generated evidence is written under:

```text
logs/Paper/ScanList/
logs/Live/ScanList/
```

Do not commit generated scan-list evidence.

---

## SAILOR-040 dynamic scan-list trading commands

### Paper dynamic scan-list dry-run

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1 --max-symbols 45
```

This command runs the scan-list runtime first, retains the best scanner-rated symbols, and then starts the paper conduct loop using only those retained symbols. It sends no broker orders in dry-run mode.

### Paper dynamic scan-list with broker order routing

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v21-15minutes 5 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --send-orders --account DUN559573 --history-batch-size 45 --history-batch-interval-minutes 10 --wait-seconds 15
```

This remains blocked unless paper reconciliation is clean. Scan-list selection controls entries only; exits and force-flat remain allowed for managed positions.

### Live scan-list pilot from the best retained symbol

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live run 1m v21-15minutes 1 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --max-notional 100 --confirm-live --operator-watching-tws --dry-run --local-cache --no-depth --iterations 5
```

SAILOR-040 uses the scan-list runtime to choose one symbol, then hands that symbol to the SAILOR-034 live pilot gate. Live trading is still blocked unless all live-readiness requirements pass.

### Paper certification with scan-list evidence

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

The report now includes the latest `logs/Paper/ScanList/scanlist_latest.json` evidence when available.

---

## SAILOR-041 scan-list certification and live pilot commands

### Paper dynamic scan-list run

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --dry-run --local-cache --no-quotes --iterations 10 --cadence-seconds 1 --max-symbols 45
```

This command selects retained scan-rated symbols from the workbook before starting the conduct loop. It is safe in dry-run mode and sends no broker orders.

### Paper certification report with scan-list data quality

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

The report now includes scan-list data-quality fields:

```text
scan-list data quality
critical data gaps
merge conflicts
stale selected symbols
not-ready selected symbols
latest selected candle age
```

Promotion is blocked if the latest selected scan-list symbols are not data-quality clean.

### Live read-only scan-list observation

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan-list 1m v21-15minutes 10 --file scan\data\scan_default.xlsx --sheet Candidates --local-cache --no-depth --max-symbols 45
```

This command is read-only. It creates live scan-list evidence under `logs/Live/ScanList` and never creates a live order router.

### Live dynamic one-symbol pilot

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live run 1m v21-15minutes 1 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --max-notional 100 --confirm-live --operator-watching-tws --dry-run --local-cache --no-depth --iterations 5
```

The live dynamic pilot selects the best one retained scan-list symbol and then applies all SAILOR-033 and SAILOR-034 gates. Live orders remain blocked unless the scan-list evidence is data-quality clean and all live-readiness, account, reconciliation, notional, and operator checks pass.

---

## Scan-list workbook open-file behavior

`scan/data/scan_default.xlsx` may be kept open in Excel while Sailor reads it. The workbook reader copies the file into memory using shared read/write/delete access, then parses the in-memory copy.

Operational guidance:

```text
- Keeping the workbook open for viewing/editing is supported.
- Save the workbook before expecting Sailor to see new symbols.
- Avoid running Sailor exactly while Excel is in the middle of saving the file.
- If a save-time lock is hit, rerun the command after a few seconds.
```
