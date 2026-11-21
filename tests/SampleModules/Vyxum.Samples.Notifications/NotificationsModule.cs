using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Notifications;

/// <summary>Sample module B: records a notification for every placed order. Test-only, never shipped.</summary>
public sealed class NotificationsModule : IModule
{
    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "samples.notifications",
        Version = new Version(1, 0),
        Optional = ["samples.orders"],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        context.AddDbContext<NotificationsDbContext>();
    }
}
