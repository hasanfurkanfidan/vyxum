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
    /// returns <c>{ modules: [{ id, version }], capabilities: ["..."], companyName }</c>; <c>companyName</c>
    /// is the name entered in the setup wizard, or null.
    /// </summary>
    public static IEndpointRouteBuilder MapVyxumUi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(StringsPath, (UiStringCatalog catalog) =>
        {
            var culture = CultureInfo.CurrentUICulture;
            return Results.Json(new UiStringsResponse(culture.Name, catalog.GetStrings(culture)), DeviceEndpointExtensions.JsonOptions);
        });

        endpoints.MapGet(ModulesPath, async (ModuleCatalog catalog, IPlatformSettings settings, CancellationToken cancellationToken) =>
        {
            var modules = catalog.Modules
                .Select(m => new UiModule(m.Manifest.Id, m.Manifest.Version.ToString()))
                .ToList();
            var capabilities = catalog.Modules
                .SelectMany(m => m.Manifest.Provides)
                .Select(c => c.Id)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            var companyName = await settings.GetAsync(PlatformSettingKeys.CompanyName, cancellationToken);
            return Results.Json(new UiModulesResponse(modules, capabilities, companyName), DeviceEndpointExtensions.JsonOptions);
        });

        return endpoints;
    }

    internal sealed record UiStringsResponse(string Culture, IReadOnlyDictionary<string, string> Strings);

    internal sealed record UiModule(string Id, string Version);

    internal sealed record UiModulesResponse(IReadOnlyList<UiModule> Modules, IReadOnlyList<string> Capabilities, string? CompanyName);
}
