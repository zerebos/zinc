using Phosphor.Panes;
using Phosphor.Session;

namespace Phosphor.Tests;

public class ClosedWorkspacesTests
{
    private static LayoutNode Pane(int tabs) =>
        new(Pane: new PaneState([.. Enumerable.Range(0, tabs).Select(i => new TabState("p", "cmd", null, null))], 0, false));

    private static ClosedWorkspace Closed(string name, LayoutNode layout) =>
        new(name, new WorkspaceState(null, null, layout), DateTimeOffset.UtcNow);

    [Fact]
    public void TheSummarySaysWhatItHad()
    {
        Assert.Equal("1 tab", Closed("a", Pane(1)).Summary);
        Assert.Equal("3 tabs", Closed("a", Pane(3)).Summary);
        Assert.Equal("3 tabs, split", Closed("a", new LayoutNode(SplitOrientation.Horizontal, 0.5, Pane(2), Pane(1))).Summary);
    }

    [Fact]
    public void NewestFirstKeepsTenAndTakesOne()
    {
        var closed = new ClosedWorkspaces();
        for (var i = 0; i < 12; i++)
        {
            closed.Note(Closed($"w{i}", Pane(1)));
        }

        Assert.Equal(ClosedWorkspaces.Capacity, closed.All.Count);
        Assert.Equal("w11", closed.All[0].Name);
        var newest = closed.All[0];
        Assert.True(closed.Take(newest));
        Assert.False(closed.Take(newest)); // reopened already
        Assert.Equal("w10", closed.All[0].Name);
    }

    [Fact]
    public void TheyRoundTripThroughTheSessionFile()
    {
        var state = new SessionState(SessionState.CurrentVersion, [], ClosedWorkspaces: [Closed("notes", Pane(2))]);
        var read = SessionStore.Deserialize(SessionStore.Serialize(state));
        Assert.Equal("notes", read!.ClosedWorkspaces![0].Name);
        Assert.Equal("2 tabs", read.ClosedWorkspaces[0].Summary);
    }
}
