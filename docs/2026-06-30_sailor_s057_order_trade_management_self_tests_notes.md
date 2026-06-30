# SAILOR-057 — Order/trade management self-tests

Date: 2026-06-30  
Status: implemented as deterministic paper/live runtime self-test command scaffolding.

## Scope

SAILOR-057 adds a non-broker self-test command for the trade-management milestones SAILOR-051 through SAILOR-056.

The command validates ownership, lifecycle, scanner target, last-entry, force-flat, and severe recovery rules without sending broker orders.

## Command

```powershell
# run all scenarios
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all

# run one scenario
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario severe-disconnect-recovery

# list scenarios
 dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --list
```

Aliases:

```text
paper trade-test --scenario all
live trade-management-test --scenario all
```

Live mode uses the same deterministic checks and still does not send broker orders.

## Implemented scenarios

```text
preexisting-position
manual-open-after-start
manual-close-stop-day
scanner-target-10-replenish
severe-disconnect-recovery
v21-multi-entry-until-close
non-v21-single-lifecycle
last-entry-945-blocks-replenishment
force-flat-955-all-strategies
all
```

## Validated rules

1. Pre-existing Sailor/broker positions are managed separately and do not reduce the scanner target.
2. Manual intraday positions are exit-only and excluded from scanner target counting.
3. Manual close/stop-for-day prevents same-day scanner re-selection of that symbol.
4. Scanner target remains 10 scanner-owned trades; manual trades do not fill the shortfall.
5. Severe disconnect recovery cannot resume entries or scanner replenishment unless reconciliation is clean.
6. V21/V22/V23/V24 multi-cycle profiles may re-enter before `LastEntryMinute` while the scanner slot is still active.
7. Non-V21/V22/V23/V24 profiles use single-lifecycle behavior and block re-entry after strategy exit.
8. `LastEntryMinute=945` blocks re-entry/replenishment from 15:45 ET onward.
9. `ForceFlatMinute=955` turns open long/short positions into exit decisions and leaves flat sessions untouched.

## Evidence files

The command writes:

```text
logs/<mode>/SelfTests/trade_management_self_test_latest.json
logs/<mode>/SelfTests/trade_management_self_test_yyyyMMdd.csv
```

## Safety

The self-tests are deterministic simulation checks. They do not connect to IBKR, do not route orders, and do not mutate the live trade registry.

Even when `--send-orders` is supplied, the command reports the flag but still does not send broker orders.

## Files

New files:

```text
src/Sailor.App/Runtime/TradeManagement/SelfTests/TradeManagementSelfTestCaseResult.cs
src/Sailor.App/Runtime/TradeManagement/SelfTests/TradeManagementSelfTestReport.cs
src/Sailor.App/Runtime/TradeManagement/SelfTests/TradeManagementSelfTestReportWriter.cs
src/Sailor.App/Runtime/TradeManagement/SelfTests/TradeManagementSelfTestRunner.cs
docs/2026-06-30_sailor_s057_order_trade_management_self_tests_notes.md
```

Modified files:

```text
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
docs/2026-06-30_sailor_order_trade_management_audit.md
```
