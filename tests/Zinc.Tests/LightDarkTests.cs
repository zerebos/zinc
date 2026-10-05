using System.Text;
using Zinc.Ghostty;
using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>Light/dark theme pairs ("light:X,dark:Y") and telling programs which is in force.</summary>
public class LightDarkTests
{
    private static string Themes()
    {
        var dir = Directory.CreateTempSubdirectory("zinc-themes-").FullName;
        File.WriteAllText(Path.Combine(dir, "Paper"), "background = #fafafa\nforeground = #222222\n");
        File.WriteAllText(Path.Combine(dir, "Night"), "background = #101418\nforeground = #dddddd\n");
        return dir;
    }

    private static ConfigFile Read(string toml, ColorScheme appearance) =>
        ConfigFile.Parse(toml, "config.toml", bundledThemes: Themes(), appearance: appearance);

    [Theory]
    [InlineData("light:Paper,dark:Night", "Paper", "Night")]
    [InlineData("dark:Night, light:Paper", "Paper", "Night")]
    [InlineData("light:Paper", "Paper", "Paper")]          // one side: used for both
    [InlineData(" light : Solarized Light , dark : Solarized Dark ", "Solarized Light", "Solarized Dark")]
    public void ReadsPairsAsGhosttyWritesThem(string value, string light, string dark)
    {
        Assert.True(ThemeLibrary.TryParsePair(value, out var l, out var d));
        Assert.Equal((light, dark), (l, d));
    }

    [Theory]
    [InlineData("Catppuccin Mocha")]
    [InlineData("0x96f")]
    public void PlainNamesAreNotPairs(string value) => Assert.False(ThemeLibrary.TryParsePair(value, out _, out _));

    [Fact]
    public void APairFollowsTheAppearance()
    {
        const string toml = "[colors]\ntheme = \"light:Paper,dark:Night\"\n";
        var light = Read(toml, ColorScheme.Light);
        var dark = Read(toml, ColorScheme.Dark);
        Assert.Empty(light.Warnings);
        Assert.True(light.FollowsAppearance);
        Assert.Equal(0xfa, light.Global.Theme.Background.R);
        Assert.Equal(0x10, dark.Global.Theme.Background.R);
        Assert.False(light.Global.IsDark);
        Assert.True(dark.Global.IsDark);
    }

    [Fact]
    public void ColorsWrittenNextToAPairStillWin()
    {
        var config = Read("[colors]\ntheme = \"light:Paper,dark:Night\"\nforeground = \"#ff0000\"\n", ColorScheme.Light);
        Assert.Equal(0xff, config.Global.Theme.Foreground.R);
        Assert.Equal(0xfa, config.Global.Theme.Background.R);
    }

    [Fact]
    public void ProfilesCanHaveTheirOwnPair()
    {
        var config = Read("[profile.x]\ncommand = \"cmd.exe\"\ncolors.theme = \"light:Paper,dark:Night\"\n", ColorScheme.Light);
        Assert.True(config.FollowsAppearance);
        Assert.Equal(0xfa, config.Profiles.Single(p => p.Id == "x").Config.Theme.Background.R);
    }

    [Fact]
    public void AMissingSideIsReportedWhateverTheMode()
    {
        var config = Read("[colors]\ntheme = \"light:Paper,dark:Nope\"\n", ColorScheme.Light);
        Assert.Contains(config.Warnings, w => w.Contains("no color scheme named \"Nope\""));
    }

    [Fact]
    public void AScheme_WithoutAPair_IsDarkByItsBackground()
    {
        Assert.False(Read("[colors]\ntheme = \"Paper\"\n", ColorScheme.Dark).Global.IsDark);
        Assert.True(Read("[colors]\ntheme = \"Night\"\n", ColorScheme.Light).Global.IsDark);
        Assert.False(Read("[colors]\ntheme = \"Night\"\n", ColorScheme.Light).FollowsAppearance);
    }

    private static string Joined(List<byte[]> replies) => string.Concat(replies.Select(Encoding.UTF8.GetString));

    [Fact]
    public void ProgramsAreToldLightOrDark()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;

        session.WriteToScreen("\x1b[?996n");
        Assert.Contains("\x1b[?997;1n", Joined(replies)); // dark

        session.SetColorScheme(dark: false);
        replies.Clear();
        session.WriteToScreen("\x1b[?996n");
        Assert.Contains("\x1b[?997;2n", Joined(replies)); // light
    }

    [Fact]
    public void ProgramsThatSubscribeHearOfChanges()
    {
        using var session = new TerminalSession(80, 24, TerminalTheme.Default);
        var replies = new List<byte[]>();
        session.PtyReplyObserver = replies.Add;

        session.SetColorScheme(dark: false);
        Assert.Empty(replies); // not subscribed: nothing unasked

        session.WriteToScreen("\x1b[?2031h");
        replies.Clear();
        session.SetColorScheme(dark: true);
        Assert.Equal("\x1b[?997;1n", Joined(replies));
        session.SetColorScheme(dark: true);
        Assert.Equal("\x1b[?997;1n", Joined(replies)); // no change, no report
    }
}
