using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Localization;
using Vyxum.Host;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;

var runningAsService = WindowsServiceHelpers.IsWindowsService();

// The installer runs "Vyxum.Host.exe --ensure-agent-key" once to give the installation its own agent key.
var ensureAgentKey = args.Contains(AgentKeyProvisioning.Argument, StringComparer.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = [.. args.Where(a => !a.Equals(AgentKeyProvisioning.Argument, StringComparison.OrdinalIgnoreCase))],

    // A service starts in the system folder; wwwroot and appsettings.json live next to the executable.
    ContentRootPath = runningAsService ? AppContext.BaseDirectory : null,
});

// The shared settings file <DataDirectory>yxum.json (decision V-37) overrides appsettings.json;
// environment variables and the command line still override it.
var settingsFile = VyxumSettingsFile.AddTo(builder.Configuration);
if (ensureAgentKey)
{
    AgentKeyProvisioning.EnsureKey(settingsFile, builder.Configuration);
    return;
}

// Runs as the "Vyxum Web" Windows service when started by the service control manager; no effect otherwise.
builder.Services.AddWindowsService(options => options.ServiceName = "Vyxum Web");
builder.Services.Configure<HostRestartOptions>(o => o.ExitForServiceRecovery = runningAsService);

// Do not wait the default 30 seconds for open agent WebSockets when the host shuts down.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

var catalog = builder.Services.AddVyxumPlatform(builder.Configuration, HostModules.Load(builder.Configuration));
builder.Services.AddVyxumAgentHub(builder.Configuration);
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddUiStrings(Strings.UiPrefix, Strings.ResourceManager);

var app = builder.Build();

// Refuses unknown host names (DNS rebinding) and data changes sent by pages of other sites, before anything else runs.
app.UseVyxumRequestProtection();

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
app.MapVyxumSetup();
app.MapVyxumLicense();
app.MapVyxumModules();

// The built web UI. Client-side routes fall back to index.html; API, hub, health and module file paths never do.
app.MapFallbackToFile("{*path:regex(^(?!api(/|$)|hubs(/|$)|health(/|$)|modules(/|$)).*$)}", "index.html");

if (!File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
{
    app.Logger.LogWarning("The web UI is not built, so the root path returns 404. Run 'npm ci' and 'npm run build' in src/Web/vyxum-web.");
}

app.Run();

/// <summary>Entry point, public so that integration tests can host the application.</summary>
public partial class Program;
