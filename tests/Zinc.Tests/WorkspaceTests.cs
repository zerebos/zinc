using Zinc.Panes;

namespace Zinc.Tests;

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

    [Fact]
    public void MaximizeTogglesTheFocusedGroup()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        ws.ToggleMaximize();
        Assert.Null(ws.Maximized); // a lone pane has nothing to hide

        var right = ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        ws.ToggleMaximize();
        Assert.Same(right, ws.Maximized);
        ws.ToggleMaximize();
        Assert.Null(ws.Maximized);
    }

    [Fact]
    public void MaximizeStaysWhileFocusStaysInItsGroup()
    {
        T a = new("a"), b = new("b"), c = new("c"), d = new("d");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        var group = ws.ActiveGroup;
        group.Add(c);
        ws.ToggleMaximize();

        ws.Activate(b); // another tab of the same pane
        Assert.Same(group, ws.Maximized);
        group.Add(d);
        Assert.True(ws.Close(d)); // one of its tabs closes, the pane stays
        Assert.Same(group, ws.Maximized);
        Assert.True(ws.Close(a)); // a hidden pane closes
        Assert.Same(group, ws.Maximized);
    }

    [Fact]
    public void FocusingAnotherPaneShowsThemAll()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        ws.ToggleMaximize();
        ws.Activate(a);
        Assert.Null(ws.Maximized);

        ws.Activate(b); // and coming back doesn't maximize it again
        Assert.Null(ws.Maximized);
    }

    [Fact]
    public void SplittingClosingOrMovingTheMaximizedPaneShowsThemAll()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        ws.ToggleMaximize();
        ws.Split(ws.ActiveGroup, PaneDirection.Down, c); // the new pane takes focus
        Assert.Null(ws.Maximized);

        ws.ToggleMaximize();
        Assert.True(ws.Close(c));
        Assert.Null(ws.Maximized);

        ws.ToggleMaximize();
        Assert.True(ws.Detach(ws.ActiveGroup));
        Assert.Null(ws.Maximized);
    }

    [Fact]
    public void MergingAnotherLayoutShowsThemAll()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        ws.ToggleMaximize();
        ws.Merge(ws.ActiveGroup, PaneDirection.Down, new Workspace<T>(c));
        Assert.Null(ws.Maximized);
    }

    [Fact]
    public void ClosingEveryTabOfAPaneRemovesItAndFocusesItsNeighbor()
    {
        // close-pane: each of the pane's tabs closes, in order (after one question for all of them).
        T a = new("a"), b = new("b"), c = new("c"), d = new("d");
        var ws = new Workspace<T>(a);
        var right = ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        right.Add(c);
        right.Add(d);
        ws.ToggleMaximize();

        foreach (var tab in right.Tabs.ToList())
        {
            Assert.True(ws.Close(tab));
        }

        Assert.Equal([a], ws.Tabs);
        Assert.Same(a, ws.Active);
        Assert.Null(ws.Maximized);
    }

    [Fact]
    public void JoiningAGroupAddsItsTabsInOrderAndShowsItsShownTab()
    {
        T a = new("a"), b = new("b"), c = new("c"), d = new("d");
        var target = new TabGroup<T>(a);
        target.Add(b);
        var other = new TabGroup<T>(c);
        other.Add(d);
        other.Activate(c);

        target.Join(other, index: 1);
        Assert.Equal([a, c, d, b], target.Tabs);
        Assert.Same(c, target.Active);
    }

    [Fact]
    public void JoiningAGroupWithoutAnIndexPutsItsTabsAfterTheShownTab()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var target = new TabGroup<T>(a);
        target.Add(b);
        target.Activate(a);

        target.Join(new TabGroup<T>(c));
        Assert.Equal([a, c, b], target.Tabs);
        Assert.Throws<ArgumentException>(() => target.Join(target));
    }

    [Fact]
    public void MovingAPaneToAnotherPanesEdgePutsItThere()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        var left = ws.ActiveGroup;
        var middle = ws.Split(left, PaneDirection.Right, b);
        var right = ws.Split(middle, PaneDirection.Right, c);

        // a | b | c  ->  b | a | c, then c below a
        Assert.True(ws.MoveGroup(left, middle, PaneDirection.Right));
        Assert.Same(left, ws.ActiveGroup);
        Assert.Equal([b, a, c], ws.Tabs);
        Assert.Same(middle, ws.Neighbor(left, PaneDirection.Left));
        Assert.Same(right, ws.Neighbor(left, PaneDirection.Right));

        Assert.True(ws.MoveGroup(right, left, PaneDirection.Down));
        Assert.Same(left, ws.Neighbor(right, PaneDirection.Up));
        Assert.Equal(3, ws.Layout.Count);
    }

    [Fact]
    public void MovingAPaneBesideItsOnlyNeighborSwapsThem()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        var left = ws.ActiveGroup;
        var right = ws.Split(left, PaneDirection.Right, b);

        Assert.True(ws.MoveGroup(left, right, PaneDirection.Right));
        Assert.Equal([b, a], ws.Tabs);
        Assert.Same(left, ws.ActiveGroup);
    }

    [Fact]
    public void MovingAPaneIntoAnotherJoinsItsTabsAndRemovesIt()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        var left = ws.ActiveGroup;
        var right = ws.Split(left, PaneDirection.Right, b);
        right.Add(c);

        Assert.True(ws.MoveGroup(right, left, null, index: 0));
        Assert.Equal([left], ws.Groups);
        Assert.Equal([b, c, a], left.Tabs);
        Assert.Same(c, ws.Active); // the pane showed c, so it still does
    }

    [Fact]
    public void MovingAPaneOntoItselfDoesNothing()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        var right = ws.Split(ws.ActiveGroup, PaneDirection.Right, b);

        Assert.False(ws.MoveGroup(right, right, PaneDirection.Left));
        Assert.False(ws.MoveGroup(right, right, null));
        Assert.Equal([a, b], ws.Tabs);
        Assert.Equal(2, ws.Layout.Count);
    }

    [Fact]
    public void MovingTheMaximizedPaneShowsThemAll()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        var middle = ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        var right = ws.Split(middle, PaneDirection.Right, c);
        ws.ToggleMaximize();
        Assert.Same(right, ws.Maximized);

        ws.MoveGroup(right, ws.GroupOf(a)!, PaneDirection.Left);
        Assert.Null(ws.Maximized);
    }

    [Fact]
    public void PlacingAGroupFromAnotherWorkspaceFocusesIt()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var source = new Workspace<T>(a);
        var moving = source.Split(source.ActiveGroup, PaneDirection.Right, b);
        var ws = new Workspace<T>(c);

        Assert.True(source.Detach(moving));
        ws.Place(moving, ws.ActiveGroup, PaneDirection.Down);
        Assert.Same(moving, ws.ActiveGroup);
        Assert.Equal([c, b], ws.Tabs);
        Assert.Throws<ArgumentException>(() => ws.Place(moving, ws.GroupOf(c)!, PaneDirection.Up));
    }

    [Fact]
    public void ClosingEveryTabOfTheOnlyPaneEmptiesTheWorkspace()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        ws.ActiveGroup.Add(b);
        Assert.True(ws.Close(a));
        Assert.False(ws.Close(b)); // the workspace closes with it
    }
}
