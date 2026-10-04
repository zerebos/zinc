using Zinc.Shaders;

namespace Zinc.Tests;

/// <summary>Custom shaders: the [shaders] settings, Ghostty's prefix, glslang's errors in the file's own lines, and GLSL to HLSL.</summary>
public class ShaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("zinc-shaders-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConfigFile Parse(string text) => ConfigFile.Parse(text, Path.Combine(_dir, "config.toml"), _dir);

    // Ghostty's src/renderer/shaders/test_shadertoy_crt.glsl, shortened (Shadertoy's style: texture(), iTime).
    private const string Crt = """
        vec2 curve(vec2 uv)
        {
            uv = (uv - 0.5) * 2.0;
            uv *= 1.1;
            uv.x *= 1.0 + pow((abs(uv.y) / 5.0), 2.0);
            uv.y *= 1.0 + pow((abs(uv.x) / 4.0), 2.0);
            return (uv / 2.0) + 0.5;
        }

        void mainImage(out vec4 fragColor, in vec2 fragCoord)
        {
            vec2 uv = curve(fragCoord.xy / iResolution.xy);
            vec3 col = texture(iChannel0, uv).xyz;
            col *= 0.9 + 0.1 * sin(10.0 * iTime + uv.y * 1000.0);
            fragColor = vec4(col, 1.0);
        }
        """;

    [Fact]
    public void FilesResolveFromTheConfigFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Parse("""
            [shaders]
            files = ["crt.glsl", "sub/bloom.glsl", "~/x.glsl", 'C:\abs\y.glsl']
            """);
        Assert.Empty(config.Warnings);
        Assert.Equal([Path.Combine(_dir, "crt.glsl"), Path.Combine(_dir, "sub", "bloom.glsl"), Path.Combine(home, "x.glsl"), @"C:\abs\y.glsl"],
            config.Global.ShaderFiles);
        Assert.Equal(ShaderAnimation.Focused, config.Global.ShaderAnimation);
    }

    [Fact]
    public void OneFileAsAString()
    {
        var config = Parse("[shaders]\nfiles = \"crt.glsl\"");
        Assert.Equal([Path.Combine(_dir, "crt.glsl")], config.Global.ShaderFiles);
    }

    [Fact]
    public void NoShadersByDefault()
    {
        var config = Parse("");
        Assert.Empty(config.Global.ShaderFiles);
        Assert.Equal("", config.Global.Shaders);
    }

    [Theory]
    [InlineData("true", ShaderAnimation.Focused)]
    [InlineData("false", ShaderAnimation.Never)]
    [InlineData("\"always\"", ShaderAnimation.Always)]
    public void Animation(string value, ShaderAnimation expected)
    {
        var config = Parse($"[shaders]\nanimation = {value}");
        Assert.Empty(config.Warnings);
        Assert.Equal(expected, config.Global.ShaderAnimation);
    }

    [Fact]
    public void BadValuesWarn()
    {
        Assert.NotEmpty(Parse("[shaders]\nanimation = \"sometimes\"").Warnings);
        Assert.NotEmpty(Parse("[shaders]\nfiles = [1, 2]").Warnings);
    }

    [Fact]
    public void ProfilesHaveTheirOwn()
    {
        var config = Parse("""
            [shaders]
            files = ["crt.glsl"]

            [profile.plain]
            name = "Plain"
            [profile.plain.shaders]
            files = []

            [profile.retro]
            name = "Retro"
            [profile.retro.shaders]
            files = ["green.glsl", "crt.glsl"]
            animation = false
            """);
        Assert.Empty(config.Warnings);
        Assert.Empty(config.Profiles.Single(p => p.Id == "plain").Config.ShaderFiles);
        var retro = config.Profiles.Single(p => p.Id == "retro").Config;
        Assert.Equal([Path.Combine(_dir, "green.glsl"), Path.Combine(_dir, "crt.glsl")], retro.ShaderFiles);
        Assert.Equal(ShaderAnimation.Never, retro.ShaderAnimation);
    }

    [Fact]
    public void TimeBasedShadersAnimate()
    {
        Assert.True(ShaderSource.UsesTime(Crt));
        Assert.True(ShaderSource.UsesTime("void mainImage(out vec4 c, in vec2 p) { c = vec4(float(iFrame)); }"));
        Assert.False(ShaderSource.UsesTime("void mainImage(out vec4 c, in vec2 p) { c = texture(iChannel0, p / iResolution.xy); }"));
        // Only mentioned in a comment, or as part of a longer name.
        Assert.False(ShaderSource.UsesTime("// uses iTime? no\n/* iFrame */ void mainImage(out vec4 c, in vec2 p) { float iTimeless = 1.0; c = vec4(iTimeless); }"));
    }

    [Fact]
    public void GhosttysShadersTranslate()
    {
        Assert.True(ShaderTranslator.TryTranslate(Crt, "crt.glsl", out var hlsl, out var error), error);
        Assert.Contains("SV_Target", hlsl);
        Assert.Contains("iChannel0", hlsl);
        // The uniform block keeps Ghostty's std140 layout: ShaderUniforms writes to these offsets.
        Assert.Contains("iPalette[256] : packoffset(c19)", hlsl);
        Assert.Contains("iSelectionBackgroundColor : packoffset(c280)", hlsl);
    }

    [Fact]
    public void VariablesStartAtZero()
    {
        // GLSL allows a variable assigned on only some paths; Direct3D's compiler refuses it (X4000, matrix-hallway.glsl).
        Assert.True(ShaderTranslator.TryTranslate("void mainImage(out vec4 c, in vec2 p) {\n float x;\n if (p.x > 1.0) x = 1.0;\n c = vec4(x);\n}\n",
            "paths.glsl", out var hlsl, out var error), error);
        Assert.Contains("float x = 0.0f;", hlsl);
    }

    [Fact]
    public void UniformOffsetsMatchTheTranslation()
    {
        // SPIRV-Cross writes the whole block with each member's packoffset: "float iTime : packoffset(c0.w);".
        Assert.True(ShaderTranslator.TryTranslate(Crt, "crt.glsl", out var hlsl, out var error), error);
        var found = System.Text.RegularExpressions.Regex.Matches(hlsl, @"_\d+_(i\w+)(?:\[\d+\])?\s*:\s*packoffset\(c(\d+)(?:\.([xyzw]))?\)")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value) * 16 + "xyzw".IndexOf(m.Groups[3].Success ? m.Groups[3].Value : "x") * 4);
        Assert.Equal(ShaderUniforms.Offsets.Count, found.Count);
        foreach (var (name, offset) in ShaderUniforms.Offsets)
        {
            Assert.True(found.TryGetValue(name, out var actual), name);
            Assert.Equal(actual, offset);
        }
    }

    [Fact]
    public void UniformValues()
    {
        var u = new ShaderUniforms();
        u.SetResolution(800, 600);
        u.SetTime(2.5f, 0.02f, 7);
        u.SetDate(new DateTime(2026, 10, 3, 1, 0, 30));
        Assert.Equal(600, u.Float("iResolution", 1));
        Assert.Equal(800, u.Float("iChannelResolution"));
        Assert.Equal(2.5f, u.Float("iTime"));
        Assert.Equal(50, u.Float("iFrameRate"), 3);
        Assert.Equal(7, BitConverter.ToInt32(u.Bytes, ShaderUniforms.Offsets["iFrame"]));
        Assert.Equal(9, u.Float("iDate", 1)); // October, counted from 0
        Assert.Equal(3630, u.Float("iDate", 3));
    }

    [Fact]
    public void ErrorsUseTheFilesLines()
    {
        Assert.False(ShaderTranslator.TryTranslate("void mainImage(out vec4 c, in vec2 p) {\n  c = vec4(1.0)\n  foo = 2;\n}\n", @"C:\s\bad.glsl",
            out _, out var syntax));
        Assert.Equal("bad.glsl, line 3: syntax error, unexpected IDENTIFIER, expecting COMMA or SEMICOLON", syntax);

        Assert.False(ShaderTranslator.TryTranslate("void mainImage(out vec4 c, in vec2 p) {\n  c = bar;\n}\n", "bad.glsl", out _, out var undeclared));
        Assert.StartsWith("bad.glsl, line 2: 'bar': undeclared identifier\n", undeclared);
    }

    [Fact]
    public void NoMainImage()
    {
        Assert.False(ShaderTranslator.TryTranslate("vec2 curve(vec2 uv) { return uv; }\n", "invalid.glsl", out _, out var error));
        Assert.StartsWith("invalid.glsl: no mainImage function", error);
    }

    [Fact]
    public void AnotherShaderAfterAFailure()
    {
        Assert.False(ShaderTranslator.TryTranslate("nonsense", "a.glsl", out _, out _));
        Assert.True(ShaderTranslator.TryTranslate(Crt, "crt.glsl", out _, out var error), error);
    }
}
