using Zinc.Terminal;

namespace Zinc.Tests;

public class PasteCheckTests
{
    [Theory]
    [InlineData("ls -la", false, PasteWarning.Auto, null)]
    [InlineData("rm -rf build\nmake", false, PasteWarning.Auto, PasteConcern.CouldRunCommands)]
    [InlineData("echo done\r", false, PasteWarning.Auto, PasteConcern.CouldRunCommands)] // one line, but it ends in Enter
    [InlineData("line one\nline two", true, PasteWarning.Auto, null)]                    // bracketed: the shell holds it
    [InlineData("line one\nline two", true, PasteWarning.Multiline, PasteConcern.MultipleLines)]
    [InlineData("line one\nline two", false, PasteWarning.Multiline, PasteConcern.CouldRunCommands)]
    [InlineData("line one\nline two", false, PasteWarning.Never, null)]
    public void MultilinePastesAskWhenTheSettingSaysSo(string text, bool bracketed, PasteWarning warning, PasteConcern? expected) =>
        Assert.Equal(expected, PasteCheck.Concern(text, bracketed, warning, warnLarge: true));

    [Fact]
    public void LargePastesAskWhenTurnedOn()
    {
        var big = new string('x', PasteCheck.LargeBytes + 1);
        Assert.Equal(PasteConcern.Large, PasteCheck.Concern(big, bracketed: true, PasteWarning.Auto, warnLarge: true));
        Assert.Null(PasteCheck.Concern(big, bracketed: true, PasteWarning.Auto, warnLarge: false));
        Assert.Null(PasteCheck.Concern(new string('x', PasteCheck.LargeBytes), bracketed: true, PasteWarning.Auto, warnLarge: true));
    }

    [Theory]
    [InlineData("one", 1)]
    [InlineData("one\ntwo", 2)]
    [InlineData("one\r\ntwo\r\n", 2)]
    [InlineData("one\n", 1)]
    public void CountsLines(string text, int lines) => Assert.Equal(lines, PasteCheck.Lines(text));

    [Fact]
    public void TheSettingsReadBack()
    {
        var config = ConfigFile.Parse("[terminal]\npaste-warning = \"multiline\"\nlarge-paste-warning = false\ncopy-on-select = true\nword-boundaries = \" ,\"\n\n[window]\nfocus-follows-mouse = true\n");
        Assert.Empty(config.Warnings);
        var g = config.Global;
        Assert.Equal((PasteWarning.Multiline, false, true, " ,", true), (g.PasteWarning, g.LargePasteWarning, g.CopyOnSelect, g.WordBoundaries, g.FocusFollowsMouse));
    }
}
