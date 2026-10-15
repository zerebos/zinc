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
    /// Whether a pane shows the accent line (<c>pane-focus = tab-accent</c>): on its shown tab whenever it has focus, as
    /// tabs always did; on a title header or along the top of a pane without a header only when there are other panes
    /// to tell it from (<paramref name="several"/>: split, nothing maximized).
    /// </summary>
    public static bool AccentLine(PaneFocus focus, PaneHeaderKind header, bool focused, bool several) =>
        focused && focus == PaneFocus.TabAccent && (header == PaneHeaderKind.Tabs || several);

    /// <summary>
    /// Whether a pane shows the ring (<c>pane-focus = ring</c>): around the focused pane when there are others to tell it
    /// from (<paramref name="several"/>), so not on a lone pane or a maximized one.
    /// </summary>
    public static bool Ring(PaneFocus focus, bool focused, bool several) => focused && several && focus == PaneFocus.Ring;

    /// <summary>
    /// Whether a header shows its buttons (<c>pane-header-buttons</c>): always; the focused pane's and any under the
    /// pointer; or only under the pointer. The pointer counts over the header, not the terminal (decision 40).
    /// </summary>
    public static bool ButtonsShown(PaneHeaderButtons mode, bool focused, bool hovered) => mode switch
    {
        PaneHeaderButtons.Always => true,
        PaneHeaderButtons.Focused => focused || hovered,
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

    /// <summary>The narrowest a shortened tab's title gets before the tabs become icons.</summary>
    public const double ShortestTitle = 64;

    /// <summary>
    /// How a pane's tabs fit in <paramref name="room"/> (decisions 49, 50). The others give way first: they shorten
    /// (down to <see cref="ShortestTitle"/>, then the shown tab does too), then become an icon and a mark, then fold all
    /// at once into a count ("4 of 5") that takes the +'s place. Which of those stages applies depends only on the room
    /// and the number of tabs, never on the titles: a title that changes (a new tab's profile name giving way to its
    /// folder, a command) only lengthens or shortens tabs, and never flips the row between icons and the count.
    /// <paramref name="titles"/> are each tab's title width in full, <paramref name="chrome"/> a tab's width beside its
    /// title, <paramref name="iconTab"/> a tab shown as its icon and mark; <paramref name="add"/> and
    /// <paramref name="count"/> the + and the count, after the tabs. With <paramref name="menu"/> (<c>pane-tabs-as = menu</c>)
    /// the tabs are folded at every width.
    /// </summary>
    public static TabsFit FitTabs(double room, IReadOnlyList<double> titles, int shown, double chrome, double iconTab,
        double add, double count, bool menu = false)
    {
        if (menu)
        {
            return Folded();
        }

        const double whole = double.PositiveInfinity;
        var others = titles.Where((_, i) => i != shown).ToList();
        var budget = room - add - titles.Count * chrome; // what all the titles may take
        if (titles.Sum() <= budget)
        {
            return new TabsFit(TabsStage.Full, whole, whole);
        }

        if (titles.Count * ShortestTitle <= budget)
        {
            // The others shorten while the shown tab stays whole; once they're all at the shortest, the shown tab shortens.
            var shortened = others.Sum(t => Math.Min(t, ShortestTitle));
            return shortened + titles[shown] <= budget
                ? new TabsFit(TabsStage.Short, ShortCap(others, budget - titles[shown]), whole)
                : new TabsFit(TabsStage.Short, ShortestTitle, Math.Floor(budget - shortened));
        }

        var left = room - add - others.Count * iconTab - chrome; // for the shown title beside the icons
        if (left >= ShortestTitle)
        {
            return new TabsFit(TabsStage.Icons, whole, left >= titles[shown] ? whole : Math.Floor(left));
        }

        return Folded();

        TabsFit Folded() => new(TabsStage.Folded, whole, Math.Max(0, Math.Floor(room - count - chrome)));
    }

    /// <summary>The widest cap on the titles that keeps their sum within the budget (short titles stay whole).</summary>
    private static double ShortCap(List<double> titles, double budget)
    {
        titles.Sort();
        double whole = 0;
        for (var i = 0; i < titles.Count; i++)
        {
            var cap = (budget - whole) / (titles.Count - i);
            if (cap <= titles[i])
            {
                return Math.Max(ShortestTitle, Math.Floor(cap));
            }

            whole += titles[i];
        }

        return double.PositiveInfinity;
    }

    /// <summary>
    /// The shown terminal's status in words for the status slot: its program's exit while the pane stays ("Exited
    /// (code 1)", or "Exited" after a clean one; decision 48), else its progress (OSC 9;4): "40%", "Working", "Paused",
    /// "Error". Null: nothing to say.
    /// </summary>
    public static string? StatusWords(TerminalProgress progress, uint? exitCode = null) => exitCode switch
    {
        0 => "Exited",
        { } code => $"Exited (code {ExitCodes.Format(code)})",
        null => progress.State switch
        {
            TerminalProgressState.Remove => null,
            TerminalProgressState.Pause => "Paused",
            TerminalProgressState.Error => "Error",
            TerminalProgressState.Set when progress.Value >= 0 => $"{progress.Value}%",
            _ => "Working",
        },
    };
}

/// <summary>A header's fit: whether Split and Maximize fold into ⋯, and whether the status slot keeps its words.</summary>
public readonly record struct HeaderFit(bool Fold, bool Words);

/// <summary>How far a pane's tabs give way in a narrow header (<see cref="PaneHeader.FitTabs"/>).</summary>
public enum TabsStage
{
    /// <summary>Every tab whole.</summary>
    Full,

    /// <summary>The other tabs' titles shortened to the cap (at the shortest, the shown tab's too).</summary>
    Short,

    /// <summary>The other tabs as their icon and mark (the shown tab's title capped to what's left).</summary>
    Icons,

    /// <summary>Only the shown tab (its title capped), the others folded into a count.</summary>
    Folded,
}

/// <summary>A stage, and how wide the other tabs' and the shown tab's titles may be (infinite: whole).</summary>
public readonly record struct TabsFit(TabsStage Stage, double OthersCap, double ShownCap);
