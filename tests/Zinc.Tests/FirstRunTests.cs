using Zinc.Settings;

namespace Zinc.Tests;

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
    public void TheOldNameForPaneTabsGoes()
    {
        // The file keeps one name for it, as when the settings window writes it (SettingsWriter).
        const string existing = "[window]\ntab-groups = true\n";
        var text = FirstRun.Apply(existing, new WelcomeChoices(WorkStyle.Simple, WorkspaceTabs.Top, null, null, null), Read(existing));
        Assert.DoesNotContain("tab-groups", text);
        Assert.False(Read(text).Global.TabGroups);
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

    [Fact]
    public void ALightDarkPairIsWrittenAsTheScheme()
    {
        var text = FirstRun.Apply(null, new WelcomeChoices(WorkStyle.Simple, WorkspaceTabs.Top, "light:Catppuccin Latte,dark:Catppuccin Mocha", null, null));
        Assert.Contains("theme = \"light:Catppuccin Latte,dark:Catppuccin Mocha\"", text);
    }

    [Fact]
    public void NotificationsAreWrittenOnlyWhenAskedFor()
    {
        var toast = FirstRun.Apply(null, new WelcomeChoices(WorkStyle.Simple, WorkspaceTabs.Top, null, null, null, ToastWhenDone: true));
        Assert.Contains("command-finished = \"toast\"", toast);
        var dot = FirstRun.Apply(null, new WelcomeChoices(WorkStyle.Simple, WorkspaceTabs.Top, null, null, null, ToastWhenDone: false));
        Assert.DoesNotContain("command-finished", dot);

        // Reopened: "toast-always" already counts as notifications on; off removes it.
        const string always = "[notify]\ncommand-finished = \"toast-always\"\n";
        var current = Read(always);
        Assert.Equal(always, FirstRun.Apply(always, new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Top, null, null, current.DefaultProfile.Id, true), current));
        Assert.DoesNotContain("command-finished",
            FirstRun.Apply(always, new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Top, null, null, current.DefaultProfile.Id, false), current));
    }

    [Theory]
    [InlineData("Catppuccin Mocha", "Catppuccin Latte")]
    [InlineData("Solarized Dark", "Solarized Light")]
    [InlineData("Tokyo Night", "Tokyo Night Day")]
    [InlineData("Rose Pine", "Rose Pine Dawn")]
    [InlineData("Dracula", "Catppuccin Latte")] // no counterpart: a well-known light scheme
    [InlineData("Lit Cell", "Lit Cell Light")]
    [InlineData(null, "Lit Cell Light")] // Zinc's own colors
    public void LightSchemesPairWithTheirDarkOnes(string? dark, string light)
    {
        string[] names = ["Catppuccin Latte", "Catppuccin Mocha", "Dracula", "Lit Cell", "Lit Cell Light", "Rose Pine", "Rose Pine Dawn",
            "Solarized Dark", "Solarized Light", "Tokyo Night", "Tokyo Night Day"];
        Assert.Equal(light, ThemeLibrary.LightCounterpart(dark, names));
    }

    [Fact]
    public void BundledLitCellIsZincsOwnColors()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Zinc.slnx")))
        {
            dir = dir.Parent;
        }

        var file = ThemeLibrary.ApplyFile(new Config { Theme = Zinc.Terminal.TerminalTheme.SchemeBase },
            Path.Combine(dir.FullName, "assets", "zinc-themes", "Lit Cell")).Theme;
        var own = Zinc.Terminal.TerminalTheme.Default;
        Assert.Equal((own.Background, own.Foreground, own.Cursor, own.CursorText, own.SelectionBackground, own.SelectionForeground),
            (file.Background, file.Foreground, file.Cursor, file.CursorText, file.SelectionBackground, file.SelectionForeground));
        Assert.Equal(own.Palette.OrderBy(p => p.Key), file.Palette.OrderBy(p => p.Key));
    }

    [Fact]
    public void CardsSetsTheFivePaneSettings()
    {
        var text = FirstRun.Apply(null, new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Left, null, null, null, PaneStyle: PaneStyleKind.Cards));
        Assert.Equal(PaneStyleKind.Cards, PaneStyle.Of(Read(text).ValueOf));
    }

    [Fact]
    public void CompactRemovesThemAndNullLeavesAMixAlone()
    {
        const string existing = "[window]\npane-gap = 6\npane-focus = \"ring\"\n";
        var current = Read(existing);
        var compact = FirstRun.Apply(existing, new WelcomeChoices(WorkStyle.Simple, WorkspaceTabs.Top, null, null, null, PaneStyle: PaneStyleKind.Compact), current);
        Assert.DoesNotContain("pane-gap", compact);
        Assert.DoesNotContain("pane-focus", compact);
        var mixed = FirstRun.Apply(existing, new WelcomeChoices(WorkStyle.Simple, WorkspaceTabs.Top, null, null, null), current);
        Assert.Contains("pane-gap = 6", mixed);
        Assert.Contains("pane-focus = \"ring\"", mixed);
    }

    [Fact]
    public void AStyleAlreadyInForceIsntRewritten()
    {
        var cards = FirstRun.Apply(null, new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Left, null, null, null, PaneStyle: PaneStyleKind.Cards));
        var current = Read(cards);
        var same = new WelcomeChoices(WorkStyle.Workspaces, WorkspaceTabs.Left, null, null, current.DefaultProfile.Id, PaneStyle: PaneStyleKind.Cards);
        Assert.Equal(cards, FirstRun.Apply(cards, same, current));
    }
}
