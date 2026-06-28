# SAILOR-022 Runtime Contracts and Command Skeleton

Date: 2026-06-28

## Purpose

SAILOR-022 starts the paper/live implementation without adding an IBKR dependency yet.
The goal is to create a simple, stable runtime boundary that can be reused by paper and live mode while keeping the current backtest behavior unchanged.

## What is implemented now

This milestone adds:

- runtime mode model: `Backtest`, `Paper`, `Live`
- runtime options and state model
- normalized strategy frame and strategy decision contracts
- normalized order intent and order status contracts
- `paper` command skeleton
- `live` command skeleton
- dry-run logging under `logs/Paper/Runtime` and `logs/Live/Runtime`
- scanner reuse from the current Sailor CSV/backtest cache for paper/live command validation

No IBKR connection is opened in this milestone.
No market data subscription is opened.
No order is sent.

## New commands

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper connect
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan 1m sailor-trend-volume 3 smallcaps
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper run 1m v21-15minutes 1 TSLA --dry-run
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper status
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper flatten TSLA
```

Live skeleton commands exist too, but they do not send orders:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live status
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live run 1m sailor-trend-volume 1 TSLA --dry-run
```

## Runtime contract files

```text
src/Sailor.App/Runtime/Common/SailorRuntimeMode.cs
src/Sailor.App/Runtime/Common/SailorRuntimeOptions.cs
src/Sailor.App/Runtime/Common/SailorRuntimeState.cs
src/Sailor.App/Runtime/Common/SailorRuntimeStatus.cs
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
src/Sailor.App/Strategy/Runtime/ISailorRuntimeStrategy.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyFrame.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyDecision.cs
src/Sailor.App/Strategy/Runtime/SailorStrategyDecisionType.cs
src/Sailor.App/Broker/Orders/SailorOrderIntent.cs
src/Sailor.App/Broker/Orders/SailorOrderSide.cs
src/Sailor.App/Broker/Orders/SailorOrderStatus.cs
src/Sailor.App/Broker/Orders/SailorOrderType.cs
src/Sailor.App/Broker/Orders/SailorOrderUpdate.cs
```

## Design rule

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

The order router later converts the decision into:

```text
SailorOrderIntent
```

This keeps strategy logic independent from IBKR and allows the same conduct strategies to be used by backtest, paper, and live.

## appsettings additions

The new `Runtime` section stores paper/live connection targets and safety defaults:

```json
"Runtime": {
  "Paper": {
    "Host": "127.0.0.1",
    "Port": 7497,
    "ClientId": 22,
    "SendOrders": false,
    "UseL1": true,
    "UseL2": true,
    "AllowShort": true
  },
  "Live": {
    "Host": "127.0.0.1",
    "Port": 7496,
    "ClientId": 21,
    "SendOrders": false,
    "UseL1": true,
    "UseL2": true,
    "AllowShort": false
  },
  "Safety": {
    "LastEntryMinute": 945,
    "ForceFlatMinute": 955,
    "EmergencyFlattenOnDisconnect": true
  }
}
```

`SendOrders` remains false by default.

## What is intentionally not implemented yet

- IBKR API reference
- TWS/Gateway connection
- historical data download
- live L1/L2 subscriptions
- real order routing
- real flatten routing
- reconnect loop
- persistent position tracking

These start in the next milestones.

## Acceptance test

```powershell
dotnet clean
dotnet build
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper connect
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper scan 1m sailor-trend-volume 3 smallcaps
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper flatten TSLA
```

Expected result:

- build succeeds
- commands print help/status/log output
- runtime logs are created
- no IBKR dependency is required
- no orders are sent
- existing backtest commands are unchanged
