# Vyxum Agent

[Türkçe](README.tr.md)

Vyxum Agent runs on the computer the scale is connected to. It reads the scale over a serial port or the network and
sends the live weight to the Vyxum web application over SignalR. It connects to the web application by itself and
reconnects automatically, both to the scale and to the web application.

## Where the devices come from

Devices are configured in the web application, under Settings → Devices, and stored in its database. The agent asks
the web application for its devices when it connects, and again whenever they change there; it then starts new
devices, stops removed ones and restarts changed ones without restarting itself. Devices that did not change keep
running. A device whose settings the agent cannot use does not run and is shown with status `Error` and the reason.

A device without a computer name is served by the first agent that asked for its devices; on a single computer that
is the only agent. A device with a computer name (`agentName`) is served by the agent with that `AgentName`.

The `Devices` list in the agent's own settings file is for development and for setups without the web settings: when
it lists any device, the agent uses those devices and ignores the ones configured in the web application. The
installed agent ships with an empty list.

## Run as a console application

`dotnet run` starts the agent in the Development environment (see `Properties/launchSettings.json`), where
`appsettings.Development.json` adds a simulated MT-SICS scale (572 g), so it works without hardware:

```
dotnet run --project src/Agent/Vyxum.Agent
```

To use another settings file instead of `appsettings.json`, pass `--settings`:

```
dotnet run --project src/Agent/Vyxum.Agent -- --settings appsettings.Ind231.example.json
```

Any setting can also be given on the command line, for example `--Vyxum:Agent:ServerUrl http://localhost:8080`.

## Install as a Windows service

The installer of a later release does this automatically. Until then, publish the agent and register it from an
administrator command prompt:

```
dotnet publish src/Agent/Vyxum.Agent -c Release -o C:\Vyxum\Agent
sc.exe create "Vyxum Agent" binPath= "C:\Vyxum\Agent\Vyxum.Agent.exe" start= auto
sc.exe start "Vyxum Agent"
```

The space after `binPath=` and `start=` is required. The service reads `appsettings.json` from its own folder and
writes its log to the Windows event log. To remove it: `sc.exe stop "Vyxum Agent"` and `sc.exe delete "Vyxum Agent"`.

## Settings

All settings are in the `Vyxum:Agent` section of `appsettings.json`. Invalid settings stop the agent at startup with
a message that names the setting.

| Setting | Meaning | Default |
|---|---|---|
| `ServerUrl` | Address of the Vyxum web application | `http://localhost:5000` |
| `Key` | Agent key, needed only when the web application has one (`Vyxum:Agents:Key`). Never logged. | empty |
| `AgentName` | Name shown in the web application | computer name |
| `HeartbeatInterval` | The latest reading is sent again this often when it does not change | `00:00:01` |
| `ReconnectDelay` | First wait before reconnecting to a scale or to the server; doubles after each failure | `00:00:01` |
| `MaxReconnectDelay` | Longest wait between attempts | `00:00:30` |
| `Devices` | Local list of devices, see below. When it is not empty, the devices from the web application are ignored. | empty |

Each device has an `Id`, a `Transport` and a `Protocol`. Any transport works with any protocol. The device settings
in the web application have the same `transport` and `protocol` fields (in camel case).

- `Transport.Type`:
  - `serial`: `PortName` (required, for example `COM3`), `BaudRate` (9600), `DataBits` (8), `Parity` (`None`, `Odd`,
    `Even`, `Mark`, `Space`), `StopBits` (`One`, `OnePointFive`, `Two`), `Handshake` (`None`, `XOnXOff`,
    `RequestToSend`, `RequestToSendXOnXOff`), `DtrEnable` (true), `RtsEnable` (true).
  - `tcp`: `Host` and `Port` (required), `ConnectTimeout` (5 s), `KeepAlive` (off).
  - `simulator`: `Simulator` with `Kind` (`MtSics`, `ToledoContinuous`, `Replay`), `Weight`, `Unit`, `State`,
    `Interval`, `RecordingPath` (replay).
- `Protocol.Type`:
  - `mt-sics`: `Command` (`SI`, `S` or `SIR`), `PollInterval` (200 ms), `ResponseTimeout` (1 s), `ZeroTimeout` (3 s),
    `RetriesOnError`, `MaxConsecutiveTimeouts`, `Terminator`.
  - `toledo-continuous`: `ChecksumEnabled`, `InactivityTimeout` (3 s), `MaxConsecutiveInvalidFrames`.
  - `generic-ascii`: `Pattern` (required, a regular expression with a `weight` group), `StablePattern`,
    `MotionPattern`, `OverloadPattern`, `UnderloadPattern`, `NetPattern`, `DecimalSeparator`, `ThousandsSeparator`,
    `DefaultUnit`, `Terminator`, `RequestCommand` (demand mode), `PollInterval`, `ResponseTimeout`, `InactivityTimeout`.

`appsettings.Ind231.example.json` shows a METTLER TOLEDO IND231 on COM3 (9600 8N1, MT-SICS, command `SI`).

## Safety: read commands, and zero on request

The agent reads the weight. With MT-SICS it sends the configured read command (`S`, `SI` or `SIR`) and, only when a
user presses "Zero" in the web application, `Z` exactly once for that press. Nothing else is ever sent: tare (`T`,
`TI`, `TA`), immediate zero (`ZI`), reset (`@`) and configuration commands are blocked in code, and `Command` accepts only
the read commands (any other value is rejected at startup). `Z` is sent between two read commands on the same
connection. If the scale answers `ES` or `ET` (not executed) it is resent like any other command; if it does not answer
within `ZeroTimeout`, it is not resent, because the scale may have zeroed. The agent reports which devices support zero
when it registers. Toledo Continuous is listen-only and does not support zero; neither does `generic-ascii`.
With `generic-ascii`, `RequestCommand` is free text because every brand uses its own: only ever configure a command
that reads or prints the weight, never one that tares, zeroes or changes the device.
