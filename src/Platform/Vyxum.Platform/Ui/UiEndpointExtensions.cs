using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Vyxum.Platform;

/// <summary>Maps the endpoints the user interface shell needs.</summary>
public static class UiEndpointExtensions
{
    /// <summary>Path of the UI strings endpoint.</summary>
    public const string StringsPath = "/api/ui/strings";

    /// <summary>Path of the loaded modules endpoint.</summary>
    public const string ModulesPath = "/api/ui/modules";

    /// <summary>
    /// Maps <c>GET /api/ui/strings</c>, which returns <c>{ culture, strings: { "prefix.Key": "text" } }</c>
    /// in the request's UI culture (set by request localization), and <c>GET /api/ui/modules</c>, which
    /// returns <c>{ modules: [{ id, version, requiresLicense, name, provides, requires, optional, license, ui }], capabilities: ["..."], companyName,
    /// license: { status, licensee, graceDaysLeft } }</c>; <c>companyName</c> is the name entered in the setup wizard, or
    /// null.
    /// </summary>
    /// <remarks>
    /// A module's <c>license</c> is <c>free</c>, <c>licensed</c>, <c>grace</c>, <c>readOnly</c> or <c>locked</c>; the shell
    /// shows a module's screens by this field. A module's <c>ui</c> is <c>{ entry, sdkVersion }</c> (<c>entry</c> is the URL of
    /// its embedded entry ES module) when it has screens and may load them, i.e. it is not locked; otherwise null. <c>capabilities</c> lists the
    /// capabilities that are available now, by the same rule as <see cref="Abstractions.ICapabilityRegistry"/>
    /// (<see cref="CapabilityRegistry"/>): provided by a free module or by a licensed module that is licensed or in grace;
    /// a locked or read-only module's capabilities are left out. A module's <c>provides</c> still lists what it declares. The top-level <c>license.status</c> is <c>none</c>,
    /// <c>invalid</c>, <c>notYetValid</c>, <c>valid</c>, <c>grace</c> or <c>readOnly</c>; <c>licensee</c> is the licensee's
    /// name of a license that verifies.
    /// </remarks>
    public static IEndpointRouteBuilder MapVyxumUi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(StringsPath, (UiStringCatalog catalog) =>
        {
            var culture = CultureInfo.CurrentUICulture;
            return Results.Json(new UiStringsResponse(culture.Name, catalog.GetStrings(culture)), DeviceEndpointExtensions.JsonOptions);
        });

        endpoints.MapGet(ModulesPath, async (ModuleCatalog catalog, CapabilityRegistry registry, IPlatformSettings settings, LicenseManager licenses, ModuleDisplayNames names, CancellationToken cancellationToken) =>
        {
            var snapshot = licenses.GetSnapshot();
            var culture = CultureInfo.CurrentUICulture;
            var modules = catalog.Modules
                .Select(m => new UiModule(
                    m.Manifest.Id,
                    m.Manifest.Version.ToString(),
                    m.Manifest.RequiresLicense,
                    names.Resolve(m.Manifest, culture),
                    m.Manifest.Provides.Select(c => c.Id).ToList(),
                    m.Manifest.Requires.Select(c => c.Id).ToList(),
                    m.Manifest.Optional.Select(c => c.Id).ToList(),
                    LicenseText.Of(snapshot.GetModuleStatus(m.Manifest.Id)),
                    UiOf(m.Manifest, snapshot.GetModuleStatus(m.Manifest.Id))))
                .ToList();
            var capabilities = registry.Available(snapshot.GetModuleStatus);
            var companyName = await settings.GetAsync(PlatformSettingKeys.CompanyName, cancellationToken);
            var license = new UiLicense(
                LicenseText.Of(snapshot.Status),
                snapshot.License?.Payload.Licensee.Name,
                snapshot.Evaluation?.GraceDaysLeft);
            return Results.Json(new UiModulesResponse(modules, capabilities, companyName, license), DeviceEndpointExtensions.JsonOptions);
        });

        return endpoints;
    }

    // The screens of a locked module are not offered: the shell does not try to load them.
    private static UiModuleScreens? UiOf(Abstractions.ModuleManifest manifest, Abstractions.ModuleLicenseState state) =>
        manifest.Ui is { } ui && LicenseGate.Deny(state, HttpMethods.Get) is null
            ? new UiModuleScreens(ModuleUiEndpointExtensions.UrlOf(manifest.Id, ui.Entry), ui.SdkVersion)
            : null;

    internal sealed record UiStringsResponse(string Culture, IReadOnlyDictionary<string, string> Strings);

    internal sealed record UiModule(
        string Id,
        string Version,
        bool RequiresLicense,
        string? Name,
        IReadOnlyList<string> Provides,
        IReadOnlyList<string> Requires,
        IReadOnlyList<string> Optional,
        string License,
        UiModuleScreens? Ui);

    internal sealed record UiModuleScreens(string Entry, int SdkVersion);

    internal sealed record UiLicense(string Status, string? Licensee, int? GraceDaysLeft);

    internal sealed record UiModulesResponse(
        IReadOnlyList<UiModule> Modules,
        IReadOnlyList<string> Capabilities,
        string? CompanyName,
        UiLicense License);
}
