using Phosphor.Ghostty;

namespace Phosphor.Tests;

public class ConfigFileTests
{
    private static ConfigFile Parse(string text) => ConfigFile.Parse(text.Split('\n'));

    [Fact]
    public void NoProfilesGivesImplicitDefault()
    {
        var config = Parse("font-size = 12\ncommand = pwsh.exe");
        var profile = Assert.Single(config.Profiles);
        Assert.Equal("Default", profile.Name);
        Assert.Same(profile, config.DefaultProfile);
        Assert.Equal(12f, profile.Config.FontSize);
        Assert.Equal("pwsh.exe", profile.Config.Command);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void ProfilesInheritGlobalsAndOverride()
    {
        var config = Parse("""
            font-family = JetBrainsMono NF
            font-size = 11
            background = #181818
            palette = 1=#AC4242

            [profile "Fedora"]
            command = wsl.exe -d Fedora
            palette = 1=#FF7272

            [profile Ubuntu]
            font-family = DepartureMono Nerd Font
            font-size = 12
            """);

        Assert.Equal(["Fedora", "Ubuntu"], config.Profiles.Select(p => p.Name));
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
    public void DefaultProfileIsSelectedByName()
    {
        var config = Parse("""
            default-profile = ubuntu
            [profile "Fedora"]
            [profile "Ubuntu"]
            """);
        Assert.Equal("Ubuntu", config.DefaultProfile.Name);
    }

    [Fact]
    public void FirstProfileIsDefaultOtherwise()
    {
        Assert.Equal("Fedora", Parse("[profile \"Fedora\"]\n[profile \"Ubuntu\"]").DefaultProfile.Name);
    }

    [Fact]
    public void MissingDefaultProfileWarnsAndFallsBack()
    {
        var config = Parse("default-profile = Nope\n[profile \"Fedora\"]");
        Assert.Equal("Fedora", config.DefaultProfile.Name);
        Assert.Contains(config.Warnings, w => w.Contains("Nope"));
    }

    [Fact]
    public void BadLinesAreReportedWithLineNumbers()
    {
        var config = Parse("""
            font-size = huge
            [profile "A"]
            no-equals
            [profile oops "B"]
            command = should-not-apply-anywhere
            [profile "A"]
            [profile "C"]
            default-profile = A
            """);

        Assert.Contains("config:1: ignored 'font-size = huge'", config.Warnings);
        Assert.Contains("config:3: expected 'key = value'", config.Warnings);
        Assert.Contains(config.Warnings, w => w.StartsWith("config:4: expected [profile"));
        Assert.Contains(config.Warnings, w => w.StartsWith("config:6: duplicate profile"));
        Assert.Contains(config.Warnings, w => w.StartsWith("config:8: default-profile belongs outside"));

        // Lines under a malformed header don't leak into globals or other profiles.
        Assert.Equal(["A", "C"], config.Profiles.Select(p => p.Name));
        Assert.All(config.Profiles, p => Assert.Null(p.Config.Command));
        Assert.Null(config.Global.Command);
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
