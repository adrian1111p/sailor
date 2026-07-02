# SAILOR-071 — Automatic SailorUI startup for conduct commands

Date: 2026-07-02

## Goal

SAILOR-071 makes SailorUI available by default whenever the user starts a normal or harsh conduct runtime. The user no longer needs to start a separate `sailor-ui --port 5101 --ui-controls` command before running paper conduct tests.

## Scope

SAILOR-071 applies to these commands:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run ...
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper harsh-test ...
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run ...
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live harsh-test ...
```

## Default behavior

### Paper normal and paper harsh

Paper conduct commands now automatically start SailorUI on:

```text
http://localhost:5101/
```

Paper auto UI starts with desired-state controls enabled by default:

```text
controlMode=paper-desired-state
controlsEnabled=True
maxStrategies=2
```

This is equivalent to starting the following command in a second window, but embedded in the conduct process:

```powershell
paper sailor-ui --port 5101 --ui-controls --account <paper-account>
```

The paper runtime continues to consume the SAILOR-067/S068 desired-state file unless explicitly disabled:

```text
state\paper\ui\desired_state_latest.json
```

### Live normal and live harsh

Live conduct commands also auto-start SailorUI on:

```text
http://localhost:5101/
```

However, SAILOR-069 live hardening remains authoritative:

```text
controlMode=live-read-only-locked
controlsEnabled=False
```

Live UI cannot write desired-state changes, cannot enable browser controls, and cannot route live desired-state actions.

## Disable and override flags

Disable embedded SailorUI for one run:

```powershell
--no-auto-ui
```

Aliases:

```powershell
--no-sailor-ui
--no-ui
```

Change the auto UI port:

```powershell
--sailor-ui-port 5102
```

Alias:

```powershell
--ui-port 5102
```

Change auto UI host:

```powershell
--sailor-ui-host 127.0.0.1
```

Live mode still forces loopback-only binding if a non-loopback host is requested.

## Interaction with UI desired-state routing

If the user runs paper conduct with UI desired-state routing enabled, the embedded UI is interactive.

If the user explicitly disables UI desired-state routing, the embedded UI becomes read-only for that run:

```powershell
--no-ui-desired-state
```

or:

```powershell
--ignore-ui-desired-state
```

## Port already in use

If `localhost:5101` is already used by a manually started SailorUI or another process, SAILOR-071 logs a warning and continues the trading runtime. This prevents the UI helper from blocking the normal or harsh conduct command.

## Acceptance criteria

1. `paper run` auto-starts SailorUI on port 5101 with paper controls enabled.
2. `paper harsh-test` auto-starts SailorUI on port 5101 with paper controls enabled.
3. `live run` auto-starts SailorUI on port 5101 in SAILOR-069 live read-only locked mode.
4. `live harsh-test` auto-starts SailorUI on port 5101 in SAILOR-069 live read-only locked mode.
5. `--no-auto-ui` disables the embedded UI without changing conduct behavior.
6. `--sailor-ui-port` changes only the embedded UI port and does not affect the IBKR/TWS port.
7. Existing SAILOR-067 desired-state persistence, SAILOR-068 max-two-strategy routing, SAILOR-069 live hardening, and SAILOR-070 export remain unchanged.

## Self-test

Added deterministic self-test scenario:

```powershell
paper trade-management-test --scenario sailor-ui-auto-start-defaults
```

Expected full suite after SAILOR-071:

```text
PASS passed=21/21
```
