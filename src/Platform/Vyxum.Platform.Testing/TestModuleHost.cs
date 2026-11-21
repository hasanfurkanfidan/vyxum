using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform.Testing;

/// <summary>
/// An in-memory host for modules: registers the platform and the given modules against a
/// <see cref="TestDatabase"/> and creates their tables. Background services are not started;
/// tests deliver events with <see cref="DispatchPendingAsync"/> and move time with <see cref="Clock"/>.
/// Disposing does not delete the database.
/// </summary>
public sealed class TestModuleHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TestModuleHost(ServiceProvider services, TestClock clock)
    {
        _services = services;
        Clock = clock;
    }

    /// <summary>The platform's clock.</summary>
    public TestClock Clock { get; }

    /// <summary>The root service provider.</summary>
    public IServiceProvider Services => _services;

    /// <summary>Builds the host and runs the module database initializer.</summary>
    public static async Task<TestModuleHost> StartAsync(TestDatabase database, params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(database);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(database.Settings).Build();
        var clock = new TestClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddVyxumPlatform(configuration, modules);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var host = new TestModuleHost(provider, clock);
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

    /// <summary>Delivers every pending outbox message that is due; see <see cref="OutboxDispatcher.DispatchPendingAsync"/>.</summary>
    public Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default) =>
        _services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
