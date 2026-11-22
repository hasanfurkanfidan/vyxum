using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// <see cref="ICapabilityRegistry"/> built from the manifests of the loaded modules, following the license at run time.
/// </summary>
/// <remarks>
/// A capability is available when at least one module that provides it may run: a free module, or a module that
/// requires a license and is licensed or in its grace period. A locked or read-only module provides nothing, so for
/// example an unlicensed label module does not make "Save and print label" appear (decision V-04). The license is read
/// at every call (it is cached by <see cref="LicenseManager"/>), so the answer changes when a license is uploaded or
/// ends; read it once per operation. Startup validation of <see cref="ModuleManifest.Requires"/> stays static: it is
/// about what is installed (<see cref="Installed"/>). The platform reads only <see cref="ModuleManifest.RequiresLicense"/>
/// and the license state of a module id; it knows no module.
/// </remarks>
public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly Dictionary<Capability, string[]> _providers;
    private readonly HashSet<string> _licensed;
    private readonly Func<ILicenseState>? _licenses;

    /// <summary>
    /// Creates a registry of what the manifests provide, without a license: every installed capability is available.
    /// This is what modules see while registering services (<see cref="ModuleContext.Capabilities"/>).
    /// </summary>
    public CapabilityRegistry(IEnumerable<ModuleManifest> manifests)
        : this(manifests, null)
    {
    }

    /// <summary>Creates a registry that also asks <paramref name="licenses"/> about modules that require a license.</summary>
    /// <param name="manifests">The manifests of the loaded modules.</param>
    /// <param name="licenses">Returns the license state; null treats every module as runnable (installed view).</param>
    public CapabilityRegistry(IEnumerable<ModuleManifest> manifests, Func<ILicenseState>? licenses)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var list = manifests.ToList();
        _providers = list
            .SelectMany(m => m.Provides.Distinct().Select(c => (Capability: c, m.Id)))
            .GroupBy(p => p.Capability)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToArray());
        _licensed = list.Where(m => m.RequiresLicense).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        _licenses = licenses;
    }

    /// <summary>Every capability some loaded module provides, licensed or not.</summary>
    public IReadOnlyCollection<Capability> Installed => _providers.Keys;

    /// <inheritdoc />
    public bool IsAvailable(Capability capability)
    {
        if (!_providers.TryGetValue(capability, out var providers))
        {
            return false;
        }

        // Free providers need no license lookup.
        if (providers.Any(id => !_licensed.Contains(id)) || _licenses is null)
        {
            return true;
        }

        var licenses = _licenses();
        return providers.Any(id => MayRun(licenses.GetModuleStatus(id)));
    }

    /// <summary>The capabilities available when module states are given by <paramref name="moduleState"/>, in id order.</summary>
    internal IReadOnlyList<string> Available(Func<string, ModuleLicenseState> moduleState) =>
    [
        .. _providers
            .Where(p => p.Value.Any(id => !_licensed.Contains(id) || MayRun(moduleState(id))))
            .Select(p => p.Key.Id)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Whether a module in <paramref name="state"/> provides its capabilities.</summary>
    internal static bool MayRun(ModuleLicenseState state) =>
        state is ModuleLicenseState.Free or ModuleLicenseState.Licensed or ModuleLicenseState.Grace;
}
