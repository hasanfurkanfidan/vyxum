using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Locks the endpoints of a module that requires a license (<see cref="ModuleManifest.RequiresLicense"/>).
/// </summary>
/// <remarks>
/// The gate wraps the request delegate of every endpoint created through the module's route group, so it covers
/// minimal API handlers, plain <see cref="RequestDelegate"/> endpoints, nested groups and SignalR hubs alike, and runs
/// before any endpoint filter of the module. Locked (no license, an invalid file, the module not in the license, or the
/// license not started yet): 403 <c>license_required</c>. Read-only (grace period over): only <c>GET</c> and
/// <c>HEAD</c> pass, everything else is 403 <c>license_read_only</c>; a hub cannot negotiate (POST) then, so a hub is
/// effectively locked in read-only mode. Licensed or in grace: the request passes. Middleware a module might add is not
/// covered (modules only map endpoints).
/// </remarks>
internal static class LicenseGate
{
    /// <summary>Error code of a request to a module without a license.</summary>
    public const string LicenseRequired = "license_required";

    /// <summary>Error code of a write request to a read-only module.</summary>
    public const string LicenseReadOnly = "license_read_only";

    /// <summary>Applies the gate to every endpoint of <paramref name="group"/>.</summary>
    public static void Apply(RouteGroupBuilder group, string moduleId)
    {
        ((IEndpointConventionBuilder)group).Add(builder =>
        {
            var next = builder.RequestDelegate;
            if (next is null)
            {
                return;
            }

            builder.RequestDelegate = async context =>
            {
                var state = context.RequestServices.GetRequiredService<LicenseManager>().GetModuleStatus(moduleId);
                if (Deny(state, context.Request.Method) is { } code)
                {
                    await LicenseProblems.Create(StatusCodes.Status403Forbidden, code).ExecuteAsync(context);
                    return;
                }

                await next(context);
            };
        });
    }

    /// <summary>The error code that denies <paramref name="method"/> in <paramref name="state"/>, or null to let it pass.</summary>
    public static string? Deny(ModuleLicenseState state, string method) => state switch
    {
        ModuleLicenseState.Free or ModuleLicenseState.Licensed or ModuleLicenseState.Grace => null,
        ModuleLicenseState.ReadOnly when HttpMethods.IsGet(method) || HttpMethods.IsHead(method) => null,
        ModuleLicenseState.ReadOnly => LicenseReadOnly,
        _ => LicenseRequired,
    };
}

/// <summary>Problem details with a machine-readable <c>code</c> for the license endpoints and the gate.</summary>
internal static class LicenseProblems
{
    public static IResult Create(int statusCode, string code) => Results.Problem(
        statusCode: statusCode,
        title: Title(code),
        extensions: new Dictionary<string, object?> { ["code"] = code });

    // English text for logs and developers; the user interface shows its own text for the code.
    private static string Title(string code) => code switch
    {
        LicenseGate.LicenseRequired => "This module needs a license.",
        LicenseGate.LicenseReadOnly => "The license of this module has ended; it is read-only.",
        _ => "The license request was rejected.",
    };
}
