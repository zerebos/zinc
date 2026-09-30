using Phosphor.Input;
using Phosphor.Settings;

namespace Phosphor.Tests;

public class KeyBindingEditorTests
{
    private static KeyChord K(string text) => KeyChord.Parse(text);

    private static readonly ActionInvocation NewTab = TerminalAction.NewTab;
    private static readonly ActionInvocation Reload = TerminalAction.ReloadConfig;

    /// <summary>The config after <paramref name="edit"/>, as the reader sees it.</summary>
    private static (string Text, ConfigFile Config) Apply(string text, Func<IReadOnlyDictionary<string, string>, IReadOnlyList<KeyEdit>> edit)
    {
        foreach (var e in edit(KeyBindingEditor.Lines(ConfigFile.Parse(text))))
        {
            text = e.Value == null ? ConfigEditor.Remove(text, e.Path) : ConfigEditor.Set(text, e.Path, e.Value);
        }

        var config = ConfigFile.Parse(text);
        Assert.Empty(config.Warnings);
        return (text, config);
    }

    [Theory]
    [InlineData("ctrl+shift+t")]
    [InlineData("alt+shift+plus")]
    [InlineData("ctrl+comma")]
    [InlineData("ctrl+numpad5")]
    [InlineData("shift+f11")]
    [InlineData("ctrl+grave")]
    [InlineData("alt+pgup")]
    public void ChordsWriteBackAsTheyRead(string text) => Assert.Equal(K(text), K(K(text).ToConfig()!));

    [Fact]
    public void AddingAKeyWritesALine()
    {
        var (text, config) = Apply("", lines => KeyBindingEditor.Add(lines, Reload, K("ctrl+alt+r")));
        Assert.Contains("\"ctrl+alt+r\" = \"reload-config\"", text);
        Assert.True(config.Keybindings.TryGet(K("ctrl+alt+r"), out var bound) && bound == Reload);
    }

    [Fact]
    public void TakingADefaultKeyAwayWritesNone()
    {
        var (text, config) = Apply("", lines => KeyBindingEditor.Remove(lines, NewTab, K("ctrl+shift+t")));
        Assert.Contains("\"ctrl+shift+t\" = \"none\"", text);
        Assert.False(config.Keybindings.TryGet(K("ctrl+shift+t"), out _));
    }

    [Fact]
    public void GivingItBackRemovesTheLine()
    {
        var (text, config) = Apply("[keys]\n\"ctrl+shift+t\" = \"none\"\n", lines => KeyBindingEditor.Add(lines, NewTab, K("ctrl+shift+t")));
        Assert.DoesNotContain("ctrl+shift+t", text);
        Assert.True(config.Keybindings.TryGet(K("ctrl+shift+t"), out var bound) && bound == NewTab);
    }

    [Fact]
    public void RemovingYourOwnKeyRemovesItsLine()
    {
        var (text, _) = Apply("[keys]\n\"ctrl+alt+r\" = \"reload-config\"\n", lines => KeyBindingEditor.Remove(lines, Reload, K("ctrl+alt+r")));
        Assert.DoesNotContain("ctrl+alt+r", text);
    }

    [Fact]
    public void AChordWrittenAnotherWayIsEditedInPlace()
    {
        var (text, config) = Apply("[keys]\n\"alt+ctrl+r\" = \"new-window\"\n", lines => KeyBindingEditor.Add(lines, Reload, K("ctrl+alt+r")));
        Assert.Contains("\"alt+ctrl+r\" = \"reload-config\"", text);
        Assert.DoesNotContain("new-window", text);
        Assert.True(config.Keybindings.TryGet(K("ctrl+alt+r"), out var bound) && bound == Reload);
    }

    [Fact]
    public void TakingAnotherActionsKeyRebindsIt()
    {
        var (_, config) = Apply("", lines => KeyBindingEditor.Add(lines, Reload, K("ctrl+shift+t")));
        Assert.True(config.Keybindings.TryGet(K("ctrl+shift+t"), out var bound) && bound == Reload);
    }

    [Fact]
    public void ResetUndoesAllOfAnActionsChanges()
    {
        const string text = "[keys]\n\"ctrl+shift+t\" = \"none\"\n\"ctrl+alt+n\" = \"new-tab\"\n\"ctrl+alt+r\" = \"reload-config\"\n";
        var lines = KeyBindingEditor.Lines(ConfigFile.Parse(text));
        Assert.True(KeyBindingEditor.Customized(lines, NewTab));
        var (after, config) = Apply(text, l => KeyBindingEditor.Reset(l, NewTab));
        Assert.Equal("[keys]\n\"ctrl+alt+r\" = \"reload-config\"\n", after);
        Assert.True(config.Keybindings.TryGet(K("ctrl+shift+t"), out var bound) && bound == NewTab);
    }

    [Fact]
    public void ResetLeavesSequencesAlone()
    {
        var (text, _) = Apply("[keys]\n\"ctrl+a > c\" = \"new-tab\"\n\"ctrl+alt+n\" = \"new-tab\"\n", l => KeyBindingEditor.Reset(l, NewTab));
        Assert.Contains("ctrl+a > c", text);
        Assert.DoesNotContain("ctrl+alt+n", text);
    }

    [Fact]
    public void SequencesAreRemovedByTheirLine()
    {
        var (text, _) = Apply("[keys]\n\"ctrl+a > c\" = \"new-tab\"\n", lines => KeyBindingEditor.Remove(lines, KeySequence.Parse("ctrl+a>c")));
        Assert.DoesNotContain("ctrl+a", text);
    }

    [Fact]
    public void EveryArgumentlessActionIsListed() =>
        Assert.Contains(new ActionInvocation(TerminalAction.OpenSettings), Actions.WithoutArguments);

    private static readonly (string Id, string Name)[] Profiles = [("wsl:Ubuntu", "Ubuntu"), ("pwsh", "PowerShell")];

    [Fact]
    public void ActionsWithArgumentsListTheirVariants()
    {
        Assert.Equal(["Right", "Down", "Left", "Up"], Actions.Variants("split", Profiles).Select(v => v.Label));
        Assert.Equal("split left", Actions.Format(Actions.Variants("split", Profiles)[2].Invocation));
        Assert.Equal(["The default profile", "Ubuntu", "PowerShell"], Actions.Variants("new-tab", Profiles).Select(v => v.Label));
        Assert.Equal("go-to-workspace last", Actions.Format(Actions.Variants("go-to-workspace", Profiles)[^1].Invocation));
        Assert.Equal("direction", Actions.ArgumentName("split"));
        Assert.False(Actions.TakesArgument("paste"));

        // Every variant of every action writes and reads back the same.
        foreach (var family in Actions.WithoutArguments.Select(Actions.Family).Concat(["split", "focus", "resize", "go-to-workspace", "jump-to-prompt"]))
        {
            foreach (var variant in Actions.Variants(family, Profiles))
            {
                Assert.Equal(variant.Invocation, Actions.Parse(Actions.Format(variant.Invocation), out _));
                Assert.Equal(family, Actions.Family(variant.Invocation));
            }
        }
    }

    [Fact]
    public void AVariantIsLabeledByItsArgument()
    {
        Assert.Equal("Ubuntu", Actions.VariantLabel(new ActionInvocation(TerminalAction.NewTabFromProfile, Profile: "wsl:Ubuntu"), Profiles));
        Assert.Equal("Profile 3 in the menu", Actions.VariantLabel(new ActionInvocation(TerminalAction.NewTabFromProfile, 2), Profiles));
        Assert.Equal("The last one", Actions.VariantLabel(new ActionInvocation(TerminalAction.SwitchToTab, -1), Profiles));
        // The plain "toggle-sidebar" is the one that cycles, as the default key binds it.
        Assert.Equal(Actions.Parse("toggle-sidebar", out _), Actions.WithoutArguments.Single(a => a.Action == TerminalAction.ToggleSidebar));
    }

    [Fact]
    public void EveryActionHasAGroup()
    {
        Assert.Empty(Actions.Uncategorized);
        Assert.Equal("Clipboard", Actions.Category(new ActionInvocation(TerminalAction.CopyIfSelection)));
        Assert.Equal("Tabs", Actions.Category(new ActionInvocation(TerminalAction.NewTabFromProfile)));
        Assert.All(Actions.WithoutArguments, a => Assert.Contains(Actions.Category(a), Actions.Categories));
    }
}
