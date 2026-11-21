# 2. Getting started

[Index](../README.md) · Previous: [1. Big picture](01-big-picture.md) · Next: [3. Repository map](03-repository-map.md)

Goal: from an empty computer to a running, tested Vyxum.

## What to install

| Tool | Version | Why |
|---|---|---|
| .NET SDK | 10 (`global.json` pins `10.0.100` and accepts newer feature bands) | Builds and runs everything on the .NET side. |
| Node.js | 22 | Builds the web UI. Only developers need it; customers do not. |
| Git | any | |
| Docker | optional | Only to run SQL Server tests locally. |
| Inno Setup 6 | only on Windows, only to build the installer | See [Chapter 8](08-release.md). |

Check: `dotnet --version` prints `10.x`, `node --version` prints `v22.x`.

Linux and macOS are fine for everything except the installer and running the Desktop window (Windows only). The Desktop project still *builds* on Linux.

## Get the code and build

```bash
git clone https://github.com/hasanfurkanfidan/vyxum.git
cd vyxum
dotnet build -warnaserror
```

`-warnaserror` is required: `Directory.Build.props` already turns warnings into errors (`TreatWarningsAsErrors`), and the build must be warning-free. Package versions live only in `Directory.Packages.props` (central package management); never put a version number inside a `.csproj`.

The first `dotnet build` also builds the web UI (it runs `npm ci` and `npm run build`) if the folder `src/Host/Vyxum.Host/wwwroot` does not exist. That needs Node.js. To skip it:

```bash
dotnet build -warnaserror -p:SkipUiBuild=true
```

CI uses this flag for the .NET job and builds the UI in a separate job.

## Run the tests

```bash
dotnet test
```

The tests use **xUnit v3** with **Shouldly** for assertions (FluentAssertions is not allowed). `global.json` selects the `Microsoft.Testing.Platform` runner, which xUnit v3 uses.

Every data-access test runs on **SQLite** and on **SQL Server**. SQLite needs nothing. SQL Server cases are *skipped with a reason* unless the environment variable `VYXUM_TEST_SQLSERVER` holds a connection string **without a database name**. Each test creates its own uniquely named database and drops it afterwards. To get a local SQL Server:

```bash
docker run -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Vyxum_Test_123!' \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

export VYXUM_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=Vyxum_Test_123!;TrustServerCertificate=True'
dotnet test
```

(PowerShell: `$env:VYXUM_TEST_SQLSERVER = '...'`.) That password is for local tests only. CI sets the same variable with a SQL Server 2022 service container (`.github/workflows/ci.yml`).

The test helpers live in `src/Platform/Vyxum.Platform.Testing`: `TestDatabase` (one database per test, either provider), `TestModuleHost` (an in-memory module host) and `TestClock`.

Web UI tests are separate: see [Chapter 7](07-web-ui.md).

## Run the web host

```bash
dotnet run --project src/Host/Vyxum.Host --urls http://localhost:5000
```

Use port 5000: the UI development server and the agent defaults expect it. No configuration is needed. On the first start the host creates the SQLite file `vyxum.db` in the data directory (`src/Host/Vyxum.Host/data/` when started like this) and creates all tables. Check it:

```bash
curl http://localhost:5000/health
# {"status":"ok","modules":[{"id":"products",...},{"id":"weighing",...}],"database":"Sqlite"}
```

Open http://localhost:5000 in a browser. The first time, the **setup wizard** appears.

## Run an Agent with a simulated scale

You do not need a real scale. In a second terminal:

```bash
dotnet run --project src/Agent/Vyxum.Agent
```

`dotnet run` starts the agent in the *Development* environment, whose `appsettings.Development.json` adds a **simulated** MT-SICS scale showing 572 g. Open http://localhost:5000/api/devices to see it, and the weighing screen to see the live weight.

With a real scale, see [Chapter 6](06-devices-and-agent.md).

## Work on the web UI with live reload

```bash
cd src/Web/vyxum-web
npm ci          # once
npm run dev     # Vite dev server, usually http://localhost:5173
```

Vite forwards `/api` and `/hubs` to `http://localhost:5000`, so keep the host running.

## Settings in one place

Settings are read in this order; **later wins**: `appsettings.json` next to the program, `vyxum.json` in the data directory, environment variables (`Vyxum__Database__Provider`), command line (`--Vyxum:Database:Provider SqlServer`). The full key table is in the [README](../../../README.md#configuration). To try SQL Server by hand:

```bash
dotnet run --project src/Host/Vyxum.Host -- \
  --Vyxum:Database:Provider SqlServer \
  --Vyxum:Database:ConnectionString "Server=localhost,1433;Database=Vyxum;User Id=sa;Password=Vyxum_Test_123!;TrustServerCertificate=True"
```

## Before every commit

1. `dotnet build -warnaserror` passes with no warning.
2. `dotnet test` passes (with `VYXUM_TEST_SQLSERVER` set if you touched data access).
3. If you touched the UI: `npm run typecheck`, `npm test`, `npm run build`.
4. One commit = one working step with its tests. Commit messages are English, imperative, short ("Add product origin field").

More rules are in [CONTRIBUTING.md](../../../CONTRIBUTING.md) and [`CLAUDE.md`](../../../CLAUDE.md).
