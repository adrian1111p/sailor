# SAILOR-033 — Live-readiness gate implementation

SAILOR-033 adds a conservative live-readiness gate. Live mode can be opened in read-only mode for connection and scanner checks, but no live order route is created by this milestone.

## Configuration

`Runtime.Live.AllowLiveTrading` defaults to `false` in `appsettings.json`.

Additional live gate settings:

- `Runtime.Live.MaxOrderNotional`: default `100.00`.
- `Runtime.Live.CertificationMaxAgeHours`: default `24`.

These settings are evaluated only by the live-readiness gate. SAILOR-033 still blocks live order routing even when the gate passes; SAILOR-034 will consume the gate evidence for a controlled pilot.

## Read-only live commands

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect --read-only

dotnet run --project src\Sailor.App\Sailor.App.csproj -- live scan 1m v21-15minutes 3 smallcaps
```

`live connect` must include `--read-only`. Without it, the command prints the gate state and does not open the live TCP probe.

`live scan` is read-only by design. It can prepare history, request snapshots, and write scanner reports, but it never creates an order router.

## Live-readiness gate command

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live readiness --account DU123456 --max-notional 100 --confirm-live
```

The gate checks:

1. explicit manual command confirmation via `--confirm-live`,
2. `Runtime.Live.AllowLiveTrading=true`,
3. latest `logs/Paper/Reports/paper_certification_latest.json` exists,
4. paper certification can promote to live-readiness,
5. paper certification is recent enough,
6. live account matches the paper certification account,
7. requested max notional is positive and not above the configured live maximum.

The gate writes:

```text
logs/Live/Readiness/live_readiness_latest.json
logs/Live/Readiness/live_readiness_YYYYMMDD.csv
```

## Live order/run safety

`live run ... --send-orders` and `live order ... --send-orders` evaluate the same gate, print the result, and then return without creating a live order router.

This means the acceptance condition is met: no live order can be sent without both configuration approval and command confirmation. In SAILOR-033, no live order is sent even with those approvals; SAILOR-034 is the first planned live pilot milestone.

## Suggested validation

```powershell
dotnet clean
dotnet build

dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect

dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect --read-only

dotnet run --project src\Sailor.App\Sailor.App.csproj -- live readiness --account DUN559573 --max-notional 100

dotnet run --project src\Sailor.App\Sailor.App.csproj -- live readiness --account DUN559573 --max-notional 100 --confirm-live

dotnet run --project src\Sailor.App\Sailor.App.csproj -- live order TSLA BUY 1 LMT 350.00 --send-orders --account DUN559573 --max-notional 100 --confirm-live
```

Expected while `AllowLiveTrading=false` or the latest paper certification is blocked: readiness status is `Blocked`, and no live order is sent.
