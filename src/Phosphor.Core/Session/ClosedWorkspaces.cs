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

/// <summary>
/// A window you closed, as a session saves an open one (place, size, its workspaces), so it can be
/// reopened; <see cref="Name"/> names its workspaces.
/// </summary>
public sealed record ClosedWindow(string Name, WindowState State, DateTimeOffset When)
{
    /// <summary>
    /// It was the last window open: the session still has it, and brings it back on the next start
    /// or tray click, which takes it off this list (else it could open twice).
    /// </summary>
    public bool WasLast { get; init; }

    /// <summary>What it had, for a menu: "3 workspaces", "1 workspace".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => $"{State.Workspaces.Count} workspace{(State.Workspaces.Count == 1 ? "" : "s")}";

    /// <summary>A window's name from its workspaces' titles: "notes", "notes and api", "notes, api and 2 more".</summary>
    public static string NameFor(IReadOnlyList<string> titles) => titles.Count switch
    {
        0 => "Window",
        1 => titles[0],
        2 => $"{titles[0]} and {titles[1]}",
        3 => $"{titles[0]}, {titles[1]} and {titles[2]}",
        _ => $"{titles[0]}, {titles[1]} and {titles.Count - 2} more",
    };
}

/// <summary>Windows closed lately, newest first (saved with the session): "Reopen closed window" brings them back.</summary>
public sealed class ClosedWindows
{
    public const int Capacity = 5;

    private readonly List<ClosedWindow> _closed = [];

    public ClosedWindows(IEnumerable<ClosedWindow>? saved = null) => _closed.AddRange((saved ?? []).Take(Capacity));

    public IReadOnlyList<ClosedWindow> All => _closed;

    public void Note(ClosedWindow closed)
    {
        _closed.Insert(0, closed);
        if (_closed.Count > Capacity)
        {
            _closed.RemoveAt(_closed.Count - 1);
        }
    }

    /// <summary>Takes <paramref name="closed"/> off the list (it's being reopened); false if it's gone already.</summary>
    public bool Take(ClosedWindow closed) => _closed.Remove(closed);
}
