# 10. Decisions and their reasons

[Index](../README.md) · Previous: [9. Troubleshooting](09-troubleshooting.md)

Code comments refer to numbered decisions such as "decision V-29". This chapter collects the ones that can be read from the repository, each with its reason. The original decision register is not part of this repository; when you make a new decision that future developers need to know, **add it here** (keep the number if it has one, or use a short name).

## Product and architecture

| Decision | Why |
|---|---|
| **Runs on the customer's own PC, offline** (V-17) | Many customers have poor or no internet and want their data on their own machine. So: no CDN, no remote fonts, no external calls; installation must be possible without help. |
| **Modular monolith** | One program is simple to install and operate, while modules keep features separable and sellable separately. Microservices would make installation hard. |
| **Modules talk only through events (outbox + inbox)** | Keeps modules removable. The outbox makes publishing atomic with the business change; the inbox makes handling happen once. |
| **No module is mandatory; capabilities** | A customer may own only some modules. Features that need another module check `ICapabilityRegistry`. |
| **The platform knows no scale brand** | New brands must be addable without touching the platform; the platform only knows "a weight was read". |
| **Agent is a separate program and cannot reference the platform** | The scale may be on another PC than the host; hardware code stays isolated, so a hardware problem cannot take down the web host. |
| **Code is database-agnostic; SQLite default, SQL Server supported** | SQLite needs no installation (simple setup); SQL Server serves bigger customers. Two providers are kept honest by running every data test on both. |
| **Business rules are settings, not code** | Customers differ (rounding, currency, whether a stable weight is required). Defaults live in options classes. |
| **UTC for stored times** | Values that are queried or ordered must compare the same on both providers. |
| **Warnings are errors; central package versions** | Keeps quality steady and upgrades in one place. |
| **Only MIT/BSD/Apache-2.0-compatible dependencies** | The core is Apache-2.0 and shipped to customers. |

## Weighing and devices

| Decision | Why |
|---|---|
| **V-03: no login screen in the free core** | The free edition is a single-station tool; the API is therefore open to every caller that can reach it. Keep the host on a trusted network. A users module is planned for a later phase. |
| **V-26: agent gate** | Without a key only the same computer may connect an agent; with a key, agents anywhere can. This protects the hub by default without any configuration. |
| **V-27: live weights are not integration events** | They change many times a second and need no history. They stay in memory behind `ILiveWeightSource`. |
| **V-28: drivers only read; zero only on explicit request** | A scale may be legally controlled; a wrong command could alter it. Dangerous commands are blocked in code and tested. |
| **V-29: tare is kept in software, zero is sent to the device** | Software tare works the same on every protocol (including listen-only ones). Known limitation: if the operator also presses the indicator's own TARE key, tare is subtracted twice. |
| **V-32: product data reaches Weighing by event** | Weighing keeps its own copy (`wgh_Products`) and writes the product code, name and price into each record, so old records never change when a product changes. |
| **V-33: UI texts come from the module's resource files** | Each module owns its texts in both languages; the UI contains only keys. |
| **V-34: fonts are bundled** | Offline use. Packages are MIT; the font files are SIL OFL 1.1, which allows bundling. |
| **V-35: product target weight and tolerances are stored now** | Prepares reports (in a separately developed module) without changing the core later. |
| **Amount = net (in kg) × unit price, rounded by settings** | Rounding mode and decimals come from `Vyxum:Weighing`; currency is stored with each record. |
| **Request id on recording** | A retry after a lost response must not create a second record. |
| **V-39: device settings live in the web database; the agent fetches them** | One place to configure; changes apply without restarting the agent. The agent's own file stays as a development override. |
| **V-40: discovery sends only `SI` and `I4`** | Finding a scale must be as safe as reading one. No network sweep, one address only. |

## Installation

| Decision | Why |
|---|---|
| **V-36: Inno Setup** | Mature, free, single `.exe`, scripting for services and backups. Needed only on the build machine. |
| **V-37: shared `vyxum.json` in the data directory** | One settings file for host, agent and desktop that survives updates (unlike `appsettings.json` in the program folder). The installer writes it; the setup wizard may change the database part. |
| **V-38: setup wizard in the web UI** | The customer finishes configuration without editing files. |
| **Self-contained publish** | The customer's PC does not need .NET installed. |
| **Backup before upgrade (installer and host)** | A failed migration must never mean lost data. |

## Open questions (no decision yet)

- How a separately developed module delivers its own screens into the React app.
- How modules outside this repository are discovered beyond the `Vyxum:Modules` list (a plugin folder is planned for the licensing phase).
- Code signing for the installer.
- Cleanup of delivered outbox messages; coordination of several dispatchers.
- Modules that ship no migrations cannot be upgraded.
