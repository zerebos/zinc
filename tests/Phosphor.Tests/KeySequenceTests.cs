using Phosphor.Input;
using Phosphor.Panes;

namespace Phosphor.Tests;

public class KeySequenceTests
{
    private static ConfigFile Parse(string text) => ConfigFile.Parse(text);

    private static KeyChord K(string text) => KeyChord.Parse(text);

    private static Keybindings Leader() => Parse("""
        [keys]
        "ctrl+a>c" = "new-tab"
        "ctrl+a > shift+5" = "split right"
        "ctrl+a>w>n" = "new-window"
        "ctrl+a>w>q" = "quit"
        """).Keybindings;

    [Theory]
    [InlineData("ctrl+a>c")]
    [InlineData("ctrl+a > c")]
    [InlineData(" ctrl + a >c ")]
    public void ParsesWithOrWithoutSpaces(string text)
    {
        var keys = KeySequence.Parse(text);
        Assert.Equal([K("ctrl+a"), K("c")], keys.Keys);
        Assert.Equal("Ctrl+A, C", keys.ToString());
    }

    [Theory]
    [InlineData("ctrl+a>")]
    [InlineData(">c")]
    [InlineData("ctrl+a>>c")]
    public void AMissingKeyIsAnError(string text) => Assert.Throws<FormatException>(() => KeySequence.Parse(text));

    [Fact]
    public void ConfiguredWithoutWarnings()
    {
        var config = Parse("""
            [keys]
            "ctrl+a>c" = "new-tab"
            "ctrl+a > shift+5" = "split right"
            "ctrl+a>r" = "reload-config"
            """);
        Assert.Empty(config.Warnings);
        Assert.Equal(3, config.Keybindings.Sequences.Count);
        // Labels: a sequence where there's no single chord for the action; else the chord.
        Assert.Equal("Ctrl+A, R", config.Keybindings.ShortcutFor(TerminalAction.ReloadConfig));
        Assert.Equal("Alt+Shift+Plus", config.Keybindings.ShortcutFor(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right)));
    }

    [Fact]
    public void ALeaderRunsTheActionItsSequenceEndsIn()
    {
        var sequencer = new KeySequencer(Leader());
        Assert.Equal(KeyStep.Waiting, sequencer.Press(K("ctrl+a"), out _));
        Assert.Equal(KeyStep.Ran, sequencer.Press(K("shift+5"), out var action));
        Assert.Equal(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right), action);
        Assert.False(sequencer.Active);
    }

    [Fact]
    public void LongerSequencesWaitForEachKey()
    {
        var sequencer = new KeySequencer(Leader());
        sequencer.Press(K("ctrl+a"), out _);
        Assert.Equal(KeyStep.Waiting, sequencer.Press(K("w"), out _));
        Assert.Equal(KeyStep.Ran, sequencer.Press(K("q"), out var action));
        Assert.Equal(TerminalAction.Quit, action.Action);
    }

    [Fact]
    public void OtherKeysAreNotItsBusiness()
    {
        var sequencer = new KeySequencer(Leader());
        Assert.Equal(KeyStep.NotMine, sequencer.Press(K("c"), out _));
        Assert.Equal(KeyStep.NotMine, sequencer.Press(K("ctrl+shift+t"), out _));
    }

    [Theory]
    [InlineData("esc")]
    [InlineData("x")]      // bound to nothing after the leader
    [InlineData("ctrl+c")]
    public void AnythingElseCancelsAndIsDropped(string key)
    {
        var sequencer = new KeySequencer(Leader());
        sequencer.Press(K("ctrl+a"), out _);
        Assert.Equal(KeyStep.Cancelled, sequencer.Press(K(key), out _));
        Assert.False(sequencer.Active);
        Assert.Equal(KeyStep.NotMine, sequencer.Press(K("c"), out _)); // over: "c" is just "c" again
    }

    [Fact]
    public void TheLeaderTwiceSendsIt()
    {
        // Ctrl+A is "start of line" in bash and zsh: pressing it twice still gets it there.
        var sequencer = new KeySequencer(Leader());
        sequencer.Press(K("ctrl+a"), out _);
        Assert.Equal(KeyStep.PassThrough, sequencer.Press(K("ctrl+a"), out _));
        Assert.False(sequencer.Active);
    }

    [Fact]
    public void UnlessTheLeaderTwiceIsBound()
    {
        var keys = Parse("""
            [keys]
            "ctrl+a>ctrl+a" = "next-tab"
            """).Keybindings;
        var sequencer = new KeySequencer(keys);
        sequencer.Press(K("ctrl+a"), out _);
        Assert.Equal(KeyStep.Ran, sequencer.Press(K("ctrl+a"), out var action));
        Assert.Equal(TerminalAction.NextTab, action.Action);
    }

    [Fact]
    public void TheHintListsWhatComesNext()
    {
        var next = Leader().Continuations([K("ctrl+a")]);
        Assert.Equal([K("c"), K("shift+5"), K("w")], next.Select(n => n.Next));
        Assert.Equal(TerminalAction.NewTab, next[0].Action?.Action);
        Assert.Null(next[2].Action); // "w" leads on to more keys
    }

    [Fact]
    public void ALeaderBoundOnItsOwnOnlyStartsSequences()
    {
        // Ctrl+Shift+T opens a tab by default; as a leader it can't do both.
        var config = Parse("""
            [keys]
            "ctrl+shift+t>c" = "new-window"
            """);
        Assert.Contains("config.toml:2: Ctrl+Shift+T starts key sequences now, so it no longer runs 'new-tab'", config.Warnings);
        Assert.False(config.Keybindings.TryGet(K("ctrl+shift+t"), out _));
        Assert.True(config.Keybindings.StartsSequence(K("ctrl+shift+t")));
    }

    [Fact]
    public void ASequencePastACompleteOneIsReported()
    {
        var config = Parse("""
            [keys]
            "ctrl+a>c" = "new-tab"
            "ctrl+a>c>x" = "quit"
            """);
        Assert.Contains("config.toml:3: Ctrl+A, C, X can't be reached: Ctrl+A, C runs first", config.Warnings);
    }

    [Fact]
    public void SequencesCanBeUnboundAndRepeatsAreReported()
    {
        var config = Parse("""
            [keys]
            "ctrl+a>c" = "new-tab"
            "ctrl+a > c" = "quit"
            "ctrl+a>x" = "none"
            """);
        Assert.Contains("config.toml:3: Ctrl+A, C is already bound (config.toml:2)", config.Warnings);
        Assert.Single(config.Keybindings.Sequences);
    }

    [Fact]
    public void ReloadCarriesSequences()
    {
        var shared = Keybindings.Default.Clone();
        shared.CopyFrom(Leader());
        Assert.True(shared.StartsSequence(K("ctrl+a")));
        shared.CopyFrom(Keybindings.Default);
        Assert.False(shared.StartsSequence(K("ctrl+a")));
    }
}
