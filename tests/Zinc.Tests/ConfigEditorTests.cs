namespace Zinc.Tests;

public class ConfigEditorTests
{
    private const string Sample = """
        # My config
        default-profile = "wsl:Fedora"

        [font]
        family = "JetBrainsMono NF"   # the one with icons
        size = 11

        # Windows
        [window]
        workspace-tabs = "left"

        [profile."wsl:Fedora"]
        colors.theme = "WT 0x96f"

        [profile."wsl:Ubuntu"]
        font = { family = "DepartureMono Nerd Font", size = 12 }

        [profile.pwsh]

        """;

    private static string Set(string text, string path, object value) => ConfigEditor.Set(text, Split(path), value);

    // Paths are dot-separated here; ids with dots or colons are quoted like in TOML.
    private static string[] Split(string path) =>
        System.Text.RegularExpressions.Regex.Matches(path, "\"[^\"]*\"|[^.]+").Select(m => m.Value.Trim('"')).ToArray();

    [Fact]
    public void ReplacesAValueAndNothingElse()
    {
        var updated = Set(Sample, "font.size", 13);
        Assert.Equal(Sample.Replace("size = 11", "size = 13"), updated);

        updated = Set(Sample, "font.family", "Iosevka");
        Assert.Equal(Sample.Replace("\"JetBrainsMono NF\"   # the one", "\"Iosevka\"   # the one"), updated);
    }

    [Fact]
    public void ReplacesDottedAndInlineValues()
    {
        Assert.Equal(Sample.Replace("colors.theme = \"WT 0x96f\"", "colors.theme = \"Dracula\""),
            Set(Sample, "profile.\"wsl:Fedora\".colors.theme", "Dracula"));
        Assert.Equal(Sample.Replace("size = 12 }", "size = 14 }"),
            Set(Sample, "profile.\"wsl:Ubuntu\".font.size", 14));
        Assert.Equal(Sample.Replace("default-profile = \"wsl:Fedora\"", "default-profile = \"pwsh\""),
            Set(Sample, "default-profile", "pwsh"));
    }

    [Fact]
    public void AddsToTheEndOfItsTable()
    {
        Assert.Equal(Sample.Replace("workspace-tabs = \"left\"\n", "workspace-tabs = \"left\"\ngroup-tab-strip = \"always\"\n"),
            Set(Sample, "window.group-tab-strip", "always"));

        // An empty table gets it right under its header.
        Assert.Equal(Sample.Replace("[profile.pwsh]\n", "[profile.pwsh]\nhidden = true\n"),
            Set(Sample, "profile.pwsh.hidden", true));

        // A profile section takes a dotted key for a group.
        Assert.Equal(Sample.Replace("colors.theme = \"WT 0x96f\"\n", "colors.theme = \"WT 0x96f\"\nfont.size = 12\n"),
            Set(Sample, "profile.\"wsl:Fedora\".font.size", 12));
    }

    [Fact]
    public void AddsToAnInlineTable()
    {
        Assert.Equal(Sample.Replace("size = 12 }", "size = 12, weight = 500 }"),
            Set(Sample, "profile.\"wsl:Ubuntu\".font.weight", 500));
        Assert.Equal("font = { size = 11 }\n", Set("font = {}\n", "font.size", 11));
    }

    [Fact]
    public void AddsNewTablesAtTheEnd()
    {
        // Set apart by a blank line.
        Assert.Equal(Sample + "\n[colors]\ntheme = \"Dracula\"\n", Set(Sample, "colors.theme", "Dracula"));
        Assert.Equal("[window]\nworkspace-tabs = \"left\"\n", Set("", "window.workspace-tabs", "left"));
        Assert.Equal("a = 1\n\n[window]\ntab-groups = false\n", Set("a = 1", "window.tab-groups", false));
    }

    [Fact]
    public void NewProfilesJoinTheOthers()
    {
        const string text = "[profile.a]\ncommand = \"x\"\n\n# my keys\n[keys]\n\"ctrl+w\" = \"close-tab\"\n";
        Assert.Equal("[profile.a]\ncommand = \"x\"\n\n[profile.\"ssh:WSL\"]\nhidden = true\n\n# my keys\n[keys]\n\"ctrl+w\" = \"close-tab\"\n",
            Set(text, "profile.\"ssh:WSL\".hidden", true));
        Assert.Equal(Sample.Replace("[profile.pwsh]\n", "[profile.pwsh]\n\n[profile.cmd]\ncolors.theme = \"Dracula\"\n"),
            Set(Sample, "profile.cmd.colors.theme", "Dracula"));
    }

    [Fact]
    public void RootKeysGoAboveTheFirstTable()
    {
        Assert.Equal("# hi\n\ndefault-profile = \"pwsh\"\n\n[font]\nsize = 11\n",
            Set("# hi\n\n[font]\nsize = 11\n", "default-profile", "pwsh"));
        Assert.Equal(Sample.Replace("default-profile = \"wsl:Fedora\"\n", "default-profile = \"wsl:Fedora\"\ninclude = [\"a.toml\"]\n"),
            Set(Sample, "include", new[] { "a.toml" }));
    }

    [Fact]
    public void KeepsWindowsLineEndings()
    {
        var crlf = Sample.Replace("\n", "\r\n");
        Assert.Equal(crlf.Replace("workspace-tabs = \"left\"\r\n", "workspace-tabs = \"left\"\r\ntab-groups = false\r\n"),
            Set(crlf, "window.tab-groups", false));
        Assert.Equal(crlf + "\r\n[colors]\r\ntheme = \"Dracula\"\r\n", Set(crlf, "colors.theme", "Dracula"));
    }

    [Fact]
    public void RemovesALine()
    {
        Assert.Equal(Sample.Replace("        colors.theme = \"WT 0x96f\"\n".TrimStart(), ""),
            ConfigEditor.Remove(Sample, Split("profile.\"wsl:Fedora\".colors.theme")));
        Assert.Equal(Sample, ConfigEditor.Remove(Sample, Split("window.nope")));
    }

    [Theory]
    [InlineData("left", "\"left\"")]
    [InlineData(@"C:\msys64\ucrt64.ico", @"'C:\msys64\ucrt64.ico'")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(true, "true")]
    [InlineData(11, "11")]
    [InlineData(10.5, "10.5")]
    [InlineData(12.0, "12.0")]
    public void FormatsValues(object value, string toml) => Assert.Equal(toml, ConfigEditor.FormatValue(value));

    [Fact]
    public void EditsReadBackAsWritten()
    {
        var text = Set(Sample, "profile.\"wsl:Fedora\".colors.theme", "Dracula");
        text = Set(text, "window.save-state", "never");
        text = Set(text, "profile.\"ssh:WSL\".hidden", true);
        var config = ConfigFile.Parse(text);
        Assert.False(config.Global.SaveState);
        Assert.True(config.Profiles.Single(p => p.Id == "ssh:WSL").Hidden);
        Assert.DoesNotContain(config.Warnings, w => !w.Contains("color scheme")); // only the unknown test themes
    }

    [Fact]
    public void RefusesInvalidToml()
    {
        Assert.Throws<FormatException>(() => Set("[font\nsize = 1", "font.size", 2));
    }

    [Fact]
    public void RemovesAProfileWithItsSubTablesAndKeepsTheNextSectionsComment()
    {
        const string text = "[profile.a]\ncommand = \"x\"\n\n[profile.a.font]\nsize = 12\n\n# my keys\n[keys]\n\"ctrl+w\" = \"close-tab\"\n";
        Assert.Equal("# my keys\n[keys]\n\"ctrl+w\" = \"close-tab\"\n", ConfigEditor.RemoveTable(text, ["profile", "a"]));
    }

    [Fact]
    public void RemovesOnlyThatProfile()
    {
        const string text = "[profile.a]\ncommand = \"x\"\n\n[profile.\"wsl:Ubuntu\"]\nhidden = true\n\n[profile.ab]\ncommand = \"y\"\n";
        Assert.Equal("[profile.a]\ncommand = \"x\"\n\n[profile.ab]\ncommand = \"y\"\n",
            ConfigEditor.RemoveTable(text, ["profile", "wsl:Ubuntu"]));
        Assert.Equal("[profile.\"wsl:Ubuntu\"]\nhidden = true\n\n[profile.ab]\ncommand = \"y\"\n",
            ConfigEditor.RemoveTable(text, ["profile", "a"]));
    }

    [Fact]
    public void RemovingATableThatIsntThereChangesNothing() =>
        Assert.Equal(Sample, ConfigEditor.RemoveTable(Sample, ["profile", "nope"]));
}
