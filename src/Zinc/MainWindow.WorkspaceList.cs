using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;
using Zinc.Terminal;
using Windows.ApplicationModel.DataTransfer;

namespace Zinc;

// workspace-tabs = left: the workspaces listed in a column on the left, under a plain title bar.
// The TabView stays (it holds the workspaces and shows the selected one's content) with its
// strip hidden; the list mirrors its items. Everything else addresses workspaces through the
// TabView, so the list is refreshed whenever its items, selection or colors change.
public sealed partial class MainWindow
{
    /// <summary>The caption buttons' height (TitleBarHeightOption.Standard).</summary>
    private const double TitleBarHeight = 32;

    private WorkspaceList? _workspaceList;
    private Grid? _listFooter;
    private TextBlock? _newWorkspaceLabel;

    private void UseWorkspaceList()
    {
        // The strip footer's parts move out: the drag region becomes the title bar, the
        // profile menu button joins the list's header.
        var footer = (Grid)Tabs.TabStripFooter;
        footer.Children.Remove(NewTabMenuButton);
        footer.Children.Remove(DragRegion);
        Tabs.TabStripFooter = null;
        Tabs.IsAddTabButtonVisible = false;
        Tabs.Loaded += (_, _) =>
        {
            if (VisualDescendants(Tabs).OfType<Grid>().FirstOrDefault(g => g.Name == "TabContainerGrid") is { } strip)
            {
                strip.Visibility = Visibility.Collapsed;
            }
            else
            {
                Log.Write("workspace list: TabView strip (TabContainerGrid) not found");
            }
        };

        // Title bar across the top; list | workspace content below. Overlays keep covering it all.
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        Root.RowDefinitions.Add(new RowDefinition());
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var child in Root.Children.OfType<FrameworkElement>())
        {
            Grid.SetRowSpan(child, 2);
            Grid.SetColumnSpan(child, 2);
        }

        Grid.SetRow(Tabs, 1);
        Grid.SetColumn(Tabs, 1);
        Grid.SetRowSpan(Tabs, 1);
        Grid.SetColumnSpan(Tabs, 1);

        DragRegion.Children.Add(BuildTitleRow());
        Grid.SetColumn(DragRegion, 0); // it was the footer's second column
        Grid.SetColumnSpan(DragRegion, 2);
        Root.Children.Insert(0, DragRegion);

        var list = new WorkspaceList(BuildWorkspaceListFooter());
        Grid.SetRow(list, 1);
        Root.Children.Insert(0, list);
        _workspaceList = list;

        list.ItemClicked += tab => Tabs.SelectedItem = tab.Item;
        list.ItemCloseClicked += CloseTab;
        list.PageClicked += () =>
        {
            if (StartPageItem() is { } page)
            {
                Tabs.SelectedItem = page;
            }
        };
        list.PageCloseClicked += () =>
        {
            if (StartPageItem() is { } page)
            {
                RemoveStartPage(page);
            }
        };
        list.ItemDragStarting += (tab, e) =>
        {
            e.AllowedOperations = DataPackageOperation.Move;
            App.Instance.Drag = new TabDrag(this, tab);
        };
        list.ItemDragCompleted += (_, result) =>
        {
            // As for a title-bar tab (OnTabDragCompleted): nobody took it, so tear it off.
            if (result == DataPackageOperation.None)
            {
                TryTearOff();
            }

            if (App.Instance.Drag is { Tab: WorkspaceTab })
            {
                App.Instance.Drag = null;
            }

            HideDropPreview();
        };
        list.ListDragOver += (e, _, under) =>
        {
            HideDropPreview();
            return AcceptWorkspaceDrop(e, under, reorder: true);
        };
        list.ListDrop += (_, index) => DropOnWorkspaces(index);
        SetUpChrome();
    }

    /// <summary>"New workspace" and the profile menu, below the list (where the pointer tends to rest).</summary>
    private FrameworkElement BuildWorkspaceListFooter()
    {
        var add = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 32,
            Padding = new Thickness(12, 0, 8, 0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new FontIcon { Glyph = "", FontSize = 12 },
                    (_newWorkspaceLabel = new TextBlock { Text = "New workspace", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }),
                },
            },
        };
        AutomationProperties.SetName(add, "New workspace");
        ToolTipService.SetToolTip(add, ShortcutTip("New workspace", new ActionInvocation(TerminalAction.NewWorkspace)));
        add.Click += (_, _) => OpenNewWorkspace();

        NewTabMenuButton.Height = 32;
        NewTabMenuButton.Margin = new Thickness(2, 0, 0, 0);
        NewTabMenuButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(NewTabMenuButton, 1);

        // From the bottom of the window, the profile menu opens upward.
        NewTabMenu.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft;

        var footer = _listFooter = new Grid { Margin = new Thickness(6, 4, 6, 6) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.Children.Add(add);
        footer.Children.Add(NewTabMenuButton);
        return footer;
    }

    /// <summary>On the rail: "+" and "▾" stacked, without the label.</summary>
    private void SetListFooterCompact(bool compact)
    {
        if (_listFooter == null || _newWorkspaceLabel == null)
        {
            return;
        }

        var add = (Button)_listFooter.Children[0];
        _newWorkspaceLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        add.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        add.Padding = compact ? new Thickness(0) : new Thickness(12, 0, 8, 0);
        Grid.SetColumnSpan(add, compact ? 2 : 1);
        Grid.SetRow(NewTabMenuButton, compact ? 1 : 0);
        Grid.SetColumn(NewTabMenuButton, compact ? 0 : 1);
        Grid.SetColumnSpan(NewTabMenuButton, compact ? 2 : 1);
        NewTabMenuButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        NewTabMenuButton.Margin = new Thickness(compact ? 0 : 2, 0, 0, 0);
    }

    /// <summary>Brings the list (if shown) up to date with the TabView's workspaces.</summary>
    private void RefreshWorkspaceList()
    {
        var page = StartPageItem();
        _workspaceList?.Update(AllTabs().ToList(), SelectedTab,
            tab => (tab.Color, TerminalRenderer.ToColor(tab.Active.Profile.Config.Theme.Background)),
            page != null, page != null && ReferenceEquals(Tabs.SelectedItem, page));
    }

    /// <summary>What shows a workspace, for flyouts to point at.</summary>
    private FrameworkElement AnchorOf(WorkspaceTab tab) => _workspaceList?.ElementFor(tab) ?? tab.Item;

    /// <summary>Moves a workspace to drop position <paramref name="index"/> among this window's (0 = first).</summary>
    private void MoveWorkspace(WorkspaceTab tab, int index)
    {
        var from = Tabs.TabItems.IndexOf(tab.Item);
        var to = Math.Clamp(index > from ? index - 1 : index, 0, Tabs.TabItems.Count - 1);
        if (from < 0 || to == from)
        {
            return;
        }

        // The shown workspace can't be taken out and put back: its content is still attached
        // to the TabView's content area until the next layout pass, and re-adding it fails fast
        // (0x800F1000). So when it's the one moving, the ones it passes move the other way.
        var items = Tabs.TabItems;
        var selected = Tabs.SelectedItem;
        if (!ReferenceEquals(selected, tab.Item))
        {
            items.RemoveAt(from);
            items.Insert(to, tab.Item);
        }
        else
        {
            var (first, count, at) = to > from ? (from + 1, to - from, from) : (to, from - to, to + 1);
            var passed = Enumerable.Range(first, count).Select(i => items[i]).ToList();
            for (var i = 0; i < count; i++)
            {
                items.RemoveAt(first);
            }

            for (var i = 0; i < count; i++)
            {
                items.Insert(at + i, passed[i]);
            }
        }

        Tabs.SelectedItem = selected;
        RefreshWorkspaceList();
    }
}
