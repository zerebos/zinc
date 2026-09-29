namespace Phosphor.Tests;

public class FirstRunTests
{
    private static ConfigFile Read(string text) => ConfigFile.Parse(text);

    [Fact]
    public void WorkspacesInTheSidebarWithTheirOwnSchemeFontAndShell()
    {
        var text = FirstRun.Apply(null, new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Left, "Dracula", "JetBrainsMono NF", "wsl:Fedora"));
        Assert.StartsWith(FirstRun.Template, text);
        var config = ConfigFile.Parse(text, "config.toml", bundledThemes: Path.GetTempPath());
        Assert.Equal(WorkspaceTabs.Left, config.Global.WorkspaceTabs);
        Assert.True(config.Global.TabGroups);
        Assert.Equal("JetBrainsMono NF", config.Global.FontFamily);
        Assert.Contains("theme = \"Dracula\"", text);
        Assert.Contains("default-profile = \"wsl:Fedora\"", text);
        // A top-level key must come before any table to stay top-level.
        Assert.True(text.IndexOf("default-profile", StringComparison.Ordinal) < text.IndexOf('[', StringComparison.Ordinal));
        // Read as the top-level setting (tests don't discover profiles, so it names none here).
        Assert.Contains(config.Warnings, w => w.Contains("default-profile 'wsl:Fedora' isn't a profile"));
    }

    [Fact]
    public void SimpleIsTabsOnTopWithoutGroups()
    {
        var config = Read(FirstRun.Apply(null, new WelcomeChoices(WorkStyle.Simple, WorkspaceTabs.Left, null, null, null)));
        Assert.Equal(WorkspaceTabs.Top, config.Global.WorkspaceTabs); // "on the left" only applies to workspaces
        Assert.False(config.Global.TabGroups);
    }

    [Fact]
    public void KnowsTheSchemeTheConfigNames()
    {
        // So the page, reopened, starts on it (and saving doesn't drop it).
        Assert.Equal("Dracula", Read("[colors]\ntheme = \"Dracula\"").SchemeName);
        Assert.Null(Read("[font]\nsize = 12").SchemeName);
        Assert.Null(Read("[profile.x]\ncolors.theme = \"Dracula\"").SchemeName); // a profile's own isn't the default
    }

    [Fact]
    public void SavingItUntouchedLeavesTheFileAlone()
    {
        const string existing = "[colors]\ntheme = \"Dracula\"\n\n[window]\nworkspace-tabs = \"left\"\n";
        var current = Read(existing);
        var same = new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Left, "Dracula", null, current.DefaultProfile.Id);
        Assert.Equal(existing, FirstRun.Apply(existing, same, current));
    }

    [Fact]
    public void ReopenedLaterItKeepsTheRestOfTheFile()
    {
        const string existing = "# mine\n[font]\nfamily = \"Iosevka\"\nsize = 12  # just right\n\n[colors]\ntheme = \"0x96f\"\n";
        var text = FirstRun.Apply(existing, new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Top, "Dracula", null, null));
        Assert.Contains("# mine", text);
        Assert.Contains("size = 12  # just right", text);
        Assert.Contains("theme = \"Dracula\"", text);
        Assert.DoesNotContain("Iosevka", text); // back to the default font
        Assert.DoesNotContain("default-profile", text); // not chosen: not written
    }
}
