using Zinc.Panes;

namespace Zinc.Tests;

public class PaneTreeTests
{
    private static PaneRect R(double x, double y, double w, double h) => new(x, y, w, h);

    private static void AssertRect(PaneRect expected, PaneRect actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
        Assert.Equal(expected.Height, actual.Height, 9);
    }

    /// <summary>
    /// Builds:  +-----+-----+
    ///          |     |  b  |
    ///          |  a  +--+--+
    ///          |     |c | d|
    ///          +-----+--+--+
    /// </summary>
    private static (PaneTree<string> Tree, PaneLeaf<string> A, PaneLeaf<string> B, PaneLeaf<string> C, PaneLeaf<string> D) Grid()
    {
        var tree = new PaneTree<string>("a");
        var a = tree.Find("a")!;
        var b = tree.Split(a, PaneDirection.Right, "b");
        var c = tree.Split(b, PaneDirection.Down, "c");
        var d = tree.Split(c, PaneDirection.Right, "d");
        return (tree, a, b, c, d);
    }

    [Fact]
    public void SplitRightThenDownLaysOut()
    {
        var (tree, a, b, c, d) = Grid();
        Assert.Equal(["a", "b", "c", "d"], tree.Leaves.Select(l => l.Content));

        var layout = tree.Layout();
        AssertRect(R(0, 0, 0.5, 1), layout[a]);
        AssertRect(R(0.5, 0, 0.5, 0.5), layout[b]);
        AssertRect(R(0.5, 0.5, 0.25, 0.5), layout[c]);
        AssertRect(R(0.75, 0.5, 0.25, 0.5), layout[d]);
    }

    [Fact]
    public void SplitLeftAndUpPutNewPaneFirst()
    {
        var tree = new PaneTree<string>("a");
        var a = tree.Find("a")!;
        var left = tree.Split(a, PaneDirection.Left, "left", newShare: 0.3);
        var layout = tree.Layout();
        AssertRect(R(0, 0, 0.3, 1), layout[left]);
        AssertRect(R(0.3, 0, 0.7, 1), layout[a]);

        var up = tree.Split(a, PaneDirection.Up, "up");
        Assert.Equal(["left", "up", "a"], tree.Leaves.Select(l => l.Content));
        AssertRect(R(0.3, 0, 0.7, 0.5), tree.Layout()[up]);
    }

    [Theory]
    [InlineData("a", PaneDirection.Right, "b")]
    [InlineData("a", PaneDirection.Left, null)]
    [InlineData("b", PaneDirection.Left, "a")]
    [InlineData("b", PaneDirection.Down, "c")]
    [InlineData("c", PaneDirection.Up, "b")]
    [InlineData("d", PaneDirection.Up, "b")]
    [InlineData("c", PaneDirection.Right, "d")]
    [InlineData("d", PaneDirection.Left, "c")]
    [InlineData("c", PaneDirection.Left, "a")]
    [InlineData("d", PaneDirection.Right, null)]
    [InlineData("a", PaneDirection.Down, null)]
    public void NeighborFollowsGeometry(string from, PaneDirection direction, string? expected)
    {
        var (tree, _, _, _, _) = Grid();
        Assert.Equal(expected, tree.Neighbor(tree.Find(from)!, direction)?.Content);
    }

    [Fact]
    public void NeighborPrefersLargestOverlap()
    {
        // a | b over c, with c getting 30% of the height. From a, Right goes to b.
        var tree = new PaneTree<string>("a");
        var b = tree.Split(tree.Find("a")!, PaneDirection.Right, "b");
        tree.Split(b, PaneDirection.Down, "c", newShare: 0.3);
        Assert.Equal("b", tree.Neighbor(tree.Find("a")!, PaneDirection.Right)?.Content);
    }

    [Fact]
    public void RemoveGivesSpaceToSiblingAndFocusesNearest()
    {
        var (tree, a, b, c, d) = Grid();

        // When c is removed, d takes its place and gets the focus.
        Assert.Same(d, tree.Remove(c));
        AssertRect(R(0.5, 0.5, 0.5, 0.5), tree.Layout()[d]);

        // When a (the root's first child) is removed, the right column takes the whole tab,
        // and focus goes to that side's first pane.
        Assert.Same(b, tree.Remove(a));
        AssertRect(R(0, 0, 1, 0.5), tree.Layout()[b]);
        AssertRect(R(0, 0.5, 1, 0.5), tree.Layout()[d]);
        Assert.Null(tree.Root!.Parent);

        Assert.Same(b, tree.Remove(d));
        Assert.IsType<PaneLeaf<string>>(tree.Root);
        Assert.Null(tree.Remove(b));
        Assert.Null(tree.Root);
        Assert.Empty(tree.Leaves);
    }

    [Fact]
    public void RemovingSecondChildFocusesNearestSideOfSibling()
    {
        // (a | b) over c. Removing c focuses b (the last pane of the sibling side).
        var tree = new PaneTree<string>("a");
        var a = tree.Find("a")!;
        var c = tree.Split(a, PaneDirection.Down, "c");
        var b = tree.Split(a, PaneDirection.Right, "b");
        Assert.Same(b, tree.Remove(c));
    }

    [Fact]
    public void ResizeMovesNearestDividerOnAxis()
    {
        var (tree, a, b, c, d) = Grid();

        // From c, Right/Left move c|d's divider (the nearest horizontal split).
        var split = tree.Resize(c, PaneDirection.Right, 0.1);
        Assert.NotNull(split);
        AssertRect(R(0.5, 0.5, 0.3, 0.5), tree.Layout()[c]);

        // From c, Up moves b/c's divider up.
        tree.Resize(c, PaneDirection.Up, 0.1);
        AssertRect(R(0.5, 0, 0.5, 0.4), tree.Layout()[b]);

        // From a, Right moves the root divider right.
        tree.Resize(a, PaneDirection.Right, 0.2);
        AssertRect(R(0, 0, 0.7, 1), tree.Layout()[a]);

        // No vertical split above a.
        Assert.Null(tree.Resize(a, PaneDirection.Down, 0.1));
        _ = d;
    }

    [Fact]
    public void ResizeClampsAndReportsNoChangeAtLimit()
    {
        var tree = new PaneTree<string>("a");
        var a = tree.Find("a")!;
        tree.Split(a, PaneDirection.Right, "b");
        tree.Resize(a, PaneDirection.Right, 5);
        AssertRect(R(0, 0, PaneTree<string>.MaxRatio, 1), tree.Layout()[a]);
        Assert.Null(tree.Resize(a, PaneDirection.Right, 0.1));
    }

    [Theory]
    [InlineData(800, 400, PaneDirection.Right)]
    [InlineData(400, 800, PaneDirection.Down)]
    [InlineData(500, 500, PaneDirection.Right)]
    public void AutoSplitsAlongLongerSide(double w, double h, PaneDirection expected)
    {
        Assert.Equal(expected, PaneDirections.Auto(w, h));
    }
}
