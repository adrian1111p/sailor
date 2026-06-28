# 2026-06-28 Sailor IBKR Connection Session Implementation

## Purpose

This document describes the SAILOR-024 implementation of the first IBKR connection session layer.

The implementation intentionally keeps Sailor simple and buildable. It does **not** import the Harvester live runtime and does **not** send any orders. It adds the Sailor-native connection/session contracts that the later history, L1/L2, order-router, and reconciliation milestones will use.

## Current SAILOR-024 scope

SAILOR-024 implements a TCP connection session probe for TWS/Gateway:

- reads paper/live host, port, client id, account, L1/L2 flags, and timeout from `appsettings.json`;
- connects to the configured IBKR/TWS/Gateway socket;
- records a normalized `IbkrConnectionSnapshot`;
- writes a paper/live runtime log under `logs/Paper/Runtime` or `logs/Live/Runtime`;
- disconnects cleanly after the connect command finishes;
- never requests market data;
- never submits orders.

## Why TCP probe first

The architecture audit requires an IBKR connection session that eventually waits for `nextValidId` and managed accounts. That full handshake requires the IBApi EReader/callback layer.

For the first Sailor paper/live step, the TCP probe is useful because it verifies the operator setup without introducing IBKR API coupling too early:

- TWS/Gateway is running;
- the configured host/port is reachable;
- the selected paper/live port is correct;
- logging and state models work;
- runtime command wiring works.

The next implementation layer will replace or extend `IbkrConnectionProbeSession` with a real IBApi adapter that waits for `nextValidId` and `managedAccounts`.

## New files

```text
src/Sailor.App/Broker/Ibkr/IbkrAccountSnapshot.cs
src/Sailor.App/Broker/Ibkr/IbkrConnectionChecklist.cs
src/Sailor.App/Broker/Ibkr/IbkrConnectionOptions.cs
src/Sailor.App/Broker/Ibkr/IbkrConnectionProbeSession.cs
src/Sailor.App/Broker/Ibkr/IbkrConnectionResult.cs
src/Sailor.App/Broker/Ibkr/IbkrConnectionSnapshot.cs
src/Sailor.App/Broker/Ibkr/IbkrConnectionState.cs
src/Sailor.App/Broker/Ibkr/IIbkrConnectionSession.cs
```

## Modified files

```text
src/Sailor.App/Runtime/Commands/SailorRuntimeCommandRunner.cs
src/Sailor.App/Configuration/SailorAppSettings.cs
src/Sailor.App/appsettings.json
```

## Commands

Paper connection probe:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- paper connect
```

Live connection probe:

```powershell
dotnet run --project src\Sailor.App\Sailor.App.csproj -- live connect
```

No market data is requested and no orders are sent by either command.

## Config

```json
"Runtime": {
  "Paper": {
    "Host": "127.0.0.1",
    "Port": 7497,
    "ClientId": 22,
    "ConnectTimeoutSeconds": 10
  },
  "Live": {
    "Host": "127.0.0.1",
    "Port": 7496,
    "ClientId": 21,
    "ConnectTimeoutSeconds": 10
  }
}
```

## Connection states

```text
Disconnected
Connecting
TcpConnected
ApiHandshakePending
ApiReady
Degraded
Disconnecting
Failed
```

Only `Connecting`, `TcpConnected`, `Disconnected`, and `Failed` are used by the TCP probe. The remaining states are prepared for the later full IBApi session.

## Expected successful output

```text
sailor paper IBKR connection session started
IBKR/TWS preflight checklist:
...
Connecting TCP to IBKR/TWS/Gateway at 127.0.0.1:7497 with clientId=22.
TCP socket connected successfully.
No market data requested. No orders sent.
SUCCESS: state=TcpConnected ...
Disconnected cleanly: state=Disconnected ...
```

## Expected failure output if TWS/Gateway is not running

```text
Socket error ConnectionRefused: Connection refused
Check that TWS/Gateway is running and API socket is enabled on the configured port.
FAILED: state=Failed tcpConnected=False ...
```

## Next milestone

Next step: add the full IBApi adapter layer:

- create an IBApi wrapper;
- start the EReader thread;
- call `eConnect`/start API;
- wait for `nextValidId`;
- wait for managed accounts;
- seed the Sailor order id allocator;
- keep orders disabled.

After that, Sailor can implement the historical 1m loader and live L1/L2 snapshot stream.
