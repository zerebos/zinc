namespace Phosphor.Session;

/// <summary>
/// A workspace you closed, as a session saves an open one (layout, tabs and their folders, title,
/// color, folder), so it can be reopened; <see cref="Name"/> is what its tab showed.
/// </summary>
public sealed record ClosedWorkspace(string Name, WorkspaceState State, DateTimeOffset When)
{
    /// <summary>What it had, for a chip: "3 tabs, split", "1 tab".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary
    {
        get
        {
            var (panes, tabs) = Count(State.Layout);
            return $"{tabs} tab{(tabs == 1 ? "" : "s")}{(panes > 1 ? ", split" : "")}";
        }
    }

    private static (int Panes, int Tabs) Count(LayoutNode node) => node.Pane is { } pane
        ? (1, pane.Tabs.Count)
        : Add(node.First is { } first ? Count(first) : (0, 0), node.Second is { } second ? Count(second) : (0, 0));

    private static (int, int) Add((int, int) a, (int, int) b) => (a.Item1 + b.Item1, a.Item2 + b.Item2);
}

/// <summary>
/// Workspaces closed lately, newest first (every window's; saved with the session): the
/// new-workspace page offers them under Reopen.
/// </summary>
public sealed class ClosedWorkspaces
{
    public const int Capacity = 10;

    private readonly List<ClosedWorkspace> _closed = [];

    public ClosedWorkspaces(IEnumerable<ClosedWorkspace>? saved = null) => _closed.AddRange((saved ?? []).Take(Capacity));

    public IReadOnlyList<ClosedWorkspace> All => _closed;

    public void Note(ClosedWorkspace closed)
    {
        _closed.Insert(0, closed);
        if (_closed.Count > Capacity)
        {
            _closed.RemoveAt(_closed.Count - 1);
        }
    }

    /// <summary>Takes <paramref name="closed"/> off the list (it's being reopened); false if it's gone already.</summary>
    public bool Take(ClosedWorkspace closed) => _closed.Remove(closed);
}
