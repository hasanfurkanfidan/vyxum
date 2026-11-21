# CLAUDE.md

Guidance for AI coding sessions in this repository (the Apache-2.0 open core of Vyxum). Read it at the start of every session.

## Product

Vyxum is modular weighing software for scales connected through a serial port or the network. It is installed on the customer's own PC or server. This repository contains the core only. Additional modules are developed separately and are not added here.

## Tech stack

| Area | Choice |
|---|---|
| Backend | .NET 10, ASP.NET Core, modular monolith |
| Data access | EF Core. SQLite (default) and SQL Server. PostgreSQL later. |
| Frontend | React + TypeScript + Vite is planned for phase F2. Built files are served by ASP.NET Core. Not in this repo yet. |
| Tests | xUnit v3, Shouldly, NetArchTest.Rules. FluentAssertions is not allowed. |
| Central packages | Versions live in `Directory.Packages.props`. Do not put versions in project files. |

Build settings in `Directory.Build.props`: `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `AnalysisLevel latest`. Warnings are errors.

## Architecture rules (invariants)

- Modules never call each other. They communicate only through integration events, delivered via the outbox and inbox.
- Modules reference only `Vyxum.Platform.Abstractions` among Vyxum assemblies.
- The platform (`Vyxum.Platform`, `Vyxum.Platform.Abstractions`) never references a module assembly and knows no specific module.
- No module is mandatory. A missing optional capability must not break a module; it checks `ICapabilityRegistry`.
- The platform knows no scale brand. It knows only the "weight read" event.
- Code is database-agnostic. Do not write provider-specific SQL. Use only LINQ and EF Core APIs that work on both SQLite and SQL Server. Store UTC `DateTime` (or ticks) for values that are queried or ordered.
- Business rules are not hard-coded. They come in as parameters or configuration.
- Each module owns exactly one `ModuleDbContext` and prefixes its table names. SQLite has no schemas.
- If a change would break one of these rules, stop and ask.

## Product rules

- The product works offline. Do not add CDN links, remote fonts, or calls to external services.
- Installation must be simple and possible without help. Do not add a database server, an extra program, or a hand-edited configuration file as a requirement without asking first.
- Do not commit signing private keys, or any secret, to this repository, to tests, or to examples.
- Before adding a NuGet or npm package, check its license. Only MIT, BSD, and Apache-2.0 compatible packages are allowed.

## Language

- Code, class, table, and variable names are in English.
- Every user-facing string lives in a resource file in both languages: `Strings.resx` (English) and `Strings.tr.resx` (Turkish). Never hard-code UI text. A screen with text in only one language is not finished.
- Documentation for users is written in both languages.
- Log and exception messages are in English.

## Testing

- Every data access change is tested on SQLite and SQL Server. Use `TestDatabase` from `Vyxum.Platform.Testing`.
- SQL Server tests read the connection string from `VYXUM_TEST_SQLSERVER`. Without it they are skipped with a reason. CI always sets it.
- Run `dotnet build -warnaserror` and `dotnet test` before committing. The build must be warning-free.
- Architecture rules are enforced in `tests/Vyxum.ArchitectureTests`. Add a test when you add a new rule.

## Working style

- Work in small steps. Each step does one thing and finishes with its tests.
- Do not start the next step until the tests pass.
- One commit per working step. Commit messages are in English, imperative, and short in the first line.
- Do not push unless asked.
- If a requirement is unclear or conflicts with these rules, do not guess. Stop and ask.
- Do not put AI model names or tool names in code, comments, or documentation.
