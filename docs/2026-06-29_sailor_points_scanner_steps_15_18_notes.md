# SAILOR-048 — Points Scanner Steps 15 to 18 Notes

Date: 2026-06-29  
Scope: final hardening for the points scanner migration: live pilot gate, regression/self-test command, operator commands, and documentation.

---

## 1. Implemented scope

SAILOR-048 completes the planned scanner audit Steps 15 to 18.

Implemented items:

1. Live pilot points gate.
2. Regression/self-test command for legacy no-selection versus points ranking.
3. Final operator commands for scan, wait-for-entry, hybrid comparison, live read-only, and live dry-run.
4. Documentation updates in the runtime command skeleton and scan-list audit.

The scanner conversion remains conservative: points ranking improves symbol selection and evidence, but broker safety remains hard-gated.

---

## 2. Step 15 — Live pilot points gate

Live send-orders now has an additional points-specific gate. This gate is advisory for read-only and dry-run commands, but mandatory for live send-orders.

Live send-orders requires all existing live gates plus:

```text
scannerMode=points-only
selected scan-list symbol exists
selected points status=Ready
selected points score >= --points-min-trade-score
```

The live points gate does not replace existing safety checks. Live orders still require:

```text
Runtime.Live.AllowLiveTrading=true
--confirm-live
--operator-watching-tws
paper certification passed
account match
small max notional
scan-list data quality clean
live reconciliation matched
max notional enforcement
```

New live gate output appears in runtime logs as:

```text
SAILOR-048 live points pilot gate
livePointsPilot required=True allowed=False scannerMode=...
```

Live pilot reports now include the points gate status and selected candidate points status/score.

---

## 3. Step 16 — Points scanner regression/self-test command

Added scanner-only regression command:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points-test 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --no-depth --wait-seconds 15
```

The command runs two passes:

1. `legacy-blocks`
2. `points-only`

It starts no conduct loop and sends no orders.

Self-test checks:

```text
points-only produces ranked candidates
points-only candidate count is not worse than legacy candidate count
points-only writes a candidate report
optional --expect-legacy-zero enforces the exact no-candidates baseline
```

Use strict baseline mode only when intentionally verifying the historical no-selection case:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points-test 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --no-depth --wait-seconds 15 --expect-legacy-zero
```

---

## 4. Step 17 — Operator commands

### Paper scan-only points diagnostics

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode points-only --points-min-trade-score 45 --no-depth --wait-seconds 15
```

### Paper wait-for-entry with points-only selection

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper run 1m v18-silver 10 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --send-orders --iterations 2700 --cadence-seconds 1 --max-symbols 45 --wait-seconds 15 --quantity 1 --no-depth --wait-for-scan-entry --scan-entry-target 10 --scan-entry-wait-seconds 2700 --scan-refresh-seconds 300 --scanner-mode points-only --points-min-trade-score 45
```

### Hybrid comparison evidence

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --scanner-mode hybrid-compare --no-depth --wait-seconds 15
```

### Live read-only points ranking

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --max-symbols 45 --scanner-mode points-only --no-depth --read-only --wait-seconds 15
```

### Live dry-run one-symbol pilot from points scan-list

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live run 1m v18-silver 1 --scan-file scan\data\scan_default.xlsx --scan-sheet Candidates --account DUN559573 --max-notional 100 --confirm-live --operator-watching-tws --dry-run --local-cache --no-depth --iterations 5 --scanner-mode points-only --points-min-trade-score 45
```

Live send-orders remains blocked by default and additionally requires the selected points candidate to be `Ready` and above the configured threshold.

---

## 5. Step 18 — Documentation update

Updated documentation:

```text
docs/2026-06-28_sailor_runtime_contracts_command_skeleton.md
docs/2026-06-29_sailor_scan_default_memory_candles_audit.md
```

Design source remains:

```text
docs/2026-06-29_sailor_scanner_points_based_audit.md
```

---

## 6. Acceptance commands

```powershell
dotnet clean
dotnet build
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
dotnet build src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper scan-points-test 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --account DUN559573 --max-symbols 45 --no-depth --wait-seconds 15
```

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live scan-points 1m v18-silver 10 --file scan\data\scan_default.xlsx --sheet Candidates --max-symbols 45 --scanner-mode points-only --no-depth --local-cache
```

---

## 7. Important limitation

SAILOR-048 completes the scanner-side conversion and live safety evidence. It does not remove conduct-strategy entry filters. Therefore, points-only scan-list selection can produce Ready symbols while the V18 conduct strategy may still hold because of its own timing/volume/pattern rules. That is expected until a later conduct-entry refactor converts those entry checks into points-aware conduct timing.
