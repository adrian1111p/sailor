# SAILOR-054 — Strategy lifecycle policies implementation notes

Date: 2026-06-30

## Goal

Implement the first lifecycle-policy layer on top of the SAILOR-053 dynamic session plan so that each runtime symbol session knows whether automatic entries/re-entries are allowed.

## Implemented behavior

- Added `StrategyLifecycleMode`:
  - `SingleLifecycleUntilStrategyExit`
  - `MultiCycleUntilLastEntryMinute`
  - `ManualManagedExitOnly`
- Added `StrategyLifecyclePolicy` and `StrategyLifecyclePolicyResolver`.
- Added `Sailor.StrategyLifecyclePolicies` configuration with safe defaults:
  - `v21-15minutes`, `v22-15minutes`, `v23-5minutes`, `v24-5minutes` => `MultiCycleUntilLastEntryMinute`
  - `default` => `SingleLifecycleUntilStrategyExit`
- Manual or unknown broker-origin sessions are forced to `ManualManagedExitOnly`, regardless of profile, so they are conducted for exits but cannot create automatic entries/re-entries.
- `PaperSymbolSession` now owns its lifecycle policy and remembers when a single-lifecycle session has closed its entry window after a strategy exit.
- `PaperConductLoop` applies the SAILOR-054 entry gate before creating any entry order intent.
- The universal market-close rule is preserved:
  - `LastEntryMinute = 945`
  - `ForceFlatMinute = 955`

## Runtime rules now enforced

| Situation | Result |
|---|---|
| V21/V22/V23/V24 scanner/SAILOR-managed session exits before 15:45 ET | Re-entry may be allowed by strategy and safety gates. |
| Any profile attempts entry at or after 15:45 ET | Entry is blocked by universal lifecycle policy. |
| Non-V21/V22/V23/V24 strategy exits and becomes flat | The session entry window is closed; later automatic re-entry is blocked. |
| Manual/pre-start/intraday/unknown broker session attempts entry | Entry is blocked; exits and force-flat remain allowed. |
| Force-flat window starts at 15:55 ET | Force-flat/exit path still has priority. |

## What remains for later milestones

SAILOR-054 does not implement scanner-slot replenishment or true parallel workers. Those remain in:

- SAILOR-055 — scanner slot target and 5-minute replenishment
- SAILOR-056 — severe disconnect recovery orchestrator
- SAILOR-057 — order/trade management self-tests

## Build note

The source tree was statically checked in this environment and `appsettings.json` validates as JSON. A local `dotnet build` could not be executed in the sandbox because the `dotnet` command is not installed here. Please run the normal local verification commands on your workstation:

```powershell
dotnet clean
dotnet build
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```
