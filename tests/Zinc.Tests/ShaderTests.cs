using Zinc.Shaders;

namespace Zinc.Tests;

/// <summary>Custom shaders: the [shaders] settings, Ghostty's prefix, glslang's errors in the file's own lines, and GLSL to HLSL.</summary>
[Collection("ShaderPaths.BundledFolder")] // tests that point it elsewhere take turns
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
    public void ShaderListsCompareByValue()
    {
        ValueList<string> one = new(["a.glsl", "b.glsl"]), same = new(["a.glsl", "b.glsl"]), other = new(["b.glsl", "a.glsl"]);
        Assert.True(one == same);
        Assert.True(one != other);
        Assert.Equal(new Config { ShaderFiles = one }, new Config { ShaderFiles = same });
    }

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
    public void SettingsWritePathsShort()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Path.Combine(_dir, "config.toml");
        Assert.Equal("shaders/crt.glsl", ShaderPaths.ForConfig(Path.Combine(_dir, "shaders", "crt.glsl"), config));
        Assert.Equal("~/dev/ghostty-shaders/bloom.glsl", ShaderPaths.ForConfig(Path.Combine(home, "dev", "ghostty-shaders", "bloom.glsl"), config));
        Assert.Equal("Z:/fx/crt.glsl", ShaderPaths.ForConfig(@"Z:\fx\crt.glsl", config));
        // What's written reads back as the same file.
        foreach (var full in new[] { Path.Combine(_dir, "shaders", "crt.glsl"), Path.Combine(home, "x.glsl"), @"Z:\fx\crt.glsl" })
        {
            Assert.Equal(full, ShaderPaths.Resolve(ShaderPaths.ForConfig(full, config), config));
        }

        Assert.Equal(["a.glsl", "b.glsl"], ShaderPaths.Listed(new List<object> { "a.glsl", "b.glsl" }));
        Assert.Equal(["a.glsl"], ShaderPaths.Listed("a.glsl"));
        Assert.Empty(ShaderPaths.Listed(null));
    }

    [Fact]
    public void NamesFindYoursThenZincs()
    {
        var bundled = Directory.CreateDirectory(Path.Combine(_dir, "bundled")).FullName;
        File.WriteAllText(Path.Combine(bundled, "crt.glsl"), "// Zinc: crt. An old monitor: curved glass. More.\n");
        File.WriteAllText(Path.Combine(bundled, "glow.glsl"), "// Zinc: glow. Soft light.\n");
        Directory.CreateDirectory(Path.Combine(_dir, "shaders"));
        File.WriteAllText(Path.Combine(_dir, "shaders", "glow.glsl"), "// mine\n");
        var was = ShaderPaths.BundledFolder;
        ShaderPaths.BundledFolder = bundled;
        try
        {
            var config = Parse("[shaders]\nfiles = [\"crt\", \"glow\", \"nope\", \"crt.glsl\"]");
            Assert.Equal([Path.Combine(bundled, "crt.glsl"), Path.Combine(_dir, "shaders", "glow.glsl"), Path.Combine(bundled, "nope.glsl"),
                Path.Combine(_dir, "crt.glsl")], config.Global.ShaderFiles);

            Assert.True(ShaderPaths.IsName("crt"));
            Assert.All(["crt.glsl", "./crt", "~/crt", "C:crt", "%X%"], w => Assert.False(ShaderPaths.IsName(w)));
            Assert.Equal("crt", ShaderPaths.ForConfig(Path.Combine(bundled, "crt.glsl"), Path.Combine(_dir, "config.toml")));
            Assert.Equal([("crt", "An old monitor: curved glass."), ("glow", "Soft light.")], ShaderPaths.Bundled());
        }
        finally
        {
            ShaderPaths.BundledFolder = was;
        }
    }

    /// <summary>The shaders that ship (assets/shaders): each compiles, says what it is, and only those meant to move use time.</summary>
    [Fact]
    public void ZincsOwnShaders()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Zinc.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        var files = Directory.GetFiles(Path.Combine(root, "assets", "shaders"), "*.glsl");
        Assert.NotEmpty(files);
        var animated = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var glsl = File.ReadAllText(file);
            Assert.StartsWith($"// Zinc: {name}. ", glsl);
            Assert.EndsWith(".", glsl.Split('\n')[0].TrimEnd()); // a whole sentence on the first line: Settings' menu shows it
            Assert.True(ShaderTranslator.TryTranslate(glsl, file, out _, out var error), error);
            if (ShaderSource.UsesTime(glsl))
            {
                animated.Add(name);
            }
        }

        // Everything else is drawn only when the terminal changes: no work at all while it's idle.
        Assert.Equal(["crt", "cursor-trail"], animated.Order());
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
    public void CursorMovesKeepThePreviousOne()
    {
        var u = new ShaderUniforms();
        u.SetCursor(10, 40, 8, 16, 1, 1, 1, 1, time: 1);
        u.SetCursor(10, 40, 8, 16, 1, 1, 1, 1, time: 2); // unchanged: no new change time
        Assert.Equal(1, u.Float("iTimeCursorChange"));

        u.SetCursor(18, 40, 8, 16, 1, 1, 1, 1, time: 3);
        Assert.Equal(18, u.Float("iCurrentCursor"));
        Assert.Equal(10, u.Float("iPreviousCursor"));
        Assert.Equal(40, u.Float("iPreviousCursor", 1));
        Assert.Equal(3, u.Float("iTimeCursorChange"));

        u.SetCursor(18, 40, 8, 16, 1, 0, 0, 1, time: 4); // a new color counts as a change too
        Assert.Equal(18, u.Float("iPreviousCursor"));
        Assert.Equal(1, u.Float("iPreviousCursorColor", 1));
        Assert.Equal(0, u.Float("iCurrentCursorColor", 1));
        Assert.Equal(4, u.Float("iTimeCursorChange"));
    }

    [Fact]
    public void CursorStyleAndColors()
    {
        var u = new ShaderUniforms();
        u.SetCursorStyle(ShaderUniforms.CursorStyle.Bar, visible: true);
        u.SetCursorStyle(ShaderUniforms.CursorStyle.BlockHollow, visible: false);
        int Int(string name) => BitConverter.ToInt32(u.Bytes, ShaderUniforms.Offsets[name]);
        Assert.Equal(1, Int("iCurrentCursorStyle"));
        Assert.Equal(2, Int("iPreviousCursorStyle"));
        Assert.Equal(0, Int("iCursorVisible"));

        var palette = new byte[256 * 3];
        palette[255 * 3 + 2] = 255;
        u.SetPalette(palette);
        Assert.Equal(1, BitConverter.ToSingle(u.Bytes, ShaderUniforms.Offsets["iPalette"] + 255 * 16 + 8));
        u.SetColor("iBackgroundColor", 255, 0, 51);
        Assert.Equal(0.2f, u.Float("iBackgroundColor", 2), 3);

        u.SetMouse(5, 6, 1, 2, down: false);
        Assert.Equal([5, 6, -1, -2], [u.Float("iMouse"), u.Float("iMouse", 1), u.Float("iMouse", 2), u.Float("iMouse", 3)]);
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
