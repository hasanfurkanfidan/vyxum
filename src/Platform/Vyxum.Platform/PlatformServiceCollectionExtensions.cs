using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Vyxum.Licensing;
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
        // While registering, modules see what is installed; at run time the registry follows the license.
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
        services.AddSingleton(new ModuleUiFiles(catalog));
        services.AddSingleton(provider =>
        {
            ILicenseState? licenses = null;
            return new CapabilityRegistry(
                descriptors.Select(d => d.Manifest),
                () => licenses ??= provider.GetRequiredService<ILicenseState>());
        });
        services.AddSingleton<ICapabilityRegistry>(provider => provider.GetRequiredService<CapabilityRegistry>());
        // Identity: a users module registers its own implementations in ConfigureServices above; without one,
        // nobody signs in and everything is allowed (decision V-03). The permission catalog is always the platform's.
        services.RemoveAll<IPermissionCatalog>();
        services.AddSingleton<IPermissionCatalog, PermissionCatalog>();
        services.AddSingleton<IReportCatalog, ReportCatalog>();
        services.TryAddScoped<ICurrentActor, NoUsersCurrentActor>();
        services.TryAddScoped<IAuthorization, NoUsersAuthorization>();
        services.TryAddScoped<ISupervisorApproval, NoUsersSupervisorApproval>();
        services.TryAddScoped<IElectronicSignature, NoUsersElectronicSignature>();
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

        // Licensing (phase F4). The trusted keys are compiled in and cannot be changed from configuration or by a
        // module: any ILicenseKeyRing a module registered above is removed. Tests replace it after the platform
        // (WebApplicationFactory.ConfigureTestServices, or a registration after AddVyxumPlatform).
        services.RemoveAll<ILicenseKeyRing>();
        services.AddSingleton<ILicenseKeyRing>(LicenseKeyRing.Trusted);
        services.AddSingleton<LicenseManager>();
        services.AddSingleton<ILicenseState>(provider => provider.GetRequiredService<LicenseManager>());
        services.AddHostedService<LicenseMaintenanceService>();
        services.AddSingleton<LicensedHubs>();
        services.AddSingleton<LicenseHubFilter>();
        services.Configure<HubOptions>(options => options.AddFilter<LicenseHubFilter>());
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxDispatcherService>();

        // Always available, so modules can depend on it; it stays empty until an agent connects (AddVyxumAgentHub).
        services.AddSingleton<LiveWeightStore>();
        services.AddSingleton<ILiveWeightSource>(provider => provider.GetRequiredService<LiveWeightStore>());

        // Merges the UiStringSource registrations of the host and the modules (GET /api/ui/strings).
        services.AddSingleton<UiStringCatalog>();
        services.AddSingleton<ModuleDisplayNames>();
        services.AddHostedService(provider => provider.GetRequiredService<ModuleDisplayNames>());

        // Setup wizard (GET/POST /api/setup/*): shared settings file, database settings, restart.
        var dataDirectory = DatabaseSettings.GetDataDirectory(configuration);
        services.AddUiStrings(SetupStrings.KeyPrefix, SetupStrings.ResourceManager);
        services.AddUiStrings(LicenseStrings.KeyPrefix, LicenseStrings.ResourceManager);

        // Glossary (GET /api/glossary): free, offline, part of the platform.
        services.AddUiStrings(GlossaryStrings.KeyPrefix, GlossaryStrings.ResourceManager);
        services.AddSingleton(new GlossaryCatalog(GlossaryStrings.Terms, [CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("tr")]));
        services.AddScoped<IPlatformSettings, PlatformSettings>();
        services.AddSingleton(new VyxumSettingsFile(Path.Combine(dataDirectory, VyxumSettingsFile.FileName)));
        services.AddSingleton(provider => new SetupState(
            dataDirectory,
            SetupState.ReadVersion(provider.GetService<IHostEnvironment>()?.ApplicationName)));
        services.AddOptions<HostRestartOptions>();
        // A factory, so that hosts without IHostApplicationLifetime (test hosts) still validate.
        services.TryAddSingleton<IHostRestarter>(provider => new ServiceRecoveryRestarter(
            provider.GetRequiredService<IHostApplicationLifetime>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HostRestartOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceRecoveryRestarter>>()));
        return catalog;
    }
}
