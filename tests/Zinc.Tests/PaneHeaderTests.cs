using Zinc.Ghostty;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc.Tests;

public class PaneHeaderTests
{
    private const PaneHeaderKind None = PaneHeaderKind.None, Title = PaneHeaderKind.Title, Tabs = PaneHeaderKind.Tabs;

    [Theory]
    // mode, pane tabs, split, tabs -> header
    [InlineData(PaneHeaders.WithTabs, true, false, 1, None)]
    [InlineData(PaneHeaders.WithTabs, true, true, 1, None)]
    [InlineData(PaneHeaders.WithTabs, true, false, 2, Tabs)]
    [InlineData(PaneHeaders.WithTabs, false, true, 1, None)]
    [InlineData(PaneHeaders.WithTabs, false, true, 2, None)]
    [InlineData(PaneHeaders.Auto, true, false, 1, None)]
    [InlineData(PaneHeaders.Auto, true, true, 1, Title)]
    [InlineData(PaneHeaders.Auto, true, false, 2, Tabs)]
    [InlineData(PaneHeaders.Auto, true, true, 3, Tabs)]
    [InlineData(PaneHeaders.Auto, false, false, 1, None)]
    [InlineData(PaneHeaders.Auto, false, true, 1, Title)]
    [InlineData(PaneHeaders.Auto, false, false, 2, None)]
    [InlineData(PaneHeaders.Always, true, false, 1, Title)]
    [InlineData(PaneHeaders.Always, true, true, 2, Tabs)]
    [InlineData(PaneHeaders.Always, false, false, 1, Title)]
    [InlineData(PaneHeaders.Always, false, true, 2, Title)]
    [InlineData(PaneHeaders.Never, true, true, 2, None)]
    [InlineData(PaneHeaders.Never, false, true, 1, None)]
    public void HeaderFollowsTheSettings(PaneHeaders mode, bool paneTabs, bool split, int tabs, PaneHeaderKind expected) =>
        Assert.Equal(expected, PaneHeader.For(mode, paneTabs, split, tabs));

    [Theory]
    // focus, header, focused, several -> line
    [InlineData(PaneFocus.TabAccent, Tabs, true, false, true)]   // a lone pane's shown tab, as always
    [InlineData(PaneFocus.TabAccent, Tabs, false, true, false)]
    [InlineData(PaneFocus.TabAccent, Title, true, false, false)]  // a lone (or maximized) pane: nothing to tell apart
    [InlineData(PaneFocus.TabAccent, Title, true, true, true)]
    [InlineData(PaneFocus.TabAccent, None, true, false, false)]
    [InlineData(PaneFocus.TabAccent, None, true, true, true)]
    [InlineData(PaneFocus.TabAccent, None, false, true, false)]
    [InlineData(PaneFocus.None, Tabs, true, true, false)]         // no mark anywhere, whatever the tab count
    [InlineData(PaneFocus.None, Title, true, true, false)]
    [InlineData(PaneFocus.None, None, true, true, false)]
    [InlineData(PaneFocus.Ring, Title, true, true, false)]        // the ring marks it instead
    [InlineData(PaneFocus.Ring, Tabs, true, false, false)]
    public void AccentLineMarksTheFocusedPane(PaneFocus focus, PaneHeaderKind header, bool focused, bool several, bool expected) =>
        Assert.Equal(expected, PaneHeader.AccentLine(focus, header, focused, several));

    [Theory]
    // focus, focused, several -> ring
    [InlineData(PaneFocus.Ring, true, true, true)]
    [InlineData(PaneFocus.Ring, false, true, false)]
    [InlineData(PaneFocus.Ring, true, false, false)]   // a lone or maximized pane: nothing to tell apart
    [InlineData(PaneFocus.TabAccent, true, true, false)]
    [InlineData(PaneFocus.None, true, true, false)]
    public void RingGoesAroundTheFocusedPaneAmongOthers(PaneFocus focus, bool focused, bool several, bool expected) =>
        Assert.Equal(expected, PaneHeader.Ring(focus, focused, several));

    [Theory]
    // mode, focused, hovered -> shown
    [InlineData(PaneHeaderButtons.Hide, true, true, false)]
    [InlineData(PaneHeaderButtons.Always, false, false, true)]
    [InlineData(PaneHeaderButtons.Focused, true, false, true)]
    [InlineData(PaneHeaderButtons.Focused, false, false, false)]
    [InlineData(PaneHeaderButtons.Focused, false, true, true)]
    [InlineData(PaneHeaderButtons.Hover, true, false, false)]
    [InlineData(PaneHeaderButtons.Hover, false, true, true)]
    public void ButtonsFollowTheSetting(PaneHeaderButtons mode, bool focused, bool hovered, bool expected) =>
        Assert.Equal(expected, PaneHeader.ButtonsShown(mode, focused, hovered));

    [Fact]
    public void SplitAndMaximizeFoldBeforeTheStatusLosesItsWords()
    {
        // content 200 + fixed 100 + foldable 60 + words 40 = 400
        Assert.Equal(new HeaderFit(false, true), PaneHeader.Fit(400, 200, 100, 60, 40));
        Assert.Equal(new HeaderFit(true, true), PaneHeader.Fit(399, 200, 100, 60, 40));
        Assert.Equal(new HeaderFit(true, true), PaneHeader.Fit(340, 200, 100, 60, 40));
        Assert.Equal(new HeaderFit(true, false), PaneHeader.Fit(339, 200, 100, 60, 40));
        Assert.Equal(new HeaderFit(false, true), PaneHeader.Fit(300, 200, 100, 0, 0)); // nothing to fold
    }

    [Fact]
    public void StatusSaysWhatTheProgressIs()
    {
        Assert.Null(PaneHeader.StatusWords(TerminalProgress.None));
        Assert.Equal("40%", PaneHeader.StatusWords(new TerminalProgress(TerminalProgressState.Set, 40)));
        Assert.Equal("Working", PaneHeader.StatusWords(new TerminalProgress(TerminalProgressState.Indeterminate, -1)));
        Assert.Equal("Working", PaneHeader.StatusWords(new TerminalProgress(TerminalProgressState.Set, -1)));
        Assert.Equal("Paused", PaneHeader.StatusWords(new TerminalProgress(TerminalProgressState.Pause, 40)));
        Assert.Equal("Error", PaneHeader.StatusWords(new TerminalProgress(TerminalProgressState.Error, 40)));
    }

    [Fact]
    public void StatusSaysTheProgramExitedBeforeAnyProgress()
    {
        var progress = new TerminalProgress(TerminalProgressState.Set, 40);
        Assert.Equal("Exited (code 255)", PaneHeader.StatusWords(progress, 255)); // a dropped ssh session
        Assert.Equal("Exited", PaneHeader.StatusWords(TerminalProgress.None, 0)); // a clean exit kept by close-on-exit = never
        Assert.Equal("Exited (code 0xC000013A)", PaneHeader.StatusWords(TerminalProgress.None, 0xC000013A));
    }

    // Three 100 px titles, the middle one shown: a tab is 60 px beside its title, 50 px as an icon; the + 30, the count 70.
    private static TabsFit FitThree(double room) => PaneHeader.FitTabs(room, [100, 100, 100], 1, 60, 50, 30, 70);

    private const double Whole = double.PositiveInfinity;

    [Fact]
    public void OtherTabsShortenThenBecomeIconsThenFold()
    {
        Assert.Equal(new TabsFit(TabsStage.Full, Whole, Whole), FitThree(510));
        Assert.Equal(new TabsFit(TabsStage.Short, 99, Whole), FitThree(509)); // 199 px for the others' titles
        Assert.Equal(new TabsFit(TabsStage.Short, 64, 99), FitThree(437)); // the others at the shortest: the shown tab gives way
        Assert.Equal(new TabsFit(TabsStage.Short, 64, 64), FitThree(402)); // every tab at the shortest
        Assert.Equal(new TabsFit(TabsStage.Icons, Whole, Whole), FitThree(401));
        Assert.Equal(new TabsFit(TabsStage.Icons, Whole, 64), FitThree(254)); // the shown tab at the shortest beside two icons
        Assert.Equal(new TabsFit(TabsStage.Folded, Whole, 123), FitThree(253)); // what the shown title keeps beside the count
        Assert.Equal(new TabsFit(TabsStage.Folded, Whole, 0), FitThree(50));
    }

    [Fact]
    public void AMenuFoldsTheTabsAtEveryWidth()
    {
        Assert.Equal(new TabsFit(TabsStage.Folded, Whole, 870), PaneHeader.FitTabs(1000, [100, 100, 100], 1, 60, 50, 30, 70, menu: true));
        Assert.Equal(FitThree(253), PaneHeader.FitTabs(253, [100, 100, 100], 1, 60, 50, 30, 70, menu: true));
    }

    [Fact]
    public void ShorteningCapsOnlyTheLongTitles()
    {
        // The shown tab is the first (30 px); the others' titles get 170 px, then 250.
        Assert.Equal(new TabsFit(TabsStage.Short, 85, Whole), PaneHeader.FitTabs(410, [30, 200, 100], 0, 60, 50, 30, 70));
        Assert.Equal(new TabsFit(TabsStage.Short, 150, Whole), PaneHeader.FitTabs(490, [30, 200, 100], 0, 60, 50, 30, 70));
    }

    [Theory]
    [InlineData(300, TabsStage.Icons)]
    [InlineData(253, TabsStage.Folded)]
    public void TitlesNeverSwitchBetweenIconsAndTheCount(double room, TabsStage stage)
    {
        // A new tab's profile name, then its folder ("~"), or a long command: the stage stays (decision 50).
        foreach (var shownTitle in new double[] { 8, 40, 180 })
        {
            Assert.Equal(stage, PaneHeader.FitTabs(room, [100, shownTitle, 100], 1, 60, 50, 30, 70).Stage);
        }
    }

    [Theory]
    [InlineData(1u, "1")]
    [InlineData(255u, "255")]
    [InlineData(0xFFFFu, "65535")]
    [InlineData(0x10000u, "0x00010000")]
    [InlineData(0xC000013Au, "0xC000013A")]
    [InlineData(0xFFFFFFFFu, "0xFFFFFFFF")]
    public void ExitCodesReadAsPeopleKnowThem(uint code, string expected) => Assert.Equal(expected, ExitCodes.Format(code));
}
