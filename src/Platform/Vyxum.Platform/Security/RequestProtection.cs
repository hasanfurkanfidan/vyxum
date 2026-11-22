using System.Net;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vyxum.Platform;

/// <summary>
/// Protects the web application against web pages that the operator opens in a browser on the same computer or
/// network. Such a page can send requests to <c>http://localhost:5000</c>; these checks stop it from changing data.
/// </summary>
/// <remarks>
/// <para><b>Host check:</b> the <c>Host</c> header must be an IP address, <c>localhost</c>, this computer's name
/// (with or without its domain) or one of the names in <see cref="AllowedHostsKey"/>. Otherwise the answer is 400.
/// This stops DNS rebinding, where a site's own domain name is made to point at this computer so that the browser
/// treats the web application as part of that site.</para>
/// <para><b>Same origin check:</b> a request that may change data (anything but GET, HEAD and OPTIONS) is refused with
/// 403 when the browser says it comes from another site: <c>Sec-Fetch-Site</c> is <c>cross-site</c> or
/// <c>same-site</c>, or, for browsers without that header, <c>Origin</c> names another host than <c>Host</c>.
/// Requests without these headers do not come from a web page (the agent, scripts, tests) and pass.</para>
/// <para>The agent hub additionally refuses every request with an <c>Origin</c> header (see
/// <see cref="AgentConnectionGate"/>).</para>
/// </remarks>
public static partial class RequestProtection
{
    /// <summary>
    /// Configuration key with extra host names the web application may be reached by, separated by <c>;</c>, for
    /// example <c>tartim.firma.local</c>.
    /// </summary>
    public const string AllowedHostsKey = "Vyxum:AllowedHosts";

    /// <summary>Adds the host check and the same origin check. Call before the static files and the routing.</summary>
    public static IApplicationBuilder UseVyxumRequestProtection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RequestProtection).FullName!);
        var names = AllowedNames(configuration);
        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (!IsAllowedHost(request.Host.Host, names))
            {
                LogUnknownHost(logger, request.Host.Value ?? string.Empty);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!IsSafeMethod(request.Method) && IsCrossSite(request))
            {
                LogCrossSite(logger, request.Method, request.Path, request.Headers.Origin.ToString());
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });
    }

    /// <summary>
    /// True when the host name is an IP address, <c>localhost</c> (or a name under it) or one of
    /// <paramref name="names"/>. An empty host (HTTP/1.0) is allowed.
    /// </summary>
    internal static bool IsAllowedHost(string? host, IReadOnlySet<string> names)
    {
        if (string.IsNullOrEmpty(host))
        {
            return true;
        }

        var bare = host.TrimEnd('.');
        if (IPAddress.TryParse(bare.Trim('[', ']'), out _))
        {
            return true;
        }

        return bare.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || bare.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || names.Contains(bare);
    }

    /// <summary>True when a browser marks the request as sent by a page of another site.</summary>
    internal static bool IsCrossSite(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0)
        {
            // "none" is a request the user started (address bar, bookmark).
            return !fetchSite.Equals("same-origin", StringComparison.OrdinalIgnoreCase)
                && !fetchSite.Equals("none", StringComparison.OrdinalIgnoreCase);
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return false;
        }

        // "null" is an opaque origin (sandboxed frame, file: page): never this application.
        return !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary>This computer's names and the configured extra names, compared ignoring case.</summary>
    internal static HashSet<string> AllowedNames(IConfiguration configuration)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machine))
        {
            names.Add(machine);
            try
            {
                var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    names.Add(machine + "." + domain.Trim('.'));
                }
            }
            catch (NetworkInformationException)
            {
                // No domain information: the plain computer name still works.
            }
        }

        try
        {
            names.Add(Dns.GetHostName());
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Same as above.
        }

        foreach (var name in (configuration[AllowedHostsKey] ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            names.Add(name.TrimEnd('.'));
        }

        return names;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a request for unknown host {Host}. Add the name to Vyxum:AllowedHosts if the web application is reached by it.")]
    private static partial void LogUnknownHost(ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a {Method} request to {Path} sent by a page of another site (origin {Origin}).")]
    private static partial void LogCrossSite(ILogger logger, string method, PathString path, string origin);
}
