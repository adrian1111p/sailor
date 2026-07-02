# SAILOR-070 — UI report export and TWS-style long/short win/loss coloring

Date: 2026-07-02

## Goal

SAILOR-070 extends SailorUI with a lightweight report export and clearer TWS-style visual risk cues.

The UI remains compact and line-based, but rows now use background coloring similar to the TWS position/P&L view:

- green background for long rows and winning rows;
- red background for short rows and loss rows;
- stale prices remain marked with `*` and yellow text.

## Scope

### Browser export

A new SailorUI endpoint is available:

```text
POST /api/export
```

The endpoint reads the current SailorUI snapshot and writes both CSV and HTML reports under:

```text
logs\Paper\SailorUI\
logs\Live\SailorUI\
```

Each export writes timestamped files and latest aliases:

```text
sailor_ui_report_YYYYMMDD_HHMMSS.csv
sailor_ui_report_YYYYMMDD_HHMMSS.html
sailor_ui_report_latest.csv
sailor_ui_report_latest.html
```

The browser has an `export` button in the SailorUI header. Pressing it triggers `/api/export` and displays the written file paths in the status line.

### Export content

The CSV/HTML export includes:

- export timestamp;
- mode and UI status;
- daily P&L, unrealized P&L, realized P&L;
- Section 2 active/today trade rows;
- Section 3 rest scanner symbols;
- stale price markers;
- strategy selection visible in the snapshot;
- trade-enabled desired state.

### Visual color semantics

SailorUI now applies row-level background classes:

| Row state | UI background |
|---|---|
| Long position | Green |
| Winning position/P&L | Green |
| Short position | Red |
| Loss position/P&L | Red |
| Scanner selected side `LONG` | Green |
| Scanner selected side `SHORT` | Red |
| Flat/unknown | Neutral/dark |

When states conflict, for example a long position currently losing, the row uses a darker warning mix instead of hiding either condition.

## Safety

SAILOR-070 does not change order routing, broker access, strategy decisions, desired-state behavior, or live hardening.

Live SailorUI remains read-only and loopback-only under SAILOR-069. The export endpoint only writes local report files from already-visible UI snapshot state.

## Commands

Start paper UI with controls:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper sailor-ui --port 5101 --ui-controls --account DUN559573
```

Start live UI read-only:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- live sailor-ui --port 5101 --read-only
```

Run the specific self-test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario sailor-ui-report-export
```

Run all self-tests:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true -- paper trade-management-test --scenario all
```

Expected after SAILOR-070:

```text
PASS passed=20/20
```

## Acceptance criteria

- `dotnet build` passes.
- IBKR-enabled build passes.
- `trade-management-test --scenario all` passes `20/20`.
- SailorUI has an `export` button.
- `/api/export` writes CSV and HTML files under `logs\Paper\SailorUI` or `logs\Live\SailorUI`.
- Short/loss rows have red background.
- Long/win rows have green background.
- Live UI remains read-only and cannot change desired-state controls.
