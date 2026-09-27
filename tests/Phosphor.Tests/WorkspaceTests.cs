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
    public void DetachedGroupsKeepTheirTabs()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        var right = ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        right.Add(c);

        Assert.True(ws.Detach(right));
        Assert.Equal([a], ws.Tabs);
        Assert.Same(a, ws.Active);

        var moved = new Workspace<T>(right);
        Assert.Equal([b, c], moved.Tabs);
        Assert.Same(c, moved.Active);
        Assert.False(moved.Detach(right)); // its only group: now empty
    }

    [Fact]
    public void MergeGraftsTheOtherLayoutBesideATarget()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        var other = new Workspace<T>(b);
        other.Split(other.ActiveGroup, PaneDirection.Down, c);

        ws.Merge(ws.ActiveGroup, PaneDirection.Right, other);

        Assert.Equal([a, b, c], ws.Tabs);
        Assert.Same(c, ws.Active); // the merged workspace's focus comes along
        Assert.Null(other.Layout.Root);
        // b and c keep their stacking, to the right of a.
        var layout = ws.Layout.Layout();
        var rect = (T t) => layout.Single(kv => kv.Key.Content.Contains(t)).Value;
        Assert.Equal(new PaneRect(0, 0, 0.5, 1), rect(a));
        Assert.Equal(new PaneRect(0.5, 0, 0.5, 0.5), rect(b));
        Assert.Equal(new PaneRect(0.5, 0.5, 0.5, 0.5), rect(c));
    }

    [Theory]
    [InlineData("a", 0, "abcd")] // before itself: no change
    [InlineData("a", 1, "abcd")] // just after itself: no change
    [InlineData("a", 2, "bacd")]
    [InlineData("a", 4, "bcda")] // past the end
    [InlineData("d", 0, "dabc")]
    [InlineData("c", 1, "acbd")]
    [InlineData("b", 9, "acdb")]
    public void MoveUsesDropPositions(string tab, int index, string expected)
    {
        var tabs = "abcd".Select(c => new T(c.ToString())).ToList();
        var ws = new Workspace<T>(tabs[0]);
        foreach (var t in tabs.Skip(1))
        {
            ws.ActiveGroup.Add(t, ws.ActiveGroup.Tabs.Count);
        }

        ws.ActiveGroup.Move(tabs.Single(t => t.Name == tab), index);
        Assert.Equal(expected, string.Concat(ws.ActiveGroup.Tabs.Select(t => t.Name)));
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
