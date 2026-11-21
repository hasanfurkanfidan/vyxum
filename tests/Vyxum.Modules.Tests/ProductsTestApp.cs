using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Modules.Products;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Modules.Tests;

/// <summary>A minimal web host with only the Products module, on a throw-away database.</summary>
internal sealed class ProductsTestApp : IAsyncDisposable
{
    private readonly TestDatabase _database;
    private readonly WebApplication _app;

    private ProductsTestApp(TestDatabase database, WebApplication app)
    {
        _database = database;
        _app = app;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public static async Task<ProductsTestApp> StartAsync(DatabaseProvider provider)
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
        builder.Services.AddVyxumPlatform(builder.Configuration, [new ProductsModule()]);

        var app = builder.Build();
        app.MapVyxumModules();
        await app.StartAsync();
        return new ProductsTestApp(database, app);
    }

    public async Task<T> WithDbAsync<T>(Func<ProductsDbContext, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ProductsDbContext>());
    }

    public async Task<T> WithServiceAsync<T>(Func<ProductService, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ProductService>());
    }

    public Task<List<OutboxMessage>> OutboxAsync() =>
        WithDbAsync(db => db.OutboxMessages.OrderBy(m => m.OccurredAt).ToListAsync(TestContext.Current.CancellationToken));

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
