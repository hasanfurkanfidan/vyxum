using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using Vyxum.Host;
using Vyxum.Platform;

var builder = WebApplication.CreateBuilder(args);

var catalog = builder.Services.AddVyxumPlatform(builder.Configuration, HostModules.Load(builder.Configuration));
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

var app = builder.Build();

string[] supportedCultures = ["tr", "en"];
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("tr")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

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

app.MapVyxumModules();

app.Run();

/// <summary>Entry point, public so that integration tests can host the application.</summary>
public partial class Program;
