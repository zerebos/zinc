using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Phosphor.Panes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;

namespace Phosphor;

// Dragging tabs. A workspace (title-bar tab) can go out of the strip (new window), onto another
// window's strip, onto a pane's edge (its layout becomes a split) or middle (its terminals join
// the pane). A pane's own tab (a terminal) can be reordered in its strip, go into another pane
// (middle or strip) or beside it (edge), onto the title bar (its own workspace; hovering a
// workspace switches to it), or out of every window (new window). Windows are in one process,
// so the dragged tab is handed over directly via App.Drag.
public sealed partial class MainWindow
{
    /// <summary>A tab being dragged: its window, and a <see cref="WorkspaceTab"/> or a <see cref="TerminalTab"/>.</summary>
    internal sealed record TabDrag(MainWindow Window, object Tab);

    private void OnTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        if (args.Tab.Tag is not WorkspaceTab dragged)
        {
            args.Cancel = true; // the new-workspace page stays where it is
            return;
        }

        args.Data.RequestedOperation = DataPackageOperation.Move;
        App.Instance.Drag = new TabDrag(this, dragged);
    }

    private void OnTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        Log.Write($"tab drag completed: {args.DropResult}");
        // Nobody took the drop: tear the tab off. (Handled here as well as in TabDroppedOutside,
        // since which of the two TabView raises first isn't something to rely on.)
        if (args.DropResult == DataPackageOperation.None)
        {
            TryTearOff();
        }

        App.Instance.Drag = null;
        HideDropPreview();
    }

    private void OnTabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        Log.Write("tab dropped outside");
        TryTearOff();
    }

    /// <summary>
    /// A dragged tab released where no target took it: outside every Phosphor window it becomes
    /// a window there; over one of our windows (a pane's middle, the window itself) nothing happens.
    /// </summary>
    private void TryTearOff()
    {
        if (App.Instance.Drag is not { } drag || drag.Window != this || drag.Tab is not WorkspaceTab tab)
        {
            return;
        }

        App.Instance.Drag = null;
        GetCursorPos(out var cursor);
        var point = new PointInt32(cursor.X, cursor.Y);
        if (App.Instance.IsOverWindow(point))
        {
            Log.Write("tab drag: released over a Phosphor window, cancelled");
            return;
        }

        var window = App.Instance.OpenWindow(DetachWorkspace(tab));
        // Put the window under the pointer, roughly where its tab would be.
        window.AppWindow.Move(new PointInt32(cursor.X - 120, cursor.Y - 20));
        CloseIfEmpty();
    }

    /// <summary>A pane's tab was dragged and released: outside every Phosphor window, it becomes a window.</summary>
    private void OnTerminalTabDragCompleted(TerminalTab pane, DataPackageOperation result)
    {
        _springTimer?.Stop();
        HideDropPreview();
        if (App.Instance.Drag is not { Tab: TerminalTab dragged } drag || dragged != pane)
        {
            return; // taken by a drop target
        }

        App.Instance.Drag = null;
        GetCursorPos(out var cursor);
        if (result != DataPackageOperation.None || App.Instance.IsOverWindow(new PointInt32(cursor.X, cursor.Y)) ||
            drag.Window.WorkspaceOf(pane) is not { } from)
        {
            return;
        }

        MainWindow window;
        if (from.Workspace.Tabs.Count() == 1)
        {
            window = App.Instance.OpenWindow(drag.Window.DetachWorkspace(from)); // its only tab: the workspace goes
        }
        else
        {
            drag.Window.TakeTab(from, pane);
            window = App.Instance.OpenWindow(new WorkspaceTransfer(new Workspace<TerminalTab>(pane), null, null));
        }

        window.AppWindow.Move(new PointInt32(cursor.X - 120, cursor.Y - 20));
        drag.Window.CloseIfEmpty();
    }

    // ------------------------------------------------------ the title bar

    private void OnTabStripDragOver(object sender, DragEventArgs e) =>
        AcceptWorkspaceDrop(e, TabUnder(e.GetPosition(Tabs)), reorder: false);

    private void OnTabStripDrop(object sender, DragEventArgs e) =>
        DropOnWorkspaces(DropIndex(e.GetPosition(Tabs)));

    /// <summary>The workspace whose title-bar tab is at <paramref name="point"/> (strip coordinates).</summary>
    private WorkspaceTab? TabUnder(Point point) =>
        Tabs.TabItems.Cast<TabViewItem>().FirstOrDefault(i =>
        {
            var left = i.TransformToVisual(Tabs).TransformPoint(new Point(0, 0)).X;
            return point.X >= left && point.X < left + i.ActualWidth;
        })?.Tag as WorkspaceTab;

    /// <summary>
    /// Whether the workspace tabs (title bar or list) take the dragged tab, which is over
    /// <paramref name="under"/>'s tab if any: another window's workspace moves here, this
    /// window's own is reordered (<paramref name="reorder"/>: the list; the title bar's
    /// TabView reorders its own), and a pane's tab becomes a workspace of its own, unless it
    /// already is one of ours. Resting a pane's tab on a workspace switches to it.
    /// </summary>
    private bool AcceptWorkspaceDrop(DragEventArgs e, WorkspaceTab? under, bool reorder)
    {
        var accept = false;
        switch (App.Instance.Drag)
        {
            case { Tab: WorkspaceTab } drag when drag.Window != this:
                accept = true;
                e.DragUIOverride.Caption = "Move to this window";
                break;
            case { Tab: WorkspaceTab } when reorder:
                accept = true;
                e.DragUIOverride.IsCaptionVisible = false;
                break;
            case { Tab: TerminalTab pane } drag:
                SpringLoad(under);
                accept = !(drag.Window == this && WorkspaceOf(pane)?.Workspace.Tabs.Count() == 1);
                if (accept)
                {
                    e.DragUIOverride.Caption = "New workspace";
                }

                break;
        }

        // Only ever raise it: the title bar's own reorder goes through this event too.
        if (accept)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }

        return accept;
    }

    /// <summary>Drops the dragged tab among the workspace tabs at position <paramref name="index"/> (see <see cref="AcceptWorkspaceDrop"/>).</summary>
    private void DropOnWorkspaces(int index)
    {
        CancelSpringLoad();
        switch (App.Instance.Drag)
        {
            case { Tab: WorkspaceTab tab } drag when drag.Window != this:
            {
                App.Instance.Drag = null;
                var transfer = drag.Window.DetachWorkspace(tab);
                AddWorkspace(transfer.Workspace, transfer.CustomTitle, transfer.Color, index);
                drag.Window.CloseIfEmpty();
                Activate();
                break;
            }

            case { Tab: WorkspaceTab tab } when _workspaceList != null:
                App.Instance.Drag = null;
                MoveWorkspace(tab, index);
                break;

            case { Tab: TerminalTab pane } drag when drag.Window.WorkspaceOf(pane) is { } from &&
                                                   !(drag.Window == this && from.Workspace.Tabs.Count() == 1):
            {
                App.Instance.Drag = null;
                drag.Window.TakeTab(from, pane);
                AddWorkspace(new Workspace<TerminalTab>(pane), index: index);
                if (drag.Window != this)
                {
                    drag.Window.CloseIfEmpty();
                }

                Activate();
                break;
            }
        }
    }

    /// <summary>Lets workspace tabs be reordered by dragging (off while a pane's tab is dragged; see App.Drag).</summary>
    internal void SetTabReordering(bool on)
    {
        if (Tabs.CanReorderTabs != on)
        {
            Tabs.CanReorderTabs = on;
        }
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _springTimer;
    private WorkspaceTab? _springTarget;

    /// <summary>
    /// Hovering a workspace's tab while dragging a pane's tab switches to that workspace after a
    /// moment, so the tab can be carried into one of its panes.
    /// </summary>
    private void SpringLoad(WorkspaceTab? under)
    {
        if (under == null || under == SelectedTab)
        {
            CancelSpringLoad();
            return;
        }

        if (under == _springTarget && _springTimer is { IsRunning: true })
        {
            return;
        }

        _springTarget = under;
        if (_springTimer == null)
        {
            _springTimer = DispatcherQueue.CreateTimer();
            _springTimer.Interval = TimeSpan.FromMilliseconds(500);
            _springTimer.IsRepeating = false;
            _springTimer.OnTick(_ =>
            {
                // DragOver only comes with movement, so a resting pointer is still where it was;
                // leaving for anywhere else cancels (CancelSpringLoad).
                if (App.Instance.Drag != null && _springTarget != null && AllTabs().Contains(_springTarget))
                {
                    Tabs.SelectedItem = _springTarget.Item;
                }
            });
        }

        _springTimer.Stop();
        _springTimer.Start();
    }

    /// <summary>The drag went somewhere other than a workspace tab: don't switch after all.</summary>
    private void CancelSpringLoad()
    {
        _springTimer?.Stop();
        _springTarget = null;
    }

    // The strip events only cover the tabs themselves; the +/▾ buttons count too, via the window
    // root. The empty rest of the title bar can't: it's the window's caption (drag-to-move),
    // which Windows owns and refuses drops on, and WinUI keeps it caption even when asked not to
    // (InputNonClientPointerSource.Clear/SetRegionRects and SetTitleBar(empty) were all tried).

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (InTitleBand(e))
        {
            OnTabStripDragOver(sender, e);
        }
        else
        {
            CancelSpringLoad(); // over a pane (their DragOver bubbles up here)
        }
    }

    private void OnRootDragLeave(object sender, DragEventArgs e)
    {
        // Children's DragLeave bubbles here too; only leaving the window itself counts.
        var p = e.GetPosition(Root);
        if (p.X < 0 || p.Y < 0 || p.X >= Root.ActualWidth || p.Y >= Root.ActualHeight)
        {
            CancelSpringLoad();
        }
    }

    private void OnRootDrop(object sender, DragEventArgs e)
    {
        if (InTitleBand(e))
        {
            OnTabStripDrop(sender, e);
        }
    }

    /// <summary>
    /// Whether a drag is over the tab strip's own part of the title bar: as tall as the strip,
    /// and left of the caption area (so no sliver of it borders a pane's "split up" zone).
    /// </summary>
    private bool InTitleBand(DragEventArgs e)
    {
        if (_workspaceList != null)
        {
            return false; // the title bar is all caption; the list takes drops itself
        }

        var caption = DragRegion.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        var p = e.GetPosition(Root);
        return p.Y <= caption.Y + DragRegion.ActualHeight && p.X < caption.X;
    }

    /// <summary>Where a tab dropped at <paramref name="point"/> (strip coordinates) goes among the tabs.</summary>
    private int DropIndex(Point point)
    {
        var index = 0;
        foreach (var item in Tabs.TabItems.Cast<TabViewItem>())
        {
            var left = item.TransformToVisual(Tabs).TransformPoint(new Point(0, 0)).X;
            if (point.X < left + item.ActualWidth / 2)
            {
                break;
            }

            index++;
        }

        return index;
    }

    // ------------------------------------------------------- a pane's edge

    /// <summary>Makes a group's host accept dragged tabs (once, when the host is created).</summary>
    private void EnableDrop(Border host)
    {
        host.AllowDrop = true;
        host.DragOver += OnPaneDragOver;
        host.DragLeave += (_, _) => HideDropPreview();
        host.Drop += OnPaneDrop;
    }

    private void OnPaneDragOver(object sender, DragEventArgs e)
    {
        var host = (Border)sender;
        var edge = EdgeUnder(host, e.GetPosition(host));
        if (DropTarget(host, edge) == null)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            HideDropPreview();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = edge switch
        {
            PaneDirection.Left => "Split left",
            PaneDirection.Right => "Split right",
            PaneDirection.Up => "Split up",
            PaneDirection.Down => "Split down",
            _ => "Add to this pane",
        };
        ShowDropPreview(host, edge);
    }

    private void OnPaneDrop(object sender, DragEventArgs e)
    {
        var host = (Border)sender;
        HideDropPreview();
        var edge = EdgeUnder(host, e.GetPosition(host));
        if (DropTarget(host, edge) is not { } target || App.Instance.Drag is not { } drag)
        {
            return;
        }

        App.Instance.Drag = null;
        switch (drag.Tab)
        {
            case WorkspaceTab dragged:
                DropWorkspace(drag.Window, dragged, target.Tab, target.Group, edge);
                break;
            case TerminalTab pane:
                MoveTerminalTab(drag.Window, pane, target.Tab, target.Group, edge);
                break;
        }
    }

    // ------------------------------------------------------- a pane's strip

    /// <summary>Whether a pane's strip takes the dragged tab (at the marker): any terminal tab, or another workspace.</summary>
    private bool AcceptStripDrop(TabGroup<TerminalTab> group, DragEventArgs e)
    {
        HideDropPreview();
        CancelSpringLoad(); // marked handled, so the window root doesn't see it
        var accept = App.Instance.Drag switch
        {
            { Tab: TerminalTab } => true,
            { Tab: WorkspaceTab dragged } => !dragged.Workspace.Groups.Contains(group),
            _ => false,
        };
        e.AcceptedOperation = accept ? DataPackageOperation.Move : DataPackageOperation.None;
        if (accept)
        {
            e.DragUIOverride.Caption = App.Instance.Drag?.Tab is TerminalTab pane && group.Contains(pane) ? "Move here" : "Add to this pane";
        }

        return accept;
    }

    private void OnStripDrop(TabGroup<TerminalTab> group, int index)
    {
        if (App.Instance.Drag is not { } drag || AllTabs().FirstOrDefault(t => t.Workspace.Groups.Contains(group)) is not { } tab)
        {
            return;
        }

        switch (drag.Tab)
        {
            case TerminalTab pane:
                App.Instance.Drag = null;
                MoveTerminalTab(drag.Window, pane, tab, group, null, index);
                break;
            case WorkspaceTab dragged when dragged != tab:
                App.Instance.Drag = null;
                DropWorkspace(drag.Window, dragged, tab, group, null);
                break;
        }
    }

    /// <summary>
    /// Moves workspace <paramref name="dragged"/> (from <paramref name="source"/>, maybe this
    /// window) into <paramref name="tab"/>: beside <paramref name="group"/> on side
    /// <paramref name="edge"/>, or into the group as tabs when <paramref name="edge"/> is null.
    /// </summary>
    private void DropWorkspace(MainWindow source, WorkspaceTab dragged, WorkspaceTab tab, TabGroup<TerminalTab> group,
        PaneDirection? edge)
    {
        var transfer = source.DetachWorkspace(dragged);
        if (edge is { } side)
        {
            tab.Workspace.Merge(group, side, transfer.Workspace);
        }
        else
        {
            // Every terminal of the dragged workspace becomes a tab of this group; the one
            // that had focus there is shown.
            foreach (var pane in transfer.Workspace.Tabs)
            {
                group.Add(pane);
            }

            tab.Workspace.Activate(transfer.Workspace.Active);
        }

        foreach (var pane in tab.Workspace.Tabs)
        {
            Wire(pane, tab);
        }

        Rebuild(tab);
        source.CloseIfEmpty();
        Activate();
    }

    /// <summary>
    /// The workspace and group under a host, if the dragged tab can be dropped there on
    /// <paramref name="edge"/> (null: the middle, which joins the pane's tabs; only with tab
    /// groups). A workspace can't be dropped onto its own panes; a terminal tab not into its
    /// own pane, nor beside it when it's the pane's only tab.
    /// </summary>
    private (WorkspaceTab Tab, TabGroup<TerminalTab> Group)? DropTarget(Border host, PaneDirection? edge)
    {
        if (host.Tag is not TabGroup<TerminalTab> group || (edge == null && !_config.Global.TabGroups) ||
            AllTabs().FirstOrDefault(t => t.Workspace.Groups.Contains(group)) is not { } tab)
        {
            return null;
        }

        return App.Instance.Drag?.Tab switch
        {
            WorkspaceTab dragged when dragged != tab => (tab, group),
            TerminalTab pane when !group.Contains(pane) || (edge != null && group.Tabs.Count > 1) => (tab, group),
            _ => null,
        };
    }

    /// <summary>The edge whose band (the outer third, like VS Code) holds <paramref name="p"/>; null in the middle.</summary>
    private static PaneDirection? EdgeUnder(FrameworkElement host, Point p)
    {
        double w = host.ActualWidth, h = host.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        // Distance to each edge as a share of the pane; the nearest edge wins if within a third.
        var (edge, share) = new[]
        {
            (PaneDirection.Left, p.X / w), (PaneDirection.Right, 1 - p.X / w),
            (PaneDirection.Up, p.Y / h), (PaneDirection.Down, 1 - p.Y / h),
        }.MinBy(x => x.Item2);
        return share < 1.0 / 3 ? edge : null;
    }

    /// <summary>Highlights where a drop lands: half the pane for an edge, all of it for the middle (null).</summary>
    private void ShowDropPreview(FrameworkElement host, PaneDirection? edge)
    {
        var origin = host.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        double w = host.ActualWidth, h = host.ActualHeight;
        var (x, y, pw, ph) = edge switch
        {
            PaneDirection.Left => (origin.X, origin.Y, w / 2, h),
            PaneDirection.Right => (origin.X + w / 2, origin.Y, w / 2, h),
            PaneDirection.Up => (origin.X, origin.Y, w, h / 2),
            PaneDirection.Down => (origin.X, origin.Y + h / 2, w, h / 2),
            _ => (origin.X, origin.Y, w, h),
        };
        DropPreview.Margin = new Thickness(x, y, 0, 0);
        DropPreview.Width = pw;
        DropPreview.Height = ph;
        DropPreview.Visibility = Visibility.Visible;
    }

    private void HideDropPreview() => DropPreview.Visibility = Visibility.Collapsed;
}
