using System;
using System.Text.Json.Nodes;
using Wallpepper.Rendering;
using Wallpepper.Settings;
using Wallpepper.Setup;
using Wallpepper.Web;
using Xunit;

namespace Wallpepper.Tests;

public class SetupAndWebTests
{
    private static readonly string[] Hosts = { "github.com", "objects.githubusercontent.com" };

    [Theory]
    [InlineData("v0.4.0", "0.3.0", "0.4.0")]
    [InlineData("0.3.1", "0.3.0", "0.3.1")]
    [InlineData("v0.3", "0.3.0", null)]
    [InlineData("v0.2.9", "0.3.0", null)]
    [InlineData("nightly", "0.3.0", null)]
    public void OnlyANewerReleaseIsAnUpdate(string tag, string current, string? expected)
    {
        Assert.Equal(expected, Updater.NewerVersion(tag, Version.Parse(current))?.ToString(3));
    }

    [Fact]
    public void TheChecksumIsReadFromTheSumsFile()
    {
        string hash = new string('a', 64);
        string sums = $"{new string('b', 64)}  other.zip\n{hash} *Wallpepper.exe\r\nshort  Wallpepper.exe\n";
        Assert.Equal(hash, Updater.ChecksumFor(sums, "Wallpepper.exe"));
        Assert.Null(Updater.ChecksumFor(sums, "missing.exe"));
    }

    [Fact]
    public void DownloadsOnlyComeFromThisRepositoryOverHttps()
    {
        const string Prefix = "/DennisWalkewitz/Wallpepper/releases/download/";
        Assert.True(Updater.IsAllowed(new Uri("https://github.com/DennisWalkewitz/Wallpepper/releases/download/v1/Wallpepper.exe"), Hosts, Prefix));
        Assert.True(Updater.IsAllowed(new Uri("https://objects.githubusercontent.com/some/signed/path"), Hosts, Prefix));
        Assert.False(Updater.IsAllowed(new Uri("https://github.com/someone/else/releases/download/v1/Wallpepper.exe"), Hosts, Prefix));
        Assert.False(Updater.IsAllowed(new Uri("http://github.com/DennisWalkewitz/Wallpepper/releases/download/v1/Wallpepper.exe"), Hosts, Prefix));
        Assert.False(Updater.IsAllowed(new Uri("https://example.com/Wallpepper.exe"), Hosts, Prefix));
    }

    [Fact]
    public void AWebWallpaperNeedsAnEncryptedAddress()
    {
        WebInfo good = WebInfo.Parse("""{ "name": "Radio", "url": "https://example.com/app", "tags": ["web"], "audio": true }""");
        Assert.Null(good.Error);
        Assert.Equal("example.com", good.Host);
        Assert.True(good.Audio);
        Assert.NotNull(WebInfo.Parse("""{ "url": "http://example.com" }""").Error);
        Assert.NotNull(WebInfo.Parse("""{ "url": "file:///C:/x.html" }""").Error);
        Assert.NotNull(WebInfo.Parse("not json").Error);
        Assert.Null(WebInfo.Parse("""{ "url": "http://localhost:8080/" } // a page served by this PC""").Error);
    }

    [Fact]
    public void APageStaysOnItsOwnSite()
    {
        Assert.True(WebViewPolicy.SameSite("flowbeat.walksoft.online", "flowbeat.walksoft.online"));
        Assert.True(WebViewPolicy.SameSite("cdn.flowbeat.walksoft.online", "flowbeat.walksoft.online"));
        Assert.False(WebViewPolicy.SameSite("evil.example", "flowbeat.walksoft.online"));
    }

    [Fact]
    public void TerminalSettingsKeepTheirCommentsWhenOnlyTheValueChanges()
    {
        const string Before = """
            {
                // my profiles
                "profiles": { "defaults": { "experimental.pixelShaderPath": "C:\\old.hlsl", "font": { "size": 11 } }, "list": [] }
            }
            """;
        string after = TerminalIntegration.SetShader(Before, @"C:\new shader.hlsl", out bool edited);
        Assert.True(edited);
        Assert.Contains("// my profiles", after);
        Assert.Contains("\"experimental.pixelShaderPath\": \"C:\\\\new shader.hlsl\"", after);
        Assert.Contains("\"size\": 11", after);
    }

    [Fact]
    public void TerminalProfilesInTheOldListFormAreWrappedNotLost()
    {
        const string Before = """{ "profiles": [ { "name": "PowerShell" }, { "name": "Ubuntu" } ] }""";
        string after = TerminalIntegration.SetShader(Before, @"C:\s.hlsl", out bool edited);
        Assert.False(edited);
        JsonNode profiles = JsonNode.Parse(after)!["profiles"]!;
        Assert.Equal(2, profiles["list"]!.AsArray().Count);
        Assert.Equal(@"C:\s.hlsl", profiles["defaults"]![TerminalIntegration.ShaderSetting]!.GetValue<string>());
    }

    [Fact]
    public void ADeletedItemLeavesNoTraceInTheSettings()
    {
        var config = new Config { Wallpaper = "Rain.hlsl" };
        config.Favourites.Add("rain.hlsl");
        config.Costs["rain.hlsl"] = 3;
        config.WallpaperByDisplay["display 2"] = "RAIN.hlsl";
        config.Overrides["rain.hlsl"] = new() { ["SPEED"] = "2.0" };
        config.Forget("rain.hlsl");
        Assert.Null(config.Wallpaper);
        Assert.Empty(config.Favourites);
        Assert.Empty(config.Costs);
        Assert.Empty(config.WallpaperByDisplay);
        Assert.Empty(config.Overrides);
    }

    [Fact]
    public void ABenchmarkResultSurvivesItsJson()
    {
        var result = new BenchResult
        {
            Mode = "quick",
            Gpu = "Test GPU",
            Width = 1280,
            Height = 720,
            Frames = 41,
            Score = 151,
            MPixelsPerSecond = 7.49,
        };
        BenchResult? read = BenchResult.Parse(result.ToJson(oneLine: true));
        Assert.NotNull(read);
        Assert.Equal(151, read!.Score);
        Assert.Equal("provisional, quick", read.Label);
        Assert.Contains("\"mpixels_per_second\":7.49", result.ToJson(oneLine: true));
        Assert.Null(BenchResult.Parse("garbage"));
    }
}
