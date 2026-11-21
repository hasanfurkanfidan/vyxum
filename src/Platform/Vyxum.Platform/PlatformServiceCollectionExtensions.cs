using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Registers the Vyxum platform and its modules.</summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>Discovers modules in <paramref name="moduleAssemblies"/> and registers the platform with them.</summary>
    /// <exception cref="ModuleValidationException">The modules do not form a valid application.</exception>
    public static ModuleCatalog AddVyxumPlatform(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> moduleAssemblies) =>
        services.AddVyxumPlatform(configuration, ModuleDiscovery.Discover(moduleAssemblies));

    /// <summary>Registers the platform with the given module instances.</summary>
    /// <exception cref="ModuleValidationException">The modules do not form a valid application.</exception>
    public static ModuleCatalog AddVyxumPlatform(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        var descriptors = modules
            .Select(m => new ModuleDescriptor(m))
            .OrderBy(d => d.Manifest.Id, StringComparer.Ordinal)
            .ToList();
        var databaseSettings = DatabaseSettings.FromConfiguration(configuration);
        var capabilities = new CapabilityRegistry(descriptors.Select(d => d.Manifest));

        // Modules only register services here; nothing is resolved, so validating afterwards
        // lets one exception report every problem, including those found during registration.
        foreach (var descriptor in descriptors)
        {
            descriptor.Module.ConfigureServices(
                services,
                new ModuleRegistrationContext(descriptor, services, configuration, capabilities));
        }

        ModuleValidator.Validate(descriptors);

        var catalog = new ModuleCatalog(descriptors);
        services.AddSingleton(catalog);
        services.AddSingleton<ICapabilityRegistry>(capabilities);
        services.AddSingleton(databaseSettings);
        services.AddDbContext<PlatformDbContext>((provider, options) =>
            provider.GetRequiredService<DatabaseSettings>().Configure(options));
        var backupsDirectory = Path.Combine(DatabaseSettings.GetDataDirectory(configuration), "backups");
        services.AddSingleton(provider => new DatabaseBackups(
            provider.GetRequiredService<DatabaseSettings>(),
            backupsDirectory,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DatabaseBackups>>()));
        services.AddSingleton<IDatabaseBackups>(provider => provider.GetRequiredService<DatabaseBackups>());
        services.AddSingleton<IModuleDatabaseInitializer, ModuleDatabaseInitializer>();
        services.AddHostedService<ModuleDatabaseInitializerService>();

        services.TryAddSingleton(TimeProvider.System);
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxDispatcherService>();

        // Always available, so modules can depend on it; it stays empty until an agent connects (AddVyxumAgentHub).
        services.AddSingleton<LiveWeightStore>();
        services.AddSingleton<ILiveWeightSource>(provider => provider.GetRequiredService<LiveWeightStore>());

        // Merges the UiStringSource registrations of the host and the modules (GET /api/ui/strings).
        services.AddSingleton<UiStringCatalog>();
        return catalog;
    }
}
