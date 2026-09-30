using Phosphor.Commands;
using Phosphor.Input;
using Phosphor.Terminal;

namespace Phosphor.Tests;

public class ZoomTests
{
    [Theory]
    [InlineData(0, 100, 14f)]
    [InlineData(1, 110, 15.4f)]
    [InlineData(-2, 80, 11.2f)]
    public void LevelsAreTenPercentStepsOfTheConfiguredSize(int level, int percent, float size)
    {
        Assert.Equal(percent, ZoomSteps.Percent(level));
        Assert.Equal(size, ZoomSteps.Size(14f, level), 3);
    }

    [Theory]
    [InlineData(-20, 14f, -5)] // 6 DIPs is the smallest size: 50% of 14 is 7, 40% would be 5.6
    [InlineData(50, 14f, 41)]  // 72 DIPs the largest: 510% of 14 is 71.4
    [InlineData(-20, 60f, -9)] // never 0%
    [InlineData(3, 14f, 3)]
    public void ClampKeepsLevelsWhoseSizeFits(int level, float configured, int expected)
    {
        Assert.Equal(expected, ZoomSteps.Clamp(level, configured, 6f, 72f));
    }

    [Fact]
    public void ZoomActionsAreFoundByFontSizeToo()
    {
        var titles = new[] { TerminalAction.ZoomIn, TerminalAction.ZoomOut, TerminalAction.ZoomReset, TerminalAction.Find };
        var commands = titles.Select(a => new PaletteCommand(Actions.Title(a), null, () => { },
            SearchWords: Actions.SearchWords(a))).ToList();
        commands.Add(new PaletteCommand("Set font size", null, () => { }));

        var found = CommandFilter.Filter(commands, "font size").Select(m => m.Command.Title).ToList();
        Assert.Equal("Set font size", found[0]); // title matches first
        Assert.Equal(["Reset zoom", "Zoom in", "Zoom out"], found.Skip(1).Order());
        Assert.Empty(CommandFilter.Filter(commands, "font size").Last().Positions);
        Assert.Equal("Zoom in", CommandFilter.Filter(commands, "bigger").Single().Command.Title);
    }
}
