namespace Phantom.Panes;

public enum PaneDirection
{
    Left,
    Right,
    Up,
    Down,
}

public enum SplitOrientation
{
    /// <summary>Side by side (vertical divider).</summary>
    Horizontal,

    /// <summary>Stacked (horizontal divider).</summary>
    Vertical,
}

/// <summary>A rectangle in layout space, where the whole tab is (0, 0, 1, 1).</summary>
public readonly record struct PaneRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public abstract class PaneNode
{
    public PaneSplit? Parent { get; internal set; }
}

public sealed class PaneLeaf<T>(T content) : PaneNode
{
    public T Content { get; } = content;
}

public sealed class PaneSplit : PaneNode
{
    internal PaneSplit(SplitOrientation orientation, PaneNode first, PaneNode second, double ratio)
    {
        Orientation = orientation;
        First = first;
        Second = second;
        Ratio = ratio;
    }

    public SplitOrientation Orientation { get; }
    public PaneNode First { get; internal set; }
    public PaneNode Second { get; internal set; }

    /// <summary>Share of the space given to <see cref="First"/>, in (0, 1).</summary>
    public double Ratio { get; internal set; }
}

/// <summary>
/// The panes of one tab: a binary tree of splits with leaves holding pane content.
/// Pure layout logic (no UI), so splitting, closing, focus navigation and resizing
/// are unit-testable.
/// </summary>
public sealed class PaneTree<T> where T : class
{
    public const double MinRatio = 0.1, MaxRatio = 0.9;

    public PaneTree(T content) => Root = new PaneLeaf<T>(content);

    /// <summary>The root node, or null once the last pane is removed.</summary>
    public PaneNode? Root { get; private set; }

    public IEnumerable<PaneLeaf<T>> Leaves => Root == null ? [] : LeavesOf(Root);

    public int Count => Leaves.Count();

    public PaneLeaf<T>? Find(T content) => Leaves.FirstOrDefault(l => EqualityComparer<T>.Default.Equals(l.Content, content));

    /// <summary>
    /// Splits <paramref name="target"/>, putting the new pane on side <paramref name="where"/>
    /// with <paramref name="newShare"/> of the target's space.
    /// </summary>
    public PaneLeaf<T> Split(PaneLeaf<T> target, PaneDirection where, T content, double newShare = 0.5)
    {
        var leaf = new PaneLeaf<T>(content);
        var orientation = where is PaneDirection.Left or PaneDirection.Right
            ? SplitOrientation.Horizontal
            : SplitOrientation.Vertical;
        var newFirst = where is PaneDirection.Left or PaneDirection.Up;
        var split = new PaneSplit(orientation, newFirst ? leaf : target, newFirst ? target : leaf,
            newFirst ? newShare : 1 - newShare);

        Replace(target, split);
        target.Parent = split;
        leaf.Parent = split;
        return leaf;
    }

    /// <summary>
    /// Removes a pane; its sibling takes the freed space. Returns the pane that should
    /// get focus next (the sibling side's pane nearest the removed one), or null if the
    /// tree is now empty.
    /// </summary>
    public PaneLeaf<T>? Remove(PaneLeaf<T> leaf)
    {
        var parent = leaf.Parent;
        if (parent == null)
        {
            Root = null;
            return null;
        }

        var removedWasFirst = parent.First == leaf;
        var sibling = removedWasFirst ? parent.Second : parent.First;
        Replace(parent, sibling);
        leaf.Parent = null;

        var candidates = LeavesOf(sibling).ToList();
        return removedWasFirst ? candidates[0] : candidates[^1];
    }

    /// <summary>Each pane's rectangle in layout space.</summary>
    public Dictionary<PaneLeaf<T>, PaneRect> Layout()
    {
        var result = new Dictionary<PaneLeaf<T>, PaneRect>();
        if (Root != null)
        {
            Place(Root, new PaneRect(0, 0, 1, 1), result);
        }

        return result;
    }

    /// <summary>
    /// The pane next to <paramref name="from"/> in <paramref name="direction"/>: among panes
    /// that lie on that side and share some of its extent, the closest, then the one that
    /// overlaps it most.
    /// </summary>
    public PaneLeaf<T>? Neighbor(PaneLeaf<T> from, PaneDirection direction)
    {
        const double eps = 1e-9;
        var layout = Layout();
        var f = layout[from];
        PaneLeaf<T>? best = null;
        double bestGap = double.MaxValue, bestOverlap = 0;

        foreach (var (leaf, r) in layout)
        {
            if (leaf == from)
            {
                continue;
            }

            var (gap, overlap) = direction switch
            {
                PaneDirection.Right => (r.X - f.Right, Overlap(f.Y, f.Bottom, r.Y, r.Bottom)),
                PaneDirection.Left => (f.X - r.Right, Overlap(f.Y, f.Bottom, r.Y, r.Bottom)),
                PaneDirection.Down => (r.Y - f.Bottom, Overlap(f.X, f.Right, r.X, r.Right)),
                _ => (f.Y - r.Bottom, Overlap(f.X, f.Right, r.X, r.Right)),
            };

            if (gap < -eps || overlap <= eps)
            {
                continue;
            }

            if (gap < bestGap - eps || (Math.Abs(gap - bestGap) <= eps && overlap > bestOverlap + eps))
            {
                best = leaf;
                bestGap = gap;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    /// <summary>
    /// Moves the nearest divider on <paramref name="direction"/>'s axis by
    /// <paramref name="delta"/> (share of that split) toward <paramref name="direction"/>.
    /// Returns the split that changed, or null if there is none on that axis or it
    /// is already at its limit.
    /// </summary>
    public PaneSplit? Resize(PaneLeaf<T> leaf, PaneDirection direction, double delta)
    {
        var axis = direction is PaneDirection.Left or PaneDirection.Right
            ? SplitOrientation.Horizontal
            : SplitOrientation.Vertical;
        for (var split = leaf.Parent; split != null; split = split.Parent)
        {
            if (split.Orientation != axis)
            {
                continue;
            }

            var sign = direction is PaneDirection.Right or PaneDirection.Down ? 1 : -1;
            var ratio = Math.Clamp(split.Ratio + sign * delta, MinRatio, MaxRatio);
            if (Math.Abs(ratio - split.Ratio) < 1e-12)
            {
                return null;
            }

            split.Ratio = ratio;
            return split;
        }

        return null;
    }

    /// <summary>Sets a split's ratio directly (divider drag), clamped to the allowed range.</summary>
    public static void SetRatio(PaneSplit split, double ratio) => split.Ratio = Math.Clamp(ratio, MinRatio, MaxRatio);

    /// <summary>Windows Terminal's "auto" split: along the pane's longer side.</summary>
    public static PaneDirection AutoDirection(double width, double height) =>
        width >= height ? PaneDirection.Right : PaneDirection.Down;

    private void Replace(PaneNode old, PaneNode replacement)
    {
        var parent = old.Parent;
        replacement.Parent = parent;
        if (parent == null)
        {
            Root = replacement;
        }
        else if (parent.First == old)
        {
            parent.First = replacement;
        }
        else
        {
            parent.Second = replacement;
        }
    }

    private static IEnumerable<PaneLeaf<T>> LeavesOf(PaneNode node) => node switch
    {
        PaneLeaf<T> leaf => [leaf],
        PaneSplit split => LeavesOf(split.First).Concat(LeavesOf(split.Second)),
        _ => [],
    };

    private static void Place(PaneNode node, PaneRect r, Dictionary<PaneLeaf<T>, PaneRect> result)
    {
        switch (node)
        {
            case PaneLeaf<T> leaf:
                result[leaf] = r;
                break;
            case PaneSplit { Orientation: SplitOrientation.Horizontal } s:
                var w = r.Width * s.Ratio;
                Place(s.First, r with { Width = w }, result);
                Place(s.Second, r with { X = r.X + w, Width = r.Width - w }, result);
                break;
            case PaneSplit s:
                var h = r.Height * s.Ratio;
                Place(s.First, r with { Height = h }, result);
                Place(s.Second, r with { Y = r.Y + h, Height = r.Height - h }, result);
                break;
        }
    }

    private static double Overlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0);
}
