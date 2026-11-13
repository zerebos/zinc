using Zinc.Panes;
using Zinc.Session;

namespace Zinc.Tests;

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

    private static ClosedWindow Window(params string[] titles) =>
        new(ClosedWindow.NameFor(titles), new WindowState(10, 20, 800, 600, false, 0,
            [.. titles.Select(t => new WorkspaceState(t, null, Pane(1)))]), DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(new string[0], "Window")]
    [InlineData(new[] { "notes" }, "notes")]
    [InlineData(new[] { "notes", "api" }, "notes and api")]
    [InlineData(new[] { "notes", "api", "logs" }, "notes, api, and logs")]
    [InlineData(new[] { "notes", "api", "logs", "db", "web" }, "notes, api, and 3 more")]
    public void AClosedWindowIsNamedForItsWorkspaces(string[] titles, string name) => Assert.Equal(name, ClosedWindow.NameFor(titles));

    [Fact]
    public void ClosedWindowsKeepFiveNewestFirst()
    {
        var closed = new ClosedWindows();
        for (var i = 0; i < 7; i++)
        {
            closed.Note(Window($"w{i}"));
        }

        Assert.Equal(ClosedWindows.Capacity, closed.All.Count);
        Assert.Equal(("w6", "1 workspace"), (closed.All[0].Name, closed.All[0].Summary));
        Assert.True(closed.Take(closed.All[0]));
        Assert.Equal("w5", closed.All[0].Name);
    }

    [Fact]
    public void ClosedWindowsRoundTripThroughTheSessionFile()
    {
        var window = Window("notes", "api") with { WasLast = true };
        var read = SessionStore.Deserialize(SessionStore.Serialize(new SessionState(SessionState.CurrentVersion, [], ClosedWindows: [window])));
        var back = read!.ClosedWindows![0];
        Assert.Equal(("notes and api", "2 workspaces", true, 800), (back.Name, back.Summary, back.WasLast, back.State.Width));
    }
}
