using Phosphor.Ghostty;

namespace Phosphor.Tests;

public class ConfigFileTests : IDisposable
{
    // A config folder of our own, for themes and includes; bundled themes in another.
    private readonly string _dir = Directory.CreateTempSubdirectory("phosphor-config-").FullName;
    private readonly string _bundled = Directory.CreateTempSubdirectory("phosphor-themes-").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Directory.Delete(_bundled, recursive: true);
    }

    private ConfigFile Parse(string text) => ConfigFile.Parse(text, Path.Combine(_dir, "config.toml"), _bundled);

    private void WriteFile(string relative, string text)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void NoProfilesGivesImplicitDefault()
    {
        var config = Parse("[font]\nsize = 12");
        var profile = Assert.Single(config.Profiles);
        Assert.Equal("default", profile.Id);
        Assert.Equal("Default", profile.Name);
        Assert.Same(profile, config.DefaultProfile);
        Assert.Equal(12f, profile.Config.FontSize);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void ProfilesInheritGlobalsAndOverride()
    {
        var config = Parse("""
            [font]
            family = "JetBrainsMono NF"
            size = 11

            [colors]
            background = "#181818"
            palette.1 = "#AC4242"

            [profile.fedora]
            name = "Fedora"
            command = 'wsl.exe -d Fedora'
            colors.palette.1 = "#FF7272"

            [profile.ubuntu]
            font = { family = "DepartureMono Nerd Font", size = 12 }
            """);

        Assert.Equal(["fedora", "ubuntu"], config.Profiles.Select(p => p.Id));
        Assert.Equal(["Fedora", "ubuntu"], config.Profiles.Select(p => p.Name)); // the id when unnamed
        var fedora = config.Profiles[0].Config;
        var ubuntu = config.Profiles[1].Config;

        Assert.Equal("JetBrainsMono NF", fedora.FontFamily);
        Assert.Equal(11f, fedora.FontSize);
        Assert.Equal("wsl.exe -d Fedora", fedora.Command);
        Assert.Equal(new GhosttyColorRgb(0xFF, 0x72, 0x72), fedora.Theme.Palette[1]);
        Assert.Equal(new GhosttyColorRgb(0x18, 0x18, 0x18), fedora.Theme.Background);

        Assert.Equal("DepartureMono Nerd Font", ubuntu.FontFamily);
        Assert.Equal(12f, ubuntu.FontSize);
        Assert.Null(ubuntu.Command);
        Assert.Equal(new GhosttyColorRgb(0xAC, 0x42, 0x42), ubuntu.Theme.Palette[1]);

        // A profile's overrides never leak into globals.
        Assert.Equal(new GhosttyColorRgb(0xAC, 0x42, 0x42), config.Global.Theme.Palette[1]);
        Assert.Null(config.Global.Command);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void QuotedIdsAndSubtables()
    {
        var config = Parse("""
            [profile."ssh - homelab"]
            command = "ssh.exe homelab"

            [profile."ssh - homelab".terminal]
            padding-x = 3
            """);
        var profile = Assert.Single(config.Profiles);
        Assert.Equal("ssh - homelab", profile.Id);
        Assert.Equal(4f, profile.Config.PaddingLeft);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void DefaultProfileIsChosenById()
    {
        var config = Parse("""
            default-profile = "ubuntu"
            [profile.fedora]
            command = "a"
            [profile.ubuntu]
            command = "b"
            """);
        Assert.Equal("ubuntu", config.DefaultProfile.Id);
    }

    [Fact]
    public void FirstVisibleProfileIsDefaultOtherwise()
    {
        var config = Parse("""
            [profile.old]
            hidden = true
            [profile.fedora]
            command = "a"
            """);
        Assert.Equal("fedora", config.DefaultProfile.Id);
        Assert.Equal(["fedora"], config.VisibleProfiles.Select(p => p.Id));
        Assert.Equal(2, config.Profiles.Count);
    }

    [Fact]
    public void MissingDefaultProfileWarnsAndFallsBack()
    {
        var config = Parse("default-profile = \"nope\"\n[profile.fedora]\ncommand = \"a\"");
        Assert.Equal("fedora", config.DefaultProfile.Id);
        Assert.Contains(config.Warnings, w => w.Contains("'nope'"));
    }

    [Fact]
    public void ProfilesAreFoundByIdOrOldName()
    {
        var config = Parse("[profile.fedora]\nname = \"Fedora\"");
        Assert.Equal("fedora", config.FindProfile("fedora")?.Id);
        Assert.Equal("fedora", config.FindProfile("Fedora")?.Id); // sessions saved before ids
        Assert.Null(config.FindProfile("ubuntu"));
    }

    [Fact]
    public void ProblemsAreReportedWithLineNumbers()
    {
        var config = Parse("""
            [font]
            size = "huge"
            colour = "red"

            [window]
            workspace-tabs = "left"

            [profile.a]
            window.workspace-tabs = "top"
            flavor = "mint"

            [keys]
            "ctrl+x" = "copy"
            this is not toml
            """);

        Assert.Contains("config.toml:2: ignored font.size = \"huge\"", config.Warnings);
        Assert.Contains("config.toml:3: unknown setting 'font.colour'", config.Warnings);
        Assert.Contains("config.toml:9: [window] applies to the whole app, not to one profile", config.Warnings);
        Assert.Contains("config.toml:10: unknown or invalid profile setting 'flavor'", config.Warnings);
        Assert.Contains(config.Warnings, w => w.StartsWith("config.toml:14:")); // a TOML syntax error
        Assert.DoesNotContain(config.Warnings, w => w.Contains("keys")); // reserved for keybindings

        // Everything valid still applies; the profile's app-wide setting doesn't.
        Assert.Equal(WorkspaceTabs.Left, config.Global.WorkspaceTabs);
    }

    [Fact]
    public void OnlyInvalidTomlCountsAsAnError()
    {
        Assert.False(Parse("[font]\nsize = \"huge\"\nnope = 1").HasErrors); // warnings, but valid TOML
        Assert.True(Parse("[font]\nsize = ").HasErrors);                     // a save mid-edit
        Assert.True(Parse("[font\nsize = 12").HasErrors);
    }

    [Fact]
    public void ThemesComeFromTheUserFolderThenBundled()
    {
        File.WriteAllText(Path.Combine(_bundled, "Alacritty"), "background = #181818\npalette = 1=#AC4242\nselection-background = #444444\n");
        File.WriteAllText(Path.Combine(_bundled, "0x96f"), "background = #262427\n");
        WriteFile("themes/0x96f", "# mine\nbackground = #111111\nforeground = #FCFCFA\n");

        var config = Parse("""
            [colors]
            theme = "alacritty"
            background = "#101010"

            [profile.fedora]
            colors.theme = "0x96f"
            """);

        // Explicit colors override the scheme, wherever they are written.
        Assert.Equal(new GhosttyColorRgb(0x10, 0x10, 0x10), config.Global.Theme.Background);
        Assert.Equal(new GhosttyColorRgb(0xAC, 0x42, 0x42), config.Global.Theme.Palette[1]);
        // The user's copy wins over the bundled one; the profile's scheme replaces the global one's
        // colors (what it leaves out is libghostty's, not the other scheme's or Phosphor's own).
        var fedora = config.Profiles[0].Config.Theme;
        Assert.Equal(new GhosttyColorRgb(0x11, 0x11, 0x11), fedora.Background);
        Assert.Equal(new GhosttyColorRgb(0xFC, 0xFC, 0xFA), fedora.Foreground);
        Assert.Empty(fedora.Palette);
        Assert.Null(fedora.SelectionBackground);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void UnknownThemesWarn()
    {
        var config = Parse("[colors]\ntheme = \"No Such Scheme\"");
        Assert.Contains(config.Warnings, w => w.StartsWith("config.toml:2: no color scheme named 'No Such Scheme'"));
        Assert.Contains(config.Warnings, w => !w.Contains("../"));
        Assert.Contains(Parse("[colors]\ntheme = \"../config.toml\"").Warnings, w => w.Contains("no color scheme"));
    }

    [Fact]
    public void IncludedFilesComeFirstAndTheIncludingFileWins()
    {
        WriteFile("profiles/work.toml", "[profile.work]\ncommand = \"pwsh.exe\"\n[font]\nsize = 9\nfamily = \"Iosevka\"");
        WriteFile("profiles/home.toml", "[profile.home]\ncommand = \"wsl.exe\"");

        var config = Parse("""
            include = ["profiles/*.toml"]
            [font]
            size = 14
            """);

        Assert.Equal(["home", "work"], config.Profiles.Select(p => p.Id)); // files in name order
        Assert.Equal(14f, config.Global.FontSize);
        Assert.Equal("Iosevka", config.Global.FontFamily);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void AnIncludeThatCannotBeReadIsReportedAndKeepsTheSettingsInForce()
    {
        const string main = "include = \"locked.toml\"\n[font]\nsize = 13";
        WriteFile("locked.toml", "[font]\nsize = 20");
        // Held open with no sharing, as some editors do mid-save.
        using var held = new FileStream(Path.Combine(_dir, "locked.toml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var config = Parse(main);
        Assert.Contains(config.Warnings, w => w.Contains("couldn't read") && w.Contains("locked.toml"));
        Assert.True(config.HasErrors); // a reload keeps what's in force rather than apply half a config
    }

    [Fact]
    public void ReadsAFileAnEditorHasOpen()
    {
        WriteFile("open.toml", "[font]\nsize = 13");
        // Open for writing but sharing reads, as most editors hold a file.
        using var open = new FileStream(Path.Combine(_dir, "open.toml"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Contains("size = 13", ConfigFile.ReadShared(Path.Combine(_dir, "open.toml")));
    }

    [Fact]
    public void BadIncludesWarn()
    {
        const string main = "include = [\"missing.toml\", \"loop.toml\"]";
        WriteFile("config.toml", main);
        WriteFile("loop.toml", "include = \"config.toml\"");
        var config = Parse(main);
        Assert.Contains(config.Warnings, w => w.Contains("can't find included file missing.toml"));
        Assert.Contains(config.Warnings, w => w.Contains("included more than once"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("~", "")]
    [InlineData("home", "")]
    [InlineData("~/dev", "dev")]
    public void WorkingDirectoryExpandsHome(string? value, string relative)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = new Config { WorkingDirectory = value };
        Assert.Equal(Path.Combine(home, relative).TrimEnd('\\'), config.ResolveWorkingDirectory().TrimEnd('\\'));
    }

    [Fact]
    public void ProfileCommandFallsBackToDefaultShell()
    {
        Assert.Equal("wsl.exe", ShellResolver.ForProfile(new Config { Command = "wsl.exe" }));
        Assert.Equal("cmd.exe", ShellResolver.ForProfile(new Config(), _ => ""));
    }
}
