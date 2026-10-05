using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// A window of workspaces (the title-bar tabs). Each workspace is a split layout of tab
/// groups, and each tab in a group is one terminal. (Groups hold one tab until tab groups
/// arrive, so a group is what the UI still calls a pane.)
/// </summary>
public sealed partial class MainWindow : Window
{
    private ConfigFile _config; // replaced on a config reload (ApplyConfig)
    private readonly Keybindings _keybindings;
    /// <summary>Zinc's line between split panes, unless <c>divider-color</c> says otherwise.</summary>
    // The line between split panes: a quiet grey on the app's light or dark (divider-color replaces it).
    private static readonly Windows.UI.Color DefaultDividerColor = Application.Current.RequestedTheme == ApplicationTheme.Light
        ? Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8)
        : Windows.UI.Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A);

    private readonly SolidColorBrush _dividerBrush = new(DefaultDividerColor);
    private readonly Brush _focusBrush;

    /// <summary>One terminal tab: the profile/command it was opened with, and its control.</summary>
    internal sealed class TerminalTab(Profile profile, string command, string workingDirectory, TerminalControl control)
    {
        /// <summary>Its profile; replaced by the same id's on a config reload.</summary>
        public Profile Profile { get; set; } = profile;
        public string Command { get; } = command;
        public string WorkingDirectory { get; } = workingDirectory;
        public TerminalControl Control { get; } = control;

        /// <summary>The terminal's number for zn while Zinc runs (its shell gets it as ZINC_TERMINAL_ID).</summary>
        public int Id { get; } = CliServer.NewTerminal(control);

        /// <summary>A title given to this tab (zn terminal title), over the program's; null: the program's.</summary>
        public string? CustomTitle { get; set; }

        /// <summary>What its tab says: the given title, else the program's, else the profile's name.</summary>
        public string Title => CustomTitle ?? (string.IsNullOrWhiteSpace(Control.Title) ? Profile.Name : Control.Title);

        /// <summary>A console Windows handed over (default terminal): not saved with the session.</summary>
        public bool HandedOver { get; set; }

        /// <summary>Removes the owning window's event wiring (set by <see cref="Wire"/>).</summary>
        public Action? Unwire { get; set; }

        /// <summary>A long command finished (or failed) here out of view; cleared once seen.</summary>
        public Attention Attention { get; set; }

        /// <summary>The exit code of the command that failed here (with <see cref="Attention.Failed"/>), for its words.</summary>
        public int? FailedExit { get; set; }

        /// <summary>
        /// A color scheme chosen for this tab alone (the palette's "Color scheme for this tab…"); its
        /// <see cref="Profile"/> then carries that scheme. Re-applied on reload and restore.
        /// </summary>
        public string? ColorScheme { get; set; }
    }

    /// <summary>
    /// A workspace on its way to another window: its terminals keep running, detached from
    /// the old window's elements and events.
    /// </summary>
    internal sealed record WorkspaceTransfer(Workspace<TerminalTab> Workspace, string? CustomTitle, Windows.UI.Color? Color,
        WorkspaceFolder? Folder = null, string? Icon = null, int? Id = null);

    /// <summary>A title-bar tab and the workspace it shows.</summary>
    private static int s_lastWorkspaceId, s_lastWindowId;

    /// <summary>The window's number for zn while Zinc runs.</summary>
    internal int Id { get; } = Interlocked.Increment(ref s_lastWindowId);

    internal sealed class WorkspaceTab(TabViewItem item, Workspace<TerminalTab> workspace)
    {
        public TabViewItem Item { get; } = item;

        /// <summary>The workspace's number for zn while Zinc runs (kept when it moves to another window).</summary>
        public required int Id { get; init; }

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

        /// <summary>Its own folder (chosen on the new-workspace page): new tabs and splits start there.</summary>
        public WorkspaceFolder? Folder { get; set; }

        /// <summary>Its own icon (a <see cref="WorkspaceIcons"/> name), shown instead of the active tab's profile icon.</summary>
        public string? Icon { get; set; }
    }

    public MainWindow(ConfigFile config, Keybindings keybindings, Profile profile, string command, bool quick = false)
        : this(config, keybindings, quick)
    {
        OpenTab(profile, command, profile.Config.ResolveWorkingDirectory());
        ReplayTestKeys();
    }

    /// <summary>A new window, opening on the new-workspace page (or the default profile, with the page turned off).</summary>
    internal MainWindow(ConfigFile config, Keybindings keybindings)
        : this(config, keybindings, quick: false)
    {
        OpenNewWorkspace();
        ReplayTestKeys();
    }

    /// <summary>A window for a console handed over by Windows (default terminal) when none is open.</summary>
    internal MainWindow(ConfigFile config, Keybindings keybindings, Zinc.Pty.ConPty handedOver, string title)
        : this(config, keybindings, quick: false)
    {
        OpenHandedOver(handedOver, title);
    }

    /// <summary>
    /// A console Windows handed over (Zinc as the default terminal: a script double-clicked, Run,
    /// an installer): a new workspace with the default profile's look, running what it runs. Not saved
    /// with the session (it can't be reopened); Duplicate starts its program again.
    /// </summary>
    internal void OpenHandedOver(Zinc.Pty.ConPty pty, string title)
    {
        // Its program, not the title: from a shortcut that's the shortcut's name ("Command Prompt").
        var program = pty.ProgramPath ?? title;
        var profile = ProfileForHandedOver(program);
        var pane = CreatePane(profile, program, profile.Config.ResolveWorkingDirectory(), recent: false);
        pane.Control.HandedOver = pty;
        pane.HandedOver = true;
        var tab = AddWorkspace(new Workspace<TerminalTab>(pane), color: AutoColor(null));
        FocusActive(tab);
        Activate();
    }

    /// <summary>
    /// Who a handed-over console's tab says it is: the one profile running the same program (cmd.exe:
    /// Command Prompt), else the program itself, named after it with its own icon, in the default
    /// profile's look (several profiles run wsl.exe: which distro isn't known, so "wsl").
    /// </summary>
    private Profile ProfileForHandedOver(string program)
    {
        var exe = Path.GetFileName(program);
        bool Runs(Profile p)
        {
            var (command, _) = ProfileIcons.SplitCommand(ShellResolver.ForProfile(p.Config));
            var name = Path.GetFileName(command);
            return name.Equals(exe, StringComparison.OrdinalIgnoreCase) ||
                   (name + ".exe").Equals(exe, StringComparison.OrdinalIgnoreCase);
        }

        var matches = _config.VisibleProfiles.Where(Runs).Take(2).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        var look = _config.DefaultProfile;
        return look with
        {
            Id = "handed-over:" + exe.ToLowerInvariant(),
            Name = Path.GetFileNameWithoutExtension(program),
            Config = look.Config with { Command = program, Icon = null },
        };
    }

    /// <summary>A window that takes over a workspace moved from another window.</summary>
    internal MainWindow(ConfigFile config, Keybindings keybindings, WorkspaceTransfer adopt)
        : this(config, keybindings, quick: false)
    {
        AddWorkspace(adopt.Workspace, adopt.CustomTitle, adopt.Color, folder: adopt.Folder, icon: adopt.Icon, id: adopt.Id);
    }

    private MainWindow(ConfigFile config, Keybindings keybindings, bool quick)
    {
        _config = config;
        _keybindings = keybindings;
        _quick = quick;
        _focusBrush = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
        InitializeComponent();
        Root.KeyDown += OnWindowKeyDown;

        if (quick)
        {
            // Sized and placed by ShowQuick each time it's summoned.
            ConfigureQuickWindow();
        }
        else
        {
            WindowPlacement.Resize(this, 1100, 700);
        }

        Activated += OnActivated;
        TrackNormalBounds();
        HotkeyNotice.Closed += (_, _) => _hotkeyNoticeDismissed = true;
        if (!quick)
        {
            // The tab strip is the title bar. (The quick terminal has none: extending into one left a light strip
            // across its top.)
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(DragRegion);
        }

        var titleBar = AppWindow.TitleBar;
        // Standard (32 px) caption buttons fit inside the ~41 px tab strip; Tall (48 px)
        // hung below it, so their hover highlight covered the top of the terminal.
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        Brand.StyleCaptionButtons(titleBar);
        ApplyBackdrop();
        // Unpackaged WinUI windows don't pick up the exe's icon on their own.
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Zinc.ico"));
        MenuKeySuppressor.Attach(this);
        ConfirmWindowClose();

        ToolTipService.SetToolTip(NewTabMenuButton, ShortcutTip("Open a profile", new ActionInvocation(TerminalAction.OpenNewTabMenu)));
        Tabs.Loaded += (_, _) => DisableTabAnimations();
        if (config.Global.WorkspaceTabs == WorkspaceTabs.Left && !quick)
        {
            UseWorkspaceList();
        }

        Root.Loaded += (_, _) =>
        {
            UpdateCaptionInset();
            Root.XamlRoot.Changed += (_, _) => UpdateCaptionInset();
        };

        BuildNewTabMenu();
        SolidNoticeBacks();
        SetUpConfigNotice();
        SetUpUpdateNotice();
        ApplyDividerColor();

        Closed += (_, _) =>
        {
            // Nothing may reach for this window once it's gone (its AppWindow is null by now): the
            // timers stop, and the terminals are unwired before they close, so events already
            // queued (titles, commands, progress) find no handler here.
            if (!_quick)
            {
                // First: its shells' folders are read from them.
                Ui.Guard("remembering a closed window", () => App.Instance.NoteClosedWindow(this));
            }

            _windowClosed = true;
            Updater.Changed -= ShowUpdateNotice;
            _chromeTimer?.Stop();
            _commandClock?.Stop();
            _commandSettle?.Stop();
            _springTimer?.Stop();
            _replayTimer?.Stop();
            foreach (var tab in AllTabs().ToList())
            {
                foreach (var pane in tab.Workspace.Tabs)
                {
                    pane.Unwire?.Invoke();
                    pane.Control.Close();
                }
            }
        };
    }

    /// <summary>The workspaces, in order (a new-workspace page among them isn't one).</summary>
    /// <summary>How many terminals this window holds (Settings > About's memory row).</summary>
    internal int PaneCount => AllTabs().Sum(t => t.Workspace.Tabs.Count());

    private IEnumerable<WorkspaceTab> AllTabs() => Tabs.TabItems.Cast<TabViewItem>().Select(i => i.Tag).OfType<WorkspaceTab>();

    private WorkspaceTab? SelectedTab => (Tabs.SelectedItem as TabViewItem)?.Tag as WorkspaceTab;

    /// <summary>
    /// Key bindings while focus is outside the terminals (the tab strip, the sidebar, a menu
    /// button): a terminal runs its own bindings, so what bubbles up here unhandled comes from
    /// elsewhere. Only chords with Ctrl or Alt: plain keys and Shift+key belong to the lists and
    /// buttons. Pane actions (copy, find…) go to the focused terminal of the shown workspace.
    /// </summary>
    private void OnWindowKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Handled || _windowClosed)
        {
            return;
        }

        var state = Keyboard.State.Capture();
        if (!(state.Ctrl || state.Alt) || !_keybindings.TryGet(new KeyChord(e.Key, state.Ctrl, state.Alt, state.Shift), out var invocation))
        {
            return;
        }

        if (SelectedTab?.Active is { } active)
        {
            Log.Write($"keys: {invocation} from outside a terminal");
            active.Control.Invoke(invocation);
            e.Handled = true;
        }
        else if (StartPageItem() is { } page && ReferenceEquals(Tabs.SelectedItem, page))
        {
            // The new-workspace page: the window's own shortcuts (it has no pane to act on).
            e.Handled = HandlePageAction(invocation, page);
            Log.Write($"keys: {invocation} on the new-workspace page{(e.Handled ? "" : ": nothing to do there")}");
        }
    }

    // ---------------------------------------------------------------- panes

    /// <summary>What the next pane created types into its shell once it's ready ("New tab running…").</summary>
    private string? _startupInput;

    /// <param name="recent">Whether it counts as opening its profile (the jump list's recent profiles): not restored
    /// terminals or a console Windows handed over.</param>
    private TerminalTab CreatePane(Profile profile, string command, string workingDirectory, bool recent = true)
    {
        if (recent && !_quick)
        {
            App.Instance.NoteProfileOpened(profile.Id);
        }

        var control = new TerminalControl(profile.Config, _keybindings, command, workingDirectory)
        {
            // A canvas has no desired size of its own; make sure it fills its slot.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            StartupInput = _startupInput,
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
        Action<string> title = _ =>
        {
            UpdateTitle(tab, fromShell: true);
            UpdateFocusBorders(tab); // group strips show titles too
        };
        Action close = () => ClosePane(tab, pane);
        Action directory = () =>
        {
            UpdateTitle(tab, fromShell: true); // the command center shows the folder
            NoteFolder(pane);
        };
        Action<ShellCommand> finished = command => OnCommandFinished(tab, pane, command);
        Action<string, string> notified = (title, body) => OnProgramNotified(tab, pane, title, body);
        Action bell = () => OnBell(tab, pane);
        Action progress = () => OnProgress(tab);
        Action<IReadOnlyList<string>> shaders = problems => ShowShaderProblems(control, problems);
        RoutedEventHandler focus = (_, _) =>
        {
            tab.Workspace.Activate(pane);
            UpdateFocusBorders(tab);
            UpdateTitle(tab);
        };

        control.ActionHandler = invocation => HandleAction(invocation, tab, pane);
        control.TitleChanged += title;
        control.DirectoryChanged += directory;
        control.CommandsChanged += directory; // and the running command
        control.CommandFinished += finished;
        control.Notified += notified;
        control.BellRang += bell;
        control.ProgressChanged += progress;
        control.ShaderProblems += shaders;
        control.CloseRequested += close;
        control.GotFocus += focus;
        pane.Unwire = () =>
        {
            control.ActionHandler = null;
            control.TitleChanged -= title;
            control.DirectoryChanged -= directory;
            control.CommandsChanged -= directory;
            control.CommandFinished -= finished;
            control.Notified -= notified;
            control.BellRang -= bell;
            control.ProgressChanged -= progress;
            control.ShaderProblems -= shaders;
            ForgetShaderProblems(control);
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
        // Swap each pane's shown terminal while its host is still in the tree: removing a
        // terminal from a subtree that was just detached makes XAML report its Unloaded
        // twice, and Win2D's canvas (which counts Loaded/Unloaded) then never draws again.
        UpdateFocusBorders(tab);

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
            var view = new GroupView();
            view.SetAddToolTip(ShortcutTip("New tab in this pane", new ActionInvocation(TerminalAction.NewTab)));
            // Groups can move between workspaces, so look the workspace up when it's needed.
            view.TabClicked += pane => WithTabOf(group, t => ShowGroupTab(t, pane));
            view.TabCloseClicked += pane => WithTabOf(group, t => AskClosePane(t, pane));
            view.NewTabClicked += () => WithTabOf(group, t => NewTabInGroup(t, group, group.Active.Profile));
            view.TabMenuRequested += (pane, anchor) => WithTabOf(group, t => ShowGroupTabMenu(t, pane, anchor));
            view.TabDragStarting += (pane, e) =>
            {
                e.AllowedOperations = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                App.Instance.Drag = new TabDrag(this, pane);
            };
            view.TabDragCompleted += OnTerminalTabDragCompleted;
            view.StripDragOver += (e, _) => AcceptStripDrop(group, e);
            view.StripDrop += (e, index) => OnStripDrop(group, index);
            // The host knows its group, so a tab dropped on it knows where to go.
            host = new Border { Tag = group, Child = view };
            EnableDrop(host);
            group.Tag = host;
        }

        return host;
    }

    private void WithTabOf(TabGroup<TerminalTab> group, Action<WorkspaceTab> action)
    {
        if (AllTabs().FirstOrDefault(t => t.Workspace.Groups.Contains(group)) is { } tab)
        {
            action(tab);
        }
    }

    /// <summary>Whether a group shows its own strip of tabs (see tab-groups, group-tab-strip).</summary>
    private bool ShowsStrip(TabGroup<TerminalTab> group) =>
        !_quick && _config.Global.TabGroups && (group.Tabs.Count > 1 || _config.Global.GroupTabStrip == GroupTabStrip.Always);

    private static void Dismantle(Panel panel)
    {
        foreach (var child in panel.Children.OfType<Panel>().ToList())
        {
            Dismantle(child);
        }

        panel.Children.Clear();
    }

    /// <summary>Brings each group's pane up to date: focus marks, tab strip, shown terminal.</summary>
    private void UpdateFocusBorders(WorkspaceTab tab)
    {
        ClearSeenAttention(tab);
        // Focus is only marked once a workspace is split: an accent line along the focused
        // pane's top (its shown tab's, when it has a strip), and optionally dimmed others.
        var several = tab.Workspace.Layout.Count > 1;
        var dim = 1 - _config.Global.UnfocusedSplitOpacity;
        foreach (var group in tab.Workspace.Groups)
        {
            var host = HostOf(group);
            var focused = group == tab.Workspace.ActiveGroup;
            var fill = TerminalRenderer.ToColor(_config.Global.UnfocusedSplitFill ?? group.Active.Profile.Config.Theme.Background);
            ((GroupView)host.Child).Update(group, ShowsStrip(group), focused, _focusBrush, several, dim, fill);
        }

        ShowWorkspaceAttention(tab);
        UpdateTaskbarProgress(); // the shown workspace's progress wins; closed panes drop theirs
    }

    private void FocusActive(WorkspaceTab tab)
    {
        // After a rebuild the panes are re-parented and not loaded yet; focus once
        // layout has run.
        var control = tab.Active.Control;
        DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
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

    /// <summary>A new workspace running <paramref name="command"/> (colored per <c>workspace-colors</c>).</summary>
    private void OpenTab(Profile profile, string command, string workingDirectory) =>
        AddWorkspace(new Workspace<TerminalTab>(CreatePane(profile, command, workingDirectory)), color: AutoColor(null));

    /// <summary>Shows a workspace (new, or moved from another window) as a new, selected tab.</summary>
    private WorkspaceTab AddWorkspace(Workspace<TerminalTab> workspace, string? customTitle = null, Windows.UI.Color? color = null,
        int? index = null, WorkspaceFolder? folder = null, string? icon = null, int? id = null)
    {
        // Note: no Horizontal/VerticalContentAlignment here — TabViewItem applies them
        // to its header, and Stretch pushed the label above the icon.
        var item = new TabViewItem();
        var tab = new WorkspaceTab(item, workspace)
        {
            Id = id ?? Interlocked.Increment(ref s_lastWorkspaceId), CustomTitle = customTitle, Color = color, Folder = folder, Icon = icon,
        };
        tab.Header.SetIcon(WorkspaceIcons.Glyph(icon));
        if (_workspaceList == null)
        {
            item.Header = tab.Header; // with the list, the header is shown there instead
            _ = new TabItemDot(item, tab.Header); // the dot in the close button's place (the item's events keep it)
        }

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
        RefreshWorkspaceList();
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
        // Terminals leave their panes before the panes leave the layout (see Rebuild: the
        // other order unloads each terminal twice, which stops Win2D drawing it).
        foreach (var group in tab.Workspace.Groups)
        {
            ReleaseHost(group);
        }

        Dismantle(tab.Container);

        foreach (var pane in tab.Workspace.Tabs)
        {
            pane.Unwire?.Invoke();
        }

        var transfer = new WorkspaceTransfer(tab.Workspace, tab.CustomTitle, tab.Color, tab.Folder, tab.Icon, tab.Id);
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

        // Release the pane's terminal while the pane is still in the layout (see Rebuild).
        if (tab.Workspace.GroupOf(pane) is { } moving)
        {
            ReleaseHost(moving);
        }

        if (TakeGroup(tab, pane) is { } group)
        {
            foreach (var t in group.Tabs)
            {
                t.Unwire?.Invoke();
            }

            App.Instance.OpenWindow(new WorkspaceTransfer(new Workspace<TerminalTab>(group), null, null));
        }
    }

    // ------------------------------------------------------------- tab groups

    /// <summary>Opens a terminal as a new tab in <paramref name="group"/> and shows it.</summary>
    private void NewTabInGroup(WorkspaceTab tab, TabGroup<TerminalTab> group, Profile profile,
        string? command = null, string? workingDirectory = null)
    {
        // Given a command, it runs where asked; otherwise the profile starts in the workspace's folder.
        var (start, directory) = command != null
            ? (command, workingDirectory ?? profile.Config.ResolveWorkingDirectory())
            : StartIn(tab.Folder, profile);
        var pane = CreatePane(profile, start, directory);
        Wire(pane, tab);
        group.Add(pane);
        ShowGroupTab(tab, pane);
    }

    /// <summary>Shows and focuses a tab of its group.</summary>
    private void ShowGroupTab(WorkspaceTab tab, TerminalTab pane)
    {
        tab.Workspace.Activate(pane);
        UpdateFocusBorders(tab);
        UpdateTitle(tab);
        FocusActive(tab);
    }

    /// <summary>Shows the next (or previous) tab of <paramref name="pane"/>'s group, wrapping.</summary>
    private void CycleGroup(WorkspaceTab tab, TabGroup<TerminalTab> group, int step)
    {
        var index = (group.Tabs.ToList().IndexOf(group.Active) + step + group.Tabs.Count) % group.Tabs.Count;
        ShowGroupTab(tab, group.Tabs[index]);
    }

    private void ShowGroupTabMenu(WorkspaceTab tab, TerminalTab pane, FrameworkElement anchor)
    {
        var group = tab.Workspace.GroupOf(pane);
        var alone = tab.Workspace.Tabs.Count() == 1;
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Close tab", "", Shortcut(TerminalAction.ClosePane), () => AskClosePane(tab, pane)));
        if (group is { Tabs.Count: > 1 })
        {
            menu.Items.Add(Item("Close other tabs", null, null, () => AskClosePanes(tab, group.Tabs.Where(t => t != pane).ToList())));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var toWorkspace = Item("Move tab to new workspace", null, null, () => MoveTabToNewWorkspace(tab, pane));
        toWorkspace.IsEnabled = !alone;
        menu.Items.Add(toWorkspace);
        menu.Items.Add(Item("Move tab to new window", "", null, () => MoveTabToNewWindow(tab, pane)));
        menu.ShowAt(anchor);
    }

    /// <summary>Takes one terminal tab out of its workspace, running, and gives it a workspace of its own.</summary>
    private void MoveTabToNewWorkspace(WorkspaceTab tab, TerminalTab pane)
    {
        if (tab.Workspace.Tabs.Count() > 1)
        {
            TakeTab(tab, pane);
            AddWorkspace(new Workspace<TerminalTab>(pane));
        }
    }

    /// <summary>Moves one terminal tab to a new window (the only tab moves its whole workspace).</summary>
    private void MoveTabToNewWindow(WorkspaceTab tab, TerminalTab pane)
    {
        if (tab.Workspace.Tabs.Count() == 1)
        {
            MoveToNewWindow(tab);
            return;
        }

        TakeTab(tab, pane);
        App.Instance.OpenWindow(new WorkspaceTransfer(new Workspace<TerminalTab>(pane), null, null));
    }

    /// <summary>
    /// Takes one terminal tab out of its workspace, still running and free to be added anywhere
    /// (another pane, workspace or window): its terminal leaves the pane showing it, its window
    /// wiring comes off, and an emptied pane (then workspace) goes. The caller closes this
    /// window if it's now empty, once the tab has its new home.
    /// </summary>
    private void TakeTab(WorkspaceTab tab, TerminalTab pane)
    {
        // Let go of the terminal while its pane is still in the layout: if the pane empties it
        // leaves the layout without being updated again, and the terminal would stay attached
        // to it (and releasing it from a detached pane unloads it twice; see Rebuild).
        if (tab.Workspace.GroupOf(pane) is { Tag: Border { Child: GroupView view } } group && group.Active == pane)
        {
            view.Release();
        }

        pane.Unwire?.Invoke();
        if (tab.Workspace.Close(pane))
        {
            Rebuild(tab);
        }
        else
        {
            DetachEmptyWorkspace(tab);
        }
    }

    /// <summary>Removes a workspace whose last tab just left, without closing this window.</summary>
    private void DetachEmptyWorkspace(WorkspaceTab tab)
    {
        Dismantle(tab.Container);
        RemoveTab(tab, closeWhenEmpty: false);
    }

    /// <summary>
    /// Moves terminal tab <paramref name="pane"/> (from <paramref name="source"/>, maybe this
    /// window) into <paramref name="tab"/>: as a new pane beside <paramref name="group"/> on side
    /// <paramref name="edge"/>, or into <paramref name="group"/> at <paramref name="index"/>.
    /// </summary>
    private void MoveTerminalTab(MainWindow source, TerminalTab pane, WorkspaceTab tab, TabGroup<TerminalTab> group,
        PaneDirection? edge, int? index = null)
    {
        // Within its own pane without an edge: just a new position.
        if (edge == null && group.Contains(pane))
        {
            group.Move(pane, index ?? group.Tabs.Count);
            ShowGroupTab(tab, pane);
            return;
        }

        if (source.WorkspaceOf(pane) is not { } from)
        {
            return;
        }

        source.TakeTab(from, pane);
        if (edge is { } side && tab.Workspace.Groups.Contains(group))
        {
            tab.Workspace.Split(group, side, pane);
        }
        else if (tab.Workspace.Groups.Contains(group))
        {
            group.Add(pane, index);
            tab.Workspace.Activate(pane);
        }
        else
        {
            // The target pane went away with the tab (it was the tab's only pane): nothing to
            // attach to, so the tab gets a workspace of its own.
            AddWorkspace(new Workspace<TerminalTab>(pane));
            source.CloseIfEmpty();
            return;
        }

        Wire(pane, tab);
        Rebuild(tab);
        if (source != this)
        {
            source.CloseIfEmpty();
            Activate();
        }
    }

    /// <summary>The workspace holding a terminal tab, in this window.</summary>
    private WorkspaceTab? WorkspaceOf(TerminalTab pane) => AllTabs().FirstOrDefault(t => t.Workspace.Tabs.Contains(pane));

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
        if (group.Tag is Border { Child: GroupView view })
        {
            view.Release();
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

    /// <summary>Closes a workspace when you ask to: asks first if a command is running.</summary>
    private async void CloseTab(WorkspaceTab tab)
    {
        if (await ConfirmClose(tab.Workspace.Tabs.ToList(), "workspace"))
        {
            Ui.Guard("closing a workspace", () => CloseTabNow(tab));
        }
        else if (SelectedTab is { } shown)
        {
            FocusActive(shown);
        }
    }

    /// <summary>Closes every terminal in a workspace and removes its tab (it can be reopened: see <see cref="RememberClosed"/>).</summary>
    private void CloseTabNow(WorkspaceTab tab)
    {
        RememberClosed(tab); // first: its shells' folders are read from them
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

        var wasSelected = ReferenceEquals(Tabs.SelectedItem, tab.Item);
        var selected = Tabs.SelectedItem;
        Tabs.TabItems.RemoveAt(index);
        if (Tabs.TabItems.Count == 0)
        {
            if (closeWhenEmpty)
            {
                Close();
            }

            return;
        }

        // The selection moves only if the removed workspace was the one shown: removing another
        // (e.g. one dragged into the shown workspace) must not switch away from it.
        Tabs.SelectedItem = wasSelected ? Tabs.TabItems[Math.Min(index, Tabs.TabItems.Count - 1)] : selected;
        RefreshWorkspaceList();
    }

    /// <param name="fromShell">A shell reported a new title or folder: the command center waits for the rest of the burst.</param>
    private void UpdateTitle(WorkspaceTab tab, bool fromShell = false)
    {
        var pane = tab.Active;
        var title = tab.CustomTitle ?? pane.Title;

        // Updated in place (see TabHeader): no header transition, no icon re-decode.
        tab.Header.SetTitle(title);
        tab.Header.SetProfile(pane.Profile);
        ApplyTabBackground(tab);

        if (SelectedTab == tab)
        {
            if (Title != title)
            {
                Title = title;
            }

            if (fromShell)
            {
                UpdateCommandCenterSoon();
            }
            else
            {
                UpdateCommandCenter();
            }
        }
    }

    /// <summary>
    /// Keeps the tab strip clear of the caption buttons: the footer's drag area is at
    /// least as wide as the buttons (plus some room to grab), so tabs shrink and then
    /// scroll instead of sliding under Minimize/Maximize/Close.
    /// </summary>
    private void UpdateCaptionInset()
    {
        if (_windowClosed)
        {
            return; // its AppWindow is gone
        }

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

        // TabView's + says "Add New Tab"; here it opens a workspace (screen readers, UIA, tooltip).
        if (VisualDescendants(Tabs).OfType<Button>().FirstOrDefault(b => b.Name == "AddButton") is { } add)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, "New workspace");
            ToolTipService.SetToolTip(add, ShortcutTip("New workspace", new ActionInvocation(TerminalAction.NewWorkspace)));
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
            UpdateFocusBorders(tab); // now in view: its dots clear
            FocusActive(tab);
        }
        else if (Tabs.SelectedItem is TabViewItem { Tag: StartPage page })
        {
            Title = "New workspace";
            page.FocusFilter();
        }

        RefreshWorkspaceList();
    }

    private void OnAddTabButtonClick(TabView sender, object args) => OpenNewWorkspace();

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is WorkspaceTab tab)
        {
            CloseTab(tab);
        }
        else
        {
            RemoveStartPage(args.Tab);
        }
    }

    private void BuildNewTabMenu()
    {
        NewTabMenu.Items.Clear(); // rebuilt when the config reloads
        var index = 0;
        foreach (var profile in _config.VisibleProfiles)
        {
            var item = new MenuFlyoutItem { Text = profile.Name, Icon = new ImageIconElement(ProfileIcons.For(profile)) };
            if (index < 9)
            {
                // Mirrors the profile bindings (Ctrl+Shift+N by default).
                item.KeyboardAcceleratorTextOverride = ShortcutForProfile(profile, index) ?? "";
            }

            item.Click += (_, _) => OpenProfile(profile);
            NewTabMenu.Items.Add(item);
            index++;
        }

        // Then Zinc itself (as Windows Terminal's menu has): settings, the palette, about.
        MenuFlyoutItem AppItem(string text, int glyph, TerminalAction? action, Action run)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = ((char)glyph).ToString() } };
            if (action is { } bound)
            {
                item.KeyboardAcceleratorTextOverride = Shortcut(new ActionInvocation(bound)) ?? "";
            }

            item.Click += (_, _) => run();
            return item;
        }

        NewTabMenu.Items.Add(new MenuFlyoutSeparator());
        NewTabMenu.Items.Add(AppItem("Settings", 0xE713, TerminalAction.OpenSettings, () => App.Instance.ShowSettings()));
        NewTabMenu.Items.Add(AppItem("Command palette", 0xE721, TerminalAction.OpenCommandPalette, () => OpenCommandPalette()));
        NewTabMenu.Items.Add(AppItem("About Zinc", 0xE946, null, () => App.Instance.ShowSettings("about")));

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
            var (command, directory) = StartIn(tab.Folder, profile);
            SplitPane(tab, pane, where, profile, command, directory);
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
    /// Test hook: ZINC_TEST_KEYS="alt+shift+plus,alt+left" replays chords (one per second)
    /// through the keybindings and action handler, as if pressed in the focused pane.
    /// Automated tests can't post real modifier state to the window.
    /// </summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _replayTimer;

    private void ReplayTestKeys()
    {
        if (Environment.GetEnvironmentVariable("ZINC_TEST_KEYS") is not { Length: > 0 } keys)
        {
            return;
        }

        var queue = new Queue<string>(keys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        // Held in a field: an unreferenced DispatcherQueueTimer is collected and stops firing.
        var timer = _replayTimer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            if (!queue.TryDequeue(out var chord))
            {
                timer.Stop();
                return;
            }

            // On the new-workspace page: a key goes to the page's own shortcuts, as a real press would.
            if (SelectedTab == null && StartPageItem() is { } page && ReferenceEquals(Tabs.SelectedItem, page) && !chord.Contains(':'))
            {
                var handled = _keybindings.TryGet(KeyChord.Parse(chord), out var onPage) && HandlePageAction(onPage, page);
                Log.Write($"test key {chord} on the new-workspace page: {(handled ? onPage.ToString() : "nothing")}");
                return;
            }

            // The palette on the page: its commands, and opening it with a query typed.
            if (SelectedTab == null && StartPageItem() is { } shownPage && ReferenceEquals(Tabs.SelectedItem, shownPage) &&
                (chord.StartsWith("palette:") || chord.StartsWith("test:palette-open:")))
            {
                if (chord.StartsWith("test:palette-open:"))
                {
                    OpenCommandPalette(chord["test:palette-open:".Length..]);
                    Log.Write($"test {chord} on the new-workspace page: done");
                    return;
                }

                var command = BuildPageCommands(shownPage).FirstOrDefault(c => c.Title == chord["palette:".Length..]);
                Log.Write($"test {chord} on the new-workspace page: {(command != null ? "found" : "missing")}");
                if (command != null)
                {
                    RunPaletteCommand(command);
                }

                return;
            }

            if (SelectedTab is not { } tab)
            {
                timer.Stop();
                return;
            }

            if (chord.StartsWith("test:drop-middle:") || chord.StartsWith("test:drop-right:"))
            {
                // "test:drop-middle:1" drops workspace 1 into the focused pane, as a drag onto
                // its middle would ("drop-right": onto its right edge). Drags can't be posted.
                var from = AllTabs().ElementAtOrDefault(int.Parse(chord[(chord.LastIndexOf(':') + 1)..]));
                Log.Write($"test {chord}: {(from != null && from != tab ? "dropping" : "no such workspace")}");
                if (from != null && from != tab && tab.Workspace.GroupOf(tab.Active) is { } target)
                {
                    DropWorkspace(this, from, tab, target, chord.Contains("middle") ? null : PaneDirection.Right);
                }

                return;
            }

            if (chord == "test:throw")
            {
                // Checks the safety net: this should be logged to crash.log, shown, and survived.
                DispatcherQueue.SafeEnqueue(() => throw new InvalidOperationException("test:throw, on purpose"));
                return;
            }

            if (chord == "test:chrome-hide")
            {
                // What the pointer leaving does, for both auto-hidden bars.
                _chromePinned = false;
                RevealSidebar(false);
                RevealTitleBar(false);
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord is "test:sidebar-show" or "test:titlebar-show")
            {
                // Reveals an auto-hidden bar and keeps it (a capture has no pointer to hold it open).
                if (chord == "test:sidebar-show")
                {
                    RevealSidebar(true, pinned: true);
                }
                else
                {
                    RevealTitleBar(true, pinned: true);
                }

                Log.Write($"test {chord}: done");
                return;
            }

            if (chord is "test:appearance-light" or "test:appearance-dark")
            {
                App.Instance.SetSystemScheme(chord.EndsWith("light") ? Ghostty.ColorScheme.Light : Ghostty.ColorScheme.Dark);
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord == "test:scrollbar-expand")
            {
                // The focused pane's scrollbar, expanded and kept so (hover can't be posted).
                tab.Active.Control.PinScrollBarExpandedForTest();
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord == "test:mem")
            {
                // Where memory goes: the process's private bytes, and how much of it is .NET's heap (after a
                // collection, so it's what's live). The rest is native: libghostty's screens, Win2D/Direct3D,
                // WinUI/XAML, DirectWrite.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using var self = System.Diagnostics.Process.GetCurrentProcess();
                var gc = GC.GetGCMemoryInfo();
                Log.Write($"test {chord}: private {self.PrivateMemorySize64 / 1048576} MB, working set {self.WorkingSet64 / 1048576} MB, " +
                    $".NET heap {gc.HeapSizeBytes / 1048576} MB (committed {gc.TotalCommittedBytes / 1048576} MB), panes {AllTabs().Sum(t => t.Workspace.Tabs.Count())}");
                return;
            }

            if (chord == "test:update")
            {
                // Check and download, as the daily check does (the update round trip test).
                Log.Write($"test {chord}");
                _ = Updater.CheckAsync();
                return;
            }

            if (chord == "test:update-restart")
            {
                // The notice's Restart now.
                Log.Write($"test {chord}");
                Updater.RestartNow();
                return;
            }

            if (chord == "test:gc")
            {
                // Frees whatever is unreferenced (e.g. a retired canvas), as a later GC would.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord.StartsWith("test:workspace-at:"))
            {
                // "test:workspace-at:0" moves the shown workspace to drop position 0, as a drag
                // in the workspace list would.
                Log.Write($"test {chord}");
                MoveWorkspace(tab, int.Parse(chord["test:workspace-at:".Length..]));
                return;
            }

            if (chord.StartsWith("test:workspace-move:"))
            {
                // "test:workspace-move:2:0" moves workspace 2 (shown or not) to drop position 0.
                var parts = chord["test:workspace-move:".Length..].Split(':');
                Log.Write($"test {chord}");
                MoveWorkspace(AllTabs().ElementAt(int.Parse(parts[0])), int.Parse(parts[1]));
                return;
            }

            if (chord.StartsWith("test:tab-"))
            {
                // What dragging the focused pane's shown tab would do: "test:tab-to:1" drops it
                // into the pane 1 (layout order) of this workspace ("tab-right:1" onto its right
                // edge), "test:tab-at:0" at position 0 of its own strip. Drags can't be posted.
                var arg = int.Parse(chord[(chord.LastIndexOf(':') + 1)..]);
                var pane = tab.Active;
                var target = chord.StartsWith("test:tab-at:") ? tab.Workspace.ActiveGroup : tab.Workspace.Groups.ElementAtOrDefault(arg);
                Log.Write($"test {chord}: {(target != null ? "moving" : "no such pane")}");
                if (target != null)
                {
                    MoveTerminalTab(this, pane, tab, target, chord.StartsWith("test:tab-right:") ? PaneDirection.Right : null,
                        chord.StartsWith("test:tab-at:") ? arg : null);
                }

                return;
            }

            if (chord.StartsWith("palette:"))
            {
                // "palette:Move tab to new workspace" runs the command palette entry with that title.
                var command = BuildPaletteCommands(tab).FirstOrDefault(c => c.Title == chord["palette:".Length..]);
                Log.Write($"test {chord}: {(command != null ? "found" : "missing")}");
                if (command != null)
                {
                    RunPaletteCommand(command); // as if chosen: it becomes a recent command
                }

                return;
            }

            if (chord.StartsWith("test:palette-open:"))
            {
                // "test:palette-open:@" opens the palette with that query typed (for captures).
                OpenCommandPalette(chord["test:palette-open:".Length..]);
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord.StartsWith("test:paste:"))
            {
                // A paste through the real path ("|" for line breaks), without the clipboard.
                tab.Active.Control.PasteForTest(chord["test:paste:".Length..].Replace('|', '\n'));
                return;
            }

            if (chord.StartsWith("test:click:"))
            {
                // A plain left click on viewport cell "col:row" of the focused pane (posted clicks can't reach WinUI).
                var cell = chord["test:click:".Length..].Split(':');
                Log.Write($"test {chord}");
                Ui.Guard("test:click", () => tab.Active.Control.ClickToMove(int.Parse(cell[0]), int.Parse(cell[1])));
                return;
            }

            if (chord.StartsWith("test:menu:"))
            {
                // The context menu as a right-click on viewport cell "col:row" of the focused pane opens it.
                var cell = chord["test:menu:".Length..].Split(':');
                Log.Write($"test {chord}");
                Ui.Guard("test:menu", () => tab.Active.Control.ShowContextMenuAtCell(int.Parse(cell[0]), int.Parse(cell[1])));
                return;
            }

            if (chord.StartsWith("test:drop:"))
            {
                // Files dropped on the focused pane ("|" between paths), without a real drag.
                tab.Active.Control.PastePaths(chord["test:drop:".Length..].Split('|'));
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

            // Through the pane's key sequences first, like a real press ("ctrl+a" then "c").
            var step = tab.Active.Control.PressForTest(KeyChord.Parse(chord));
            if (step is not (KeyStep.NotMine or KeyStep.PassThrough))
            {
                Log.Write($"test key {chord}: {step}");
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

    /// <summary>The shortcut shown for an action in menus, from the current bindings.</summary>
    private string? Shortcut(ActionInvocation action) => _keybindings.ShortcutFor(action);

    /// <summary>
    /// A tooltip: <paramref name="text"/> plus the action's shortcut, read from the current bindings each
    /// time it opens (so a key change in config.toml shows); just the text while the action is unbound.
    /// </summary>
    private ToolTip ShortcutTip(string text, ActionInvocation action)
    {
        string Text() => Shortcut(action) is { } keys ? $"{text} ({keys})" : text;
        var tip = new ToolTip { Content = Text() };
        tip.Opened += (_, _) => tip.Content = Text();
        return tip;
    }

    /// <summary>A profile's shortcut: bound by id, or by its position in the menu (0-based).</summary>
    private string? ShortcutForProfile(Profile profile, int index) =>
        Shortcut(new ActionInvocation(TerminalAction.NewTabFromProfile, Profile: profile.Id)) ??
        Shortcut(new ActionInvocation(TerminalAction.NewTabFromProfile, index));

    private bool HandleAction(ActionInvocation invocation, WorkspaceTab tab, TerminalTab origin)
    {
        if (_quick && NotInQuickTerminal(invocation.Action))
        {
            return true; // taken (not typed into the shell), and nothing happens
        }

        var count = Tabs.TabItems.Count;
        // With tab groups, new tabs open in the focused pane (with the focused tab's profile);
        // without, a new tab is a new workspace, as in Windows Terminal.
        var tabGroup = _config.Global.TabGroups ? tab.Workspace.GroupOf(origin) : null;
        switch (invocation.Action)
        {
            case TerminalAction.NewTab when tabGroup != null:
                NewTabInGroup(tab, tabGroup, origin.Profile);
                return true;
            case TerminalAction.NewTab:
            case TerminalAction.NewWorkspace:
                OpenNewWorkspace();
                return true;
            case TerminalAction.NewTabFromProfile:
                var visible = _config.VisibleProfiles.ToList();
                // By id ("new-tab wsl:Fedora"), or by position in the menu ("new-tab 2", Ctrl+Shift+2).
                var chosen = invocation.Profile != null
                    ? _config.FindProfile(invocation.Profile)
                    : invocation.Index < visible.Count ? visible[invocation.Index] : null;
                if (chosen is { } profile)
                {
                    if (tabGroup != null)
                    {
                        NewTabInGroup(tab, tabGroup, profile);
                    }
                    else
                    {
                        OpenTab(profile);
                    }
                }

                return true;
            case TerminalAction.DuplicateTab when tabGroup != null:
                NewTabInGroup(tab, tabGroup, origin.Profile, origin.Command, origin.WorkingDirectory);
                return true;
            case TerminalAction.DuplicateTab:
                OpenTab(origin.Profile, origin.Command, origin.WorkingDirectory);
                return true;
            // Ctrl+Tab flips through the focused pane's tabs when it has several, else workspaces.
            case TerminalAction.NextTab when tabGroup is { Tabs.Count: > 1 }:
                CycleGroup(tab, tabGroup, 1);
                return true;
            case TerminalAction.PreviousTab when tabGroup is { Tabs.Count: > 1 }:
                CycleGroup(tab, tabGroup, -1);
                return true;
            case TerminalAction.TabSwitcher:
                OpenCommandPalette(TabPrefix);
                return true;
            case TerminalAction.NextAttention:
                GoToNextAttention();
                return true;
            case TerminalAction.OpenCommandPalette:
                OpenCommandPalette();
                return true;
            case TerminalAction.OpenNewTabMenu:
                NewTabMenu.ShowAt(NewTabMenuButton);
                return true;
            case TerminalAction.ClosePane:
                AskClosePane(tab, origin);
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
            case TerminalAction.ReopenWorkspace:
                if (App.Instance.ClosedWorkspaces.All.FirstOrDefault() is { } last)
                {
                    ReopenWorkspace(last);
                }

                return true;
            case TerminalAction.ReopenWindow:
                App.Instance.ReopenWindow();
                return true;
            case TerminalAction.NewWindow:
                App.Instance.OpenNewWindow();
                return true;
            case TerminalAction.SplitPane:
                // A new pane opens with the focused tab's profile (splitting a Fedora pane gives Fedora),
                // in the workspace's folder if it has one.
                var (splitCommand, splitDirectory) = StartIn(tab.Folder, origin.Profile);
                SplitPane(tab, origin, invocation.Direction, origin.Profile, splitCommand, splitDirectory);
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
            case TerminalAction.OpenSettings:
                App.Instance.ShowSettings();
                return true;
            case TerminalAction.ReloadConfig:
                App.Instance.ReloadConfig();
                return true;
            case TerminalAction.ToggleSidebar or TerminalAction.ToggleTitleBar or TerminalAction.FocusMode:
                return HandleChromeAction(invocation);
            case TerminalAction.Quit:
                App.Instance.AskQuit();
                return true;
            case TerminalAction.QuitFresh:
                App.Instance.AskQuit(fresh: true);
                return true;
            case TerminalAction.ResizePane:
                if (tab.Workspace.GroupOf(origin) is { } group &&
                    tab.Workspace.Resize(group, invocation.Direction, _config.Global.PaneResizeStep) is { } split &&
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
