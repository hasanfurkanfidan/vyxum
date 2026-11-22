using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Modules.Tests;

public class WeighingHistoryTests
{
    private const string Scale = "scale-1";

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_history_request_sends_every_record_in_pages_with_its_cancel(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        var ids = new List<long>();
        for (var i = 0; i < WeighingHistoryRequestedHandler.PageSize + 2; i++)
        {
            app.Scale.Set(Scale, 1.2m + (i * 0.001m));
            var (created, json) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", new { requestId = Guid.NewGuid(), deviceId = Scale });
            created.StatusCode.ShouldBe(HttpStatusCode.Created, json.ToString());
            ids.Add(json.GetProperty("id").GetInt64());
        }

        (await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{ids[1]}/cancel", new { reason = "Wrong product" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var requestId = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeighingDbContext>();
            var handler = scope.ServiceProvider.GetRequiredService<WeighingHistoryRequestedHandler>();
            await handler.HandleAsync(new WeighingHistoryRequested(requestId), new IntegrationEventContext(Guid.NewGuid(), DateTime.UtcNow, "weighing.history-requested", 1), TestContext.Current.CancellationToken);
        }

        var pages = (await app.OutboxAsync())
            .Where(m => m.EventName == "weighing.history-page")
            .Select(m => JsonSerializer.Deserialize<WeighingHistoryPage>(m.Payload, JsonSerializerOptions.Web)!)
            .OrderBy(p => p.Page)
            .ToList();
        pages.Select(p => (p.Page, p.IsLast, p.Records.Count)).ShouldBe([(1, false, WeighingHistoryRequestedHandler.PageSize), (2, true, 2)]);
        pages.ShouldAllBe(p => p.RequestId == requestId);
        var records = pages.SelectMany(p => p.Records).ToList();
        records.Select(r => r.Recorded.RecordId).ShouldBe(ids);
        records.ShouldAllBe(r => !r.Recorded.LabelRequested);
        records[1].CancelledAt.ShouldNotBeNull();
        records[1].CancelReason.ShouldBe("Wrong product");
        records[0].CancelledAt.ShouldBeNull();
        records[0].Recorded.DeviceId.ShouldBe(Scale);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Without_records_the_answer_is_one_empty_last_page(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<WeighingHistoryRequestedHandler>();
            await handler.HandleAsync(new WeighingHistoryRequested(Guid.NewGuid()), new IntegrationEventContext(Guid.NewGuid(), DateTime.UtcNow, "weighing.history-requested", 1), TestContext.Current.CancellationToken);
        }

        var page = (await app.OutboxAsync()).ShouldHaveSingleItem();
        var answer = JsonSerializer.Deserialize<WeighingHistoryPage>(page.Payload, JsonSerializerOptions.Web)!;
        answer.IsLast.ShouldBeTrue();
        answer.Records.ShouldBeEmpty();
    }

    [Fact]
    public void The_records_report_is_declared_for_a_reports_catalog()
    {
        var report = new WeighingModule().Manifest.Reports.ShouldHaveSingleItem();
        report.Id.ShouldBe("weighing.records");
        report.Link.ShouldBe("/records");
        report.Outputs.ShouldBe(ReportOutputs.Screen | ReportOutputs.Excel);
    }
}
