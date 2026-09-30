using Phosphor.Commands;
using Phosphor.Input;
using Phosphor.Panes;

namespace Phosphor.Tests;

public class FuzzyMatchTests
{
    [Theory]
    [InlineData("", "Anything")]
    [InlineData("nt", "New tab")]
    [InlineData("NEW TAB", "New tab")]
    [InlineData("spl gb", "Split right: Git Bash")]
    [InlineData("srgb", "Split right: Git Bash")]
    [InlineData("fedora", "New tab: Fedora")]
    public void Matches(string query, string text)
    {
        Assert.NotNull(FuzzyMatch.Score(query, text, out _));
    }

    [Theory]
    [InlineData("xyz", "New tab")]
    [InlineData("tabnew", "New tab")] // order matters
    [InlineData("newtabs", "New tab")]
    public void RejectsNonMatches(string query, string text)
    {
        Assert.Null(FuzzyMatch.Score(query, text, out _));
    }

    [Fact]
    public void ReportsMatchedPositions()
    {
        FuzzyMatch.Score("nt", "New tab", out var positions);
        Assert.Equal([0, 4], positions); // word starts: N(ew) and t(ab)
    }

    [Fact]
    public void PrefersWordStartsOverEarlierLetters()
    {
        // "gb" should land on G(it) B(ash), not the 'b' in "tab"-like earlier letters.
        FuzzyMatch.Score("gb", "Debug: Git Bash", out var positions);
        Assert.Equal([7, 11], positions);
    }

    [Fact]
    public void RanksTighterMatchesHigher()
    {
        var exact = FuzzyMatch.Score("close tab", "Close tab", out _);
        var scattered = FuzzyMatch.Score("close tab", "Close tabs to the right", out _);
        var loose = FuzzyMatch.Score("ct", "Scroll to top", out _);
        var initials = FuzzyMatch.Score("ct", "Close tab", out _);
        Assert.True(exact > scattered);
        Assert.True(initials > loose);
    }

    [Fact]
    public void FilterSortsByScoreAndKeepsOrderForEmptyQuery()
    {
        var commands = new[] { "Scroll to top", "Close tab", "Copy", "New tab" }
            .Select(t => new PaletteCommand(t, null, () => { }))
            .ToList();

        Assert.Equal(["Scroll to top", "Close tab", "Copy", "New tab"],
            CommandFilter.Filter(commands, "").Select(m => m.Command.Title));
        Assert.Equal("Close tab", CommandFilter.Filter(commands, "ct")[0].Command.Title);
        // Equal matches ("tab" at a word start in both): the shorter title wins.
        Assert.Equal(["New tab", "Close tab"],
            CommandFilter.Filter(commands, "tab").Select(m => m.Command.Title));
    }

    private static List<PaletteCommand> Commands(params string[] titles) =>
        titles.Select(t => new PaletteCommand(t, null, () => { })).ToList();

    [Fact]
    public void RecentCommandsLeadAnEmptyQuery()
    {
        var commands = Commands("Scroll to top", "Close tab", "Copy", "New tab", "Paste");
        var recent = new RecentCommands();
        recent.Note("Copy");
        recent.Note("New tab"); // most recent

        var list = CommandFilter.Filter(commands, "", recent);
        Assert.Equal(["New tab", "Copy", "Scroll to top", "Close tab", "Paste"], list.Select(m => m.Command.Title));
        Assert.Equal([true, true, false, false, false], list.Select(m => m.Recent));
    }

    [Fact]
    public void RecentCommandsRankHigherButABetterMatchStillWins()
    {
        var commands = Commands("New tab", "Close tab", "Next tab");
        var recent = new RecentCommands();
        recent.Note("Close tab");
        // "tab" matches all three equally well: the recent one comes first.
        Assert.Equal("Close tab", CommandFilter.Filter(commands, "tab", recent)[0].Command.Title);
        // "nt" is a far better match for New/Next tab (initials) than for Close tab.
        Assert.NotEqual("Close tab", CommandFilter.Filter(commands, "nt", recent)[0].Command.Title);
    }

    [Fact]
    public void EntriesThatComeAndGoAreNotRecent()
    {
        var commands = new List<PaletteCommand>
        {
            new("Switch to tab: build", null, () => { }, Remember: false),
            new("Copy", null, () => { }),
        };
        var recent = new RecentCommands(["Switch to tab: build"]);
        Assert.All(CommandFilter.Filter(commands, "", recent), m => Assert.False(m.Recent));
    }

    [Fact]
    public void RecentCommandsKeepTheNewestFirstUpToTheirCapacity()
    {
        var recent = new RecentCommands();
        for (var i = 0; i < RecentCommands.Capacity + 5; i++)
        {
            recent.Note($"Command {i}");
        }

        recent.Note("Command 20"); // again: moves to the front, no duplicate
        Assert.Equal(RecentCommands.Capacity, recent.Titles.Count);
        Assert.Equal("Command 20", recent.Titles[0]);
        Assert.Single(recent.Titles, t => t == "Command 20");
        Assert.Equal(["a", "b"], new RecentCommands(["a", "b", "a"]).Titles);
    }
}

public class ShortcutDisplayTests
{
    [Theory]
    [InlineData("ctrl+shift+t", "Ctrl+Shift+T")]
    [InlineData("alt+shift+plus", "Alt+Shift+Plus")]
    [InlineData("ctrl+alt+9", "Ctrl+Alt+9")]
    [InlineData("shift+pgup", "Shift+PgUp")]
    [InlineData("ctrl+numpad0", "Ctrl+Numpad 0")]
    [InlineData("alt+left", "Alt+Left")]
    [InlineData("shift+f11", "Shift+F11")]
    public void ChordsDisplayLikeWindowsTerminal(string chord, string expected)
    {
        Assert.Equal(expected, KeyChord.Parse(chord).ToString());
    }

    [Fact]
    public void ChordForReturnsFirstBinding()
    {
        var keys = Keybindings.Default;
        Assert.Equal("Ctrl+Shift+T", keys.ChordFor(TerminalAction.NewTab)?.ToString());
        Assert.Equal("Ctrl+V", keys.ChordFor(TerminalAction.Paste)?.ToString());
        Assert.Equal("Ctrl+Plus", keys.ChordFor(TerminalAction.ZoomIn)?.ToString());
        Assert.Equal("Ctrl+Shift+P", keys.ChordFor(TerminalAction.OpenCommandPalette)?.ToString());
        Assert.Equal("Alt+Shift+Minus",
            keys.ChordFor(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Down))?.ToString());
        Assert.Null(keys.ChordFor(new ActionInvocation(TerminalAction.NewTabFromProfile, 42)));
    }
}
