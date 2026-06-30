# SAILOR-055 — Scanner slot target and 5-minute replenishment notes

Date: 2026-06-30  
Status: implemented as the next trade-management runtime milestone after SAILOR-054.

## What changed

SAILOR-055 adds a scanner-owned slot manager around the paper/live-pilot conduct loop.

Implemented source changes:

- Added `ScannerSlotManager`.
- Added `ScannerSlotReplenishmentReport`.
- Added `ScannerSlotReplenishmentReportWriter`.
- Added scanner target settings in `ScannerSettings` and `appsettings.json`:
  - `TargetScannerTrades`
  - `ReplenishmentIntervalSeconds`
  - `ReplenishmentAllowWeakEntry`
  - `AvoidSameDayStoppedSymbols`
- Increased default `Runtime.Safety.MaxActiveSymbols` from `3` to `10` so the configured scanner target can be represented in runtime sessions.
- Preserved the original scan-list workbook scanner options for replenishment, so a paper run that initially narrows the universe to retained trade symbols can still replenish from the broader scan-list input later.
- Added `LastFrameTime` to `PaperSymbolSession` so replenishment can respect the same market-time frame used by conduct.
- Changed the conduct loop to use a mutable session list so scanner replenishment can add new scanner-owned sessions while exits/strategy conduct continue for existing sessions.

## Runtime behavior

At startup, SAILOR now writes an initial scanner-slot report before conduct begins.

Every configured replenishment interval, default `300` seconds, the conduct loop checks:

1. runtime safety allows new entries;
2. the reference ET minute is before `LastEntryMinute`;
3. scanner target is enabled;
4. active scanner-owned lifecycle count is below target.

If the scanner-owned count is below target, SAILOR runs a replenishment scanner pass and creates new scanner-owned sessions until the target shortfall is filled or no more eligible symbols are available.

Manual, pre-existing, explicit-runtime, and unknown-broker sessions are still conducted, but they do not reduce the scanner shortfall.

## Safety behavior

Replenishment is blocked when:

- runtime safety is degraded or close-only;
- broker reconciliation does not allow entries;
- the market-time frame is at or after `LastEntryMinute`;
- a candidate is already active;
- a candidate was stopped for the day and `AvoidSameDayStoppedSymbols=true`;
- session creation fails because history/profile data is unavailable.

From `LastEntryMinute` to `ForceFlatMinute`, existing sessions continue to be managed for exits only. Scanner replenishment does not create new sessions in that window.

## Reports

Reports are written to:

```text
logs/<mode>/ScannerSlots/scanner_slots_latest.json
logs/<mode>/ScannerSlots/scanner_slots_YYYYMMDD.csv
```

Report fields:

```text
targetScannerTrades
activeScannerTrades
manualManagedTrades
shortfall
newSlotsRequested
newSlotsCreated
blockedSymbols
reason
```

## Important limitation

This milestone implements slot target accounting and timed replenishment. It does not yet implement the severe-disconnect recovery rebuild from broker truth. That remains SAILOR-056.
