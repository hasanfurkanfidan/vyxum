# 9. Troubleshooting

[Index](../README.md) · Previous: [8. Installer and releases](08-release.md) · Next: [10. Decisions](10-decisions.md)

Format: **symptom → likely cause → what to do.**

## Build

- **`dotnet: command not found` or SDK errors** → .NET 10 is missing or too old. Install it; `global.json` needs `10.0.100` or a newer feature band.
- **Build fails with warnings listed as errors** → expected: warnings are errors. Fix the warning; do not disable `TreatWarningsAsErrors`.
- **Build fails in the `BuildWebUi` step / "npm was not found"** → Node.js 22 is not installed. Install it, or build with `-p:SkipUiBuild=true`. The host then logs "The web UI is not built" and `/` returns 404 until you run `npm ci && npm run build` in `src/Web/vyxum-web`.
- **A package version error** → versions belong only in `Directory.Packages.props`.
- **`dotnet ef` fails** → run `dotnet tool restore` first; pass `-- --provider Sqlite|SqlServer`.

## Tests

- **SQL Server tests show as skipped** → `VYXUM_TEST_SQLSERVER` is not set. Start SQL Server (Docker command in [Chapter 2](02-getting-started.md)) and set it, without a database name.
- **SQL Server tests cannot connect** → the container needs about 20 s to start; check `docker ps`; the connection string needs `TrustServerCertificate=True`.
- **`Every_model_change_has_a_migration` fails** → you changed a model but did not add a migration for **both** providers ([Chapter 5](05-database.md)).
- **An architecture test fails** → you added a forbidden reference. Remove the `ProjectReference` instead of changing the test. If the rule really must change, discuss it first.
- **A Turkish resource test fails** → `Strings.tr.resx` is missing a key or has an empty value.
- **A test passes alone but fails with others** → look for shared state or a fixed port; tests use unique databases and should not share files.

## Running the host

- **Host does not start and prints a list of module problems** → `ModuleValidationException`: read each line (duplicate id, missing required capability, event without attribute, ...).
- **`Configuration 'Vyxum:Database:ConnectionString' is required`** → the provider is `SqlServer` but no connection string is set.
- **Port already in use** → pass `--urls http://localhost:5001` (and give the agent `--Vyxum:Agent:ServerUrl`).
- **Database file locked / cannot open** → another host instance uses the same data directory. Run one host per database.
- **Events seem stuck** → look at `<prefix>_OutboxMessages`: a row with `ProcessedAt` NULL, a growing `Attempts` and a `LastError` shows which handler fails; retries slow down up to 5 min.
- **Where are the data and logs?** → data: `%ProgramData%\Vyxum` (Windows) or `data/` under the working directory. As a Windows service, logs go to the Windows event log; under `dotnet run` they go to the console.
- **After the setup wizard changes the database the host exits** → intended; as a service it is restarted by the recovery settings; from a console start it again by hand.

## Agent and scales

- **`/api/devices` is empty** → no agent has connected. Check the agent's `ServerUrl`, that the host is running, and the agent key. Without `Vyxum:Agents:Key` on the host, only agents on the **same computer** are accepted (a remote agent gets 403); with a key set, the agent must send the same value as `Vyxum:Agent:Key` (wrong key: 401).
- **Device state `Error`** → its settings are invalid; the message names the setting.
- **Device `Disconnected` and retrying** → wrong port, port used by another program (for example another Vyxum Agent or a terminal program), wrong baud rate or parity, cable. The `lastError` in `/api/devices` tells more.
- **Discovery says `InUse`** → a running device already uses that port; the agent never opens it.
- **No weight, but connected** → protocol mismatch. Use Settings → Devices → Search, or check the raw text in the agent log. For MT-SICS use command `SI`.
- **Zero button disabled** → the device's protocol does not support zero (only MT-SICS does).
- **Agent settings changes have no effect** → devices from the web settings are ignored when the agent's own `Devices` list is not empty; empty it.

## Installer

- **Setup says the services could not be stopped** → restart the PC and run setup again.
- **A service was not created** → open Windows Services; start "Vyxum Web" and "Vyxum Agent" by hand; run `sc.exe query "Vyxum Web"`.
- **Build script: "Inno Setup 6 (ISCC.exe) was not found"** → install Inno Setup 6 or use `-SkipCompile`.
- **Desktop window shows "Starting Vyxum..." for a minute** → the host is not answering `GET /health` at the configured address (first entry of `Urls` in `vyxum.json`).

## Web UI

- **Texts show as keys like `products.Title`** → the key is missing in the resource files, or the host was not rebuilt after you added it.
- **UI shows old content after `npm run build`** → hard-refresh the browser; check that the build wrote to `src/Host/Vyxum.Host/wwwroot`.
- **`npm run dev` shows API errors** → the host is not running on port 5000.
