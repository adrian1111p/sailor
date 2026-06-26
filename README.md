# sailor

Simple C# day trading research application.

## Current scope

The application is still development / backtest only.

It does **not** connect to IBKR, TWS, DAS Trader, or any real broker.

## Features

- C# / .NET 8
- Console application
- CSV-based backtest data
- Technical indicators: EMA9, SMA20, SMA200, VWAP, VolumeAverage20
- First Sailor scanner/strategy profile: `sailor-trend-volume`
- Conduct profiles: `sailor-conduct-v3`, `harvester-conduct-v3`, `harvester-conduct-v9`
- Optional simple profile: `simple-momentum`
- Logs under root `logs`
- No Python

## Build

```bash
dotnet build
```

## List available data

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest --list
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest --list AAPL
```

## Run scanner

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- scan 1m
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- scan 1m sailor-trend-volume 20
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- scan 1m harvester-conduct-v3 20
```

## Run backtest

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest AAPL 1m sailor-trend-volume
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest TSLA 1m simple-momentum
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest TSLA 1m harvester-conduct-v3
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest TSLA 1m harvester-conduct-v9
```

## SAILOR-005 profile

`sailor-trend-volume` uses these filters:

- close price between 0.50 and 300.00
- latest volume at least 100,000
- EMA9 above SMA20
- close above VWAP
- close above SMA200 when SMA200 is available
- latest volume at least 1.00x the 20-bar volume average
- entry requires short-term positive momentum
- exit can happen on momentum loss, close below EMA9, close below VWAP, EMA9 below SMA20, stop loss, take profit, or max hold

## Logs

SAILOR-009 moves all logs to the repository root:

```text
logs/Backtest
logs/Live
logs/Paper
```

The old location is no longer used:

```text
src/Sailor.App/Logs
```

## SAILOR-006 scanner + automatic backtest ranking

Run scanner, automatically backtest the top candidates, and create one Markdown ranking report:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- rank 1m sailor-trend-volume 20 all
```

Small-cap universe from the user list:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- rank 1m sailor-trend-volume 20 smallcaps
```

Custom comma-separated universe:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- rank 1m simple-momentum 20 ALIT,BARK,SOFI,PLTR
```

Aliases are also supported:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- scan-backtest 1m sailor-trend-volume 20 smallcaps
```

The ranking report is written to:

```text
logs/Backtest/ranking_<universe>_<profile>_<timeframe>_<timestamp>.md
```

## SAILOR-007 appsettings.json configuration

Risk and profile settings are now read from:

```text
src/Sailor.App/appsettings.json
```

Configurable defaults:

- default timeframe
- default profile
- default universe
- initial cash
- max position notional
- stop loss percent
- take profit percent
- max hold bars
- scanner top count
- profile filters and thresholds
- conduct exit profile settings
- market-hours entry and force-flat settings

Examples using configured defaults:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- scan
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- rank
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest TSLA
```

Command-line values still override the configured defaults.

## SAILOR-008 conduct-style exits

SAILOR-008 adds a Sailor-native conduct profile without compiling the old Harvester legacy code:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest TSLA 1m sailor-conduct-v3
```

The profile still uses the Sailor scanner and indicator entry filters, but once a position is open the exit is managed by the conduct engine:

- hard stop
- move stop to breakeven after a configured profit percent
- trailing giveback after a configured profit percent
- giveback notional cap
- EMA9 reversal exit
- VWAP reversal exit
- EMA9/SMA20 trend reversal exit
- max-hold exit

Scanner + automatic backtest ranking with conduct exits:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- rank 1m sailor-conduct-v3 20 smallcaps
```

## SAILOR-009 Harvester-inspired conduct profiles

SAILOR-009 keeps the Harvester legacy folder excluded from compilation, but ports the useful **conduct behavior** into Sailor-native profiles:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest TSLA 1m harvester-conduct-v3
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- backtest TSLA 1m harvester-conduct-v9
```

The new conduct profiles add:

- scanner-driven entry candidates
- next-bar-open entry simulation
- market-hours entry gating
- forced intraday flat before close
- minimum bars between entries
- opposite momentum exit
- breakeven protection
- trailing giveback with notional cap
- optional micro-trail behavior for the V9-style profile
- multiple entries and exits over the same symbol/day when the setup reappears

Small-cap ranking examples:

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- rank 1m harvester-conduct-v3 20 smallcaps
```

```bash
dotnet run --project src/Sailor.App/Sailor.App.csproj -- rank 1m harvester-conduct-v9 20 smallcaps
```
