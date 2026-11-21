using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Vyxum.Platform;

/// <summary>Maps the endpoints the user interface shell needs.</summary>
public static class UiEndpointExtensions
{
    /// <summary>Path of the UI strings endpoint.</summary>
    public const string StringsPath = "/api/ui/strings";

    /// <summary>Path of the loaded modules endpoint.</summary>
    public const string ModulesPath = "/api/ui/modules";

    /// <summary>Configuration key of the contact e-mail address shown in the trial license request (written by the installer or the reseller).</summary>
    public const string ContactEmailKey = "Vyxum:Contact:Email";

    /// <summary>Configuration key of the contact web address shown in the trial license request.</summary>
    public const string ContactUrlKey = "Vyxum:Contact:Url";

    /// <summary>
    /// Maps <c>GET /api/ui/strings</c>, which returns <c>{ culture, strings: { "prefix.Key": "text" } }</c>
    /// in the request's UI culture (set by request localization), and <c>GET /api/ui/modules</c>, which
    /// returns <c>{ modules: [{ id, version, requiresLicense, name, provides, requires, optional, license, ui }], capabilities: ["..."], companyName,
    /// license: { status, licensee, graceDaysLeft } }</c>; <c>companyName</c> is the name entered in the setup wizard, or
    /// null. <c>contact</c> is <c>{ email, url }</c> from the configuration keys <see cref="ContactEmailKey"/> and
    /// <see cref="ContactUrlKey"/> (either may be null). A module's <c>preview</c> is <c>{ description, imageUrl }</c> for a
    /// module that declares a <see cref="Abstractions.ModulePreview"/> (locked or not), otherwise null.
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

        endpoints.MapGet(ModulesPath, async (ModuleCatalog catalog, CapabilityRegistry registry, IPlatformSettings settings, LicenseManager licenses, ModuleDisplayNames names, IConfiguration configuration, CancellationToken cancellationToken) =>
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
                    UiOf(m.Manifest, snapshot.GetModuleStatus(m.Manifest.Id)),
                    PreviewOf(m.Manifest, names, culture)))
                .ToList();
            var capabilities = registry.Available(snapshot.GetModuleStatus);
            var companyName = await settings.GetAsync(PlatformSettingKeys.CompanyName, cancellationToken);
            var license = new UiLicense(
                LicenseText.Of(snapshot.Status),
                snapshot.License?.Payload.Licensee.Name,
                snapshot.Evaluation?.GraceDaysLeft);
            var contact = new UiContact(NullIfBlank(configuration[ContactEmailKey]), NullIfBlank(configuration[ContactUrlKey]));
            return Results.Json(new UiModulesResponse(modules, capabilities, companyName, license, contact), DeviceEndpointExtensions.JsonOptions);
        });

        return endpoints;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The preview is offered locked or not: it is a static sample the module declared on purpose.
    private static UiModulePreview? PreviewOf(Abstractions.ModuleManifest manifest, ModuleDisplayNames names, CultureInfo culture) =>
        manifest.Preview is { } preview
            ? new UiModulePreview(names.ResolveDescription(manifest, culture) ?? string.Empty, ModuleUiEndpointExtensions.PreviewUrlOf(manifest.Id, preview.Image))
            : null;

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
        UiModuleScreens? Ui,
        UiModulePreview? Preview);

    internal sealed record UiModulePreview(string Description, string ImageUrl);

    internal sealed record UiContact(string? Email, string? Url);

    internal sealed record UiModuleScreens(string Entry, int SdkVersion);

    internal sealed record UiLicense(string Status, string? Licensee, int? GraceDaysLeft);

    internal sealed record UiModulesResponse(
        IReadOnlyList<UiModule> Modules,
        IReadOnlyList<string> Capabilities,
        string? CompanyName,
        UiLicense License,
        UiContact Contact);
}
