using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Platform.Tests.Modules;

public class ReportCatalogTests
{
    private static ReportDefinition Report(string id, string link = "/x", ReportAudience audience = ReportAudience.Owner, ReportOutputs outputs = ReportOutputs.Screen) => new()
    {
        Id = id,
        NameKey = id + ".Name",
        DescriptionKey = id + ".Description",
        Audience = audience,
        Outputs = outputs,
        Link = link,
    };

    private static TestModule Module(string id, bool paid, params ReportDefinition[] reports) =>
        new(new ModuleManifest { Id = id, Version = new Version(1, 0), RequiresLicense = paid, DisplayNameKey = id + ".ModuleName", Reports = reports });

    private static IReadOnlyList<string> Problems(params TestModule[] modules) =>
        ModuleValidator.FindProblems(modules.Select(m => new ModuleDescriptor(m)).ToList());

    [Fact]
    public void The_catalog_lists_reports_by_module_with_the_module_facts()
    {
        var services = new ServiceCollection();
        services.AddVyxumPlatform(new ConfigurationBuilder().Build(),
            [Module("b.second", true, Report("b.second.list")), Module("a.first", false, Report("a.first.day"), Report("a.first.week"))]);
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<IReportCatalog>();
        catalog.Reports.Select(r => $"{r.ModuleId}:{r.Definition.Id}:{r.RequiresLicense}").ShouldBe(["a.first:a.first.day:False", "a.first:a.first.week:False", "b.second:b.second.list:True"]);
        catalog.Reports[0].DisplayNameKey.ShouldBe("a.first.ModuleName");
    }

    [Fact]
    public void Valid_reports_have_no_problems()
    {
        Problems(Module("a", false, Report("a.day", "/m/a/day", ReportAudience.Owner | ReportAudience.Auditor, ReportOutputs.Pdf | ReportOutputs.Excel))).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://example.com/x")]
    [InlineData("//example.com/x")]
    [InlineData("records")]
    [InlineData("/a\\b")]
    public void A_link_outside_the_application_is_a_problem(string link)
    {
        Problems(Module("a", false, Report("a.day", link))).ShouldHaveSingleItem().ShouldContain("invalid link");
    }

    [Fact]
    public void Ids_audiences_outputs_and_duplicates_are_checked()
    {
        var problems = Problems(
            Module("a", false, Report("A Day"), Report("a.none", audience: ReportAudience.None), Report("a.empty", outputs: ReportOutputs.None)),
            Module("b", false, Report("a.none")));

        problems.ShouldContain(p => p.Contains("invalid report id 'A Day'"));
        problems.ShouldContain(p => p.Contains("'a.none'") && p.Contains("no valid audience"));
        problems.ShouldContain(p => p.Contains("'a.empty'") && p.Contains("no valid output"));
        problems.ShouldContain("Report 'a.none' is declared more than once: 'a', 'b'.");
    }
}
