using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Phosphor.Panes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;

namespace Phosphor;

// Dragging workspace tabs: out of the strip (new window), onto another window's strip (moves
// there), or onto a pane's edge (its whole layout becomes a split there). Windows are in one
// process, so the dragged tab is handed over directly via App.TabDrag.
public sealed partial class MainWindow
{
    /// <summary>A workspace tab being dragged: its window, and the tab.</summary>
    internal sealed record TabDrag(MainWindow Window, object Tab);

    private void OnTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        args.Data.RequestedOperation = DataPackageOperation.Move;
        App.Instance.Drag = new TabDrag(this, (WorkspaceTab)args.Tab.Tag);
    }

    private void OnTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        App.Instance.Drag = null;
        HideDropPreview();
    }

    /// <summary>Released outside every window (no target took it): the tab becomes a window there.</summary>
    private void OnTabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        if (App.Instance.Drag is not { } drag || drag.Window != this || drag.Tab is not WorkspaceTab tab)
        {
            return;
        }

        App.Instance.Drag = null;
        GetCursorPos(out var cursor);
        var window = App.Instance.OpenWindow(DetachWorkspace(tab));
        // Put the window under the pointer, roughly where its tab would be.
        window.AppWindow.Move(new PointInt32(cursor.X - 120, cursor.Y - 20));
        CloseIfEmpty();
    }

    // ------------------------------------------------ another window's strip

    private void OnTabStripDragOver(object sender, DragEventArgs e)
    {
        if (App.Instance.Drag is { } drag && drag.Window != this)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = "Move to this window";
        }
    }

    private void OnTabStripDrop(object sender, DragEventArgs e)
    {
        if (App.Instance.Drag is not { } drag || drag.Window == this || drag.Tab is not WorkspaceTab tab)
        {
            return;
        }

        App.Instance.Drag = null;
        var index = DropIndex(e.GetPosition(Tabs));
        var transfer = drag.Window.DetachWorkspace(tab);
        AddWorkspace(transfer.Workspace, transfer.CustomTitle, transfer.Color, index);
        drag.Window.CloseIfEmpty();
        Activate();
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
        if (DropTarget(host) is not { } target || EdgeUnder(host, e.GetPosition(host)) is not { } edge)
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
            _ => "Split down",
        };
        ShowDropPreview(host, edge);
    }

    private void OnPaneDrop(object sender, DragEventArgs e)
    {
        var host = (Border)sender;
        HideDropPreview();
        if (DropTarget(host) is not { } target || EdgeUnder(host, e.GetPosition(host)) is not { } edge ||
            App.Instance.Drag is not { Tab: WorkspaceTab dragged } drag)
        {
            return;
        }

        App.Instance.Drag = null;
        var (tab, group) = target;
        var transfer = drag.Window.DetachWorkspace(dragged);
        tab.Workspace.Merge(group, edge, transfer.Workspace);
        foreach (var pane in tab.Workspace.Tabs)
        {
            Wire(pane, tab);
        }

        Rebuild(tab);
        drag.Window.CloseIfEmpty();
        Activate();
    }

    /// <summary>
    /// The workspace and group under a host, when a tab from elsewhere is being dragged
    /// (a tab can't be dropped onto its own panes).
    /// </summary>
    private (WorkspaceTab Tab, TabGroup<TerminalTab> Group)? DropTarget(Border host)
    {
        if (App.Instance.Drag is not { Tab: WorkspaceTab dragged } || host.Tag is not TabGroup<TerminalTab> group)
        {
            return null;
        }

        var tab = AllTabs().FirstOrDefault(t => t.Workspace.Groups.Contains(group));
        return tab == null || tab == dragged ? null : (tab, group);
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

    private void ShowDropPreview(FrameworkElement host, PaneDirection edge)
    {
        var origin = host.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        double w = host.ActualWidth, h = host.ActualHeight;
        var (x, y, pw, ph) = edge switch
        {
            PaneDirection.Left => (origin.X, origin.Y, w / 2, h),
            PaneDirection.Right => (origin.X + w / 2, origin.Y, w / 2, h),
            PaneDirection.Up => (origin.X, origin.Y, w, h / 2),
            _ => (origin.X, origin.Y + h / 2, w, h / 2),
        };
        DropPreview.Margin = new Thickness(x, y, 0, 0);
        DropPreview.Width = pw;
        DropPreview.Height = ph;
        DropPreview.Visibility = Visibility.Visible;
    }

    private void HideDropPreview() => DropPreview.Visibility = Visibility.Collapsed;
}
