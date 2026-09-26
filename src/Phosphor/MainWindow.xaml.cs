using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Phosphor.Input;
using Phosphor.Panes;
using Phosphor.Terminal;

namespace Phosphor;

/// <summary>A window of tabs; each tab holds a tree of terminal panes.</summary>
public sealed partial class MainWindow : Window
{
    private readonly ConfigFile _config;
    private readonly Keybindings _keybindings;
    private readonly Brush _dividerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));
    private readonly Brush _focusBrush;
    private readonly Brush _noFocusBrush = new SolidColorBrush(Colors.Transparent);

    /// <summary>One terminal: the profile/command it was opened with, and its control.</summary>
    private sealed class Pane(Profile profile, string command, string workingDirectory, TerminalControl control)
    {
        public Profile Profile { get; } = profile;
        public string Command { get; } = command;
        public string WorkingDirectory { get; } = workingDirectory;
        public TerminalControl Control { get; } = control;

        /// <summary>Wraps the control; its border marks the focused pane when a tab has several.</summary>
        public Border Host { get; } = new() { Child = control };
    }

    private sealed class Tab(TabViewItem item, PaneTree<Pane> panes, Pane active)
    {
        public TabViewItem Item { get; } = item;

        /// <summary>
        /// The tab's content, set once. Layouts are rebuilt inside it so pane hosts only
        /// ever live in Panels, where removing a child detaches it immediately (a
        /// ContentControl's content lets go only on its next layout pass).
        /// </summary>
        public Grid Container { get; } = new();
        public PaneTree<Pane> Panes { get; } = panes;
        public Pane Active { get; set; } = active;

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
        SystemBackdrop = new MicaBackdrop();
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
        OpenTab(profile, command, profile.Config.ResolveWorkingDirectory());
        ReplayTestKeys();

        Closed += (_, _) =>
        {
            foreach (var tab in AllTabs().ToList())
            {
                foreach (var leaf in tab.Panes.Leaves)
                {
                    leaf.Content.Control.Close();
                }
            }
        };
    }

    private IEnumerable<Tab> AllTabs() => Tabs.TabItems.Cast<TabViewItem>().Select(i => (Tab)i.Tag);

    private Tab? SelectedTab => (Tabs.SelectedItem as TabViewItem)?.Tag as Tab;

    // ---------------------------------------------------------------- panes

    private Pane CreatePane(Profile profile, string command, string workingDirectory, Func<Tab> tab)
    {
        var control = new TerminalControl(profile.Config, _keybindings, command, workingDirectory)
        {
            // A canvas has no desired size of its own; make sure it fills its slot.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var pane = new Pane(profile, command, workingDirectory, control);

        control.ActionHandler = invocation => HandleAction(invocation, tab(), pane);
        control.TitleChanged += _ => UpdateTitle(tab());
        control.CloseRequested += () => ClosePane(tab(), pane);
        control.GotFocus += (_, _) =>
        {
            var t = tab();
            t.Active = pane;
            UpdateFocusBorders(t);
            UpdateTitle(t);
        };
        return pane;
    }

    private void SplitPane(Tab tab, Pane origin, PaneDirection where, Profile profile, string command, string workingDirectory)
    {
        if (tab.Panes.Find(origin) is not { } leaf)
        {
            return;
        }

        var pane = CreatePane(profile, command, workingDirectory, () => tab);
        tab.Panes.Split(leaf, where, pane);
        tab.Active = pane;
        Rebuild(tab);
    }

    private void ClosePane(Tab tab, Pane pane)
    {
        if (tab.Panes.Find(pane) is not { } leaf)
        {
            return;
        }

        pane.Control.Close();
        var next = tab.Panes.Remove(leaf);
        if (next == null)
        {
            RemoveTab(tab);
            return;
        }

        tab.Active = next.Content;
        Rebuild(tab);
    }

    /// <summary>Rebuilds a tab's content from its pane tree and focuses the active pane.</summary>
    private void Rebuild(Tab tab)
    {
        // Take the old layout apart first: pane hosts are reused and XAML elements can't
        // have two parents (0x800F1000). FrameworkElement.Parent is null for children
        // added to a Panel in code, so detaching by asking each host for its parent fails.
        Dismantle(tab.Container);
        if (tab.Panes.Root is { } root)
        {
            tab.Container.Children.Add(Build(root));
        }

        UpdateFocusBorders(tab);
        UpdateTitle(tab);
        FocusActive(tab);
    }

    private UIElement Build(PaneNode node)
    {
        if (node is PaneLeaf<Pane> leaf)
        {
            // The host may still sit in the previous layout's Grid; detach it first.
            return leaf.Content.Host;
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

    private static void Dismantle(Panel panel)
    {
        foreach (var child in panel.Children.OfType<Panel>().ToList())
        {
            Dismantle(child);
        }

        panel.Children.Clear();
    }

    private void UpdateFocusBorders(Tab tab)
    {
        // Borders only appear once a tab is split, so a lone pane uses the full area.
        var several = tab.Panes.Count > 1;
        foreach (var leaf in tab.Panes.Leaves)
        {
            leaf.Content.Host.BorderThickness = new Thickness(several ? 1 : 0);
            leaf.Content.Host.BorderBrush = several && leaf.Content == tab.Active ? _focusBrush : _noFocusBrush;
        }
    }

    private void FocusActive(Tab tab)
    {
        // After a rebuild the panes are re-parented and not loaded yet; focus once
        // layout has run.
        var control = tab.Active.Control;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => control.Focus(FocusState.Programmatic));
    }

    /// <summary>Finds the Grid built for a split in a tab's current content.</summary>
    private static Grid? GridFor(Tab tab, PaneSplit split) =>
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

    private void OpenTab(Profile profile, string command, string workingDirectory)
    {
        Tab? tab = null;
        var pane = CreatePane(profile, command, workingDirectory, () => tab!);
        // Note: no Horizontal/VerticalContentAlignment here — TabViewItem applies them
        // to its header, and Stretch pushed the label above the icon.
        var item = new TabViewItem();
        tab = new Tab(item, new PaneTree<Pane>(pane), pane);
        item.Header = tab.Header;
        item.Tag = tab;
        item.Content = tab.Container;
        item.ContextFlyout = BuildTabMenu(tab);

        Tabs.TabItems.Add(item);
        Rebuild(tab);
        Tabs.SelectedItem = item;
    }

    private void OpenTab(Profile profile) =>
        OpenTab(profile, ShellResolver.ForProfile(profile.Config), profile.Config.ResolveWorkingDirectory());

    /// <summary>Closes every pane in a tab and removes it.</summary>
    private void CloseTab(Tab tab)
    {
        foreach (var leaf in tab.Panes.Leaves.ToList())
        {
            leaf.Content.Control.Close();
        }

        RemoveTab(tab);
    }

    private void RemoveTab(Tab tab)
    {
        var index = Tabs.TabItems.IndexOf(tab.Item);
        if (index < 0)
        {
            return;
        }

        Tabs.TabItems.RemoveAt(index);
        if (Tabs.TabItems.Count == 0)
        {
            Close();
            return;
        }

        Tabs.SelectedIndex = Math.Min(index, Tabs.TabItems.Count - 1);
    }

    private void UpdateTitle(Tab tab)
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
        CloseTab((Tab)args.Tab.Tag);

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
            var where = PaneTree<Pane>.AutoDirection(pane.Control.ActualWidth, pane.Control.ActualHeight);
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

    private bool HandleAction(ActionInvocation invocation, Tab tab, Pane origin)
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
                var where = PaneTree<Pane>.AutoDirection(origin.Control.ActualWidth, origin.Control.ActualHeight);
                SplitPane(tab, origin, where, origin.Profile, origin.Command, origin.WorkingDirectory);
                return true;
            case TerminalAction.MoveFocus:
                if (tab.Panes.Find(origin) is { } from && tab.Panes.Neighbor(from, invocation.Direction) is { } to)
                {
                    tab.Active = to.Content;
                    FocusActive(tab);
                }

                return true;
            case TerminalAction.ResizePane:
                if (tab.Panes.Find(origin) is { } leaf &&
                    tab.Panes.Resize(leaf, invocation.Direction, 0.05) is { } split &&
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
