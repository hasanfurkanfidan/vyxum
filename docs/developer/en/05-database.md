# 5. Database and migrations

[Index](../README.md) · Previous: [4. Modules and events](04-modules.md) · Next: [6. Devices and the Agent](06-devices-and-agent.md)

## One database, many owners

Vyxum uses **EF Core** with two providers: **SQLite** (the default, one file, no installation) and **SQL Server**. PostgreSQL is planned. The provider is chosen with `Vyxum:Database:Provider` (`Sqlite` or `SqlServer`); SQL Server also needs `Vyxum:Database:ConnectionString`. By default SQLite uses `vyxum.db` in the data directory.

All modules and the platform share **one database**. To avoid mixing, each owner has its own context:

- The platform has `PlatformDbContext` (installed modules, device settings, platform settings). Its history table is `__plt_migrations`.
- Each module has exactly one `ModuleDbContext` and a **table prefix** (`prd_` for Products, `wgh_` for Weighing). SQLite has no schemas, so prefixes are used on both providers. History table: `__<prefix>_migrations`.

## Rules for writing data access

- Write **no provider-specific SQL**. Use LINQ and EF Core APIs that work on both providers. (SQL Server and SQLite differ, for example, in how they sort and compare `DateTimeOffset` and `decimal`; code that works on one can fail on the other. That is why the tests run on both.)
- Store `DateTime` as **UTC**. `ModuleDbContext` already converts. Store values you filter or order by as UTC `DateTime` (or ticks).
- Every data-access change gets tests on both providers, using `TestDatabase`.
- SQLite runs with `synchronous=FULL` (`SqliteDurabilityInterceptor`): a power cut must not lose a record that the screen said was saved.

## What happens at start

`ModuleDatabaseInitializer` runs when the host starts:

1. Checks that no two modules use the same table prefix.
2. Works out, for the platform and each module, which migrations are pending.
3. If the database already has data and something is pending, writes a **backup** first (SQLite only): a copy named `vyxum-<date>-<time>-migration.db` in `backups/` under the data directory; the newest 10 are kept. SQL Server is **not** backed up by Vyxum.
4. Applies the platform's migrations, then each module's, and records each module and version in the platform table of installed modules.

Two special cases: a database made before migrations existed has the first migration's tables but no history rows; the initializer marks the first migration as applied (the *baseline*) and runs only later ones. And a module that ships **no** migrations for the active provider (for example a test module) just gets its tables created once and cannot be upgraded.

## Add a migration

Migrations are **generated**, never hand-written. They live in two folders per project, one per provider: `Migrations/Sqlite` and `Migrations/SqlServer`.

1. Change the model (entity or `ConfigureModel`).
2. Restore the tool once: `dotnet tool restore`.
3. From the repository root, create the migration **for both providers with the same name** (example: Products):

```bash
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/Sqlite -- --provider Sqlite
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/SqlServer -- --provider SqlServer
```

Use `src/Modules/Weighing/Vyxum.Modules.Weighing` for Weighing, or `src/Platform/Vyxum.Platform` for the platform. To undo the last one, run `dotnet ef migrations remove` with the same `--project` and `-- --provider`.

4. Read the generated code. Make sure it does what you expect, particularly for a column rename (EF may generate drop+add, which loses data; fix by hand-editing the migration to use `RenameColumn`).
5. Run the tests. `Every_model_change_has_a_migration` (in `tests/Vyxum.Modules.Tests/MigrationTests.cs` and the platform tests) fails if the model and the migrations disagree on either provider.
6. A migration that drops or rewrites data needs a test with rows already in the database.

**First migration of a new context:** EF names both model snapshots `<Context>ModelSnapshot`. After creating the first SQLite migration rename that class and its file to `<Context>SqliteModelSnapshot`; after the first SQL Server migration rename to `<Context>SqlServerModelSnapshot`. Later migrations need nothing extra. (This is how `ProviderMigrations` tells the two sets apart; see its comments.)

**Never edit or delete a migration that has been released.** Customers' databases already contain it. Add a new migration instead.

## How `dotnet ef` finds the provider

`DesignTimeDbContextFactory<TContext>` (in Abstractions) reads the `--provider` argument that you pass after the `--`. That is why each module only needs the one-line factory class.

## Backups and upgrades in practice

- The installer also copies the SQLite database before an upgrade ([Chapter 8](08-release.md)).
- For SQL Server, tell the customer to back up before upgrading.
- To restore on SQLite: stop the "Vyxum Web" service, copy a backup over `vyxum.db`, start the service.
