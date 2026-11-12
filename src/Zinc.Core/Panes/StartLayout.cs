namespace Zinc.Panes;

/// <summary>
/// How a workspace started from the new-workspace page is split. Every pane runs the same profile, in the same folder.
/// </summary>
public enum StartLayout
{
    One,
    SideBySide,
    Stacked,

    /// <summary>One pane on the left, two stacked on the right.</summary>
    BigAndTwo,

    /// <summary>Two by two.</summary>
    Grid,
}

public static class StartLayouts
{
    public static IReadOnlyList<StartLayout> All { get; } = Enum.GetValues<StartLayout>();

    public static string Title(StartLayout layout) => layout switch
    {
        StartLayout.One => "One pane",
        StartLayout.SideBySide => "Side by side",
        StartLayout.Stacked => "Stacked",
        StartLayout.BigAndTwo => "One big, two small",
        _ => "Grid of four",
    };

    public static int PaneCount(StartLayout layout) => layout switch
    {
        StartLayout.One => 1,
        StartLayout.SideBySide or StartLayout.Stacked => 2,
        StartLayout.BigAndTwo => 3,
        _ => 4,
    };

    /// <summary>
    /// A workspace in <paramref name="layout"/>, each pane from <paramref name="newPane"/>. The first (top left) has focus.
    /// </summary>
    public static Workspace<T> Build<T>(StartLayout layout, Func<T> newPane) where T : class
    {
        var workspace = new Workspace<T>(newPane());
        var first = workspace.ActiveGroup;
        switch (layout)
        {
            case StartLayout.SideBySide:
                workspace.Split(first, PaneDirection.Right, newPane());
                break;
            case StartLayout.Stacked:
                workspace.Split(first, PaneDirection.Down, newPane());
                break;
            case StartLayout.BigAndTwo:
                var right = workspace.Split(first, PaneDirection.Right, newPane());
                workspace.Split(right, PaneDirection.Down, newPane());
                break;
            case StartLayout.Grid:
                var topRight = workspace.Split(first, PaneDirection.Right, newPane());
                workspace.Split(first, PaneDirection.Down, newPane());
                workspace.Split(topRight, PaneDirection.Down, newPane());
                break;
        }

        workspace.Activate(first.Active);
        return workspace;
    }
}
