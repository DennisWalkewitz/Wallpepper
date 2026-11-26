using System.Collections.Generic;
using System.Linq;
using Wallpepper.Library;
using Wallpepper.Rendering;
using Xunit;

namespace Wallpepper.Tests;

public class ShaderTextTests
{
    private const string Source = """
        // @name: Test Shader
        // @tags: Ocean, multi-mon, terminal-only
        // @light: SAMPLES=1, BOUNCES=2

        #define PI 3.14159
        #define BENCHMARK 0           // 1 = fixed reference workload
        #define SAMPLES 4             // paths per pixel (1..16): more = less grain
        #define FIELD_OF_VIEW 42.0    // vertical field of view in degrees (15..100)
        #define GRAIN_ANIMATE 1       // 1 = grain changes every frame
        // ---- colours (0..1 each) ----
        #define SKY_R 1.00            // oculus light colour: red
        #define SKY_G 0.84            // oculus light colour: green
        #define SKY_B 0.62            // oculus light colour: blue

        #define BH_POS_X 0.60         // shadow centre
        float4 main() : SV_TARGET { return 0; }
        """;

    [Fact]
    public void HeaderGivesNameNormalisedTagsAndLightSettings()
    {
        ShaderHeader header = ShaderHeader.Parse(Source);
        Assert.Equal("Test Shader", header.Name);
        Assert.Equal(new[] { "multimonitor", "terminalonly", "water" }, header.Tags.OrderBy(tag => tag));
        Assert.Equal("1", header.LightSettings["SAMPLES"]);
        Assert.Equal("2", header.LightSettings["BOUNCES"]);
    }

    [Fact]
    public void ConstantsAndTheBenchmarkSwitchAreNotSettings()
    {
        var names = ShaderParamLayout.Parse(Source).SelectMany(section => section.Params).Select(param => param.Name).ToList();
        Assert.DoesNotContain("PI", names);
        Assert.DoesNotContain("BENCHMARK", names);
        Assert.Equal(new[] { "SAMPLES", "FIELD_OF_VIEW", "GRAIN_ANIMATE", "SKY_R", "BH_POS_X" }, names);
    }

    [Fact]
    public void RangeUnitKindAndDescriptionComeFromTheShadersOwnLine()
    {
        var all = ShaderParamLayout.Parse(Source).SelectMany(section => section.Params).ToDictionary(param => param.Name);
        ShaderParam samples = all["SAMPLES"];
        Assert.Equal(ShaderParamKind.Number, samples.Kind);
        Assert.True(samples.IsInteger);
        Assert.Equal((1, 16), (samples.Minimum, samples.Maximum));
        Assert.StartsWith("Paths per pixel", samples.Description);

        ShaderParam fieldOfView = all["FIELD_OF_VIEW"];
        Assert.Equal((15, 100), (fieldOfView.Minimum, fieldOfView.Maximum));
        Assert.Equal("42.00°", fieldOfView.Display(42));

        Assert.Equal(ShaderParamKind.Toggle, all["GRAIN_ANIMATE"].Kind);

        ShaderParam sky = all["SKY_R"];
        Assert.Equal(ShaderParamKind.Colour, sky.Kind);
        Assert.Equal(new[] { "SKY_R", "SKY_G", "SKY_B" }, sky.Defines);
        Assert.Equal("Sky", sky.Label);
        Assert.Equal("Oculus light colour", sky.Description);

        ShaderParam position = all["BH_POS_X"];
        Assert.Equal("BH position X", position.Label);
        Assert.Equal((0, 1), (position.Minimum, position.Maximum));
    }

    [Fact]
    public void HeadingsAndBlankLinesMakeSections()
    {
        var sections = ShaderParamLayout.Parse(Source);
        Assert.Equal(3, sections.Count);
        Assert.Null(sections[0].Title);
        Assert.Equal("Colours", sections[1].Title);
        Assert.Equal(new[] { "BH_POS_X" }, sections[2].Params.Select(param => param.Name));
    }

    [Theory]
    [InlineData("TEXT_VEIL", "Text veil")]
    [InlineData("GLASS_IOR", "Glass IOR")]
    [InlineData("COLOUR_1", "Colour 1")]
    [InlineData("NUM_STARS", "Number of stars")]
    public void DefineNamesBecomeReadableLabels(string define, string label)
    {
        Assert.Equal(label, ShaderParamLayout.Humanise(define));
    }

    [Fact]
    public void OverridesRewriteTheLiteralAndKeepItsType()
    {
        var values = new Dictionary<string, string> { ["FIELD_OF_VIEW"] = "60", ["SAMPLES"] = "2", ["SKY_R"] = "system(\"x\")" };
        string changed = ShaderCompiler.ApplyOverrides(Source, values);
        Assert.Contains("#define FIELD_OF_VIEW 60.0 ", changed);
        Assert.Contains("#define SAMPLES 2 ", changed);
        Assert.Contains("#define SKY_R 1.00 ", changed);   // not a number: ignored
        Assert.Contains("// vertical field of view in degrees", changed);
    }

    [Fact]
    public void TheBenchmarkSwitchIsForcedOffForDisplay()
    {
        string on = Source.Replace("#define BENCHMARK 0", "#define BENCHMARK 1");
        Assert.Contains("#define BENCHMARK 0", ShaderCompiler.WithoutBenchmarkMode(on));
    }

    [Fact]
    public void ALiteralIsOnlyWrittenWhenItDiffersFromTheDefault()
    {
        ShaderParam samples = ShaderParamLayout.Parse(Source)[0].Params.First(param => param.Name == "SAMPLES");
        Assert.Null(samples.Literal(4));
        Assert.Equal("7", samples.Literal(7.2));
    }
}
