# Vyxum

**English** | [Türkçe](README.tr.md)

Vyxum is modular weighing software for scales connected to a PC or server through a serial port or the network. It is installed on the customer's own computer or server and works without an internet connection.

This repository is the open core of Vyxum, licensed under the Apache License 2.0. Additional modules are developed separately.

> **Status: early development.** The repository contains the platform (module loading and validation, integration events with an outbox and inbox, database provider selection), the Products and Weighing modules, the web UI and the device drivers. Phase F2 is in progress.

## Kurulum / Installation

Windows 10 or 11 (64-bit). No internet connection, database server or extra program is needed. Run `VyxumSetup-<version>.exe` and follow the step-by-step guide: [Installation guide](docs/INSTALL.md) (Türkçe: [docs/KURULUM.md](docs/KURULUM.md)).

## Repository layout

| Path | Contents |
|---|---|
| `src/Platform/Vyxum.Platform.Abstractions` | The only Vyxum assembly modules may reference: module contracts, integration events, module `DbContext` base class |
| `src/Platform/Vyxum.Platform` | Runtime: module discovery and validation, capability registry, outbox dispatcher, inbox, database provider selection |
| `src/Platform/Vyxum.Platform.Testing` | Test helpers: in-memory module host and database fixtures for SQLite and SQL Server |
| `src/Host/Vyxum.Host` | ASP.NET Core application (`/health`, `/api/hello`) |
| `src/Modules/Products` | Products module: product catalog |
| `src/Modules/Weighing` | Weighing module: records, tare, amounts, cancel, export |
| `src/Web/vyxum-web` | React web UI, built into the host's `wwwroot` |
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
| `src/Devices/Vyxum.Devices.Discovery` | Read-only search for scales on serial ports or at one network address |

Supported transports are serial and TCP. Supported protocols are MT-SICS, Toledo Continuous and generic ASCII.

The drivers read the weight. The MT-SICS driver sends only `S`, `SI` and `SIR`, plus `Z` once when a user explicitly asks to zero the scale; tare, reset and settings commands are never sent. Toledo Continuous is listen-only. Generic ASCII sends only the request command you configure, which must only read the weight.

[Vyxum Agent](src/Agent/Vyxum.Agent/README.md) runs on the computer the scale is connected to. It reads the devices and sends the live weight to the web host.

To run the host and an agent with the simulator on one computer, use two terminals:

```bash
dotnet run --project src/Host/Vyxum.Host --urls http://localhost:5000
dotnet run --project src/Agent/Vyxum.Agent
```

Then open http://localhost:5000/api/devices. Under `dotnet run` the agent starts in the Development environment, whose `appsettings.Development.json` adds a simulated MT-SICS scale (572 g), so no hardware is needed.

Devices are configured in the web application (Settings → Devices) and stored in the `plt_Devices` table; the agent fetches them when it connects and applies every change without restarting. `GET`, `POST /api/devices/config` and `GET`, `PUT`, `DELETE /api/devices/config/{id}` read and change them (`transport` and `protocol` have the same fields as the agent settings; `capacity` and `division` are decimal strings). The `Devices` list in the agent's own settings file is for development: when it is not empty, the agent ignores the devices from the web application. See the [agent README](src/Agent/Vyxum.Agent/README.md). Each device in `/api/devices` also carries `name`, `capacity`, `division` and `unit` from its settings; a device whose settings the agent cannot use has the state `Error`.

http://localhost:5000/api/devices/{id}/live streams one device as Server-Sent Events: its current state at once, then a new event whenever its state or weight changes. A device id the agent has not reported yet is not an error; the stream starts in the `Disconnected` state and continues once the agent connects.

`POST /api/devices/{id}/zero` asks the scale to zero, once per request. It always answers 200 with `{ "result": "Done" | "NotSupported" | "Rejected" | "Unavailable", "detail": "..." }`; `detail` is English text for logs and may be missing. Each device lists the commands it supports in `supportedCommands` (`["Zero"]` for MT-SICS).

`POST /api/devices/discover` asks the agent to search for scales (body optional: `{ "agentName"?: "...", "tcp"?: { "host": "...", "port": 4001 } }`). It answers 202 with `{ "id" }`, 409 with `{ "code": "devices.DiscoveryRunning" }` while the agent already searches, and 503 with `{ "code": "devices.NoAgent" }` without a connected agent. `GET /api/devices/discover/{id}` returns the job: `status` (`Running`, `Completed`, `Failed` with `code` `devices.AgentTimeout` or `devices.DiscoveryFailed`), the `ports` with their state (`Pending`, `Scanning`, `Found`, `NotFound`, `InUse`, `Error`) and the `candidates`, whose `transport` and `protocol` objects can be posted to `/api/devices/config` unchanged. Jobs are kept in memory for 10 minutes. Discovery sends only the MT-SICS commands `SI` and `I4`; see [Device discovery](src/Agent/Vyxum.Agent/README.md#device-discovery).

## Modules

Products and Weighing are built into the host. The host always loads them, and neither depends on the other: they talk only through integration events. Each module keeps its own tables in the same database.

| Module | Project | What it does |
|---|---|---|
| Products | `src/Modules/Products/Vyxum.Modules.Products` | Product catalog: code, name, unit price, package tare, shelf life, barcode prefix, ingredients, and target weight with lower and upper tolerance (kg) |
| Weighing | `src/Modules/Weighing/Vyxum.Modules.Weighing` | Weighing records: live weight from a device, software tare, net, amount, lot, cancel with a reason, list, totals and Excel export |

Products endpoints (`/api/products`):

- `GET /api/products?search=&includeInactive=` lists products; `GET /api/products/{id}` returns one.
- `POST /api/products` creates a product; `PUT /api/products/{id}` updates it.
- `POST /api/products/{id}/deactivate` and `POST /api/products/{id}/activate` decide whether a product can be used for weighing.

Weighing endpoints (`/api/weighing`):

- `GET /settings` returns the rules the screen uses; `GET /products` searches products for the screen.
- `GET /records` lists records, filtered by `from`, `to`, `productId`, `lot` and `includeCancelled`, with `skip` and `take`. `GET /records/recent` and `GET /records/{id}` return recent and single records.
- `GET /records/export` downloads the filtered records as an Excel (.xlsx) file.
- `POST /records` records a weighing. Every request carries a `requestId`, so a retry with the same id does not record a second time.
- `POST /records/{id}/cancel` cancels a record. A reason is required while `RequireCancelReason` is true.

Integration events (version 1):

- `products.product-saved` is published when a product is saved. Weighing keeps its own copy of the product, so it never calls Products.
- `weighing.recorded` is published when a record is saved.
- `weighing.cancelled` is published when a record is cancelled.

Weighing rules are read from the `Vyxum:Weighing` section of the configuration:

| Key | Default | Meaning |
|---|---|---|
| `Vyxum:Weighing:RequireStable` | `true` | Only a stable weight can be recorded |
| `Vyxum:Weighing:MinimumNet` | `0` | Smallest net weight in kg that can be recorded. The net must always be greater than 0; a positive value also sets a lower limit. |
| `Vyxum:Weighing:MaxReadingAge` | `00:00:03` | Oldest scale reading that can be recorded. An older reading means the agent or its network stopped; the save is rejected. `00:00:00` turns the check off. |
| `Vyxum:Weighing:MaxSaveDelay` | `00:00:10` | Longest time between the weight the operator saw and the save reaching the server. A later save (for example one retried after a network error) is rejected, so it cannot record the next load. `00:00:00` turns the check off. |
| `Vyxum:Weighing:RequireCancelReason` | `true` | A cancel needs a reason |
| `Vyxum:Weighing:Currency` | `TRY` | Currency of amounts, stored with every record |
| `Vyxum:Weighing:AmountDecimals` | `2` | Decimals amounts are rounded to (0 to 4) |
| `Vyxum:Weighing:AmountRounding` | `AwayFromZero` | Rounding mode, by its .NET `MidpointRounding` name |

## Web UI

The screens are a React, TypeScript and Vite application in `src/Web/vyxum-web`. Vite builds it into `src/Host/Vyxum.Host/wwwroot`, and the host serves those files. Customers install nothing else: Node.js is needed only on the developer's computer.

- **Build:** `dotnet build` builds the UI when `wwwroot` is missing. This needs Node.js 22. Pass `-p:SkipUiBuild=true` to skip it; the host then serves the UI only if `wwwroot` was built earlier, and the root path returns 404 otherwise.
- **Development:** run `npm ci` once, then `npm run dev` in `src/Web/vyxum-web`. Vite serves the UI with live reload (usually at http://localhost:5173) and forwards `/api` and `/hubs` to http://localhost:5000, so start the host on port 5000 first.
- **Text:** the UI contains no literal text. Every string lives in a `Strings.resx` and `Strings.tr.resx` file, and reaches the UI through `/api/ui/strings`. The UI is available in Turkish and English.
- **Offline:** the fonts are bundled with the app through `@fontsource`. The UI loads nothing from the internet.

## Try it with a real scale

This uses a METTLER TOLEDO IND231 connected to a serial port:

1. Start the host:

   ```bash
   dotnet run --project src/Host/Vyxum.Host --urls http://localhost:5000
   ```

2. In a second terminal, start the agent with the IND231 example settings:

   ```bash
   dotnet run --project src/Agent/Vyxum.Agent -- --settings appsettings.Ind231.example.json
   ```

   The example reads `COM3` at 9600 8N1 with the MT-SICS command `SI`. Change `PortName` if the scale is on another port. The `--settings` path is relative to the agent's project folder, where `dotnet run --project` starts.

3. Open http://localhost:5000 in a browser.

How the screen works with the scale:

- Tare is kept in software by Vyxum. **Take tare** stores the current gross weight as the tare. The scale's own tare command is never sent.
- The **Zero** button sends `Z` to the scale, once per press. Nothing else is sent.

The database schema is created on the first start. The default SQLite file is `vyxum.db` in the data directory: `data/` under the working directory (`src/Host/Vyxum.Host/data/` when you start the host with the commands above), or `%ProgramData%\Vyxum` on Windows. On every start the platform and each module apply their pending EF Core migrations, so an existing database is upgraded and keeps its records. Before a SQLite database is changed, a copy is written to `backupsvyxum-<date>-<time>-migration.db` in the data directory; the newest 10 copies are kept. A SQL Server database is not copied: back it up yourself before an upgrade. See [CONTRIBUTING.md](CONTRIBUTING.md#database-migrations) for adding a migration.

## Configuration

Keys are read, later sources winning, from `appsettings.json` next to the executable, the shared settings file `vyxum.json` in the data directory, environment variables (for example `Vyxum__Database__Provider`) and command-line arguments.

`vyxum.json` is shared by the web host, the agent and the desktop window, and survives updates (unlike `appsettings.json` in the program folder). The installer writes it, and the setup wizard updates it when a different database is chosen; customers do not edit it by hand. Its location follows `Vyxum:DataDirectory`, so a data directory given as an environment variable or on the command line moves it too. The file is read once at start; after the database changes, the web host restarts (as a Windows service) or asks for a manual restart.

| Key | Default | Meaning |
|---|---|---|
| `Vyxum:Database:Provider` | `Sqlite` | `Sqlite` or `SqlServer` |
| `Vyxum:Database:ConnectionString` | SQLite file `vyxum.db` in the data directory | Required when the provider is `SqlServer` |
| `Vyxum:DataDirectory` | `%ProgramData%\Vyxum` on Windows, `data/` elsewhere | Folder for the default SQLite file |
| `Vyxum:Modules` | empty | List of assembly names to load modules from, e.g. `Vyxum:Modules:0` |
| `Vyxum:Outbox:PollingInterval` | `00:00:00.5` | How often outboxes are polled |
| `Vyxum:Outbox:BatchSize` | `100` | Maximum messages read from one outbox per poll |
| `Vyxum:Outbox:RetryBaseDelay` | `00:00:01` | Delay before the first retry; doubles after each failure |
| `Vyxum:Agents:Key` | written by the installer | Key every agent must send. The installer writes a random key for the web application and the agent on the same computer; an agent on another computer needs the same value as `Vyxum:Agent:Key`. Without a key only connections from this computer are accepted. Connections from a web browser are always refused. |
| `Vyxum:AllowedHosts` | empty | Extra host names the web application is reached by, separated by `;`. IP addresses, `localhost` and this computer's name always work; other names are refused (DNS rebinding protection). |

Time values use the .NET `TimeSpan` format (`hh:mm:ss` or `d.hh:mm:ss`).

## Architecture rules

These rules are enforced by the architecture tests where they can be checked mechanically.

- Modules do not call each other. They communicate only through integration events, delivered via the outbox and inbox.
- A module references only `Vyxum.Platform.Abstractions` among Vyxum assemblies. It does not reference another module, the host, the platform runtime, the devices or the agent.
- Each module owns exactly one `ModuleDbContext`, and its table names start with its prefix.
- The platform knows no module. The platform and `Vyxum.Platform.Abstractions` reference no module assembly and use no module namespace. Abstractions references no other Vyxum assembly.
- The platform and the host reference no device assembly and not the agent. The host does not reference sample modules.
- `Vyxum.Devices` references no Vyxum assembly. Protocols, transports and the simulator reference only `Vyxum.Devices`.
- `Vyxum.Agent` references no platform assembly and not the host.
- No module is mandatory. A missing optional capability leaves the module working; it only reports that the capability is unavailable.
- Code does not use provider-specific SQL. Data access must pass tests on both SQLite and SQL Server.

## Known limitations in F0

- **Modules without migrations.** A module that ships no migrations for the active provider gets its tables created once and cannot be upgraded until it ships migrations.
- **Single-node dispatcher.** Run one host instance per database. Several dispatchers on the same database are not coordinated.
- **No outbox cleanup.** Delivered messages are kept in the outbox tables.
- **No plugin folder loading.** Modules are loaded from assemblies listed in configuration; loading from a plugin folder is planned for phase F4.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Apache License 2.0. See [LICENSE](LICENSE).
