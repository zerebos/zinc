using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

// Pane animations (`pane-animations` in [window], on by default). A pane layout change (split, close, maximize and
// restore, drops) moves the split Grids' real sizes a step each frame, the way a divider drag does, so cards, borders and headers are always really laid out. The terminals inside don't
// follow: each is held at one size for the whole animation, pinned to its card's edge that moves least, and its card
// reveals or covers it ("covered": the pane gaining space sweeps over the one losing it). A terminal that grows takes
// its new size at the start, one that shrinks at the end, one going to or coming from nothing never resizes; and that
// one resize reaches the shell at once (no settling wait), so a TUI repaints while the panes move. Nothing leaves or
// joins the tree for this (Win2D's Loaded/Unloaded counts).
public sealed partial class MainWindow
{
    private static readonly TimeSpan TweenLength = TimeSpan.FromMilliseconds(250);

    // Fluent's standard curve (a CSS cubic-bezier), picked over decelerate (0.2,0,0,1), which put about 80% of the
    // motion in the first 50 ms and felt like a jump, then a drift.
    private static readonly (double X1, double Y1, double X2, double Y2) TweenEase = (0.4, 0, 0.2, 1);

    private static readonly TimeSpan MaxStep = TimeSpan.FromMilliseconds(20);

    private static readonly Windows.UI.ViewManagement.UISettings WindowsSettings = new();

    /// <summary>
    /// Whether layout changes should animate, which needs both <c>pane-animations</c> and Windows' "Animation effects"
    /// on. Both are read on every change, so turning either off applies to the next change without a restart.
    /// </summary>
    private bool LayoutAnimationsEnabled => _config.Global.PaneAnimations && WindowsSettings.AnimationsEnabled;

    private readonly Dictionary<WorkspaceTab, LayoutTween> _tweens = new();
    private bool _tweenHooked;

    /// <summary>A split Grid's three slots: the first side's star, the band's pixels, the second side's star.</summary>
    private readonly record struct Weights(double First, double Band, double Second)
    {
        public static Weights Lerp(Weights a, Weights b, double t) =>
            new(a.First + (b.First - a.First) * t, a.Band + (b.Band - a.Band) * t, a.Second + (b.Second - a.Second) * t);
    }

    /// <summary>An element held at a fixed size (a terminal, or a new pane's whole view), and the view filled around it.</summary>
    private sealed record Hold(FrameworkElement Element, TerminalControl? Control, GroupView? Filled, Size? Then = null);

    /// <summary>
    /// An animation that moves pane cards by their margins and leaves the layout alone until the end. Maximize and
    /// restore grow one pane over the others with negative margins. A pane dropped beside another moves every pane
    /// from its old place to its new one (<see cref="PlayShape"/>). <paramref name="Moves"/> holds each card's start
    /// and end margins. <paramref name="Raised"/> is the pane that passes over the others plus the split slots holding
    /// it, all raised in z-order. <paramref name="Stills"/> are the covered panes, which show still pictures meanwhile.
    /// </summary>
    private sealed record Lift(List<(Border Host, Thickness From, Thickness To)> Moves, List<UIElement> Raised,
        List<TerminalControl> Stills);

    /// <summary>
    /// Where each shown pane was before a move: its place, the size of its card's border without the header (see
    /// ChromeOf and WithoutHeader), the terminal it showed and that terminal's size. These are measured before the
    /// rebuild, because a pane taken out of the tree and put back reads as 0 until the next layout pass.
    /// </summary>
    private sealed record ShapeBefore(Dictionary<Border, (Rect Place, Size Chrome, TerminalControl Control, Size Terminal)> Panes);

    /// <summary>The maximizing or restoring pane's place and its terminal's size, before the change.</summary>
    private sealed record LiftBefore(Rect Place, Size Terminal);

    /// <summary>
    /// The layout before a rebuild: the splits' sizes, the panes on screen, every pane in the layout (shown or hidden
    /// behind a maximized one), and for each shown pane the size of its card's border without the header (see ChromeOf
    /// and WithoutHeader), the terminal it showed and that terminal's size. The sizes are measured here because a pane
    /// taken out of the tree and put back reads as 0 until the next layout pass.
    /// </summary>
    private sealed record LayoutBefore(Dictionary<PaneSplit, Weights> Splits, HashSet<Border> Shown, HashSet<Border> Known,
        Dictionary<Border, (Size Chrome, TerminalControl Control, Size Terminal)> Sizes);

    private sealed class LayoutTween(WorkspaceTab tab, List<(Grid Grid, PaneSplit Split, Weights From, Weights To)> splits,
        List<Hold> holds, TabGroup<TerminalTab>? maximized, Action? then, Action? always, Lift? lift = null)
    {
        public WorkspaceTab Tab { get; } = tab;

        public Lift? Lift { get; } = lift;
        public List<(Grid Grid, PaneSplit Split, Weights From, Weights To)> Splits { get; } = splits;
        public List<Hold> Holds { get; } = holds;
        public TabGroup<TerminalTab>? Maximized { get; } = maximized;
        public Action? Then { get; } = then;

        /// <summary>Done however the animation ends, even cut short (a closed pane's terminal closes).</summary>
        public Action? Always { get; } = always;

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        /// <summary>How far the animation has got: real time, but at most <see cref="MaxStep"/> a frame.</summary>
        public TimeSpan Played { get; set; }

        public int Frames { get; set; }

        public TimeSpan Last { get; set; }

        public TimeSpan Longest { get; set; }
    }

    /// <summary>Notes the split sizes and shown panes before a layout change; null when off or not on screen.</summary>
    private LayoutBefore? NoteLayout(WorkspaceTab tab)
    {
        if (!LayoutAnimationsEnabled || !tab.Container.IsLoaded)
        {
            return null;
        }

        SettleTween(tab, run: true);
        var splits = new Dictionary<PaneSplit, Weights>();
        var shown = new HashSet<Border>();
        var known = new HashSet<Border>();
        var sizes = new Dictionary<Border, (Size, TerminalControl, Size)>();
        foreach (var element in Descendants(tab.Container))
        {
            if (element is Grid { Tag: PaneSplit split } grid)
            {
                splits[split] = ReadWeights(grid, split);
            }
            else if (element is Border { Tag: TabGroup<TerminalTab> } host && known.Add(host) && host.IsLoaded
                && host.ActualWidth > 1 && host.ActualHeight > 1)
            {
                shown.Add(host);
                if (host is { Tag: TabGroup<TerminalTab> group, Child: GroupView view })
                {
                    var control = group.Active.Control;
                    sizes[host] = (WithoutHeader(ChromeOf(host, view), view), control,
                        new Size(Math.Max(0, control.ActualWidth), Math.Max(0, control.ActualHeight)));
                }
            }
        }

        return new LayoutBefore(splits, shown, known, sizes);
    }

    /// <summary>After a layout change (model updated, Grids built): moves every split from where it was to the model's sizes.</summary>
    private void PlayLayout(WorkspaceTab tab, LayoutBefore? before)
    {
        if (before == null || before.Shown.Count == 0)
        {
            return; // off, or nothing was on screen to move from
        }

        var band = PaneGap.Band(Gap);
        var path = MaximizedPath(tab);
        var splits = new List<(Grid, PaneSplit, Weights, Weights)>();
        foreach (var grid in Descendants(tab.Container).OfType<Grid>())
        {
            if (grid.Tag is not PaneSplit split)
            {
                continue;
            }

            var to = path.Contains(split) ? (path.Contains(split.First) ? new Weights(1, 0, 0) : new Weights(0, 0, 1))
                : new Weights(split.Ratio, band, 1 - split.Ratio);
            var from = before.Splits.TryGetValue(split, out var was) ? was : Entering(grid, split, to, before.Shown);
            splits.Add((grid, split, from, to));
        }

        StartTween(tab, splits, tab.Workspace.Maximized, then: null, always: null, before.Sizes, before.Known);
    }

    /// <summary>
    /// Closes a pane's last tab by shrinking its pane away first, then rebuilding. False when that can't animate (the
    /// caller closes it the usual way).
    /// </summary>
    private bool CloseAnimated(WorkspaceTab tab, TerminalTab pane)
    {
        if (!LayoutAnimationsEnabled || !tab.Container.IsLoaded || tab.Workspace.Maximized != null
            || !tab.Workspace.Groups.Skip(1).Any() || tab.Workspace.GroupOf(pane) is not { } group || group.Tabs.Count != 1)
        {
            return false;
        }

        SettleTween(tab, run: true);
        var host = HostOf(group);
        if (!host.IsLoaded || Descendants(tab.Container).OfType<Grid>().FirstOrDefault(g => g.Tag is PaneSplit && g.Children.Contains(host))
            is not { Tag: PaneSplit split } parent)
        {
            return false;
        }

        // Its terminal keeps its last picture while its pane shrinks away and closes at the end; its events go now.
        pane.Unwire?.Invoke();
        if (!tab.Workspace.Close(pane))
        {
            pane.Control.Close();
            RemoveTab(tab);
            return true;
        }

        ((GroupView)host.Child).HideFocus();

        var first = (split.Orientation == SplitOrientation.Horizontal ? Grid.GetColumn(host) : Grid.GetRow(host)) == 0;
        var splits = Descendants(tab.Container).OfType<Grid>().Where(g => g.Tag is PaneSplit).Select(g =>
        {
            var s = (PaneSplit)g.Tag;
            var now = ReadWeights(g, s);
            return (g, s, now, g == parent ? (first ? new Weights(0, 0, 1) : new Weights(1, 0, 0)) : now);
        }).ToList();

        UpdateFocusBorders(tab);
        UpdateTitle(tab);
        FocusActive(tab);
        StartTween(tab, splits, null, then: () => Rebuild(tab), always: pane.Control.Close);
        return true;
    }

    /// <summary>
    /// Resize-pane: moves <paramref name="grid"/> from the sizes it shows to <paramref name="split"/>'s new ratio. False
    /// when off (the caller applies the ratio at once). A held key repeats: each press starts where the last one got to.
    /// </summary>
    private bool ResizeAnimated(WorkspaceTab tab, Grid grid, PaneSplit split)
    {
        if (!LayoutAnimationsEnabled || !tab.Container.IsLoaded)
        {
            return false;
        }

        var from = ReadWeights(grid, split);
        var band = PaneGap.Band(Gap);
        StartTween(tab, [(grid, split, from, new Weights(split.Ratio, band, 1 - split.Ratio))], tab.Workspace.Maximized,
            then: null, always: null);
        return true;
    }

    /// <summary>The pane each workspace shows maximized (its model gets ahead: focus moved off a maximized pane).</summary>
    private readonly Dictionary<WorkspaceTab, TabGroup<TerminalTab>?> _shownMaximized = new();

    /// <summary>Above 0: layout changes that aren't a maximize or restore of their own (a rebuild, a drag's targets).</summary>
    private int _noLift;

    /// <summary>
    /// For UpdateFocusBorders, before it shows the model: the pane to grow or shrink when the model's maximized pane
    /// isn't what's on screen, however that came about (maximize-pane, its button, focus moving to a hidden pane).
    /// </summary>
    private (TabGroup<TerminalTab> Group, LiftBefore Before)? NoteMaximizeChange(WorkspaceTab tab)
    {
        var shown = _shownMaximized.GetValueOrDefault(tab);
        if (_noLift > 0 || shown == tab.Workspace.Maximized || (tab.Workspace.Maximized ?? shown) is not { } group
            || !tab.Workspace.Groups.Contains(group) || (_tweens.TryGetValue(tab, out var running) && running.Lift == null))
        {
            return null; // a split or close moving, it settles the layout itself
        }

        return NoteLift(tab, group) is { } before ? (group, before) : null;
    }

    /// <summary>Before a maximize or restore: where the pane is and how big its terminal is; null when off.</summary>
    private LiftBefore? NoteLift(WorkspaceTab tab, TabGroup<TerminalTab> group)
    {
        if (!LayoutAnimationsEnabled || !tab.Container.IsLoaded || tab.Workspace.Layout.Count < 2)
        {
            return null;
        }

        SettleTween(tab, run: true);
        var host = HostOf(group);
        var control = group.Active.Control;
        if (!host.IsLoaded || host.ActualWidth <= 1 || control.ActualWidth <= 0 || control.ActualHeight <= 0)
        {
            return null;
        }

        var pad = tab.Container.Padding;
        var origin = host.TransformToVisual(tab.Container).TransformPoint(new Point(0, 0));
        return new LiftBefore(new Rect(origin.X - pad.Left, origin.Y - pad.Top, host.ActualWidth, host.ActualHeight),
            new Size(control.ActualWidth, control.ActualHeight));
    }

    /// <summary>
    /// After maximize-pane changed the model and the layout: the others stay exactly where they are, and the pane grows
    /// over them from its place to the whole workspace (or shrinks back into its place, uncovering them). The layout
    /// shows every pane during a maximize and takes the maximized one only at the end.
    /// </summary>
    private void PlayLift(WorkspaceTab tab, TabGroup<TerminalTab> group, LiftBefore? before)
    {
        if (before == null || tab.Container.Children.FirstOrDefault() is not { } root)
        {
            return;
        }

        var host = HostOf(group);
        var maximizing = tab.Workspace.Maximized == group;
        var whole = Whole(tab);
        if (maximizing)
        {
            ShowMaximized(tab, Gap, everyPane: true); // until the end
        }

        // Its place among the others: where it was (maximizing), or where restoring puts it.
        var place = before.Place;
        if (!maximizing)
        {
            var places = new Dictionary<Border, Rect>();
            Measure(root, whole, g => ReadWeights(g, (PaneSplit)g.Tag), places);
            if (!places.TryGetValue(host, out place))
            {
                return;
            }
        }

        var cover = new Thickness(-place.X, -place.Y, -(whole.Width - place.Right), -(whole.Height - place.Bottom));
        var raised = new List<UIElement>();
        RaisePath(root, host, raised);
        foreach (var element in raised)
        {
            Canvas.SetZIndex(element, 1);
        }

        // Its terminal takes its end size at once (the card's edge and header keep their size) and its app hears now.
        var control = group.Active.Control;
        var end = maximizing ? whole : place;
        var chrome = maximizing ? before.Place : whole; // the pane's size before, around the terminal's
        control.Width = Math.Max(1, end.Width - (chrome.Width - before.Terminal.Width));
        control.Height = Math.Max(1, end.Height - (chrome.Height - before.Terminal.Height));
        control.ResizeAtOnce = true;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        var view = (GroupView)host.Child;
        var config = group.Active.Profile.Config;
        view.FillContent(new SolidColorBrush(WithOpacity(config.Theme.Background, config)));

        var from = maximizing ? default : cover;
        host.Margin = from;

        // The others show still pictures while it moves over them: their swap chains could draw over it.
        var stills = tab.Workspace.Groups.Where(g => g != group).Select(g => g.Active.Control).ToList();
        foreach (var other in stills)
        {
            other.ShowStill(true);
        }

        var lift = new Lift([(host, from, maximizing ? cover : default)], raised, stills);
        _tweens[tab] = new LayoutTween(tab, [], [new Hold(control, control, view)], tab.Workspace.Maximized, null, null, lift);
        if (!_tweenHooked)
        {
            _tweenHooked = true;
            CompositionTarget.Rendering += OnTweenFrame;
        }
    }

    /// <summary>
    /// Measures where every shown pane is before a pane moves (see <see cref="PlayShape"/>).
    /// </summary>
    private ShapeBefore? NoteShape(WorkspaceTab tab)
    {
        if (!LayoutAnimationsEnabled || !tab.Container.IsLoaded || tab.Workspace.Maximized != null)
        {
            return null;
        }

        SettleTween(tab, run: true);
        var pad = tab.Container.Padding;
        var panes = new Dictionary<Border, (Rect, Size, TerminalControl, Size)>();
        foreach (var group in tab.Workspace.Groups)
        {
            var host = HostOf(group);
            if (!host.IsLoaded || host.ActualWidth <= 1 || host.ActualHeight <= 1 || host.Child is not GroupView view)
            {
                continue;
            }

            var origin = host.TransformToVisual(tab.Container).TransformPoint(new Point(0, 0));
            var control = group.Active.Control;
            panes[host] = (new Rect(origin.X - pad.Left, origin.Y - pad.Top, host.ActualWidth, host.ActualHeight),
                WithoutHeader(ChromeOf(host, view), view), control, new Size(Math.Max(0, control.ActualWidth), Math.Max(0, control.ActualHeight)));
        }

        return panes.Count > 1 ? new ShapeBefore(panes) : null;
    }

    /// <summary>
    /// After a pane moved (beside another pane, into another's tabs, or out of the workspace) and the new layout is
    /// built, animates every pane from where it was to
    /// its new place. Each card gets margins that put it back at its old place inside its new slot, and the margins animate
    /// to 0, so the card has its real size at every step (a negative margin grows a card past its slot, a positive one
    /// shrinks it inside). The moved pane is raised so it passes over the others, which show still pictures meanwhile
    /// because swap chains don't keep XAML's z-order. Terminals take their new sizes on the second frame, as in the
    /// other animations. Dividers and gaps jump to their new places at once.
    /// </summary>
    private void PlayShape(WorkspaceTab tab, ShapeBefore? before, TabGroup<TerminalTab> moved)
    {
        if (before == null || tab.Container.Children.FirstOrDefault() is not { } root)
        {
            return;
        }

        var places = new Dictionary<Border, Rect>();
        Measure(root, Whole(tab), g => ReadWeights(g, (PaneSplit)g.Tag), places);

        // A pane that joined another's tabs or left the workspace has no card of its own any more, so nothing passes
        // over anything and the terminals stay live.
        var movedHost = moved.Tag is Border b && places.ContainsKey(b) ? b : null;
        var moves = new List<(Border, Thickness, Thickness)>();
        var holds = new List<Hold>();
        var stills = new List<TerminalControl>();
        foreach (var (host, now) in places)
        {
            if (!before.Panes.TryGetValue(host, out var was) || host is not { Tag: TabGroup<TerminalTab> group, Child: GroupView view })
            {
                continue;
            }

            var from = new Thickness(was.Place.X - now.X, was.Place.Y - now.Y, now.Right - was.Place.Right, now.Bottom - was.Place.Bottom);
            if (Math.Abs(from.Left) + Math.Abs(from.Top) + Math.Abs(from.Right) + Math.Abs(from.Bottom) < 1)
            {
                continue; // didn't move
            }

            host.Margin = from;
            moves.Add((host, from, default));

            var control = group.Active.Control;
            var end = new Size(Math.Max(1, now.Width - was.Chrome.Width),
                Math.Max(1, now.Height - was.Chrome.Height - view.HeaderHeight));
            var same = control == was.Control && was.Terminal.Width > 0 && was.Terminal.Height > 0;

            // The terminal it showed keeps its size for the first frame and takes its new one on the second. A pane now
            // showing the terminal that joined it takes the new size at once, which saves that terminal a resize.
            control.Width = same ? was.Terminal.Width : end.Width;
            control.Height = same ? was.Terminal.Height : end.Height;
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.VerticalAlignment = VerticalAlignment.Top;
            control.ResizeAtOnce = true;
            var config = group.Active.Profile.Config;
            view.FillContent(new SolidColorBrush(WithOpacity(config.Theme.Background, config)));
            holds.Add(new Hold(control, control, view, same ? end : null));

            if (movedHost != null && group != moved)
            {
                control.ShowStill(true);
                stills.Add(control);
            }
        }

        if (moves.Count == 0)
        {
            return;
        }

        var raised = new List<UIElement>();
        if (movedHost != null)
        {
            RaisePath(root, movedHost, raised);
        }

        foreach (var element in raised)
        {
            Canvas.SetZIndex(element, 1);
        }

        _tweens[tab] = new LayoutTween(tab, [], holds, tab.Workspace.Maximized, null, null, new Lift(moves, raised, stills));
        if (!_tweenHooked)
        {
            _tweenHooked = true;
            CompositionTarget.Rendering += OnTweenFrame;
        }
    }

    /// <summary>
    /// The size of a card's border and header, meaning everything in the card around the terminal's slot. It's the
    /// border plus the view's size minus the slot's, not the card's size minus the slot's, because a new pane's view is
    /// held at its end size and overflows its card while the card grows.
    /// </summary>
    private static Size ChromeOf(Border host, GroupView view)
    {
        var (b, slot) = (host.BorderThickness, view.ContentSize);
        return new Size(Math.Max(0, b.Left + b.Right + view.ActualWidth - slot.Width), Math.Max(0, b.Top + b.Bottom + view.ActualHeight - slot.Height));
    }

    /// <summary>
    /// A card's chrome minus its header. PlayShape and StartTween add back the header the pane has after the rebuild,
    /// because a pane can gain or lose its tab strip in one (another pane joined it, or a tab was dragged out of it).
    /// </summary>
    private static Size WithoutHeader(Size chrome, GroupView view) => new(chrome.Width, Math.Max(0, chrome.Height - view.HeaderHeight));

    /// <summary>The workspace's area for panes (inside its padding), at the origin the pane places are measured from.</summary>
    private static Rect Whole(WorkspaceTab tab)
    {
        var pad = tab.Container.Padding;
        return new Rect(0, 0, Math.Max(0, tab.Container.ActualWidth - pad.Left - pad.Right),
            Math.Max(0, tab.Container.ActualHeight - pad.Top - pad.Bottom));
    }

    /// <summary>The pane host and each split slot above it, from the root: raised so the pane draws over its neighbors.</summary>
    private static bool RaisePath(UIElement element, Border host, List<UIElement> path)
    {
        if (element == host || (element is Grid { Tag: PaneSplit } grid && grid.Children.Any(c => RaisePath(c, host, path))))
        {
            path.Add(element);
            return true;
        }

        return false;
    }

    /// <param name="sizes">
    /// Each pane's card border size (without the header) and terminal size, measured before a rebuild. When it's null, they're
    /// read from the panes.
    /// </param>
    /// <param name="known">
    /// Every pane in the layout before a rebuild. A pane not in it arrived with the rebuild (a new split, or a pane or
    /// tab dropped here). When it's null, no pane arrived.
    /// </param>
    private void StartTween(WorkspaceTab tab, List<(Grid Grid, PaneSplit Split, Weights From, Weights To)> splits,
        TabGroup<TerminalTab>? maximized, Action? then, Action? always,
        Dictionary<Border, (Size Chrome, TerminalControl Control, Size Terminal)>? sizes = null,
        HashSet<Border>? known = null)
    {
        // The Grid keeps a ratio as a float (0.45 reads back as 0.44999998), so compare with room for that, or a rebuild
        // that changed nothing held every terminal for a whole animation.
        splits.RemoveAll(s => Math.Abs(s.From.First - s.To.First) < 1e-4 && Math.Abs(s.From.Second - s.To.Second) < 1e-4
            && Math.Abs(s.From.Band - s.To.Band) < 0.5);
        if (splits.Count == 0 || tab.Container.Children.FirstOrDefault() is not { } root)
        {
            always?.Invoke();
            then?.Invoke();
            return;
        }

        // Places at the start and the end, to find the panes that come from or go to nothing.
        var byGrid = splits.ToDictionary(s => s.Grid);
        var start = new Dictionary<Border, Rect>();
        var end = new Dictionary<Border, Rect>();
        var whole = Whole(tab);
        Measure(root, whole, g => byGrid.TryGetValue(g, out var s) ? s.From : ReadWeights(g, (PaneSplit)g.Tag), start);
        Measure(root, whole, g => byGrid.TryGetValue(g, out var s) ? s.To : ReadWeights(g, (PaneSplit)g.Tag), end);
        var holds = new List<Hold>();
        foreach (var (host, from) in start)
        {
            var to = end[host];
            if (from == to || host is not { Tag: TabGroup<TerminalTab> group, Child: GroupView view })
            {
                continue;
            }

            var control = group.Active.Control;
            var noted = sizes != null && sizes.TryGetValue(host, out var n) && n.Terminal.Width > 0 ? n : ((Size, TerminalControl, Size)?)null;

            // The pane shows another terminal than the one noted: a tab was dragged out of it, and the tab behind it shows.
            var swapped = noted is var (_, notedControl, _) && notedControl != control;
            var (nowW, nowH) = noted is var (_, _, terminal) && !swapped ? (terminal.Width, terminal.Height)
                : control.ActualWidth > 0 && control.ActualHeight > 0 ? (control.ActualWidth, control.ActualHeight)
                : (control.LaidOutSize.Width, control.LaidOutSize.Height);
            if (nowW <= 0 || nowH <= 0)
            {
                // A new pane, never laid out: the whole pane is held at its end size (it starts its shell at that size).
                var b = host.BorderThickness;
                view.Width = Math.Max(1, Math.Max(from.Width, to.Width) - b.Left - b.Right);
                view.Height = Math.Max(1, Math.Max(from.Height, to.Height) - b.Top - b.Bottom);
                Pin(view, from, to, stayPut: true);
                holds.Add(new Hold(view, null, null));
                continue;
            }

            // Going to or coming from nothing: the size it has (or had before it was hidden), so it never resizes.
            // Otherwise its end size, taken at the start, growing or shrinking: its app repaints while the card moves.
            // (Plain numbers, not Size: its constructor throws on a negative, and rounding can make one here.)
            var (width, height) = (nowW, nowH);
            if (known?.Contains(host) == false)
            {
                // The pane arrived from elsewhere (another window, or a tab dragged out of its pane) with the size it had
                // there, so its terminal takes its end size on the second frame, as the others do. Taking the new size at
                // once would show the terminal at that size in the old window's last frame, inside its old card, until that
                // window drew again. The pane's view hasn't been laid out here yet, so its chrome is its border plus the
                // header it will show.
                var b = host.BorderThickness;
                (width, height) = (Math.Max(1, to.Width - b.Left - b.Right), Math.Max(1, to.Height - b.Top - b.Bottom - view.HeaderHeight));
            }
            else if (!Empty(from) && !Empty(to))
            {
                // The card's border and header stay the same size, so subtract them from the card's end size to get the
                // terminal's. We use the chrome measured before the rebuild (via ChromeOf) instead of working it out from
                // the terminal's current size, because an interrupted animation can leave the terminal at a stale size,
                // which made it jump by a column when this animation finished.
                var (chromeW, chromeH) = noted is var (chrome, _, _) ? (chrome.Width, chrome.Height + view.HeaderHeight)
                    : view.ContentSize.Width > 0 ? (ChromeOf(host, view).Width, ChromeOf(host, view).Height)
                    : (from.Width - nowW, from.Height - nowH);
                (width, height) = (Math.Max(1, to.Width - chromeW), Math.Max(1, to.Height - chromeH));
            }

            // Its new size waits for the second frame (Then, in OnTweenFrame). A terminal shows a new size as soon as it
            // presents, the XAML around it only once its frame is done, and the first frame is the slow one (a new pane
            // starts its shell: 50-90 ms). When resized there, the terminal sat at its new size inside its old card, with
            // no fill and the backdrop beside it, for that long.
            // A terminal that wasn't showing has no size here to keep for a frame, so it takes its end size at once.
            control.Width = swapped ? width : nowW;
            control.Height = swapped ? height : nowH;
            control.ResizeAtOnce = true;
            // A pane going to or coming from nothing stays put and is covered or uncovered; one on screen throughout
            // carries its text with its card's top-left corner, as layout does (pinned to the far edge, a maximizing
            // pane's prompt was out of sight until the end).
            Pin(control, from, to, stayPut: Empty(from) || Empty(to));
            var config = group.Active.Profile.Config;
            view.FillContent(new SolidColorBrush(WithOpacity(config.Theme.Background, config)));
            holds.Add(new Hold(control, control, view, swapped ? null : new Size(width, height)));
        }

        foreach (var (grid, split, from, _) in splits)
        {
            foreach (var child in grid.Children)
            {
                child.Visibility = Visibility.Visible; // a maximized pane's hidden sides show while they grow or shrink
            }

            WriteWeights(grid, split, from);
        }

        _tweens[tab] = new LayoutTween(tab, splits, holds, maximized, then, always);
        if (!_tweenHooked)
        {
            _tweenHooked = true;
            CompositionTarget.Rendering += OnTweenFrame;
        }
    }

    private void OnTweenFrame(object? sender, object e) => Ui.Guard("layout animation", () =>
    {
        foreach (var tween in _tweens.Values.ToList())
        {
            // A slow frame (a new pane starting its shell takes 50-70 ms) delays the motion instead of skipping it.
            var now = tween.Clock.Elapsed;
            var step = now - tween.Last;
            tween.Longest = step > tween.Longest ? step : tween.Longest;
            tween.Last = now;
            tween.Frames++;
            if (tween.Frames == 2)
            {
                foreach (var hold in tween.Holds)
                {
                    if (hold.Then is { } size)
                    {
                        hold.Element.Width = size.Width;
                        hold.Element.Height = size.Height;
                    }
                }
            }

            tween.Played += step < MaxStep ? step : MaxStep;
            var t = _windowClosed ? 1 : Math.Min(1, tween.Played / TweenLength);
            if (t >= 1)
            {
                Finish(tween);
                continue;
            }

            var eased = Ease(t);
            foreach (var (grid, split, from, to) in tween.Splits)
            {
                WriteWeights(grid, split, Weights.Lerp(from, to, eased));
            }

            if (tween.Lift is { } lift)
            {
                static double At(double a, double b, double e) => a + (b - a) * e;
                // interpolate the margins of every moving card
                foreach (var (host, from, to) in lift.Moves)
                {
                    host.Margin = new Thickness(At(from.Left, to.Left, eased), At(from.Top, to.Top, eased),
                        At(from.Right, to.Right, eased), At(from.Bottom, to.Bottom, eased));
                }
            }
        }

        if (_tweens.Count == 0)
        {
            _tweenHooked = false;
            CompositionTarget.Rendering -= OnTweenFrame;
        }
    });

    private void Finish(LayoutTween tween)
    {
        _tweens.Remove(tween.Tab);
        Log.Write($"layout: animation done: {tween.Frames} frames in {tween.Clock.ElapsedMilliseconds} ms, longest {tween.Longest.TotalMilliseconds:0} ms");
        Release(tween);
        tween.Always?.Invoke();
        if (_windowClosed)
        {
            return;
        }

        if (tween.Then is { } then)
        {
            then();
        }
        else
        {
            ShowMaximized(tween.Tab, Gap); // the exact end: a maximized pane's other sides collapse
        }
    }

    /// <summary>
    /// Ends a running animation where it is (holds let go). <paramref name="run"/>: also does what it was waiting to do
    /// (a close's rebuild); a caller about to rebuild anyway passes false.
    /// </summary>
    private void SettleTween(WorkspaceTab tab, bool run)
    {
        if (_tweens.Remove(tab, out var tween))
        {
            Release(tween);
            tween.Always?.Invoke();
            if (run)
            {
                tween.Then?.Invoke();
            }
        }
    }

    /// <summary>
    /// For UpdateFocusBorders: true while an animation is moving this workspace to the layout it shows now (it applies
    /// the exact end itself); a different layout (focus left a maximized pane) ends it.
    /// </summary>
    private bool Tweening(WorkspaceTab tab)
    {
        if (!_tweens.TryGetValue(tab, out var tween))
        {
            return false;
        }

        if (tween.Then == null && tween.Maximized != tab.Workspace.Maximized)
        {
            SettleTween(tab, run: false);
            return false;
        }

        return true;
    }

    private static void Release(LayoutTween tween)
    {
        if (tween.Lift is { } lift)
        {
            // reset margins for moved hosts
            foreach (var (host, _, _) in lift.Moves)
            {
                host.Margin = default;
            }

            foreach (var other in lift.Stills)
            {
                other.ShowStill(false);
            }
            foreach (var element in lift.Raised)
            {
                Canvas.SetZIndex(element, 0);
            }
        }

        foreach (var hold in tween.Holds)
        {
            hold.Element.Width = double.NaN;
            hold.Element.Height = double.NaN;
            hold.Element.HorizontalAlignment = HorizontalAlignment.Stretch;
            hold.Element.VerticalAlignment = VerticalAlignment.Stretch;
            if (hold.Control != null)
            {
                hold.Control.ResizeAtOnce = false; // used at the start, or not needed (no resize)
            }

            hold.Filled?.FillContent(null);
        }
    }

    /// <summary>
    /// <paramref name="stayPut"/>: covered, not pushed: held content stays where it is on screen, pinned to its card's
    /// edge that moves least, and the pane gaining the space sweeps over it (or uncovers it). Otherwise top-left.
    /// </summary>
    private static void Pin(FrameworkElement element, Rect from, Rect to, bool stayPut)
    {
        element.HorizontalAlignment = stayPut && Math.Abs(to.Right - from.Right) < Math.Abs(to.Left - from.Left)
            ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        element.VerticalAlignment = stayPut && Math.Abs(to.Bottom - from.Bottom) < Math.Abs(to.Top - from.Top)
            ? VerticalAlignment.Bottom : VerticalAlignment.Top;
    }

    private static HashSet<PaneNode> MaximizedPath(WorkspaceTab tab)
    {
        var path = new HashSet<PaneNode>();
        for (PaneNode? node = tab.Workspace.Maximized is { } group ? tab.Workspace.Layout.Find(group) : null; node != null; node = node.Parent)
        {
            path.Add(node);
        }

        return path;
    }

    /// <summary>A split that's new: the side whose panes weren't on screen grows from nothing.</summary>
    private static Weights Entering(Grid grid, PaneSplit split, Weights to, HashSet<Border> shown)
    {
        bool Seen(int slot) => grid.Children.Where(c => SlotOf(c, split) == slot)
            .SelectMany(Descendants).OfType<Border>().Any(b => b.Tag is TabGroup<TerminalTab> && shown.Contains(b));
        var (first, second) = (Seen(0), Seen(2));
        return first == second ? to : first ? new Weights(to.First, 0, 0) : new Weights(0, 0, to.Second);
    }

    private static int SlotOf(UIElement child, PaneSplit split) =>
        split.Orientation == SplitOrientation.Horizontal ? Grid.GetColumn((FrameworkElement)child) : Grid.GetRow((FrameworkElement)child);

    private static Weights ReadWeights(Grid grid, PaneSplit split)
    {
        if (split.Orientation == SplitOrientation.Horizontal)
        {
            var c = grid.ColumnDefinitions;
            return new Weights(c[0].Width.Value, c[1].Width.Value, c[2].Width.Value);
        }

        var r = grid.RowDefinitions;
        return new Weights(r[0].Height.Value, r[1].Height.Value, r[2].Height.Value);
    }

    private static void WriteWeights(Grid grid, PaneSplit split, Weights w)
    {
        var (first, band, second) = (new GridLength(Math.Max(0, w.First), GridUnitType.Star), new GridLength(Math.Max(0, w.Band)),
            new GridLength(Math.Max(0, w.Second), GridUnitType.Star));
        if (split.Orientation == SplitOrientation.Horizontal)
        {
            grid.ColumnDefinitions[0].Width = first;
            grid.ColumnDefinitions[1].Width = band;
            grid.ColumnDefinitions[2].Width = second;
        }
        else
        {
            grid.RowDefinitions[0].Height = first;
            grid.RowDefinitions[1].Height = band;
            grid.RowDefinitions[2].Height = second;
        }
    }

    /// <summary>Each pane host's place for the given split weights, worked out the way the Grids lay them out.</summary>
    private static void Measure(UIElement element, Rect place, Func<Grid, Weights> weights, Dictionary<Border, Rect> places)
    {
        if (element is Border { Tag: TabGroup<TerminalTab> } host)
        {
            places[host] = place;
            return;
        }

        if (element is not Grid { Tag: PaneSplit split } grid)
        {
            return;
        }

        var w = weights(grid);
        var horizontal = split.Orientation == SplitOrientation.Horizontal;
        var space = Math.Max(0, (horizontal ? place.Width : place.Height) - w.Band);
        var total = w.First + w.Second;
        // Layout rounds each split to whole physical pixels, giving the first side the extra one at a half (541.5 each
        // became 542 and 541). Rounding the same way here keeps a terminal's end size from coming out a column off.
        var scale = grid.XamlRoot?.RasterizationScale ?? 1;
        var first = total > 0 ? Math.Round(space * w.First / total * scale, MidpointRounding.AwayFromZero) / scale : 0;
        var second = total > 0 ? space - first : 0;
        foreach (var child in grid.Children)
        {
            var slot = SlotOf(child, split);
            if (slot == 1)
            {
                continue;
            }

            var (offset, along) = slot == 0 ? (0, Math.Max(0, first)) : (first + w.Band, Math.Max(0, second)); // (Rect throws on a negative)
            Measure(child, horizontal ? new Rect(place.X + offset, place.Y, along, place.Height)
                : new Rect(place.X, place.Y + offset, place.Width, along), weights, places);
        }
    }

    private static bool Empty(Rect size) => size.Width < 1 || size.Height < 1;

    /// <summary>The CSS cubic-bezier timing function at progress <paramref name="x"/>.</summary>
    private static double Ease(double x)
    {
        var (x1, y1, x2, y2) = TweenEase;
        static double Bezier(double t, double a, double b) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 30; i++)
        {
            t = (lo + hi) / 2;
            if (Bezier(t, x1, x2) < x)
            {
                lo = t;
            }
            else
            {
                hi = t;
            }
        }

        return Bezier(t, y1, y2);
    }
}
