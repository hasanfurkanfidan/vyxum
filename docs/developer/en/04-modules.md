# 4. Modules and events

[Index](../README.md) · Previous: [3. Repository map](03-repository-map.md) · Next: [5. Database and migrations](05-database.md)

## How a module is loaded

1. The host calls `AddVyxumPlatform(configuration, HostModules.Load(configuration))`.
2. `HostModules.BuiltIn` (`src/Host/Vyxum.Host/HostModules.cs`) lists the assemblies that are always loaded. Extra assemblies can be added by name through the configuration list `Vyxum:Modules` (for example `Vyxum:Modules:0`). That is how modules from outside this repository are loaded.
3. `ModuleDiscovery` finds every public class implementing `IModule` in those assemblies and creates it (it needs a public parameterless constructor).
4. Each module's `ConfigureServices` runs. It only *registers* things; nothing is resolved yet.
5. `ModuleValidator` checks **all** modules together and, if anything is wrong, throws one `ModuleValidationException` listing every problem. The host then refuses to start. It checks: module ids and capability ids are lowercase dot-separated kebab-case; ids are unique; every capability has exactly one provider; every `Requires` capability exists; no dependency cycles; a module with event handlers has a DbContext; an event name+version is published by one module only; every event type has `[IntegrationEvent]`; a DbContext belongs to one module.
6. On start, a hosted service creates or upgrades the database ([Chapter 5](05-database.md)), and another delivers events.
7. `MapVyxumModules` calls each module's `MapEndpoints`.

## The manifest

`IModule.Manifest` declares the module:

```csharp
Manifest = new()
{
    Id = "weighing",                              // lowercase kebab, dots allowed
    Version = new Version(1, 0),
    Provides = ["weighing.records"],              // capabilities offered (one provider each)
    Requires = [],                                // must exist or startup fails
    Optional = ["products.catalog", "labels.print"], // used if present
    Publishes = [typeof(WeighingRecorded)],       // events it publishes
}
```

**Requires vs Optional.** Rule 4 says no module is mandatory. Prefer `Optional` and ask `ICapabilityRegistry.IsAvailable("labels.print")` at run time; show or hide the feature. Weighing works without Products and without a label module this way. The UI also learns what is loaded from `GET /api/ui/modules`, so menu entries appear only for loaded modules.

## Events instead of calls

Modules may not call each other, but Weighing needs product data. The answer: Products **publishes** `products.product-saved` whenever a product changes, carrying the full current state; Weighing **handles** it and keeps its **own copy** of the data in its own table (`wgh_Products`). Weighing also copies the product code, name and price into each record at weighing time, so later product changes never alter old records.

An event is a plain `record` with an attribute that gives it a stable wire name and version:

```csharp
[IntegrationEvent("products.product-saved", 1)]
public sealed record ProductSaved(Guid ProductId, string Code, /* ... */) : IIntegrationEvent;
```

Events travel as JSON (System.Text.Json, camelCase), so a consumer module defines **its own copy** of the record with the same name and version. It does not reference the publisher. Check `src/Modules/Weighing/.../ProductSaved.cs` next to `src/Modules/Products/.../ProductSaved.cs`. To change an event, add fields that are optional (the existing events were extended this way), or publish a new version.

### Outbox and inbox (why events do not get lost)

- **Publish:** `db.Publish(new ProductSaved(...))` adds a row to the module's `{prefix}_OutboxMessages` table. It is saved by the *same* `SaveChanges` as the business data, in one transaction. So either both happen or neither.
- **Deliver:** `OutboxDispatcher` (a background service) polls every module's outbox (every 0.5 s by default, `Vyxum:Outbox:PollingInterval`), and for each pending message runs the registered handlers.
- **Handle once:** each handler runs inside a transaction of its module's DbContext. The `{prefix}_InboxMessages` row `(MessageId, HandlerName)` is written in the same transaction. If the message arrives twice (for example after a crash), the inbox row makes the second run a no-op.
- **Fail and retry:** if a handler throws, its changes roll back, `Attempts`/`LastError` are recorded and the message is retried with exponential delay (1 s, doubling, up to 5 min; `Vyxum:Outbox:Retry*`). Only failed handlers run again.

Consequences you must remember: handlers must be idempotent-friendly and fast; delivery order is only best-effort by publish time; run **one host per database** (several dispatchers are not coordinated); delivered messages are not yet cleaned up.

## Add a new module (checklist)

Suppose you add `Labels`.

1. **Project.** Create `src/Modules/Labels/Vyxum.Modules.Labels/Vyxum.Modules.Labels.csproj`. Copy the Products `.csproj`: it references **only** `Vyxum.Platform.Abstractions`, and `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets=all`. Add it to `Vyxum.slnx`.
2. **Module class** `LabelsModule : IModule` with a manifest (id, version, `Provides`, `Publishes`). In `ConfigureServices` call `context.AddDbContext<LabelsDbContext>()`, register services, `context.AddEventHandler<TEvent, THandler>()` for events you consume, and `services.AddUiStrings("labels", LabelsStrings.ResourceManager)`.
3. **DbContext** `LabelsDbContext : ModuleDbContext` with `TablePrefix => "lbl"` (letters and digits, starts with a letter, unique) and `ConfigureModel`. Table names get the prefix automatically. Use `DateTime` in UTC.
4. **Design-time factory:** `internal sealed class LabelsDesignTimeFactory : DesignTimeDbContextFactory<LabelsDbContext>;`
5. **Texts:** `Resources/Strings.resx` (English, defines the keys) and `Resources/Strings.tr.resx` (Turkish, same keys, no empty values). A `LabelsStrings` class exposes `ResourceManager` as in Products. A test fails if the Turkish file is missing or different.
6. **Endpoints:** `MapEndpoints` with a group such as `/api/labels`. Return error codes/keys, not translated text; the UI translates.
7. **Migrations** for both providers ([Chapter 5](05-database.md)).
8. **Register it in the host:** a `ProjectReference` in `Vyxum.Host.csproj` and a line in `HostModules.BuiltIn`. (Modules built outside this repository are loaded through `Vyxum:Modules` instead.)
9. **Architecture tests:** add the module assembly to the list in `tests/Vyxum.ArchitectureTests/ArchitectureTests.cs` so the dependency rules cover it. Also the module test projects need to reference it to test.
10. **Tests:** an API test in the style of `tests/Vyxum.Modules.Tests/ProductsApiTests.cs` (use `TestDatabase` so it runs on SQLite and SQL Server), and a test that the module still works when its optional capabilities are missing.
11. **Docs:** describe the module's endpoints and settings in the README (English and Turkish).

If the UI needs screens, see [Chapter 7](07-web-ui.md).

## Settings of a module

A module reads its settings from the `Vyxum:<Name>` configuration section with the options pattern and **validates them on start** (`ValidateOnStart`), as `WeighingOptions` does. Business rules are never hard-coded: rounding, currency, "require stable weight" and so on are settings with defaults.
