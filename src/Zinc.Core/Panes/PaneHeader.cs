using Zinc.Ghostty;
using Zinc.Terminal;

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

    /// <summary>
    /// Whether a header shows its buttons (<c>pane-header-buttons</c>): with Show the focused pane's and any under the
    /// pointer, with On hover only under the pointer. The pointer counts over the header, not the terminal (decision 40).
    /// </summary>
    public static bool ButtonsShown(PaneHeaderButtons mode, bool focused, bool hovered) => mode switch
    {
        PaneHeaderButtons.Show => focused || hovered,
        PaneHeaderButtons.Hover => hovered,
        _ => false,
    };

    /// <summary>
    /// What gives way in a narrow header (plan: "when narrow, Split and Maximize fold into ⋯ first"; the status slot
    /// "drops its words before its icon"). <paramref name="content"/> is what the tabs or title want,
    /// <paramref name="fixedWidth"/> what never folds (+, ⋯, ×, the status icon), <paramref name="foldable"/> Split and
    /// Maximize, <paramref name="words"/> the status words.
    /// </summary>
    public static HeaderFit Fit(double width, double content, double fixedWidth, double foldable, double words)
    {
        var kept = content + fixedWidth;
        return kept + foldable + words <= width ? new HeaderFit(false, true)
            : kept + words <= width ? new HeaderFit(true, true)
            : new HeaderFit(true, false);
    }

    /// <summary>A program's progress (OSC 9;4) in words for the status slot: "40%", "Working", "Paused", "Error".</summary>
    public static string? StatusWords(TerminalProgress progress) => progress.State switch
    {
        TerminalProgressState.Remove => null,
        TerminalProgressState.Pause => "Paused",
        TerminalProgressState.Error => "Error",
        TerminalProgressState.Set when progress.Value >= 0 => $"{progress.Value}%",
        _ => "Working",
    };
}

/// <summary>A header's fit: whether Split and Maximize fold into ⋯, and whether the status slot keeps its words.</summary>
public readonly record struct HeaderFit(bool Fold, bool Words);
