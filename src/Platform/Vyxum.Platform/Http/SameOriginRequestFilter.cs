using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Vyxum.Platform;

/// <summary>
/// Rejects state-changing requests that a page on another site could make the browser send (cross-site request
/// forgery). The free version has no sign-in (decision V-03), so nothing else stops such a request.
/// </summary>
/// <remarks>
/// <para>
/// A request passes when it carries <see cref="HeaderName"/>: <see cref="HeaderValue"/>, or has the content type
/// <c>application/json</c>. A browser sends neither cross-site without a CORS preflight, which the host does not allow
/// (no CORS policy). Form posts, <c>text/plain</c> and multipart bodies, which browsers send cross-site freely, need the
/// header. In addition, a request whose <c>Origin</c> header is present and is not the request's own scheme and host is
/// rejected. Errors are 403 problem details with <c>code</c> <see cref="HeaderRequired"/> or <see cref="CrossOrigin"/>.
/// </para>
/// <para>
/// Applied to the license endpoints for now (<c>POST</c>/<c>DELETE /api/license</c>, <c>PUT /api/license/stations</c>);
/// it is meant to be applied to every state-changing API later (<see cref="SameOriginRequestExtensions.RequireSameOriginRequest{TBuilder}"/>).
/// </para>
/// </remarks>
internal static class SameOriginRequestFilter
{
    /// <summary>The custom request header the web application sends with every state-changing request.</summary>
    public const string HeaderName = "X-Vyxum-Request";

    /// <summary>The value of <see cref="HeaderName"/>.</summary>
    public const string HeaderValue = "1";

    /// <summary>Error code of a request without the header (and not JSON).</summary>
    public const string HeaderRequired = "request_header_required";

    /// <summary>Error code of a request from another origin.</summary>
    public const string CrossOrigin = "cross_origin_request";

    /// <summary>Wraps <paramref name="next"/> so that the check runs first, before the body is read or bound.</summary>
    public static RequestDelegate Wrap(RequestDelegate next) => async context =>
    {
        if (Deny(context.Request) is { } code)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The request was not sent by this application.",
                extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
            return;
        }

        await next(context);
    };

    /// <summary>The error code that rejects <paramref name="request"/>, or null.</summary>
    internal static string? Deny(HttpRequest request)
    {
        var origin = request.Headers.Origin;
        if (origin.Count > 0 && !IsSameOrigin(origin.ToString(), request))
        {
            return CrossOrigin;
        }

        if (string.Equals(request.Headers[HeaderName].ToString(), HeaderValue, StringComparison.Ordinal))
        {
            return null;
        }

        return MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            && type.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            ? null
            : HeaderRequired;
    }

    private static bool IsSameOrigin(string origin, HttpRequest request) =>
        request.Host.HasValue
        && Uri.TryCreate(origin, UriKind.Absolute, out var given)
        && Uri.TryCreate($"{request.Scheme}://{request.Host.Value}", UriKind.Absolute, out var own)
        && given.AbsolutePath == "/"
        && string.Equals(given.Scheme, own.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(given.Authority, own.Authority, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Applies <see cref="SameOriginRequestFilter"/>.</summary>
public static class SameOriginRequestExtensions
{
    /// <summary>
    /// Requires the request to come from this application: the <c>X-Vyxum-Request: 1</c> header (or a JSON body) and no
    /// foreign <c>Origin</c>. Use it on every endpoint that changes state.
    /// </summary>
    /// <remarks>Works on single endpoints and on route groups; the check runs before parameter binding.</remarks>
    public static TBuilder RequireSameOriginRequest<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(endpoint =>
        {
            if (endpoint.RequestDelegate is { } next)
            {
                endpoint.RequestDelegate = SameOriginRequestFilter.Wrap(next);
            }
        });
        return builder;
    }
}
