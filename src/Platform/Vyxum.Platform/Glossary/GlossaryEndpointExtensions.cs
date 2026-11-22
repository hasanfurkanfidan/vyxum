using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Vyxum.Platform;

/// <summary>Maps the glossary endpoint.</summary>
public static class GlossaryEndpointExtensions
{
    /// <summary>Path of the glossary endpoint.</summary>
    public const string GlossaryPath = "/api/glossary";

    /// <summary>
    /// Maps <c>GET /api/glossary</c>, which returns <c>{ culture, sections: [{ id, title, intro, terms: [{ id, name, text,
    /// inVyxum, example, otherNames: [{ culture, name }] }] }] }</c> in the request's UI culture. <c>inVyxum</c> and
    /// <c>example</c> are null when the term has none. The glossary is free: it needs no license and no module.
    /// </summary>
    public static IEndpointRouteBuilder MapVyxumGlossary(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(GlossaryPath, (GlossaryCatalog catalog) =>
        {
            var culture = CultureInfo.CurrentUICulture;
            return Results.Json(new GlossaryResponse(culture.Name, catalog.GetSections(culture)), DeviceEndpointExtensions.JsonOptions);
        });

        return endpoints;
    }

    internal sealed record GlossaryResponse(string Culture, IReadOnlyList<GlossarySection> Sections);
}
