using Zinc.Settings;

namespace Zinc.Tests;

public sealed class SchemeCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zinc-scheme-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly string _user;
    private readonly ThemeLibrary _library;

    public SchemeCatalogTests()
    {
        _user = Path.Combine(_root, "user");
        var bundled = Path.Combine(_root, "bundled");
        Directory.CreateDirectory(_user);
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "Dracula"), "background = #282a36\nforeground = #f8f8f2\n");
        File.WriteAllText(Path.Combine(bundled, "Paper"), "background = #f2eede\nforeground = #000000\n");
        File.WriteAllText(Path.Combine(bundled, "Plain"), "foreground = #ffffff\n"); // no background: Zinc's, dark
        File.WriteAllText(Path.Combine(_user, "dracula"), "background = #fafafa\n"); // your copy, made light
        File.WriteAllText(Path.Combine(_user, "Mine"), "# mine\nbackground = #101010\n");
        _library = new ThemeLibrary(_user, bundled);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ListsEverySchemeWithItsLightnessAndWhoseItIs() =>
        Assert.Equal(
            [
                new SchemeEntry("dracula", IsDark: false, Yours: true, ReplacesBundled: true),
                new SchemeEntry("Mine", IsDark: true, Yours: true, ReplacesBundled: false),
                new SchemeEntry("Paper", IsDark: false, Yours: false, ReplacesBundled: false),
                new SchemeEntry("Plain", IsDark: true, Yours: false, ReplacesBundled: false),
            ],
            SchemeCatalog.List(_library));

    [Fact]
    public void FiltersByKindAndName()
    {
        var all = SchemeCatalog.List(_library);
        string[] Names(SchemeKind kind, string query) => [.. SchemeCatalog.Filter(all, kind, query).Select(e => e.Name)];
        Assert.Equal(["Mine", "Plain"], Names(SchemeKind.Dark, ""));
        Assert.Equal(["dracula", "Paper"], Names(SchemeKind.Light, ""));
        Assert.Equal(["dracula", "Mine"], Names(SchemeKind.Yours, ""));
        Assert.Equal(["Paper", "Plain"], Names(SchemeKind.All, "p"));
        Assert.Equal(["dracula"], Names(SchemeKind.All, " DRAC "));
        Assert.Empty(Names(SchemeKind.Dark, "paper"));
    }

    [Fact]
    public void ReadsAFileAgainOnceItChanges()
    {
        var mine = Path.Combine(_user, "Mine");
        Assert.True(SchemeCatalog.IsDark(mine));
        File.WriteAllText(mine, "background = #ffffff\n");
        File.SetLastWriteTimeUtc(mine, DateTime.UtcNow.AddMinutes(1));
        Assert.False(SchemeCatalog.IsDark(mine));
    }
}
