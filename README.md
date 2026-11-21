# Vyxum

**English** | [Türkçe](README.tr.md)

Vyxum is modular weighing software for scales connected to a PC or server through a serial port or the network. It is installed on the customer's own computer or server and works without an internet connection.

This repository is the open core of Vyxum, licensed under the Apache License 2.0. Additional modules are developed separately.

> **Status: early development.** The repository currently contains the phase F0 platform skeleton: module loading and validation, integration events with an outbox and inbox, database provider selection, and a host with a health endpoint. There are no weighing features yet.

## Repository layout

| Path | Contents |
|---|---|
| `src/Platform/Vyxum.Platform.Abstractions` | The only Vyxum assembly modules may reference: module contracts, integration events, module `DbContext` base class |
| `src/Platform/Vyxum.Platform` | Runtime: module discovery and validation, capability registry, outbox dispatcher, inbox, database provider selection |
| `src/Platform/Vyxum.Platform.Testing` | Test helpers: in-memory module host and database fixtures for SQLite and SQL Server |
| `src/Host/Vyxum.Host` | ASP.NET Core application (`/health`, `/api/hello`) |
| `tests/Vyxum.Platform.Tests` | Unit tests |
| `tests/Vyxum.IntegrationTests` | End-to-end tests on both database providers, including host startup |
| `tests/Vyxum.ArchitectureTests` | Architecture rules, checked with NetArchTest.Rules |
| `tests/SampleModules` | Two sample modules used only by tests. They are not shipped. |
| `.github/workflows/ci.yml` | CI: build and test on Ubuntu with a SQL Server 2022 service container |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download). `global.json` pins SDK `10.0.100` and allows newer feature bands.
- Docker, optional, only to run the SQL Server tests locally.

## Build and test

```bash
dotnet build -warnaserror
dotnet test
```

Tests on SQL Server need the environment variable `VYXUM_TEST_SQLSERVER`, set to a connection string without a database name. Each test creates its own uniquely named database and drops it afterwards. If the variable is missing, the SQL Server cases are reported as skipped with a reason; SQLite cases always run.

To run SQL Server 2022 locally in Docker:

```bash
docker run -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Vyxum_Test_123!' \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

export VYXUM_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=Vyxum_Test_123!;TrustServerCertificate=True'
dotnet test
```

On Windows PowerShell, use `$env:VYXUM_TEST_SQLSERVER = '...'` instead of `export`. The password above is for local testing only. Use your own password for any other environment.

## Running the host

```bash
dotnet run --project src/Host/Vyxum.Host
```

To choose the address, add `--urls http://127.0.0.1:5199` after `--`. No configuration is needed. The host uses SQLite and creates `vyxum.db` in the data directory on first start. The data directory is `%ProgramData%\Vyxum` on Windows and `data/` under the working directory elsewhere.

Endpoints:

```bash
curl http://127.0.0.1:5199/health
# {"status":"ok","modules":[],"database":"Sqlite"}

curl -H "Accept-Language: en" http://127.0.0.1:5199/api/hello
# {"message":"Hello from Vyxum","culture":"en"}
```

The host supports `tr` (default) and `en`.

## Devices

A scale is read in two parts. A **transport** moves bytes over a serial port or TCP. A **protocol** understands the bytes the scale sends. Any transport works with any protocol.

| Project | Contents |
|---|---|
| `src/Devices/Vyxum.Devices` | Contracts (`ITransport`, `IScaleProtocol`), frame reader and device runner |
| `src/Devices/Vyxum.Devices.Transports` | Serial port and TCP transports |
| `src/Devices/Vyxum.Devices.MtSics` | MT-SICS protocol |
| `src/Devices/Vyxum.Devices.ToledoContinuous` | Toledo continuous output protocol |
| `src/Devices/Vyxum.Devices.GenericAscii` | Rule-based protocol for other ASCII output |
| `src/Devices/Vyxum.Devices.Simulator` | Simulated scales over memory or TCP, and replay of recorded lines |

Supported transports are serial and TCP. Supported protocols are MT-SICS, Toledo Continuous and generic ASCII.

The drivers read the weight. The MT-SICS driver sends only `S`, `SI` and `SIR`, plus `Z` once when a user explicitly asks to zero the scale; tare, reset and settings commands are never sent. Toledo Continuous is listen-only. Generic ASCII sends only the request command you configure, which must only read the weight.

[Vyxum Agent](src/Agent/Vyxum.Agent/README.md) runs on the computer the scale is connected to. It reads the devices and sends the live weight to the web host.

To run the host and an agent with the simulator on one computer, use two terminals:

```bash
dotnet run --project src/Host/Vyxum.Host --urls http://localhost:5000
dotnet run --project src/Agent/Vyxum.Agent
```

Then open http://localhost:5000/api/devices. The agent's shipped settings use a simulated MT-SICS scale (572 g), so no hardware is needed.

http://localhost:5000/api/devices/{id}/live streams one device as Server-Sent Events: its current state at once, then a new event whenever its state or weight changes. A device id the agent has not reported yet is not an error; the stream starts in the `Disconnected` state and continues once the agent connects.

`POST /api/devices/{id}/zero` asks the scale to zero, once per request. It always answers 200 with `{ "result": "Done" | "NotSupported" | "Rejected" | "Unavailable", "detail": "..." }`; `detail` is English text for logs and may be missing. Each device lists the commands it supports in `supportedCommands` (`["Zero"]` for MT-SICS).

## Configuration

Keys are read from `appsettings.json`, environment variables (for example `Vyxum__Database__Provider`) or command-line arguments.

| Key | Default | Meaning |
|---|---|---|
| `Vyxum:Database:Provider` | `Sqlite` | `Sqlite` or `SqlServer` |
| `Vyxum:Database:ConnectionString` | SQLite file `vyxum.db` in the data directory | Required when the provider is `SqlServer` |
| `Vyxum:DataDirectory` | `%ProgramData%\Vyxum` on Windows, `data/` elsewhere | Folder for the default SQLite file |
| `Vyxum:Modules` | empty | List of assembly names to load modules from, e.g. `Vyxum:Modules:0` |
| `Vyxum:Outbox:PollingInterval` | `00:00:00.5` | How often outboxes are polled |
| `Vyxum:Outbox:BatchSize` | `100` | Maximum messages read from one outbox per poll |
| `Vyxum:Outbox:RetryBaseDelay` | `00:00:01` | Delay before the first retry; doubles after each failure |
| `Vyxum:Outbox:RetryMaxDelay` | `00:05:00` | Upper limit of the retry delay |

Time values use the .NET `TimeSpan` format (`hh:mm:ss` or `d.hh:mm:ss`).

## Architecture rules

These rules are enforced by the architecture tests where they can be checked mechanically.

- Modules do not call each other. They communicate only through integration events, delivered via the outbox and inbox.
- Modules reference only `Vyxum.Platform.Abstractions` among Vyxum assemblies. Modules do not reference each other or the host.
- The platform knows no module. Platform and Abstractions do not reference any module assembly.
- No module is mandatory. A missing optional capability leaves the module working; it only reports that the capability is unavailable.
- Code does not use provider-specific SQL. Data access must pass tests on both SQLite and SQL Server.

## Known limitations in F0

- **No schema migrations yet.** Each module's tables are created on first start. If a module's model changes after its tables exist, the change is not applied and a warning is logged. Per-provider EF Core migrations are planned for phase F2.
- **Crash during table creation.** If the process stops after a module's tables are created but before the installation is recorded, the database must be cleaned up by hand.
- **Single-node dispatcher.** Run one host instance per database. Several dispatchers on the same database are not coordinated.
- **No outbox cleanup.** Delivered messages are kept in the outbox tables.
- **No plugin folder loading.** Modules are loaded from assemblies listed in configuration; loading from a plugin folder is planned for phase F4.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Apache License 2.0. See [LICENSE](LICENSE).
