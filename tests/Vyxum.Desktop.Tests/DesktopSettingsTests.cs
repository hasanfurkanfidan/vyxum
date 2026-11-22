using Shouldly;
using Xunit;
using Vyxum.Desktop;

namespace Vyxum.Desktop.Tests;

public sealed class DesktopSettingsTests
{
    [Fact]
    public void Missing_file_content_gives_defaults()
    {
        var settings = DesktopSettings.Parse(null, []);

        settings.Url.ShouldBe(new Uri("http://localhost:5000/"));
        settings.FullScreen.ShouldBeFalse();
    }

    [Fact]
    public void Missing_file_on_disk_gives_defaults()
    {
        var settings = DesktopSettings.Load([], Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "vyxum.json"));

        settings.Url.ShouldBe(new Uri("http://localhost:5000/"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"Urls\": 5}")]
    [InlineData("{\"Urls\": \"ftp://x\"}")]
    [InlineData("")]
    public void Bad_content_gives_defaults(string json)
    {
        DesktopSettings.Parse(json, []).Url.ShouldBe(new Uri("http://localhost:5000/"));
    }

    [Theory]
    [InlineData("http://localhost:5100", "http://localhost:5100/")]
    [InlineData("http://*:5000", "http://localhost:5000/")]
    [InlineData("http://+:6000", "http://localhost:6000/")]
    [InlineData("http://0.0.0.0:5000", "http://localhost:5000/")]
    [InlineData("http://[::]:5000", "http://localhost:5000/")]
    [InlineData("HTTP://127.0.0.1:5099/", "http://127.0.0.1:5099/")]
    [InlineData("https://scale-pc:8443", "https://scale-pc:8443/")]
    [InlineData("http://*", "http://localhost/")]
    [InlineData("nonsense; http://*:5001 ;http://localhost:5002", "http://localhost:5001/")]
    public void Urls_are_mapped_to_a_browsable_address(string urls, string expected)
    {
        DesktopSettings.ParseUrls(urls).ShouldBe(new Uri(expected));
    }

    [Fact]
    public void Urls_key_of_the_file_is_used()
    {
        DesktopSettings.Parse("{\"Urls\":\"http://*:5088;https://*:5089\"}", []).Url.ShouldBe(new Uri("http://localhost:5088/"));
    }

    [Theory]
    [InlineData("--url", "http://localhost:5099")]
    [InlineData("--URL=http://localhost:5099", null)]
    public void Command_line_overrides_the_file(string first, string? second)
    {
        string[] args = second is null ? [first] : [first, second];

        DesktopSettings.Parse("{\"Urls\":\"http://*:5088\"}", args).Url.ShouldBe(new Uri("http://localhost:5099/"));
    }

    [Theory]
    [InlineData("--full-screen")]
    [InlineData("--FULL-SCREEN")]
    public void Full_screen_can_come_from_the_command_line_without_the_file(string flag)
    {
        DesktopSettings.Parse(null, [flag]).FullScreen.ShouldBeTrue();
        DesktopSettings.Parse(null, []).FullScreen.ShouldBeFalse();
    }

    [Fact]
    public void Invalid_command_line_address_falls_back_to_the_file()
    {
        DesktopSettings.Parse("{\"Urls\":\"http://*:5088\"}", ["--url", "garbage"]).Url.ShouldBe(new Uri("http://localhost:5088/"));
    }

    [Theory]
    [InlineData("{\"Vyxum\":{\"Desktop\":{\"FullScreen\":true}}}", true)]
    [InlineData("{\"Vyxum:Desktop:FullScreen\":true}", true)]
    [InlineData("{\"Vyxum\":{\"Desktop\":{\"FullScreen\":\"true\"}}}", true)]
    [InlineData("{\"Vyxum\":{\"Desktop\":{\"FullScreen\":false}}}", false)]
    [InlineData("{\"Vyxum\":{}}", false)]
    public void Full_screen_setting_is_read(string json, bool expected)
    {
        DesktopSettings.Parse(json, []).FullScreen.ShouldBe(expected);
    }

    [Fact]
    public void Load_reads_the_file_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{\"Urls\":\"http://*:5321\",\"Vyxum\":{\"Desktop\":{\"FullScreen\":true}}}");
        try
        {
            var settings = DesktopSettings.Load([], path);

            settings.Url.ShouldBe(new Uri("http://localhost:5321/"));
            settings.FullScreen.ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("http://localhost:5000/a", "http://LOCALHOST:5000/b", true)]
    [InlineData("http://localhost:5000", "http://localhost:5001", false)]
    [InlineData("http://localhost:5000", "https://localhost:5000", false)]
    [InlineData("http://localhost:5000", "http://example.com", false)]
    public void Same_origin_compares_scheme_host_and_port(string a, string b, bool expected)
    {
        DesktopSettings.IsSameOrigin(new Uri(a), new Uri(b)).ShouldBe(expected);
    }
}
