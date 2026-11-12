namespace Zinc.Panes;

/// <summary>
/// The tabs sharing one region of a workspace's layout, of which one is shown. Each tab is
/// one terminal. With pane tabs off, or until a second tab joins, a group is simply a pane.
/// </summary>
public sealed class TabGroup<T> where T : class
{
    private readonly List<T> _tabs = [];
    private static int _lastId;

    /// <summary>The pane's number for zn while Zinc runs (1, 2, 3 and so on, never reused in a run).</summary>
    public int Id { get; } = Interlocked.Increment(ref _lastId);

    public TabGroup(T first)
    {
        _tabs.Add(first);
        Active = first;
    }

    public IReadOnlyList<T> Tabs => _tabs;

    /// <summary>The tab shown in this group.</summary>
    public T Active { get; private set; }

    /// <summary>Anything the UI attaches to the group (its host element).</summary>
    public object? Tag { get; set; }

    public bool Contains(T tab) => _tabs.Contains(tab);

    /// <summary>Adds <paramref name="tab"/> after the active tab (or at <paramref name="index"/>) and shows it.</summary>
    public void Add(T tab, int? index = null)
    {
        var at = Math.Clamp(index ?? _tabs.IndexOf(Active) + 1, 0, _tabs.Count);
        _tabs.Insert(at, tab);
        Active = tab;
    }

    public void Activate(T tab)
    {
        if (!_tabs.Contains(tab))
        {
            throw new ArgumentException("Not in this group.", nameof(tab));
        }

        Active = tab;
    }

    /// <summary>
    /// Adds every tab of <paramref name="other"/>, in their order, after the active tab (or at <paramref name="index"/>),
    /// and shows the one <paramref name="other"/> showed. <paramref name="other"/> should be discarded.
    /// </summary>
    public void Join(TabGroup<T> other, int? index = null)
    {
        if (other == this)
        {
            throw new ArgumentException("Can't join a group to itself.", nameof(other));
        }

        var at = Math.Clamp(index ?? _tabs.IndexOf(Active) + 1, 0, _tabs.Count);
        _tabs.InsertRange(at, other.Tabs);
        Active = other.Active;
    }

    /// <summary>
    /// Moves <paramref name="tab"/> so it sits before the tab now at <paramref name="index"/>
    /// (an index counted with the tab still in place, as a drop position is, and past the end means last).
    /// </summary>
    public void Move(T tab, int index)
    {
        var from = _tabs.IndexOf(tab);
        if (from < 0)
        {
            throw new ArgumentException("Not in this group.", nameof(tab));
        }

        _tabs.RemoveAt(from);
        _tabs.Insert(Math.Clamp(index > from ? index - 1 : index, 0, _tabs.Count), tab);
    }

    /// <summary>
    /// Removes <paramref name="tab"/>. If it was shown, its right neighbor (else its left one)
    /// is shown next. Returns false when the group is now empty.
    /// </summary>
    public bool Remove(T tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return _tabs.Count > 0;
        }

        _tabs.RemoveAt(index);
        if (_tabs.Count == 0)
        {
            return false;
        }

        if (Active == tab)
        {
            Active = _tabs[Math.Min(index, _tabs.Count - 1)];
        }

        return true;
    }
}

/// <summary>
/// One workspace (a tab in the title bar, or an item in the sidebar). It holds a split layout whose regions are tab
/// groups, and which group has focus. Pure logic, no UI, so it's unit-testable.
/// </summary>
public sealed class Workspace<T> where T : class
{
    public Workspace(T first) : this(new TabGroup<T>(first))
    {
    }

    /// <summary>A workspace made of one existing group (e.g. a pane moved out of another workspace).</summary>
    public Workspace(TabGroup<T> group)
    {
        ActiveGroup = group;
        Layout = new PaneTree<TabGroup<T>>(group);
    }

    /// <summary>A workspace around a restored layout, with <paramref name="active"/> focused.</summary>
    internal Workspace(PaneTree<TabGroup<T>> layout, TabGroup<T> active)
    {
        Layout = layout;
        ActiveGroup = active;
    }

    public PaneTree<TabGroup<T>> Layout { get; }

    /// <summary>The group with focus.</summary>
    public TabGroup<T> ActiveGroup
    {
        get;
        private set
        {
            field = value;
            if (Maximized != value)
            {
                Maximized = null; // focus went to a pane that was hidden so show them all again
            }
        }
    }

    /// <summary>
    /// The group filling the workspace while the others are hidden (maximize-pane), or null. It's always the focused
    /// group, because anything that focuses another one (moving focus, splitting, closing or moving this one out) shows every pane
    /// again. Not saved with the layout.
    /// </summary>
    public TabGroup<T>? Maximized { get; private set; }

    /// <summary>Maximizes the focused group, or shows every pane again if one is maximized. A lone pane should stay as it is.</summary>
    public void ToggleMaximize() => Maximized = Maximized == null && Layout.Count > 1 ? ActiveGroup : null;

    /// <summary>The focused tab, which is the active group's shown tab.</summary>
    public T Active => ActiveGroup.Active;

    public IEnumerable<TabGroup<T>> Groups => Layout.Leaves.Select(l => l.Content);

    public IEnumerable<T> Tabs => Groups.SelectMany(g => g.Tabs);

    public TabGroup<T>? GroupOf(T tab) => Groups.FirstOrDefault(g => g.Contains(tab));

    /// <summary>Focuses <paramref name="tab"/>, so its group becomes active and shows it.</summary>
    public void Activate(T tab)
    {
        var group = GroupOf(tab) ?? throw new ArgumentException("Not in this workspace.", nameof(tab));
        group.Activate(tab);
        ActiveGroup = group;
    }

    /// <summary>Splits <paramref name="target"/>, putting a new group holding <paramref name="tab"/> on side <paramref name="where"/>, and focuses it.</summary>
    public TabGroup<T> Split(TabGroup<T> target, PaneDirection where, T tab, double newShare = 0.5)
    {
        var leaf = Layout.Find(target) ?? throw new ArgumentException("Not in this workspace.", nameof(target));
        var group = new TabGroup<T>(tab);
        Layout.Split(leaf, where, group, newShare);
        ActiveGroup = group;
        return group;
    }

    /// <summary>
    /// Puts <paramref name="group"/> (from another workspace, or just detached from this one) beside
    /// <paramref name="target"/> on side <paramref name="where"/>, and focuses it.
    /// </summary>
    public void Place(TabGroup<T> group, TabGroup<T> target, PaneDirection where, double newShare = 0.5)
    {
        var leaf = Layout.Find(target) ?? throw new ArgumentException("Not in this workspace.", nameof(target));
        if (Layout.Find(group) != null)
        {
            throw new ArgumentException("Already in this workspace.", nameof(group));
        }

        Layout.Split(leaf, where, group, newShare);
        ActiveGroup = group;
    }

    /// <summary>
    /// Adds <paramref name="group"/>'s tabs to <paramref name="target"/>'s (see <see cref="TabGroup{T}.Join"/>) and
    /// focuses them there. <paramref name="group"/> must not be in this layout, and should be discarded.
    /// </summary>
    public void Join(TabGroup<T> group, TabGroup<T> target, int? index = null)
    {
        if (Layout.Find(target) == null)
        {
            throw new ArgumentException("Not in this workspace.", nameof(target));
        }

        target.Join(group, index);
        ActiveGroup = target;
    }

    /// <summary>
    /// Moves <paramref name="group"/> within this workspace, either beside <paramref name="target"/> on side
    /// <paramref name="where"/>, or into its tabs (at <paramref name="index"/>) when <paramref name="where"/> is null,
    /// as a drop of the pane there does. Returns false (and changes nothing) for a move onto itself.
    /// </summary>
    public bool MoveGroup(TabGroup<T> group, TabGroup<T> target, PaneDirection? where, int? index = null)
    {
        if (group == target)
        {
            return false;
        }

        if (Layout.Find(group) == null || Layout.Find(target) == null)
        {
            throw new ArgumentException("Not in this workspace.");
        }

        Detach(group); // target stays
        if (where is { } side)
        {
            Place(group, target, side);
        }
        else
        {
            Join(group, target, index);
        }

        return true;
    }

    /// <summary>
    /// Removes <paramref name="tab"/>. An emptied group leaves the layout. If the closed tab's
    /// group had focus and is gone, focus moves to the nearest group. Returns false when the
    /// workspace is now empty.
    /// </summary>
    public bool Close(T tab)
    {
        if (GroupOf(tab) is not { } group)
        {
            return Layout.Root != null;
        }

        if (group.Remove(tab))
        {
            return true;
        }

        var next = Layout.Remove(Layout.Find(group)!);
        if (next == null)
        {
            return false;
        }

        if (ActiveGroup == group)
        {
            ActiveGroup = next.Content;
        }

        return true;
    }

    /// <summary>
    /// Takes <paramref name="group"/> out of the layout with its tabs intact (to move it
    /// elsewhere). Focus moves as for <see cref="Close"/>. Returns false when the workspace
    /// is now empty.
    /// </summary>
    public bool Detach(TabGroup<T> group)
    {
        var leaf = Layout.Find(group) ?? throw new ArgumentException("Not in this workspace.", nameof(group));
        var next = Layout.Remove(leaf);
        if (next == null)
        {
            return false;
        }

        if (ActiveGroup == group)
        {
            ActiveGroup = next.Content;
        }

        return true;
    }

    /// <summary>
    /// Moves <paramref name="other"/>'s whole layout beside <paramref name="target"/> on side
    /// <paramref name="where"/>. Its focused group gets focus here. <paramref name="other"/>
    /// is left empty and should be discarded.
    /// </summary>
    public void Merge(TabGroup<T> target, PaneDirection where, Workspace<T> other, double newShare = 0.5)
    {
        var leaf = Layout.Find(target) ?? throw new ArgumentException("Not in this workspace.", nameof(target));
        var focus = other.ActiveGroup;
        Layout.Graft(leaf, where, other.Layout, newShare);
        ActiveGroup = focus;
    }

    /// <summary>The group next to <paramref name="from"/> in <paramref name="direction"/>.</summary>
    public TabGroup<T>? Neighbor(TabGroup<T> from, PaneDirection direction) =>
        Layout.Find(from) is { } leaf ? Layout.Neighbor(leaf, direction)?.Content : null;

    /// <summary>Moves the nearest divider of <paramref name="group"/> (see <see cref="PaneTree{T}.Resize"/>).</summary>
    public PaneSplit? Resize(TabGroup<T> group, PaneDirection direction, double delta) =>
        Layout.Find(group) is { } leaf ? Layout.Resize(leaf, direction, delta) : null;
}
