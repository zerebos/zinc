using Phosphor.Panes;

namespace Phosphor.Tests;

public class WorkspaceTests
{
    private sealed record T(string Name);

    [Fact]
    public void StartsWithOneGroupHoldingTheFirstTab()
    {
        var a = new T("a");
        var ws = new Workspace<T>(a);
        Assert.Same(a, ws.Active);
        Assert.Single(ws.Groups);
        Assert.Equal([a], ws.Tabs);
    }

    [Fact]
    public void SplitCreatesAFocusedGroup()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        var right = ws.Split(ws.ActiveGroup, PaneDirection.Right, b);

        Assert.Same(right, ws.ActiveGroup);
        Assert.Same(b, ws.Active);
        Assert.Equal([a, b], ws.Tabs);
        Assert.Same(ws.GroupOf(a), ws.Neighbor(right, PaneDirection.Left));
    }

    [Fact]
    public void ClosingTheLastTabOfAGroupRemovesItAndMovesFocus()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        ws.Split(ws.ActiveGroup, PaneDirection.Down, c);

        Assert.True(ws.Close(c));
        Assert.Equal(2, ws.Groups.Count());
        Assert.Same(b, ws.Active); // c's sibling takes focus

        Assert.True(ws.Close(a)); // not focused: focus stays on b
        Assert.Same(b, ws.Active);
        Assert.False(ws.Close(b)); // workspace empty
    }

    [Fact]
    public void GroupsHoldSeveralTabs()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        var group = ws.ActiveGroup;
        group.Add(b);
        group.Add(c, index: 0);

        Assert.Equal([c, a, b], group.Tabs);
        Assert.Same(c, ws.Active);

        ws.Activate(a);
        Assert.True(ws.Close(a)); // right neighbor is shown next
        Assert.Same(b, ws.Active);
        Assert.Single(ws.Groups);

        Assert.True(ws.Close(b));
        Assert.Same(c, ws.Active);
    }

    [Fact]
    public void ActivateFocusesTheTabsGroup()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Down, b);

        ws.Activate(a);
        Assert.Same(ws.GroupOf(a), ws.ActiveGroup);
        Assert.Throws<ArgumentException>(() => ws.Activate(new T("stranger")));
    }

    [Fact]
    public void ResizeMovesTheGroupsDivider()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Right, b);

        var split = ws.Resize(ws.ActiveGroup, PaneDirection.Left, 0.1);
        Assert.NotNull(split);
        Assert.Equal(0.4, split.Ratio, 6);
    }
}
