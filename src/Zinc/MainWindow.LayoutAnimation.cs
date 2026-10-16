using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

// EXPERIMENT (ZINC_TRY_LAYOUT_ANIM=1, or a duration in ms; ZINC_TRY_LAYOUT_ANIM_EASE="x1,y1,x2,y2" for the curve):
// pane layout changes (split, close, maximize and restore, drops) move the split Grids' real sizes a step each frame,
// the way a divider drag does, so cards, borders and headers are always really laid out. The terminals inside don't
// follow: each is held at one size for the whole animation, pinned to its card's edge that moves least, and its card
// reveals or covers it ("covered": the pane gaining space sweeps over the one losing it). A terminal that grows takes
// its new size at the start, one that shrinks at the end, one going to or coming from nothing never resizes; and that
// one resize reaches the shell at once (no settling wait), so a TUI repaints while the panes move. Nothing leaves or
// joins the tree for this (Win2D's Loaded/Unloaded counts).
public sealed partial class MainWindow
{
    private static readonly TimeSpan? TweenLength = ReadTweenLength();
    private static readonly (double X1, double Y1, double X2, double Y2) TweenEase = ReadTweenEase();

    private static readonly TimeSpan MaxStep = TimeSpan.FromMilliseconds(20);

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
    /// Maximize and restore: the pane lifted above the others (<paramref name="Raised"/>: it and the split slots holding it,
    /// raised in z-order) and grown over them by negative margins, the layout itself left alone until the end.
    /// </summary>
    private sealed record Lift(Border Host, Thickness From, Thickness To, List<UIElement> Raised, List<TerminalControl> Stills);

    /// <summary>The maximizing or restoring pane's place and its terminal's size, before the change.</summary>
    private sealed record LiftBefore(Rect Place, Size Terminal);

    private sealed record LayoutBefore(Dictionary<PaneSplit, Weights> Splits, HashSet<Border> Shown);

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

    private static TimeSpan? ReadTweenLength()
    {
        var value = Environment.GetEnvironmentVariable("ZINC_TRY_LAYOUT_ANIM");
        if (string.IsNullOrEmpty(value) || value == "0" || !int.TryParse(value, out var ms))
        {
            return null;
        }

        return TimeSpan.FromMilliseconds(ms < 20 ? 250 : ms);
    }

    private static (double, double, double, double) ReadTweenEase()
    {
        var parts = (Environment.GetEnvironmentVariable("ZINC_TRY_LAYOUT_ANIM_EASE") ?? "").Split(',');
        if (parts.Length == 4 && parts.All(p => double.TryParse(p, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            var v = parts.Select(p => double.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return (v[0], v[1], v[2], v[3]);
        }

        // Fluent's standard curve, picked over decelerate (0.2,0,0,1), which put about
        // 80% of the motion in the first 50 ms and felt like a jump, then a drift.
        return (0.4, 0, 0.2, 1);
    }

    /// <summary>Notes the split sizes and shown panes before a layout change; null when off or not on screen.</summary>
    private LayoutBefore? NoteLayout(WorkspaceTab tab)
    {
        if (TweenLength == null || !tab.Container.IsLoaded)
        {
            return null;
        }

        SettleTween(tab, run: true);
        var splits = new Dictionary<PaneSplit, Weights>();
        var shown = new HashSet<Border>();
        foreach (var element in Descendants(tab.Container))
        {
            if (element is Grid { Tag: PaneSplit split } grid)
            {
                splits[split] = ReadWeights(grid, split);
            }
            else if (element is Border { Tag: TabGroup<TerminalTab> } host && host.IsLoaded && host.ActualWidth > 1 && host.ActualHeight > 1)
            {
                shown.Add(host);
            }
        }

        return new LayoutBefore(splits, shown);
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

        StartTween(tab, splits, tab.Workspace.Maximized, then: null, always: null);
    }

    /// <summary>
    /// Closes a pane's last tab by shrinking its pane away first, then rebuilding. False when that can't animate (the
    /// caller closes it the usual way).
    /// </summary>
    private bool CloseAnimated(WorkspaceTab tab, TerminalTab pane)
    {
        if (TweenLength == null || !tab.Container.IsLoaded || tab.Workspace.Maximized != null
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

    /// <summary>Before maximize-pane: where the pane is and how big its terminal is; null when off.</summary>
    private LiftBefore? NoteLift(WorkspaceTab tab, TabGroup<TerminalTab> group)
    {
        if (TweenLength == null || !tab.Container.IsLoaded || tab.Workspace.Layout.Count < 2)
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

        var lift = new Lift(host, from, maximizing ? cover : default, raised, stills);
        _tweens[tab] = new LayoutTween(tab, [], [new Hold(control, control, view)], tab.Workspace.Maximized, null, null, lift);
        if (!_tweenHooked)
        {
            _tweenHooked = true;
            CompositionTarget.Rendering += OnTweenFrame;
        }
    }

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

    private void StartTween(WorkspaceTab tab, List<(Grid Grid, PaneSplit Split, Weights From, Weights To)> splits,
        TabGroup<TerminalTab>? maximized, Action? then, Action? always)
    {
        splits.RemoveAll(s => s.From == s.To);
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
            var (nowW, nowH) = control.ActualWidth > 0 && control.ActualHeight > 0 ? (control.ActualWidth, control.ActualHeight)
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
            if (!Empty(from) && !Empty(to))
            {
                // The card's edge and header stay the same size, so the terminal's end size follows from the card's.
                (width, height) = (Math.Max(1, to.Width - (from.Width - nowW)), Math.Max(1, to.Height - (from.Height - nowH)));
            }

            // Its new size waits for the second frame (Then, in OnTweenFrame). A terminal shows a new size as soon as it
            // presents, the XAML around it only once its frame is done, and the first frame is the slow one (a new pane
            // starts its shell: 50-90 ms). When resized there, the terminal sat at its new size inside its old card, with
            // no fill and the backdrop beside it, for that long.
            control.Width = nowW;
            control.Height = nowH;
            control.ResizeAtOnce = true;
            // A pane going to or coming from nothing stays put and is covered or uncovered; one on screen throughout
            // carries its text with its card's top-left corner, as layout does (pinned to the far edge, a maximizing
            // pane's prompt was out of sight until the end).
            Pin(control, from, to, stayPut: Empty(from) || Empty(to));
            var config = group.Active.Profile.Config;
            view.FillContent(new SolidColorBrush(WithOpacity(config.Theme.Background, config)));
            holds.Add(new Hold(control, control, view, new Size(width, height)));
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
            var t = _windowClosed ? 1 : Math.Min(1, tween.Played / TweenLength!.Value);
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
                lift.Host.Margin = new Thickness(At(lift.From.Left, lift.To.Left, eased), At(lift.From.Top, lift.To.Top, eased),
                    At(lift.From.Right, lift.To.Right, eased), At(lift.From.Bottom, lift.To.Bottom, eased));
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
            lift.Host.Margin = default;
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
        var first = total > 0 ? space * w.First / total : 0;
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
