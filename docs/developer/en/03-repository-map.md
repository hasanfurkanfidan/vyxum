# 3. Repository map

[Index](../README.md) · Previous: [2. Getting started](02-getting-started.md) · Next: [4. Modules and events](04-modules.md)

Where things live. When you need to change something, find its row first.

## Top level

| Path | What it is |
|---|---|
| `Vyxum.slnx` | The solution file (new XML solution format). `dotnet build` at the root builds it. |
| `global.json` | Pins the .NET SDK (`10.0.100`, newer feature bands allowed) and selects the test runner. |
| `Directory.Build.props` | Settings for every project: `net10.0`, nullable, implicit usings, warnings are errors, and **`<Version>`**, the single source of the product version. |
| `Directory.Packages.props` | All NuGet package versions (central package management). |
| `.config/dotnet-tools.json` | The repository-local tool `dotnet-ef` (run `dotnet tool restore` once). |
| `.editorconfig` | Code style. |
| `.github/workflows/ci.yml` | CI: .NET build+test (with SQL Server), web checks, installer build. |
| `CLAUDE.md` | The project's non-negotiable rules. Read it. |
| `README.md`, `README.tr.md` | Overview, configuration keys and HTTP API of the modules. |
| `CONTRIBUTING.md` | Contribution rules, migration commands. |
| `docs/` | `INSTALL.md` / `KURULUM.md` for end users, and this developer guide. |
| `installer/` | Inno Setup script and build script ([Chapter 8](08-release.md)). |

## `src/Platform`

| Project | Role |
|---|---|
| `Vyxum.Platform.Abstractions` | What modules may use. Interfaces (`IModule`, `ICapabilityRegistry`, `IIntegrationEventHandler<T>`, `ILiveWeightSource`, `IDeviceCommands`), `ModuleDbContext`, outbox/inbox entities, `UiStringSource`. Keep it small: everything added here becomes a contract for all modules. |
| `Vyxum.Platform` | The runtime. Folders: `Modules/` (discovery, validation, catalog), `Events/` (outbox dispatcher), `Data/` (database provider, initializer, backups, platform tables), `Devices/` (agent hub, live weight store, device settings, discovery jobs), `Setup/` (setup wizard API, restart), `Configuration/` (`vyxum.json` reader/writer), `Ui/` (`/api/ui/strings`, `/api/ui/modules`). |
| `Vyxum.Platform.Testing` | Test helpers: `TestDatabase`, `TestModuleHost`, `TestClock`. |

## `src/Host/Vyxum.Host`

`Program.cs` is short and worth reading in full: it loads `vyxum.json`, registers the platform with the modules from `HostModules`, adds the agent hub, request localization (Turkish default, English), static files, the `/health` endpoint, and maps the API groups. `HostModules.cs` lists the built-in modules. `Resources/Strings*.resx` hold the shell texts. The `.csproj` contains the MSBuild targets that build the web UI into `wwwroot/`.

## `src/Modules`

`Products/Vyxum.Modules.Products` and `Weighing/Vyxum.Modules.Weighing`. Every module has the same shape:

```
<Name>Module.cs          IModule: manifest + service registration + endpoint mapping
<Name>DbContext.cs       the module's only DbContext (derives from ModuleDbContext)
<Name>DesignTimeFactory  one line, lets `dotnet ef` create the context
<Name>Endpoints.cs       HTTP endpoints
<Name>Service.cs         business logic
<Name>Strings.cs         ResourceManager for the texts
<Event>.cs               integration events this module publishes or copies
Resources/Strings.resx, Strings.tr.resx
Migrations/Sqlite, Migrations/SqlServer
```

## `src/Devices` (no Vyxum assembly except `Vyxum.Devices` itself)

| Project | Role |
|---|---|
| `Vyxum.Devices` | Contracts `ITransport`, `IScaleProtocol`, `ISupportsZero`; `FrameReader` (cuts a byte stream into frames); `DeviceRunner` (connect, read, reconnect with back-off); `WeightReading`, `DeviceStatus`. |
| `Vyxum.Devices.Transports` | `SerialTransport`, `TcpTransport`. |
| `Vyxum.Devices.MtSics` | METTLER TOLEDO MT-SICS protocol. |
| `Vyxum.Devices.ToledoContinuous` | Toledo continuous output (listen-only). |
| `Vyxum.Devices.GenericAscii` | Regular-expression based protocol for other ASCII output. |
| `Vyxum.Devices.Simulator` | Fake scales over memory or TCP, and replay of recorded lines (`Recordings/`). |
| `Vyxum.Devices.Discovery` | Read-only search for scales (`PortScanner`, `WriteGuardTransport`). |

## `src/Agent/Vyxum.Agent`

`Program.cs` / `AgentHost.cs` start it; `HubWorker.cs` keeps the SignalR connection to the host; `DeviceFleet.cs` starts, stops and restarts device runners when settings change; `Configuration/` parses and validates settings (this is also where protocol and transport **type names** are registered: `AgentConfiguration`); `Contract/AgentHubContract.cs` is the agent-side copy of the wire contract; `Discovery/` runs the scale search.

## `src/Desktop/Vyxum.Desktop`

WinForms + WebView2 window, `net10.0-windows`. Logic that can be tested on any OS is in `Logic/`.

## `src/Web/vyxum-web`

React + TypeScript + Vite ([Chapter 7](07-web-ui.md)). Output goes to `src/Host/Vyxum.Host/wwwroot` (not committed).

## `tests`

| Project | Tests |
|---|---|
| `Vyxum.Platform.Tests` | Platform unit tests (modules, events, data, setup, UI strings). |
| `Vyxum.Modules.Tests` | Products and Weighing through the HTTP API, plus `MigrationTests`. |
| `Vyxum.IntegrationTests` | Whole host end to end: event flow, agent hub, device settings, discovery, setup API, static frontend. Runs on both databases. |
| `Vyxum.Devices.Tests` | Protocol parsers (with recorded raw lines), transports, simulator, discovery. |
| `Vyxum.Desktop.Tests` | Address and health-check logic of the desktop window. |
| `Vyxum.ArchitectureTests` | Dependency rules (NetArchTest) and resource-file rules (every `Strings.resx` has a `Strings.tr.resx` with the same keys and no empty values). |
| `SampleModules/` | Two tiny modules (Orders, Notifications) used **only by tests** to prove modules can talk by events. Never shipped. |
