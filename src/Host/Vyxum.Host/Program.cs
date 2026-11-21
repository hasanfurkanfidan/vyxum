using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using Vyxum.Host;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Do not wait the default 30 seconds for open agent WebSockets when the host shuts down.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

var catalog = builder.Services.AddVyxumPlatform(builder.Configuration, HostModules.Load(builder.Configuration));
builder.Services.AddVyxumAgentHub(builder.Configuration);
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddUiStrings(Strings.UiPrefix, Strings.ResourceManager);

var app = builder.Build();

string[] supportedCultures = ["tr", "en"];
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("tr")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

// Static files must run before routing: the middleware skips requests that already matched an endpoint, such as the fallback.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.MapGet("/health", (DatabaseSettings database) => Results.Ok(new
{
    status = "ok",
    modules = catalog.Modules.Select(m => new { id = m.Manifest.Id, version = m.Manifest.Version.ToString() }),
    database = database.Provider.ToString(),
}));

app.MapGet("/api/hello", (IStringLocalizer<Strings> strings) => Results.Ok(new
{
    message = strings["Hello"].Value,
    culture = CultureInfo.CurrentUICulture.Name,
}));

app.MapVyxumDevices();
app.MapVyxumUi();
app.MapVyxumModules();

// The built web UI. Client-side routes fall back to index.html; API, hub and health paths never do.
app.MapFallbackToFile("{*path:regex(^(?!api(/|$)|hubs(/|$)|health(/|$)).*$)}", "index.html");

if (!File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
{
    app.Logger.LogWarning("The web UI is not built, so the root path returns 404. Run 'npm ci' and 'npm run build' in src/Web/vyxum-web.");
}

app.Run();

/// <summary>Entry point, public so that integration tests can host the application.</summary>
public partial class Program;
