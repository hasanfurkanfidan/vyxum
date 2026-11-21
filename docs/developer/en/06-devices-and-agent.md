# 6. Devices and the Agent

[Index](../README.md) · Previous: [5. Database](05-database.md) · Next: [7. Web UI](07-web-ui.md)

## The two-part idea

Reading a scale has two independent problems:

1. **Transport:** moving bytes (a serial port at 9600 baud 8N1, or a TCP socket). Interface `ITransport`: `OpenAsync`, `ReadAsync`, `WriteAsync`, `CloseAsync`, `DiscardInput`.
2. **Protocol:** understanding the bytes (MT-SICS sends `S S     0.572 kg`; Toledo Continuous sends frames starting with STX). Interface `IScaleProtocol`: `IAsyncEnumerable<WeightReading> ReadAsync(ITransport, CancellationToken)`.

Any transport works with any protocol. A protocol never knows whether it is talking to a serial port or the simulator, which is exactly why the simulator can be used for tests.

A `WeightReading` is brand-independent: value (as the device sent it), unit, state (`Stable`, `Motion`, `Overload`, `Underload`), optional tare, gross/net flag, the raw text, and a timestamp.

`DeviceRunner` keeps one device alive: open the transport, run the protocol, push readings into a bounded channel (oldest dropped if nobody reads), and on **any** failure close and reopen with a growing wait. A device unplugged for an hour must come back by itself.

`FrameReader` helps protocols that need to cut a byte stream into frames (line ending, or STX...CR with optional checksum byte).

## The safety rule (read this twice)

A scale in a shop is a measuring instrument that may be legally controlled. A wrong command could tare, zero or reconfigure it. So:

- Drivers **only read** the weight.
- MT-SICS sends only `S`, `SI`, `SIR`, and `Z` **once per press** when a user explicitly presses "Zero". Tare (`T`, `TI`, `TA`), immediate zero (`ZI`), reset (`@`) and settings commands are blocked in code.
- Toledo Continuous is listen-only. Generic ASCII sends only the request command the user configured, which must only read the weight.
- Discovery sends exactly two commands, `SI` and `I4` (each followed by CR LF), through a whitelist (`WriteGuardTransport`) that rejects any other write before a byte reaches the port, and a test checks it.

Never add a command that changes the device. If a customer asks for one, stop and discuss it.

## From scale to screen

```
Device ─bytes─> ITransport ─> IScaleProtocol ─> WeightReading
   (in Vyxum.Devices*, inside the Agent)
        │ DeviceRunner / DeviceFleet
        ▼
   HubWorker ──SignalR /hubs/agent──> AgentHub ──> LiveWeightStore (memory)
                                                       │ ILiveWeightSource
                       ┌───────────────────────────────┴───────────────┐
                 Weighing module                         /api/devices/{id}/live
              (reads the current weight)                 (Server-Sent Events → browser)
```

Key points:

- Live weights are kept **in memory only** and are not integration events (they change several times per second; storing them would be wasteful). The platform exposes them through `ILiveWeightSource` (in Abstractions), so a module gets weights without knowing any brand or the Agent.
- The wire contract between Agent and host is `AgentHubContract` (hub path `/hubs/agent`, key header `X-Vyxum-Agent-Key`). Agent→host methods: `Register`, `ReportReading`, `ReportStatus`, `GetDevices`, `ReportDiscoveryProgress`. Host→agent calls: `Zero`, `Discover`, `DevicesChanged`. The Agent cannot reference the platform (an architecture rule), so there are **two copies of the contract**, `src/Platform/Vyxum.Platform/Devices/AgentHubContract.cs` and `src/Agent/Vyxum.Agent/Contract/AgentHubContract.cs`. `tests/Vyxum.IntegrationTests/AgentContractTests.cs` keeps them in sync: if you change one, change the other and run that test.
- The agent key (`Vyxum:Agents:Key` on the host, `Vyxum:Agent:Key` on the agent) is what lets an agent on **another** computer connect: with no key set the host accepts only local agents (`AgentConnectionGate`). The key is only accepted in the header, never in the URL, and never logged.

## Where device settings live

Devices are configured in the web UI (Settings → Devices) and stored in the platform table `plt_Devices` (`DeviceConfigService`). The host stores `transport` and `protocol` as JSON objects and only checks that a `type` exists. It does **not** know protocol names, so adding a protocol never changes the platform. When an agent connects it calls `GetDevices`; when settings change the host sends `DevicesChanged` and the agent's `DeviceFleet` starts, stops or restarts only the devices that changed. A device whose settings the agent cannot use shows state `Error` with the reason.

For development, the `Devices` list in the agent's own `appsettings*.json` overrides everything. See the [Agent README](../../../src/Agent/Vyxum.Agent/README.md) for the settings table.

## Try a real scale

See the "Try it with a real scale" section of the [README](../../../README.md#try-it-with-a-real-scale) (IND231 on COM3, `--settings appsettings.Ind231.example.json`).

## Add a new protocol (step by step)

Imagine a brand "Acme" whose scale prints `ST,GS,  1.250kg` lines.

1. **Project.** Create `src/Devices/Vyxum.Devices.Acme/Vyxum.Devices.Acme.csproj` referencing **only** `Vyxum.Devices` (copy `Vyxum.Devices.MtSics.csproj`). Add it to `Vyxum.slnx`. The architecture test `Protocol_assemblies_reference_only_vyxum_devices` guards this; add your assembly to its list.
2. **Parser as a pure function.** `AcmeParser.Parse(string line)` returns a value or "invalid". No I/O, no clock. This is the part that has the bugs, so make it easy to test.
3. **Protocol class** `AcmeProtocol : IScaleProtocol`. Inside `ReadAsync`, get lines (use `FrameReader` or your own loop), call the parser and `yield return` a `WeightReading`. Throw `DeviceConnectionLostException` when the link is dead (silence for too long, too many invalid frames) so `DeviceRunner` reconnects. Pass `TimeProvider` in so tests control the clock. Keep an options record for timeouts. Look at `ToledoContinuousProtocol` (listen-only) and `MtSicsProtocol` (request/response) as models.
4. **Tests** in `tests/Vyxum.Devices.Tests/Acme/`. Include **raw lines recorded from a real scale** as test data. A parser tested only with invented lines will fail on the first real one.
5. **Simulator scenario.** Add an `ISimulatedDevice` (it speaks bytes only and knows nothing about your protocol class) in `Vyxum.Devices.Simulator`, add a `SimulatorKind` value and a case in `SimulatorFactory`. A recorded file for the `Replay` kind can be put in `Recordings/`. Now the protocol can be tested end to end without hardware.
6. **Register the type name in the Agent.** In `src/Agent/Vyxum.Agent/Configuration/AgentConfiguration.cs` add `"acme"` to `ProtocolTypes` and a `case "acme":` in `CreateProtocol` that validates the settings and builds the protocol. Add any new setting fields to `ProtocolSettings` in `AgentSettings.cs`. Settings errors must name the setting (see the existing cases). Add the type to the Agent README's settings table (English and Turkish).
7. **Web UI.** The device screens know the protocol types they offer. Search `src/Web/vyxum-web/src` for `mt-sics` (it appears in `components/devices/ConnectionFields.tsx` and `lib/devices.ts`) and add yours with texts in the resource files ([Chapter 7](07-web-ui.md)).
8. **Optional:** zero support (`ISupportsZero`, sent once, serialized with the read loop), and discovery support: implement `IPassiveProbe` (recognise a protocol from what the device sends on its own) or `IActiveProbe` (list every allowed write in `AllowedWrites`; only read-only commands) in your package and add it to the lists in `PortScannerOptions`.
9. **Docs:** update the Devices table in both READMEs.

## Add a new transport

Same idea: implement `ITransport` in `Vyxum.Devices.Transports` (it references only `Vyxum.Devices`), add the type name to `TransportTypes` and a case in the transport part of `AgentConfiguration`, and to the host's transport check in `DeviceConfigService.ValidateTransport`. Transports must not interpret bytes.
