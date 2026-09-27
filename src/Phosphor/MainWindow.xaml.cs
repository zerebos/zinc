using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Phosphor.Input;
using Phosphor.Panes;
using Phosphor.Terminal;

namespace Phosphor;

/// <summary>
/// A window of workspaces (the title-bar tabs). Each workspace is a split layout of tab
/// groups, and each tab in a group is one terminal. (Groups hold one tab until tab groups
/// arrive, so a group is what the UI still calls a pane.)
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ConfigFile _config;
    private readonly Keybindings _keybindings;
    private readonly Brush _dividerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));
    private readonly Brush _focusBrush;
    private readonly Brush _noFocusBrush = new SolidColorBrush(Colors.Transparent);

    /// <summary>One terminal tab: the profile/command it was opened with, and its control.</summary>
    internal sealed class TerminalTab(Profile profile, string command, string workingDirectory, TerminalControl control)
    {
        public Profile Profile { get; } = profile;
        public string Command { get; } = command;
        public string WorkingDirectory { get; } = workingDirectory;
        public TerminalControl Control { get; } = control;

        /// <summary>Removes the owning window's event wiring (set by <see cref="Wire"/>).</summary>
        public Action? Unwire { get; set; }
    }

    /// <summary>
    /// A workspace on its way to another window: its terminals keep running, detached from
    /// the old window's elements and events.
    /// </summary>
    internal sealed record WorkspaceTransfer(Workspace<TerminalTab> Workspace, string? CustomTitle, Windows.UI.Color? Color);

    /// <summary>A title-bar tab and the workspace it shows.</summary>
    private sealed class WorkspaceTab(TabViewItem item, Workspace<TerminalTab> workspace)
    {
        public TabViewItem Item { get; } = item;

        /// <summary>
        /// The tab's content, set once. Layouts are rebuilt inside it so pane hosts only
        /// ever live in Panels, where removing a child detaches it immediately (a
        /// ContentControl's content lets go only on its next layout pass).
        /// </summary>
        public Grid Container { get; } = new();
        public Workspace<TerminalTab> Workspace { get; } = workspace;

        /// <summary>The focused terminal tab.</summary>
        public TerminalTab Active => Workspace.Active;

        /// <summary>Icon + title, updated in place.</summary>
        public TabHeader Header { get; } = new(item);

        /// <summary>Set by "Rename tab"; overrides the app/profile title until cleared.</summary>
        public string? CustomTitle { get; set; }

        /// <summary>Set by "Change tab color".</summary>
        public Windows.UI.Color? Color { get; set; }

        /// <summary>The (tab color, pane background) last applied to the item's resources.</summary>
        public object? AppliedColors { get; set; }
    }

    public MainWindow(ConfigFile config, Keybindings keybindings, Profile profile, string command, bool quick = false)
        : this(config, keybindings, quick)
    {
        OpenTab(profile, command, profile.Config.ResolveWorkingDirectory());
        ReplayTestKeys();
    }

    /// <summary>A window that takes over a workspace moved from another window.</summary>
    internal MainWindow(ConfigFile config, Keybindings keybindings, WorkspaceTransfer adopt)
        : this(config, keybindings, quick: false)
    {
        AddWorkspace(adopt.Workspace, adopt.CustomTitle, adopt.Color);
    }

    private MainWindow(ConfigFile config, Keybindings keybindings, bool quick)
    {
        _config = config;
        _keybindings = keybindings;
        _quick = quick;
        _focusBrush = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
        InitializeComponent();

        if (quick)
        {
            // Sized and placed by ShowQuick each time it's summoned.
            ConfigureQuickWindow();
        }
        else
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 700));
        }

        Activated += OnActivated;
        HotkeyNotice.Closed += (_, _) => _hotkeyNoticeDismissed = true;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        var titleBar = AppWindow.TitleBar;
        // Standard (32 px) caption buttons fit inside the ~41 px tab strip; Tall (48 px)
        // hung below it, so their hover highlight covered the top of the terminal.
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.White;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
        SystemBackdrop = TestHooks.Deterministic ? null : new MicaBackdrop();
        // Unpackaged WinUI windows don't pick up the exe's icon on their own.
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"));
        MenuKeySuppressor.Attach(this);

        Tabs.Loaded += (_, _) => DisableTabAnimations();
        Root.Loaded += (_, _) =>
        {
            UpdateCaptionInset();
            Root.XamlRoot.Changed += (_, _) => UpdateCaptionInset();
        };

        BuildNewTabMenu();

        Closed += (_, _) =>
        {
            foreach (var tab in AllTabs().ToList())
            {
                foreach (var pane in tab.Workspace.Tabs)
                {
                    pane.Control.Close();
                }
            }
        };
    }

    private IEnumerable<WorkspaceTab> AllTabs() => Tabs.TabItems.Cast<TabViewItem>().Select(i => (WorkspaceTab)i.Tag);

    private WorkspaceTab? SelectedTab => (Tabs.SelectedItem as TabViewItem)?.Tag as WorkspaceTab;

    // ---------------------------------------------------------------- panes

    private TerminalTab CreatePane(Profile profile, string command, string workingDirectory)
    {
        var control = new TerminalControl(profile.Config, _keybindings, command, workingDirectory)
        {
            // A canvas has no desired size of its own; make sure it fills its slot.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        return new TerminalTab(profile, command, workingDirectory, control);
    }

    /// <summary>
    /// Connects a terminal's events to this window, as part of <paramref name="tab"/>.
    /// Undone by <see cref="TerminalTab.Unwire"/> when it moves elsewhere.
    /// </summary>
    private void Wire(TerminalTab pane, WorkspaceTab tab)
    {
        pane.Unwire?.Invoke();
        var control = pane.Control;
        Action<string> title = _ => UpdateTitle(tab);
        Action close = () => ClosePane(tab, pane);
        RoutedEventHandler focus = (_, _) =>
        {
            tab.Workspace.Activate(pane);
            UpdateFocusBorders(tab);
            UpdateTitle(tab);
        };

        control.ActionHandler = invocation => HandleAction(invocation, tab, pane);
        control.TitleChanged += title;
        control.CloseRequested += close;
        control.GotFocus += focus;
        pane.Unwire = () =>
        {
            control.ActionHandler = null;
            control.TitleChanged -= title;
            control.CloseRequested -= close;
            control.GotFocus -= focus;
            pane.Unwire = null;
        };
    }

    /// <summary>Opens a terminal in a new group beside <paramref name="origin"/>'s group.</summary>
    private void SplitPane(WorkspaceTab tab, TerminalTab origin, PaneDirection where, Profile profile, string command, string workingDirectory)
    {
        if (tab.Workspace.GroupOf(origin) is not { } group)
        {
            return;
        }

        var pane = CreatePane(profile, command, workingDirectory);
        Wire(pane, tab);
        tab.Workspace.Split(group, where, pane);
        Rebuild(tab);
    }

    /// <summary>Closes one terminal tab; an emptied group, then workspace, goes with it.</summary>
    private void ClosePane(WorkspaceTab tab, TerminalTab pane)
    {
        if (tab.Workspace.GroupOf(pane) == null)
        {
            return;
        }

        pane.Control.Close();
        if (!tab.Workspace.Close(pane))
        {
            RemoveTab(tab);
            return;
        }

        Rebuild(tab);
    }

    /// <summary>Rebuilds a workspace's content from its layout and focuses the active tab.</summary>
    private void Rebuild(WorkspaceTab tab)
    {
        // Take the old layout apart first: pane hosts are reused and XAML elements can't
        // have two parents (0x800F1000). FrameworkElement.Parent is null for children
        // added to a Panel in code, so detaching by asking each host for its parent fails.
        Dismantle(tab.Container);
        if (tab.Workspace.Layout.Root is { } root)
        {
            tab.Container.Children.Add(Build(root));
        }

        UpdateFocusBorders(tab);
        UpdateTitle(tab);
        FocusActive(tab);
    }

    private UIElement Build(PaneNode node)
    {
        if (node is PaneLeaf<TabGroup<TerminalTab>> leaf)
        {
            return HostOf(leaf.Content);
        }

        var split = (PaneSplit)node;
        var grid = new Grid { Tag = split };
        var horizontal = split.Orientation == SplitOrientation.Horizontal;
        for (var i = 0; i < 3; i++)
        {
            if (horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition());
            }
        }

        if (horizontal)
        {
            grid.ColumnDefinitions[1].Width = new GridLength(PaneDivider.Thickness);
        }
        else
        {
            grid.RowDefinitions[1].Height = new GridLength(PaneDivider.Thickness);
        }

        PaneDivider.ApplyRatio(grid, split);
        var slots = new[] { Build(split.First), new PaneDivider(split, grid, _dividerBrush), Build(split.Second) };
        for (var slot = 0; slot < 3; slot++)
        {
            var element = (FrameworkElement)slots[slot];
            if (horizontal)
            {
                Grid.SetColumn(element, slot);
            }
            else
            {
                Grid.SetRow(element, slot);
            }

            grid.Children.Add(element);
        }

        return grid;
    }

    /// <summary>
    /// A group's element in the layout: a border (marking focus once a workspace is split)
    /// around the shown tab's terminal. Kept on the group and reused across rebuilds.
    /// </summary>
    private Border HostOf(TabGroup<TerminalTab> group)
    {
        if (group.Tag is not Border host)
        {
            // The host knows its group, so a tab dropped on it knows where to split.
            host = new Border { Tag = group };
            EnableDrop(host);
            group.Tag = host;
        }

        if (host.Child != group.Active.Control)
        {
            host.Child = group.Active.Control;
        }

        return host;
    }

    private static void Dismantle(Panel panel)
    {
        foreach (var child in panel.Children.OfType<Panel>().ToList())
        {
            Dismantle(child);
        }

        panel.Children.Clear();
    }

    private void UpdateFocusBorders(WorkspaceTab tab)
    {
        // Borders only appear once a workspace is split, so a lone group uses the full area.
        var several = tab.Workspace.Layout.Count > 1;
        foreach (var group in tab.Workspace.Groups)
        {
            var host = HostOf(group);
            host.BorderThickness = new Thickness(several ? 1 : 0);
            host.BorderBrush = several && group == tab.Workspace.ActiveGroup ? _focusBrush : _noFocusBrush;
        }
    }

    private void FocusActive(WorkspaceTab tab)
    {
        // After a rebuild the panes are re-parented and not loaded yet; focus once
        // layout has run.
        var control = tab.Active.Control;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => control.Focus(FocusState.Programmatic));
    }

    /// <summary>Finds the Grid built for a split in a tab's current content.</summary>
    private static Grid? GridFor(WorkspaceTab tab, PaneSplit split) =>
        Descendants(tab.Container).OfType<Grid>().FirstOrDefault(g => g.Tag == split);

    private static IEnumerable<UIElement> Descendants(UIElement? element)
    {
        if (element == null)
        {
            yield break;
        }

        yield return element;
        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                foreach (var d in Descendants(child))
                {
                    yield return d;
                }
            }
        }
    }

    // ---------------------------------------------------------------- tabs

    private void OpenTab(Profile profile, string command, string workingDirectory) =>
        AddWorkspace(new Workspace<TerminalTab>(CreatePane(profile, command, workingDirectory)));

    /// <summary>Shows a workspace (new, or moved from another window) as a new, selected tab.</summary>
    private WorkspaceTab AddWorkspace(Workspace<TerminalTab> workspace, string? customTitle = null, Windows.UI.Color? color = null,
        int? index = null)
    {
        // Note: no Horizontal/VerticalContentAlignment here — TabViewItem applies them
        // to its header, and Stretch pushed the label above the icon.
        var item = new TabViewItem();
        var tab = new WorkspaceTab(item, workspace) { CustomTitle = customTitle, Color = color };
        item.Header = tab.Header;
        item.Tag = tab;
        item.Content = tab.Container;
        item.ContextFlyout = BuildTabMenu(tab);
        foreach (var pane in workspace.Tabs)
        {
            Wire(pane, tab);
        }

        Tabs.TabItems.Insert(Math.Clamp(index ?? Tabs.TabItems.Count, 0, Tabs.TabItems.Count), item);
        Rebuild(tab);
        Tabs.SelectedItem = item;
        return tab;
    }

    /// <summary>
    /// Takes a workspace out of this window without ending its terminals: its tab goes, its
    /// layout elements are taken apart and its terminals unwired, ready for another window.
    /// The caller closes this window if it's now empty, once the workspace has a new home
    /// (closing first could quit the app: it would have no windows left).
    /// </summary>
    private WorkspaceTransfer DetachWorkspace(WorkspaceTab tab)
    {
        Dismantle(tab.Container);
        foreach (var group in tab.Workspace.Groups)
        {
            ReleaseHost(group);
        }

        foreach (var pane in tab.Workspace.Tabs)
        {
            pane.Unwire?.Invoke();
        }

        var transfer = new WorkspaceTransfer(tab.Workspace, tab.CustomTitle, tab.Color);
        RemoveTab(tab, closeWhenEmpty: false);
        return transfer;
    }

    /// <summary>Moves a workspace, running terminals and all, to a new window.</summary>
    private void MoveToNewWindow(WorkspaceTab tab)
    {
        App.Instance.OpenWindow(DetachWorkspace(tab));
        CloseIfEmpty();
    }

    /// <summary>Moves the group showing <paramref name="pane"/> out into a new tab of this window.</summary>
    private void MovePaneToNewTab(WorkspaceTab tab, TerminalTab pane)
    {
        if (TakeGroup(tab, pane) is { } group)
        {
            AddWorkspace(new Workspace<TerminalTab>(group));
        }
    }

    /// <summary>Moves the group showing <paramref name="pane"/> to a new window (a lone group moves its whole tab).</summary>
    private void MovePaneToNewWindow(WorkspaceTab tab, TerminalTab pane)
    {
        if (tab.Workspace.Layout.Count == 1)
        {
            MoveToNewWindow(tab);
            return;
        }

        if (TakeGroup(tab, pane) is { } group)
        {
            ReleaseHost(group);
            foreach (var t in group.Tabs)
            {
                t.Unwire?.Invoke();
            }

            App.Instance.OpenWindow(new WorkspaceTransfer(new Workspace<TerminalTab>(group), null, null));
        }
    }

    /// <summary>Takes <paramref name="pane"/>'s group out of a split workspace, terminals running; null if it's the only group.</summary>
    private TabGroup<TerminalTab>? TakeGroup(WorkspaceTab tab, TerminalTab pane)
    {
        if (tab.Workspace.Layout.Count < 2 || tab.Workspace.GroupOf(pane) is not { } group)
        {
            return null;
        }

        tab.Workspace.Detach(group);
        Rebuild(tab);
        return group;
    }

    /// <summary>Drops a group's host element (it belongs to this window's tree).</summary>
    private static void ReleaseHost(TabGroup<TerminalTab> group)
    {
        if (group.Tag is Border host)
        {
            host.Child = null;
        }

        group.Tag = null;
    }

    private void CloseIfEmpty()
    {
        if (Tabs.TabItems.Count == 0)
        {
            Close();
        }
    }

    private void OpenTab(Profile profile) =>
        OpenTab(profile, ShellResolver.ForProfile(profile.Config), profile.Config.ResolveWorkingDirectory());

    /// <summary>Closes every terminal in a workspace and removes its tab.</summary>
    private void CloseTab(WorkspaceTab tab)
    {
        foreach (var pane in tab.Workspace.Tabs.ToList())
        {
            pane.Control.Close();
        }

        RemoveTab(tab);
    }

    private void RemoveTab(WorkspaceTab tab, bool closeWhenEmpty = true)
    {
        var index = Tabs.TabItems.IndexOf(tab.Item);
        if (index < 0)
        {
            return;
        }

        Tabs.TabItems.RemoveAt(index);
        if (Tabs.TabItems.Count == 0)
        {
            if (closeWhenEmpty)
            {
                Close();
            }

            return;
        }

        Tabs.SelectedIndex = Math.Min(index, Tabs.TabItems.Count - 1);
    }

    private void UpdateTitle(WorkspaceTab tab)
    {
        var pane = tab.Active;
        var title = tab.CustomTitle ??
                    (string.IsNullOrWhiteSpace(pane.Control.Title) ? pane.Profile.Name : pane.Control.Title);

        // Updated in place (see TabHeader): no header transition, no icon re-decode.
        tab.Header.SetTitle(title);
        tab.Header.SetProfile(pane.Profile);
        ApplyTabBackground(tab);

        if (SelectedTab == tab && Title != title)
        {
            Title = title;
        }
    }

    /// <summary>
    /// Keeps the tab strip clear of the caption buttons: the footer's drag area is at
    /// least as wide as the buttons (plus some room to grab), so tabs shrink and then
    /// scroll instead of sliding under Minimize/Maximize/Close.
    /// </summary>
    private void UpdateCaptionInset()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        DragRegion.MinWidth = AppWindow.TitleBar.RightInset / scale + 48;
    }

    /// <summary>
    /// Turns off the tab strip's add/remove/reorder transitions: new tabs otherwise
    /// slide in visibly after their terminal has already rendered.
    /// </summary>
    private void DisableTabAnimations()
    {
        foreach (var list in VisualDescendants(Tabs).OfType<ListView>())
        {
            list.ItemContainerTransitions = [];
            Log.Write($"tab strip: cleared transitions on {list.Name}");
        }
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in VisualDescendants(child))
            {
                yield return d;
            }
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTab is { } tab)
        {
            UpdateTitle(tab);
            FocusActive(tab);
        }
    }

    private void OnAddTabButtonClick(TabView sender, object args) => OpenTab(_config.DefaultProfile);

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
        CloseTab((WorkspaceTab)args.Tab.Tag);

    private void BuildNewTabMenu()
    {
        var index = 0;
        foreach (var profile in _config.Profiles)
        {
            var item = new MenuFlyoutItem { Text = profile.Name, Icon = new ImageIconElement(ProfileIcons.For(profile)) };
            if (index < 9)
            {
                // Mirrors the Ctrl+Shift+N (profile N) bindings.
                item.KeyboardAcceleratorTextOverride = $"Ctrl+Shift+{index + 1}";
            }

            item.Click += (_, _) => OpenProfile(profile);
            NewTabMenu.Items.Add(item);
            index++;
        }

    }

    /// <summary>
    /// Opens a profile from the profile menu: a new tab, or with Alt held a split of the
    /// focused pane (along its longer side), or with Shift held a new window.
    /// </summary>
    private void OpenProfile(Profile profile)
    {
        var keys = Keyboard.State.Capture();
        if (keys.Alt && SelectedTab is { } tab)
        {
            var pane = tab.Active;
            var where = PaneDirections.Auto(pane.Control.ActualWidth, pane.Control.ActualHeight);
            SplitPane(tab, pane, where, profile, ShellResolver.ForProfile(profile.Config),
                profile.Config.ResolveWorkingDirectory());
        }
        else if (keys.Shift)
        {
            App.Instance.OpenWindow(profile);
        }
        else
        {
            OpenTab(profile);
        }
    }

    /// <summary>
    /// Test hook: PHOSPHOR_TEST_KEYS="alt+shift+plus,alt+left" replays chords (one per second)
    /// through the keybindings and action handler, as if pressed in the focused pane.
    /// Automated tests can't post real modifier state to the window.
    /// </summary>
    private void ReplayTestKeys()
    {
        if (Environment.GetEnvironmentVariable("PHOSPHOR_TEST_KEYS") is not { Length: > 0 } keys)
        {
            return;
        }

        var queue = new Queue<string>(keys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            if (!queue.TryDequeue(out var chord) || SelectedTab is not { } tab)
            {
                timer.Stop();
                return;
            }

            if (chord.StartsWith("menu:"))
            {
                // "menu:Split pane/Split right" runs that tab context menu item's command.
                var item = FindMenuItem(tab.Item.ContextFlyout as MenuFlyout, chord["menu:".Length..].Split('/'));
                Log.Write($"test menu {chord}: {(item != null ? "found" : "missing")}");
                item?.Command?.Execute(null);
                return;
            }

            var bound = _keybindings.TryGet(KeyChord.Parse(chord), out var invocation);
            Log.Write($"test key {chord}: {(bound ? invocation.ToString() : "unbound")}");
            if (bound)
            {
                // The pane first, like a real key press (pane actions: copy, find, ...).
                tab.Active.Control.Invoke(invocation);
            }
        };
        timer.Start();
    }

    private static MenuFlyoutItem? FindMenuItem(MenuFlyout? menu, string[] path)
    {
        IList<MenuFlyoutItemBase>? items = menu?.Items;
        for (var i = 0; items != null && i < path.Length; i++)
        {
            var match = items.FirstOrDefault(x => x is MenuFlyoutItem m && m.Text == path[i] || x is MenuFlyoutSubItem sub && sub.Text == path[i]);
            if (i == path.Length - 1)
            {
                return match as MenuFlyoutItem;
            }

            items = (match as MenuFlyoutSubItem)?.Items;
        }

        return null;
    }

    // ------------------------------------------------------------- actions

    private bool HandleAction(ActionInvocation invocation, WorkspaceTab tab, TerminalTab origin)
    {
        var count = Tabs.TabItems.Count;
        var defaults = _config.DefaultProfile;
        switch (invocation.Action)
        {
            case TerminalAction.NewTab:
                OpenTab(defaults);
                return true;
            case TerminalAction.NewTabFromProfile:
                if (invocation.Index < _config.Profiles.Count)
                {
                    OpenTab(_config.Profiles[invocation.Index]);
                }

                return true;
            case TerminalAction.DuplicateTab:
                OpenTab(origin.Profile, origin.Command, origin.WorkingDirectory);
                return true;
            case TerminalAction.OpenCommandPalette:
                OpenCommandPalette();
                return true;
            case TerminalAction.OpenNewTabMenu:
                NewTabMenu.ShowAt(NewTabMenuButton);
                return true;
            case TerminalAction.ClosePane:
                ClosePane(tab, origin);
                return true;
            case TerminalAction.NextTab:
                Tabs.SelectedIndex = (Tabs.SelectedIndex + 1) % count;
                return true;
            case TerminalAction.PreviousTab:
                Tabs.SelectedIndex = (Tabs.SelectedIndex - 1 + count) % count;
                return true;
            case TerminalAction.SwitchToTab:
                var target = invocation.Index < 0 ? count - 1 : invocation.Index;
                if (target < count)
                {
                    Tabs.SelectedIndex = target;
                }

                return true;
            case TerminalAction.NewWindow:
                App.Instance.OpenWindow(defaults);
                return true;
            case TerminalAction.SplitPane:
                SplitPane(tab, origin, invocation.Direction, defaults, ShellResolver.ForProfile(defaults.Config),
                    defaults.Config.ResolveWorkingDirectory());
                return true;
            case TerminalAction.DuplicatePane:
                var where = PaneDirections.Auto(origin.Control.ActualWidth, origin.Control.ActualHeight);
                SplitPane(tab, origin, where, origin.Profile, origin.Command, origin.WorkingDirectory);
                return true;
            case TerminalAction.MoveFocus:
                if (tab.Workspace.GroupOf(origin) is { } from && tab.Workspace.Neighbor(from, invocation.Direction) is { } to)
                {
                    tab.Workspace.Activate(to.Active);
                    FocusActive(tab);
                }

                return true;
            case TerminalAction.ResizePane:
                if (tab.Workspace.GroupOf(origin) is { } group &&
                    tab.Workspace.Resize(group, invocation.Direction, 0.05) is { } split &&
                    GridFor(tab, split) is { } grid)
                {
                    PaneDivider.ApplyRatio(grid, split);
                }

                return true;
            default:
                return false;
        }
    }
}

/// <summary>Adapts an <see cref="IconSource"/> for places that take an <see cref="IconElement"/>.</summary>
internal sealed partial class ImageIconElement : IconSourceElement
{
    public ImageIconElement(IconSource source) => IconSource = source;
}
