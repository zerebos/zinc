using Zinc.Input;
using Zinc.Panes;
using Windows.System;

namespace Zinc.Tests;

public class KeysConfigTests
{
    [Theory]
    [InlineData("copy", TerminalAction.Copy)]
    [InlineData("copy if-selection", TerminalAction.CopyIfSelection)]
    [InlineData("paste", TerminalAction.Paste)]
    [InlineData("new-tab", TerminalAction.NewTab)]
    [InlineData("close-tab", TerminalAction.ClosePane)]
    [InlineData("command-palette", TerminalAction.OpenCommandPalette)]
    [InlineData("reload-config", TerminalAction.ReloadConfig)]
    [InlineData("quit", TerminalAction.Quit)]
    [InlineData("  Find  ", TerminalAction.Find)]
    public void ParsesActions(string text, TerminalAction expected)
    {
        Assert.Equal(expected, Actions.Parse(text, out var error)?.Action);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("split right", TerminalAction.SplitPane, PaneDirection.Right)]
    [InlineData("split down", TerminalAction.SplitPane, PaneDirection.Down)]
    [InlineData("focus left", TerminalAction.MoveFocus, PaneDirection.Left)]
    [InlineData("resize up", TerminalAction.ResizePane, PaneDirection.Up)]
    public void ParsesDirections(string text, TerminalAction action, PaneDirection direction) =>
        Assert.Equal(new ActionInvocation(action, Direction: direction), Actions.Parse(text, out _));

    [Fact]
    public void ParsesWorkspacesAndProfiles()
    {
        Assert.Equal(new ActionInvocation(TerminalAction.SwitchToTab, 2), Actions.Parse("go-to-workspace 3", out _));
        Assert.Equal(new ActionInvocation(TerminalAction.SwitchToTab, -1), Actions.Parse("go-to-workspace last", out _));
        Assert.Equal(new ActionInvocation(TerminalAction.NewTabFromProfile, 0), Actions.Parse("new-tab 1", out _));
        Assert.Equal(new ActionInvocation(TerminalAction.NewTabFromProfile, Profile: "wsl:Fedora"), Actions.Parse("new-tab wsl:Fedora", out _));
        Assert.Equal(new ActionInvocation(TerminalAction.NewTabFromProfile, Profile: "ssh - homelab"), Actions.Parse("new-tab ssh - homelab", out _));
    }

    [Theory]
    [InlineData("toggle-sidebar", -1, "Toggle sidebar")]
    [InlineData("toggle-sidebar rail", (int)SidebarMode.Rail, "Toggle sidebar rail")]
    [InlineData("toggle-sidebar auto-hide", (int)SidebarMode.AutoHide, "Toggle sidebar auto-hide")]
    public void ParsesSidebarToggles(string text, int mode, string title)
    {
        var parsed = Actions.Parse(text, out var error);
        Assert.Null(error);
        Assert.Equal(new ActionInvocation(TerminalAction.ToggleSidebar, mode), parsed);
        Assert.Equal(text, Actions.Format(parsed!.Value));
        Assert.Equal(title, Actions.Title(parsed.Value));
    }

    [Theory]
    [InlineData("toggle-sidebar full")]
    [InlineData("toggle-sidebar sideways")]
    public void RejectsOtherSidebarModes(string text)
    {
        Assert.Null(Actions.Parse(text, out var error));
        Assert.StartsWith("'toggle-sidebar' takes nothing, \"rail\" or \"auto-hide\"", error);
    }

    [Theory]
    [InlineData("teleport", "unknown action 'teleport'")]
    [InlineData("split sideways", "'split' takes left, right, up or down, not 'sideways'")]
    [InlineData("split", "'split' takes left, right, up or down")]
    [InlineData("split 1", "'split' takes left, right, up or down, not '1'")]
    [InlineData("paste now", "'paste' takes no argument")]
    [InlineData("go-to-workspace 0", "'go-to-workspace' takes a workspace number from 1, or \"last\"")]
    [InlineData("copy all", "'copy' takes nothing or \"if-selection\", not 'all'")]
    public void ExplainsBadActions(string text, string message)
    {
        Assert.Null(Actions.Parse(text, out var error));
        Assert.Equal(message, error);
    }

    [Fact]
    public void EveryDefaultBindingRoundTripsThroughText()
    {
        foreach (var (_, action) in Keybindings.Default.All)
        {
            Assert.Equal(action, Actions.Parse(Actions.Format(action), out _));
            Assert.False(string.IsNullOrWhiteSpace(Actions.Title(action)));
        }
    }

    [Fact]
    public void ReadmeListsEveryAction()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "README.md")))
        {
            dir = dir.Parent;
        }

        var readme = File.ReadAllText(Path.Combine(dir!.FullName, "README.md"));
        Assert.All(Actions.All, a => Assert.Contains($"`{a.Name}", readme));
    }

    private static ConfigFile Parse(string text) => ConfigFile.Parse(text);

    [Fact]
    public void KeysTableChangesTheDefaults()
    {
        var config = Parse("""
            [keys]
            "ctrl+shift+w" = "none"
            "ctrl+w" = "close-tab"
            "alt+shift+plus" = "split down"
            "ctrl+alt+r" = "reload-config"
            """);
        var keys = config.Keybindings;
        Assert.Empty(config.Warnings);

        Assert.False(keys.TryGet(new KeyChord(VirtualKey.W, Ctrl: true, Shift: true), out _));
        Assert.True(keys.TryGet(new KeyChord(VirtualKey.W, Ctrl: true), out var close));
        Assert.Equal(TerminalAction.ClosePane, close.Action);
        Assert.True(keys.TryGet(KeyChord.Parse("alt+shift+plus"), out var split));
        Assert.Equal(PaneDirection.Down, split.Direction);
        Assert.Equal(KeyChord.Parse("ctrl+alt+r"), keys.ChordFor(TerminalAction.ReloadConfig));

        // Untouched defaults stay, and the shared defaults aren't changed.
        Assert.True(keys.TryGet(KeyChord.Parse("ctrl+shift+t"), out _));
        Assert.True(Keybindings.Default.TryGet(KeyChord.Parse("ctrl+shift+w"), out _));
    }

    [Fact]
    public void KeyProblemsAreReportedWithLineNumbers()
    {
        var config = Parse("""
            [keys]
            "ctrl+shift+c" = "copy"
            "shift+ctrl+c" = "paste"
            "ctrl+a>" = "new-tab"
            "hyper+x" = "copy"
            "ctrl+y" = "teleport"
            "ctrl+u" = 3
            """);
        Assert.Contains("config.toml:3: Ctrl+Shift+C is already bound (config.toml:2)", config.Warnings);
        Assert.Contains("config.toml:4: 'ctrl+a>': a key is missing around '>'", config.Warnings);
        Assert.Contains(config.Warnings, w => w.StartsWith("config.toml:5: 'hyper+x': unknown key 'hyper'"));
        Assert.Contains("config.toml:6: unknown action 'teleport'", config.Warnings);
        Assert.Contains(config.Warnings, w => w.StartsWith("config.toml:7: a key's action is text"));

        // The first binding of a chord stands.
        Assert.True(config.Keybindings.TryGet(KeyChord.Parse("ctrl+shift+c"), out var copy));
        Assert.Equal(TerminalAction.Copy, copy.Action);
    }

    [Fact]
    public void CopyFromReplacesBindingsInPlace()
    {
        var shared = Keybindings.Default.Clone();
        var fresh = Parse("[keys]\n\"ctrl+shift+w\" = \"none\"").Keybindings;
        shared.CopyFrom(fresh);
        Assert.False(shared.TryGet(KeyChord.Parse("ctrl+shift+w"), out _));
        Assert.Equal(fresh.All.Count(), shared.All.Count());
    }
}
