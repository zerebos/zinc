using Zinc.Shaders;

namespace Zinc.Tests;

[Collection("ShaderPaths.BundledFolder")] // tests that point it elsewhere take turns
public class ShaderLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zinc-shaderlib-" + Guid.NewGuid().ToString("N"));
    private readonly string _bundledBefore = ShaderPaths.BundledFolder;

    public ShaderLibraryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bundled"));
        Directory.CreateDirectory(Path.Combine(_root, "config", "shaders"));
        Directory.CreateDirectory(Path.Combine(_root, "pack"));
        File.WriteAllText(Path.Combine(_root, "bundled", "crt.glsl"), "// Zinc: crt. A curved screen. More text.\nvoid mainImage(out vec4 c, in vec2 p) { c = vec4(iTime); }");
        File.WriteAllText(Path.Combine(_root, "config", "shaders", "mine.glsl"), "// My own glow\nvoid mainImage(out vec4 c, in vec2 p) { c = vec4(0); }");
        File.WriteAllText(Path.Combine(_root, "pack", "bloom.glsl"), "// ---------\n// MIT License\n// Soft bloom around bright text\nvoid mainImage(out vec4 c, in vec2 p) { c = vec4(0); }");
        File.WriteAllText(Path.Combine(_root, "pack", "plain.frag"), "void mainImage(out vec4 c, in vec2 p) { c = vec4(iTime); }");
        File.WriteAllText(Path.Combine(_root, "pack", "README.md"), "# not a shader");
        File.WriteAllText(Path.Combine(_root, "config", "config.toml"), "");
        ShaderPaths.BundledFolder = Path.Combine(_root, "bundled");
    }

    public void Dispose()
    {
        ShaderPaths.BundledFolder = _bundledBefore;
        Directory.Delete(_root, recursive: true);
    }

    private string Config => Path.Combine(_root, "config", "config.toml");

    [Fact]
    public void ListsZincsYoursAndAddedFolders()
    {
        var sections = ShaderLibrary.List(Config, [Path.Combine(_root, "pack"), Path.Combine(_root, "gone")]);
        Assert.Equal([ShaderLibrary.ZincTitle, ShaderLibrary.YoursTitle, "pack", "gone"], sections.Select(s => s.Title));

        var crt = sections[0].Shaders.Single();
        Assert.Equal(("crt", "crt", "A curved screen.", true), (crt.Name, crt.Written, crt.Description, crt.UsesTime));

        var mine = sections[1].Shaders.Single();
        Assert.Equal(("mine", "My own glow", false), (mine.Written, mine.Description, mine.UsesTime));

        Assert.Equal(["bloom", "plain"], sections[2].Shaders.Select(s => s.Name)); // .glsl and .frag; not README.md
        Assert.Equal("Soft bloom around bright text", sections[2].Shaders[0].Description);
        Assert.Null(sections[2].Shaders[1].Description);
        Assert.True(sections[2].Shaders[1].UsesTime);
        Assert.Equal(ShaderPaths.Resolve(sections[2].Shaders[0].Written, Config), sections[2].Shaders[0].File);

        Assert.True(sections[3].Missing);
        Assert.Empty(sections[3].Shaders);
    }

    [Theory]
    [InlineData("// A CRT effect\nvoid main(){}", "A CRT effect")]
    [InlineData("/*\n * Retro scanlines, by someone\n */\nvoid main(){}", "Retro scanlines, by someone")]
    [InlineData("// https://www.shadertoy.com/view/abc\n// Animated starfield\n", "Animated starfield")]
    [InlineData("void mainImage(){}\n// too late", null)]
    [InlineData("// ====\n", null)]
    public void DescribesFromTheFirstComment(string text, string? description)
    {
        Assert.Equal(description, ShaderLibrary.Describe(text));
    }

    [Fact]
    public void FoldersResolveLikeFiles()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, "dev", "pack"), ShaderPaths.ResolveFolder("~/dev/pack/", Config));
        Assert.Equal(Path.Combine(_root, "config", "packs"), ShaderPaths.ResolveFolder("packs", Config)); // not a shader name
    }

    [Fact]
    public void TheConfigReadsFolders()
    {
        var file = ConfigFile.Parse("[shaders]\nfolders = [\"~/dev/ghostty-shaders\", \"C:/packs\"]", Config);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal([Path.Combine(home, "dev", "ghostty-shaders"), @"C:\packs"], file.Global.ShaderFolders);
        Assert.Empty(file.Warnings);
    }
}
