using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Vyxum.Platform;

/// <summary>Serves the embedded front end files of the modules (<see cref="Abstractions.ModuleUi"/>).</summary>
public static class ModuleUiEndpointExtensions
{
    /// <summary>Path prefix of the module front end files: <c>/modules/{moduleId}/{path}</c>.</summary>
    public const string PathPrefix = "/modules";

    /// <summary>The address of a file of a module's front end.</summary>
    public static string UrlOf(string moduleId, string path) => $"{PathPrefix}/{moduleId}/{path}";

    /// <summary>
    /// Maps <c>GET</c> and <c>HEAD</c> <c>/modules/{moduleId}/{**path}</c>. A module that requires a license is gated
    /// like its endpoints (<see cref="LicenseGate"/>): locked → 403 <c>license_required</c>; read-only still serves the
    /// files, since a read-only module shows its data. Unknown modules, modules without screens and unknown files → 404.
    /// Responses carry <c>Cache-Control: no-cache</c> and an ETag: the browser revalidates every time, so a license
    /// change takes effect at once while unchanged files answer 304.
    /// </summary>
    public static IEndpointRouteBuilder MapVyxumModuleUi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapMethods(PathPrefix + "/{moduleId}/{**path}", [HttpMethods.Get, HttpMethods.Head], Serve)
            .ExcludeFromDescription();
        return endpoints;
    }

    private static async Task Serve(HttpContext context, string moduleId, string? path)
    {
        var catalog = context.RequestServices.GetRequiredService<ModuleCatalog>();
        var files = context.RequestServices.GetRequiredService<ModuleUiFiles>();
        var module = catalog.Modules.FirstOrDefault(m => string.Equals(m.Manifest.Id, moduleId, StringComparison.Ordinal));
        if (module is null || !files.HasUi(moduleId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (module.Manifest.RequiresLicense)
        {
            var state = context.RequestServices.GetRequiredService<LicenseManager>().GetModuleStatus(moduleId);
            if (LicenseGate.Deny(state, context.Request.Method) is { } code)
            {
                await LicenseProblems.Create(StatusCodes.Status403Forbidden, code).ExecuteAsync(context);
                return;
            }
        }

        var file = string.IsNullOrEmpty(path) || !IsSafe(path) ? null : files.Find(moduleId, path);
        if (file is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var headers = context.Response.Headers;
        headers.CacheControl = "no-cache";
        headers.ETag = file.ETag;
        headers.XContentTypeOptions = "nosniff";
        if (context.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',').Any(t => t.Trim() == file.ETag || t.Trim() == "*")))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        context.Response.ContentType = file.ContentType;
        context.Response.ContentLength = file.Content.Length;
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(file.Content, context.RequestAborted);
        }
    }

    // Lookups are exact, so this is defence in depth: no parent or current segments, backslashes or empty segments.
    private static bool IsSafe(string path) =>
        !path.Contains('\\', StringComparison.Ordinal) &&
        path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
}
