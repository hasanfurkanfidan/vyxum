using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Vyxum.Licensing;
using Vyxum.Platform;

namespace Vyxum.E2E.Host;

/// <summary>
/// Runs the real Vyxum host (the same <c>Program</c>, middleware and web UI) on a loopback port with an empty data
/// directory, for the end-to-end screen check. Two things differ from a customer installation:
/// <list type="bullet">
/// <item>It also trusts a signing key generated at start-up. The key lives only in memory and dies with the process.</item>
/// <item><c>GET /e2e/license</c> returns license files signed with that key, so scenarios can load valid, expired or
/// damaged licenses without real license files.</item>
/// <item>A simulated MT-SICS scale listens on the next port (<see cref="FakeScale"/>); <c>/e2e/scale</c> controls it.</item>
/// </list>
/// The production keys stay trusted, so real license files work here as well.
/// </summary>
public static class E2EServer
{
    /// <summary>Path prefix of the test-only endpoints.</summary>
    public const string ControlPrefix = "/e2e";

    private const string KeyId = "e2e-run";

    /// <summary>
    /// Starts the server and runs until Ctrl+C, SIGTERM or the end of standard input.
    /// Arguments: <c>--port</c> (default 5390), <c>--data</c> (default: a new temporary folder),
    /// <c>--scale-port</c> (default: port + 1), <c>--content-root</c> (default: the <c>src/Host/Vyxum.Host</c> folder of
/// the checkout).
    /// Other <c>--Key value</c> pairs are passed to the host as configuration.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="moduleAssemblies">Module assemblies to load besides the built-in ones (referenced by the caller).</param>
    public static async Task<int> RunAsync(string[] args, IReadOnlyList<string> moduleAssemblies)
    {
        var options = ParseArguments(args);
        var port = int.Parse(options.GetValueOrDefault("port", "5390"), CultureInfo.InvariantCulture);
        var dataDirectory = options.GetValueOrDefault("data") ?? Path.Combine(Path.GetTempPath(), "vyxum-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dataDirectory);
        var contentRoot = options.GetValueOrDefault("content-root") ?? FindHostContentRoot();
        var scalePort = int.Parse(options.GetValueOrDefault("scale-port", (port + 1).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        await using var scale = new FakeScale(scalePort);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyRing = LicenseKeyRing.FromBase64(
            TrustedKeys.PublicKeys.Append(KeyValuePair.Create(KeyId, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))));
        var signer = new LicenseSigner(key, KeyId);

        // WebApplicationFactory looks for the content root next to a solution file; the paid modules repository has
        // another solution, so the folder is always given explicitly.
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_VYXUM_HOST", contentRoot);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(contentRoot);
            builder.UseEnvironment("Production");
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, dataDirectory);
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            builder.UseSetting("Logging:LogLevel:Microsoft.Hosting.Lifetime", "Information");
            for (var i = 0; i < moduleAssemblies.Count; i++)
                builder.UseSetting($"Vyxum:Modules:{i}", moduleAssemblies[i]);
            foreach (var (name, value) in options.Where(o => o.Key.Contains(':', StringComparison.Ordinal)))
                builder.UseSetting(name, value);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILicenseKeyRing>(keyRing);
                services.AddSingleton<IStartupFilter>(new ControlEndpoints(signer, scale));
            });
        });
        factory.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
        factory.StartServer();

        Console.WriteLine($"Vyxum E2E host listening on http://127.0.0.1:{port} (fake scale: tcp 127.0.0.1:{scale.Port}, data: {dataDirectory})");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
        // The runner closes standard input to stop the server; this also works on Windows, which has no SIGTERM.
        _ = Task.Run(() =>
        {
            while (Console.In.ReadLine() is not null) { }
            stop.Cancel();
        });

        try { await Task.Delay(Timeout.Infinite, stop.Token); }
        catch (OperationCanceledException) { }
        return 0;
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                throw new ArgumentException($"Unexpected argument '{args[i]}'. Use '--name value' pairs.");
            result[args[i][2..]] = args[++i];
        }

        return result;
    }

    /// <summary>The host project folder (it holds the built web UI in wwwroot), found above this program.</summary>
    private static string FindHostContentRoot()
    {
        string[] candidates = [Path.Combine("src", "Host", "Vyxum.Host"), Path.Combine("..", "vyxum", "src", "Host", "Vyxum.Host")];
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (var candidate in candidates)
            {
                var path = Path.GetFullPath(Path.Combine(directory.FullName, candidate));
                if (File.Exists(Path.Combine(path, "Vyxum.Host.csproj")))
                    return path;
            }
        }

        throw new InvalidOperationException("The Vyxum.Host project folder was not found. Pass --content-root.");
    }

    /// <summary>Adds the test-only endpoints in front of the host's own pipeline.</summary>
    private sealed class ControlEndpoints(LicenseSigner signer, FakeScale scale) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map(ControlPrefix + "/license", branch => branch.Run(context => WriteLicenseAsync(context, signer)));
            app.Map(ControlPrefix + "/scale", branch => branch.Run(scale.HandleAsync));
            next(app);
        };
    }

    /// <summary>
    /// <c>GET /e2e/license?kind=valid&amp;modules=a,b&amp;stations=2&amp;licensee=Name</c>. Kinds: <c>valid</c>,
    /// <c>perpetual</c>, <c>not-yet</c>, <c>grace</c> (ended 5 days ago), <c>read-only</c> (grace ended too),
    /// <c>tampered</c> (signature no longer matches), <c>not-a-license</c>.
    /// </summary>
    private static Task WriteLicenseAsync(HttpContext context, LicenseSigner signer)
    {
        var query = context.Request.Query;
        var kind = query["kind"].ToString() is { Length: > 0 } k ? k : "valid";
        var modules = query["modules"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stations = int.TryParse(query["stations"], CultureInfo.InvariantCulture, out var s) ? s : 2;
        var licensee = query["licensee"].ToString() is { Length: > 0 } l ? l : "Deneme Gıda A.Ş.";
        var today = DateOnly.FromDateTime(DateTime.Now);

        LicensePayload Payload(DateOnly from, DateOnly? until, LicenseType type = LicenseType.Subscription) => new()
        {
            LicenseId = "E2E-" + kind.ToUpperInvariant(),
            Type = type,
            Licensee = new Licensee { Name = licensee },
            Modules = modules,
            Stations = stations,
            IssuedAt = DateTimeOffset.Now.AddDays(-1),
            ValidFrom = from,
            ValidUntil = until,
        };

        string? file = kind switch
        {
            "valid" => signer.Sign(Payload(today.AddDays(-10), today.AddDays(365))),
            "perpetual" => signer.Sign(Payload(today.AddDays(-10), null, LicenseType.Perpetual)),
            "not-yet" => signer.Sign(Payload(today.AddDays(10), today.AddDays(375))),
            "grace" => signer.Sign(Payload(today.AddDays(-370), today.AddDays(-5))),
            "read-only" => signer.Sign(Payload(today.AddDays(-400), today.AddDays(-5 - LicensePayload.DefaultGraceDays))),
            "tampered" => Tamper(signer.Sign(Payload(today.AddDays(-10), today.AddDays(365))), signer.Sign(Payload(today.AddDays(-10), today.AddDays(3650)))),
            "not-a-license" => "{\"hello\":\"this is not a license\"}",
            _ => null,
        };

        if (file is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsync($"Unknown license kind '{kind}'.");
        }

        context.Response.ContentType = "application/octet-stream";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"e2e-{kind}.vxlic\"";
        return context.Response.WriteAsync(file, Encoding.UTF8);
    }

    /// <summary>The envelope of <paramref name="original"/> carrying the payload of <paramref name="changed"/>.</summary>
    private static string Tamper(string original, string changed)
    {
        using var originalFile = System.Text.Json.JsonDocument.Parse(original);
        using var changedFile = System.Text.Json.JsonDocument.Parse(changed);
        var envelope = originalFile.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        envelope["payload"] = changedFile.RootElement.GetProperty("payload").GetString();
        return System.Text.Json.JsonSerializer.Serialize(envelope);
    }
}
