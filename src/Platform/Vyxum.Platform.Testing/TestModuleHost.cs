using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform.Testing;

/// <summary>
/// An in-memory host for modules: registers the platform and the given modules against a
/// <see cref="TestDatabase"/> and creates their tables. Background services are not started;
/// tests drive the platform explicitly. Disposing does not delete the database.
/// </summary>
public sealed class TestModuleHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TestModuleHost(ServiceProvider services)
    {
        _services = services;
    }

    /// <summary>The root service provider.</summary>
    public IServiceProvider Services => _services;

    /// <summary>Builds the host and runs the module database initializer.</summary>
    public static async Task<TestModuleHost> StartAsync(TestDatabase database, params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(database);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(database.Settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddVyxumPlatform(configuration, modules);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var host = new TestModuleHost(provider);
        try
        {
            await provider.GetRequiredService<IModuleDatabaseInitializer>().InitializeAsync();
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }

        return host;
    }

    /// <summary>Creates a service scope, e.g. to resolve a module's DbContext.</summary>
    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
