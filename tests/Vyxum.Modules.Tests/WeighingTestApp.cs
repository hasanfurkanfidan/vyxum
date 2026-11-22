using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Modules.Products;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Modules.Tests;

/// <summary>A minimal web host with the Weighing module (and optionally Products) on a throw-away database and a fake scale.</summary>
internal sealed class WeighingTestApp : IAsyncDisposable
{
    private readonly TestDatabase _database;
    private readonly WebApplication _app;

    private WeighingTestApp(TestDatabase database, WebApplication app, FakeLiveWeightSource scale, TestClock clock)
    {
        _database = database;
        _app = app;
        Scale = scale;
        Clock = clock;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public FakeLiveWeightSource Scale { get; }

    /// <summary>The clock records are stamped with.</summary>
    public TestClock Clock { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<WeighingTestApp> StartAsync(
        DatabaseProvider provider,
        bool withProducts = false,
        bool withWeighing = true,
        IReadOnlyDictionary<string, string?>? settings = null,
        IReadOnlyList<IModule>? otherModules = null)
    {
        var reason = TestDatabase.GetUnavailableReason(provider);
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        var database = TestDatabase.Create(provider);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(database.Settings);
        // The background dispatcher stays out of the way; tests dispatch explicitly.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Vyxum:Outbox:PollingInterval"] = "01:00:00" });
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        var modules = new List<IModule>();
        if (withWeighing)
        {
            modules.Add(new WeighingModule());
        }

        if (withProducts)
        {
            modules.Add(new ProductsModule());
        }

        modules.AddRange(otherModules ?? []);
        var clock = new TestClock();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddVyxumPlatform(builder.Configuration, modules);
        var scale = new FakeLiveWeightSource(clock);
        builder.Services.AddSingleton<ILiveWeightSource>(scale);

        var app = builder.Build();
        string[] cultures = ["tr", "en"];
        app.UseRequestLocalization(new RequestLocalizationOptions()
            .SetDefaultCulture("en")
            .AddSupportedCultures(cultures)
            .AddSupportedUICultures(cultures));
        app.MapVyxumModules();
        await app.StartAsync();
        return new WeighingTestApp(database, app, scale, clock);
    }

    public async Task<T> WithDbAsync<T>(Func<WeighingDbContext, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<WeighingDbContext>());
    }

    public Task<List<OutboxMessage>> OutboxAsync() =>
        WithDbAsync(db => db.OutboxMessages.OrderBy(m => m.OccurredAt).ThenBy(m => m.EventName).ToListAsync(TestContext.Current.CancellationToken));

    public Task<int> DispatchAsync() =>
        _app.Services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync(TestContext.Current.CancellationToken);

    /// <summary>Delivers a product event to the Weighing module as if the Products module had published it.</summary>
    public async Task DeliverAsync(Vyxum.Modules.Weighing.ProductSaved product, DateTime? occurredAt = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProductSavedHandler>();
        var context = new IntegrationEventContext(Guid.NewGuid(), occurredAt ?? DateTime.UtcNow, "products.product-saved", 1);
        await handler.HandleAsync(product, context, TestContext.Current.CancellationToken);
    }

    public async Task<(HttpResponseMessage Response, JsonElement Body)> SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response, json);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }
}
