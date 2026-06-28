# SAILOR-032 — Paper certification report implementation

## Scope

SAILOR-032 adds a paper certification report command that consolidates the artifacts produced by the paper runtime milestones before the live-readiness gate is implemented.

Command:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper report latest
```

## Implemented files

```text
src/Sailor.App/Reporting/PaperCertificationReport.cs
src/Sailor.App/Reporting/PaperSessionReportWriter.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
```

## Report outputs

The command writes the latest certification artifacts under:

```text
logs/Paper/Reports/paper_certification_latest.json
logs/Paper/Reports/paper_certification_latest.md
logs/Paper/Reports/paper_certification_YYYYMMDD.csv
```

A runtime command log is also written under:

```text
logs/Paper/Runtime/paper_report_latest_YYYYMMDD_HHMMSS.log
```

## Evidence included

The report combines:

- latest `paper_run` runtime log
- Sailor paper order ledger
- local position store derived from the ledger
- latest broker reconciliation JSON
- runtime incident JSONL files

The report includes the required certification fields:

- session mode
- account
- profile
- symbols
- orders submitted
- orders filled
- orders rejected
- positions opened/closed
- force-flat result
- disconnect/degraded incidents
- reconciliation status
- L1/L2 health
- P&L
- strategy decisions
- all open exposure at end = zero

## Promotion rule

The report sets `CanPromoteToLiveReadiness=false` when any of the following is true:

1. End open exposure is non-zero.
2. Latest broker reconciliation is missing or not `Matched`.
3. A degraded/disconnect incident exists for the latest paper session.

The main acceptance requirement is enforced explicitly: a session cannot be promoted if end exposure is non-zero.

## Current expected result with existing paper account state

If the old TSLA paper order from SAILOR-028 is still visible in TWS as `PreSubmitted`, the report should be generated but blocked because the latest reconciliation remains `CriticalMismatch`.

This is correct safety behavior. Cancel the old TSLA paper order in TWS and rerun `paper reconcile` before expecting a clean certification.
