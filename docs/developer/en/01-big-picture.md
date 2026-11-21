# 1. The big picture

[Developer guide index](../README.md) · Next: [2. Getting started](02-getting-started.md)

This chapter explains what Vyxum is made of and why. Read it first. You do not need to know the code yet.

## What Vyxum does

Vyxum is weighing software. A shop or factory has a **scale** (a weighing device) connected to a PC by a cable (serial port) or a network. An operator picks a product, puts goods on the scale, and presses a button. Vyxum records the weight, calculates the amount to pay, and keeps a record that can be listed, cancelled and exported to Excel.

Vyxum is installed on the **customer's own computer**. It has no cloud part and must work **without internet**. That one fact explains many choices below: fonts are bundled, there is no external service, and the default database is a file.

## Three programs

When Vyxum runs, three programs work together:

| Program | Project | What it is |
|---|---|---|
| **Web host** | `src/Host/Vyxum.Host` | An ASP.NET Core application. It serves the web screens, the HTTP API and the database. Runs as the Windows service "Vyxum Web". |
| **Agent** | `src/Agent/Vyxum.Agent` | A small program that sits next to the scale, reads the weight and sends it to the host. Runs as the Windows service "Vyxum Agent". |
| **Desktop window** | `src/Desktop/Vyxum.Desktop` | A window (`Vyxum.exe`) that shows the web screens without browser chrome. It is only a frame around the web application. |

```
 scale ──cable/network──> Agent ──SignalR──> Web host ──HTTP──> Browser / Desktop window
                                                │
                                                └──> Database (SQLite file or SQL Server)
```

Why is the Agent separate from the host? A scale is connected to *one specific PC*, but the host may run on a server in another room. In the simplest install both are on the same PC. Keeping the Agent separate lets one host serve scales on several PCs, and keeps hardware code away from the business code.

## Modular monolith

The host is **one program**, but its inside is split into **modules**. A module is a feature area, for example Products (the product catalog) or Weighing (the weighing records). In this repository there are two modules. More modules are developed in a separate repository and loaded the same way.

Why modules? Some customers pay only for some features. A module must therefore work when another module is missing, and must be addable without changing the rest. To make that true, the code follows strict rules (the **invariants**, listed in [`CLAUDE.md`](../../../CLAUDE.md) and enforced by tests):

1. **Modules never call each other.** Weighing does not call Products. They send *integration events* to each other instead (see [Chapter 4](04-modules.md)).
2. A module references only one Vyxum assembly: `Vyxum.Platform.Abstractions`.
3. The **platform** (`Vyxum.Platform`) never references a module and knows no module by name.
4. No module is mandatory. If a module wants a feature that another module provides, it asks `ICapabilityRegistry` whether it exists and copes if it does not.
5. The platform knows no scale brand. It only knows "a weight was read".
6. Code is database-agnostic: no SQL written by hand, only LINQ and EF Core features that work on both SQLite and SQL Server.

If a change would break one of these, stop and discuss it. The architecture tests in `tests/Vyxum.ArchitectureTests` will fail anyway.

## Layers, from bottom to top

| Layer | Projects | Job |
|---|---|---|
| Contracts | `Vyxum.Platform.Abstractions` | The small set of interfaces and base classes modules are allowed to use (`IModule`, `ModuleDbContext`, `IIntegrationEvent`, `ILiveWeightSource`, ...). |
| Platform | `Vyxum.Platform` | Finds and checks modules, creates and upgrades the database, delivers events, talks to Agents, setup wizard, UI strings. |
| Modules | `Vyxum.Modules.Products`, `Vyxum.Modules.Weighing` | The features. |
| Host | `Vyxum.Host` | Glues everything: starts the platform, lists the modules, serves the web UI. |
| Devices | `Vyxum.Devices*` | Reading scales. Completely independent from the platform. Used only by the Agent. |
| Agent | `Vyxum.Agent` | Runs devices, reports to the host. |
| Web UI | `src/Web/vyxum-web` | React screens. |

The platform and the device world never see each other directly. The only bridge is the **agent hub** (a SignalR connection), plus the small interface `ILiveWeightSource` that modules use to read the current weight.

## Glossary

- **Scale / device**: the weighing hardware.
- **Transport**: how bytes travel to the device (serial port, TCP, or the simulator).
- **Protocol**: how to understand those bytes (MT-SICS, Toledo Continuous, generic ASCII).
- **Module**: a feature area with its own tables, endpoints and texts.
- **Capability**: a named feature a module provides, such as `products.catalog`.
- **Integration event**: a message one module publishes and others may handle.
- **Outbox / inbox**: database tables that make event delivery safe even if the program crashes ([Chapter 4](04-modules.md)).
- **Migration**: a generated, versioned database change ([Chapter 5](05-database.md)).
- **Data directory**: where Vyxum keeps its data: `%ProgramData%\Vyxum` on Windows, `data/` under the working directory elsewhere.
- **`vyxum.json`**: the settings file in the data directory, shared by host, agent and desktop window.

## What is in this repository and what is not

This repository is the **open core** (Apache-2.0). Additional modules are developed elsewhere and are never added here. Business information (prices, sales plans) and secrets such as signing keys must never be committed here.
