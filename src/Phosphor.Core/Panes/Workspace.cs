namespace Phosphor.Panes;

/// <summary>
/// The tabs sharing one region of a workspace's layout; one of them is shown. (Each tab is
/// one terminal. With tab groups off, or until a second tab joins, a group is simply a pane.)
/// </summary>
public sealed class TabGroup<T> where T : class
{
    private readonly List<T> _tabs = [];

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
/// One workspace (a title-bar tab): a split layout whose regions are tab groups, and which
/// group has focus. Pure logic, no UI, so it's unit-testable.
/// </summary>
public sealed class Workspace<T> where T : class
{
    public Workspace(T first)
    {
        ActiveGroup = new TabGroup<T>(first);
        Layout = new PaneTree<TabGroup<T>>(ActiveGroup);
    }

    public PaneTree<TabGroup<T>> Layout { get; }

    /// <summary>The group with focus.</summary>
    public TabGroup<T> ActiveGroup { get; private set; }

    /// <summary>The focused tab: the active group's shown tab.</summary>
    public T Active => ActiveGroup.Active;

    public IEnumerable<TabGroup<T>> Groups => Layout.Leaves.Select(l => l.Content);

    public IEnumerable<T> Tabs => Groups.SelectMany(g => g.Tabs);

    public TabGroup<T>? GroupOf(T tab) => Groups.FirstOrDefault(g => g.Contains(tab));

    /// <summary>Focuses <paramref name="tab"/>: its group becomes active and shows it.</summary>
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
    /// Removes <paramref name="tab"/>; an emptied group leaves the layout. If the closed tab's
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

    /// <summary>The group next to <paramref name="from"/> in <paramref name="direction"/>.</summary>
    public TabGroup<T>? Neighbor(TabGroup<T> from, PaneDirection direction) =>
        Layout.Find(from) is { } leaf ? Layout.Neighbor(leaf, direction)?.Content : null;

    /// <summary>Moves the nearest divider of <paramref name="group"/> (see <see cref="PaneTree{T}.Resize"/>).</summary>
    public PaneSplit? Resize(TabGroup<T> group, PaneDirection direction, double delta) =>
        Layout.Find(group) is { } leaf ? Layout.Resize(leaf, direction, delta) : null;
}
