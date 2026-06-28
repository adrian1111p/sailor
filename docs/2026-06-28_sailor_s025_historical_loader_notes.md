# 2026-06-28 Sailor SAILOR-025 Historical 1m Data Loader Notes

## Goal

SAILOR-025 starts the paper/live historical data path without sending orders.

It adds a `paper history` / `live history` command that prepares one-minute bars for selected symbols and writes them to the Sailor cache.

```text
cache/history/{yyyy-MM-dd}/{SYMBOL}/1m.csv
```

By default the command also mirrors the file to:

```text
backtest/data/{SYMBOL}/1m.csv
```

This allows the existing backtest module to immediately reuse the same bars.

## Commands

Local cache smoke test:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper history 1m TSLA --local-cache
```

Paper history request path:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper history 1m TSLA --days 5
```

Smallcaps subset:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper history 1m smallcaps --top 10 --days 5
```

All-hours history instead of regular trading hours:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper history 1m TSLA --all-hours
```

Do not mirror into backtest data:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper history 1m TSLA --no-backtest-copy
```

## IBApi mode

The default Sailor build remains dependency-free, so it can compile even where NuGet/IBKR API is unavailable.

The real IBKR historical adapter is implemented behind the optional build property:

```powershell
dotnet restore src\Sailor.App\Sailor.App.csproj -p:EnableIbkrApi=true

dotnet run -p:EnableIbkrApi=true --project src\Sailor.App\Sailor.App.csproj -- paper history 1m TSLA --days 5
```

This enables the `IBApi` NuGet package and compiles the Sailor-native IBKR historical provider.

## Safety

SAILOR-025 sends no orders.

The command only performs one of the following:

1. TCP preflight probe to TWS/Gateway.
2. Optional IBApi historical data request when built with `EnableIbkrApi=true`.
3. Local CSV fallback when IBApi is not enabled or `--local-cache` is used.
4. CSV cache writing.

No market data subscriptions are opened yet. L1/L2 live snapshots start in the next milestone.

## Intended next step

SAILOR-026 should use the established connection/history foundation to fill the Sailor-native L1/L2 snapshot store from IBKR callbacks.
