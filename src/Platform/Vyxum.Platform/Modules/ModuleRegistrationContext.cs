using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>The platform's <see cref="ModuleContext"/> for one module.</summary>
internal sealed class ModuleRegistrationContext(
    ModuleDescriptor descriptor,
    IServiceCollection services,
    IConfiguration configuration,
    ICapabilityRegistry capabilities) : ModuleContext
{
    public ModuleDescriptor Descriptor { get; } = descriptor;

    public IServiceCollection Services { get; } = services;

    public override IConfiguration Configuration { get; } = configuration;

    public override ICapabilityRegistry Capabilities { get; } = capabilities;
}
