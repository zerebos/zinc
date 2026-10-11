using Zinc.Panes;

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
    [InlineData(PaneFocus.Ring, Title, true, true, true)]         // the line stands in until the ring exists
    public void AccentLineMarksTheFocusedPane(PaneFocus focus, PaneHeaderKind header, bool focused, bool several, bool expected) =>
        Assert.Equal(expected, PaneHeader.AccentLine(focus, header, focused, several));
}
