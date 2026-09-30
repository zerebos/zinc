using Phosphor.Terminal;

namespace Phosphor.Tests;

public sealed class ThemeLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "phosphor-themes-" + Guid.NewGuid().ToString("N"));
    private readonly ThemeLibrary _library;

    public ThemeLibraryTests()
    {
        var user = Path.Combine(_root, "user");
        var bundled = Path.Combine(_root, "bundled");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "Dracula"), "background = #282a36\nforeground = #f8f8f2\n");
        File.WriteAllText(Path.Combine(bundled, "0x96f"), "background = #262427\n");
        File.WriteAllText(Path.Combine(user, "dracula"), "background = #000000\n"); // the user's own, same name
        File.WriteAllText(Path.Combine(user, "Mine"), "# a comment\nbackground = #101010\nselection-background = #ffffff\n");
        _library = new ThemeLibrary(user, bundled);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ListsEachNameOnceSorted() =>
        Assert.Equal(["0x96f", "dracula", "Mine"], _library.Names());

    [Fact]
    public void TheUsersSchemeWinsAndNamesMatchInAnyCase()
    {
        var applied = _library.Apply(new Config(), "DRACULA");
        Assert.NotNull(applied);
        Assert.Equal(0x00, applied.Theme.Background.R); // the user's black, not Dracula's
        Assert.Equal(0x26, _library.Apply(new Config(), "0x96F")!.Theme.Background.R);
    }

    [Fact]
    public void AppliesOnlyColorsAndRejectsWhatIsNotAScheme()
    {
        Assert.Equal(0x10, _library.Apply(new Config(), "Mine")!.Theme.Background.R);
        Assert.Null(_library.Apply(new Config(), "Nope"));
        Assert.Null(_library.Apply(new Config(), "../user/Mine"));
        Assert.Null(_library.Apply(new Config(), ""));
    }
}

public class BaseColorTests
{
    [Fact]
    public void ASchemesOwnColorsWinTheRestAreLibghosttys()
    {
        var builtin = TerminalSession.BuiltinBaseColors;
        Assert.Equal(16, builtin.Count);
        Assert.Contains(builtin, c => c.R + c.G + c.B > 0); // read from libghostty, not left zeroed

        var theme = new Config().Apply("palette", "1=#ff0000")!.Theme;
        var colors = theme.BaseColors;
        Assert.Equal((255, 0, 0), (colors[1].R, colors[1].G, colors[1].B));
        Assert.Equal((builtin[2].R, builtin[2].G, builtin[2].B), (colors[2].R, colors[2].G, colors[2].B));
    }
}
