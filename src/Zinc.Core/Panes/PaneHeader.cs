namespace Zinc.Panes;

/// <summary>What a pane's header shows, if it has one.</summary>
public enum PaneHeaderKind
{
    /// <summary>No header: the terminal starts at the pane's top edge.</summary>
    None,

    /// <summary>The shown tab's title, drawn as the tab (a pane with one tab, or with pane tabs off).</summary>
    Title,

    /// <summary>The pane's tabs.</summary>
    Tabs,
}

/// <summary>Which panes get a header, and which kind (<c>[window] pane-headers</c>, <c>pane-tabs</c>).</summary>
public static class PaneHeader
{
    /// <summary>
    /// The header for a pane holding <paramref name="tabs"/> tabs, in a workspace that's split or not. Several tabs show
    /// as tabs; one tab (or any number with pane tabs off) as its title. With pane tabs off, With several tabs never
    /// shows a header, as before headers had titles; Automatic and Always still do.
    /// </summary>
    public static PaneHeaderKind For(PaneHeaders mode, bool paneTabs, bool split, int tabs)
    {
        var several = paneTabs && tabs > 1;
        var shown = mode switch
        {
            PaneHeaders.Always => true,
            PaneHeaders.Auto => split || several,
            PaneHeaders.WithTabs => several,
            _ => false,
        };
        return !shown ? PaneHeaderKind.None : several ? PaneHeaderKind.Tabs : PaneHeaderKind.Title;
    }

    /// <summary>
    /// Whether a pane shows the accent line (<c>pane-focus</c>): on its shown tab whenever it has focus, as tabs always
    /// did; on a title header or along the top of a pane without a header only when there are other panes to tell
    /// it from (<paramref name="several"/>: split, nothing maximized; decision 33). Ring draws the line too until the
    /// ring exists.
    /// </summary>
    public static bool AccentLine(PaneFocus focus, PaneHeaderKind header, bool focused, bool several) =>
        focused && focus != PaneFocus.None && (header == PaneHeaderKind.Tabs || several);
}
