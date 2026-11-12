using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;
using Windows.Storage.Pickers;

namespace Zinc;

// The tab context menu: rename, color, duplicate, split, export, close.
public sealed partial class MainWindow
{
    private MenuFlyout BuildTabMenu(WorkspaceTab tab)
    {
        var menu = new MenuFlyout();

        menu.Items.Add(Item("Rename workspace…", "", null, () => ShowRename(tab)));

        var colors = new MenuFlyoutSubItem { Text = "Workspace color", Icon = Glyph("") };
        foreach (var (name, rgb) in WorkspaceColors.All)
        {
            var color = WorkspaceLookPickers.ToColor(rgb);
            colors.Items.Add(new MenuFlyoutItem
            {
                Text = name,
                Icon = new FontIcon { Glyph = "", Foreground = new SolidColorBrush(color) }, // filled circle
                Command = Run(() => SetTabColor(tab, color)),
            });
        }

        colors.Items.Add(new MenuFlyoutSeparator());
        colors.Items.Add(new MenuFlyoutItem { Text = "No color", Command = Run(() => SetTabColor(tab, null)) });
        menu.Items.Add(colors);
        menu.Items.Add(Item("Workspace icon…", ((char)0xE8B9).ToString(), null, () => ShowIconPicker(tab)));

        // The workspace's folder. What's set shows on the first line, and Clear is enabled only while there's one.
        var folder = new MenuFlyoutSubItem { Text = "Workspace folder", Icon = Glyph(((char)0xE8B7).ToString()) };
        var current = new MenuFlyoutItem { IsEnabled = false };
        var clear = new MenuFlyoutItem { Text = "Clear folder", Command = Run(() => ClearWorkspaceFolder(tab)) };
        folder.Items.Add(current);
        folder.Items.Add(new MenuFlyoutItem { Text = "Set folder…", Command = Run(() => AskWorkspaceFolder(tab)) });
        folder.Items.Add(clear);
        menu.Items.Add(folder);
        menu.Opening += (_, _) =>
        {
            // Without pane tabs, a new tab is a new workspace (the page or its profile's folder), so only splits
            // follow the folder.
            var what = _config.Global.PaneTabs ? "new tabs and splits" : "splits";
            current.Text = tab.Folder is { } f ? f.Path
                : _config.Global.StartIn == NewTerminalFolder.Current ? $"No folder set: {what} start where you are"
                : $"No folder set: {what} start in their profile's folder";
            clear.IsEnabled = tab.Folder != null;
        };

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Duplicate workspace", "", null, () =>
            DuplicateWorkspace(tab)));

        var split = new MenuFlyoutSubItem { Text = "Split pane", Icon = Glyph("") };
        split.Items.Add(Item("Split right", null, Shortcut(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right)), () => SplitFocused(tab, PaneDirection.Right)));
        split.Items.Add(Item("Split down", null, Shortcut(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Down)), () => SplitFocused(tab, PaneDirection.Down)));
        split.Items.Add(Item("Duplicate pane", null, Shortcut(TerminalAction.DuplicatePane), () =>
            HandleAction(new ActionInvocation(TerminalAction.DuplicatePane), tab, tab.Active)));
        split.Items.Add(new MenuFlyoutSeparator());
        split.Items.Add(Item("Move pane to new workspace", null, null, () => MovePaneToNewTab(tab, tab.Active)));
        split.Items.Add(Item("Move pane to new window", null, null, () => MovePaneToNewWindow(tab, tab.Active)));
        menu.Items.Add(split);

        menu.Items.Add(Item("Save tab text…", "", null, () => _ = ExportText(tab)));

        menu.Items.Add(Item("Move workspace to new window", "", null, () => MoveToNewWindow(tab)));

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Close other workspaces", null, null, () => CloseWorkspaces(AllTabs().Where(t => t != tab))));
        menu.Items.Add(Item("Close workspaces to the right", null, null, () =>
            CloseWorkspaces(AllTabs().SkipWhile(t => t != tab).Skip(1))));
        menu.Items.Add(Item("Close workspace", "", null, () => CloseWorkspace(tab)));

        return menu.RunAfterClose();
    }

    private static MenuFlyoutItem Item(string text, string? glyph, string? shortcut, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Command = Run(action) };
        if (glyph != null)
        {
            item.Icon = Glyph(glyph);
        }

        if (shortcut != null)
        {
            // Shown for discoverability, because the real bindings live in Keybindings.
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        return item;
    }

    private static FontIcon Glyph(string glyph) => new() { Glyph = glyph };

    private static RelayCommand Run(Action action) => new(action);

    /// <summary>Splits the focused pane with <paramref name="profile"/>, starting where that pane is (see <see cref="StartFor"/>).</summary>
    private void SplitWith(WorkspaceTab tab, PaneDirection direction, Profile profile)
    {
        var (command, directory) = StartFor(StartKind.Split, tab, tab.Active, profile);
        SplitPane(tab, tab.Active, direction, profile, command, directory);
    }

    private void SplitFocused(WorkspaceTab tab, PaneDirection direction) =>
        HandleAction(new ActionInvocation(TerminalAction.SplitPane, Direction: direction), tab, tab.Active);

    private async void CloseWorkspaces(IEnumerable<WorkspaceTab> tabs)
    {
        var list = tabs.ToList();
        if (!await ConfirmClose(list.SelectMany(t => t.Workspace.Tabs).ToList(), list.Count == 1 ? "workspace" : "workspaces"))
        {
            return;
        }

        Ui.Guard("closing workspaces", () =>
        {
            foreach (var t in list)
            {
                CloseWorkspaceNow(t);
            }
        });
    }

    // ---- rename ----

    private void ShowRename(WorkspaceTab tab) => ShowRenameBox(tab, tab.CustomTitle ?? tab.Header.Title, AnchorOf(tab), name =>
    {
        tab.CustomTitle = name;
        UpdateTitle(tab);
    });

    /// <summary>
    /// Where a terminal tab's own popups go, which is its tab or title in its pane's header, else its workspace's tab.
    /// </summary>
    private FrameworkElement TabAnchor(WorkspaceTab tab, TerminalTab pane) =>
        tab.Workspace.GroupOf(pane) is { Tag: Border { Child: GroupView view } } && view.AnchorFor(pane) is { } anchor ? anchor : AnchorOf(tab);

    /// <summary>
    /// Renames one terminal tab (its pane's header and tab label). A null name gives the title back to the program, as
    /// zn's rename does.
    /// </summary>
    private void ShowRenameTab(WorkspaceTab tab, TerminalTab pane, FrameworkElement anchor) =>
        ShowRenameBox(tab, pane.CustomTitle ?? pane.Title, anchor, name =>
        {
            pane.CustomTitle = name;
            UpdateTitle(tab);
            UpdateFocusBorders(tab);
        });

    /// <summary>
    /// A rename box under <paramref name="anchor"/>. Enter sets the name (empty clears it), and Escape leaves it.
    /// </summary>
    private void ShowRenameBox(WorkspaceTab tab, string current, FrameworkElement anchor, Action<string?> rename)
    {
        var box = new TextBox
        {
            Text = current,
            PlaceholderText = "Leave empty to use the program's title",
            MinWidth = 240,
        };
        var flyout = new Flyout { Content = box, Placement = FlyoutPlacementMode.Bottom };

        void Commit()
        {
            rename(string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim());
            flyout.Hide();
        }

        // Enter and Escape act on their release, not their press, because closing on the press would hand focus to the
        // terminal before the key's character arrived, and the shell would get the Enter. Only a press made in the box
        // counts, so the Enter that opened it (from the palette) doesn't commit it.
        Windows.System.VirtualKey? pressed = null;
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                pressed = e.Key;
            }
        };
        box.KeyUp += (_, e) =>
        {
            if (e.Key != pressed)
            {
                return;
            }

            e.Handled = true;
            pressed = null;
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
            }
            else
            {
                flyout.Hide();
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        flyout.Closed += (_, _) => FocusActive(tab);
        flyout.ShowAt(anchor);
    }

    // ---- color ----

    private static readonly string[] TabBackgroundKeys =
    [
        "TabViewItemHeaderBackground", "TabViewItemHeaderBackgroundSelected",
        "TabViewItemHeaderBackgroundPointerOver", "TabViewItemHeaderBackgroundPressed",
        "TabViewItemHeaderForegroundSelected", "TabViewItemIconForegroundSelected", "TabViewItemHeaderSelectedCloseButtonForeground",
    ];

    private void SetTabColor(WorkspaceTab tab, Windows.UI.Color? color)
    {
        tab.Color = color;
        ApplyTabBackground(tab, force: true);
    }

    /// <summary>
    /// Colors a tab with its chosen tab color if set. Otherwise the selected tab takes the
    /// focused pane's terminal background, so it flows into the terminal below it.
    /// Only touches the item when the colors change (re-applying flickers).
    /// </summary>
    private void ApplyTabBackground(WorkspaceTab tab, bool force = false)
    {
        var paneBackground = TerminalRenderer.ToColor(tab.Active.Profile.Config.Theme.Background);
        var key = (tab.Color, paneBackground);
        if (!force && Equals(tab.AppliedColors, key))
        {
            return;
        }

        tab.AppliedColors = key;
        foreach (var resource in TabBackgroundKeys)
        {
            tab.Item.Resources.Remove(resource);
        }

        if (tab.Color is { } c)
        {
            // Selected tabs get the full color; others a translucent tint of it.
            var tint = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, c.R, c.G, c.B));
            var solid = new SolidColorBrush(c);
            tab.Item.Resources["TabViewItemHeaderBackground"] = tint;
            tab.Item.Resources["TabViewItemHeaderBackgroundPointerOver"] = tint;
            tab.Item.Resources["TabViewItemHeaderBackgroundPressed"] = solid;
            tab.Item.Resources["TabViewItemHeaderBackgroundSelected"] = solid;
        }
        else
        {
            tab.Item.Resources["TabViewItemHeaderBackgroundSelected"] = new SolidColorBrush(paneBackground);
        }

        // The selected tab's text is dark on a light background (a light scheme, a yellow tab).
        if (Contrast.TextOn(tab.Color ?? paneBackground) is { } text)
        {
            tab.Item.Resources["TabViewItemHeaderForegroundSelected"] = text;
            tab.Item.Resources["TabViewItemIconForegroundSelected"] = text;
            tab.Item.Resources["TabViewItemHeaderSelectedCloseButtonForeground"] = text;
        }

        // Theme resources are resolved when the template applies, so flipping the theme
        // makes the item re-resolve them now.
        var theme = Brand.IsDark(tab.Item) ? ElementTheme.Dark : ElementTheme.Light;
        tab.Item.RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        tab.Item.RequestedTheme = theme;
        RefreshWorkspaceList();
    }

    // ---- export ----

    private async Task ExportText(WorkspaceTab tab)
    {
        var picker = new FileSavePicker { SuggestedFileName = "terminal", DefaultFileExtension = ".txt" };
        picker.FileTypeChoices.Add("Text", [".txt"]);
        // Unpackaged apps must tell the picker which window owns it.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        try
        {
            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await File.WriteAllTextAsync(file.Path, tab.Active.Control.ReadText());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // A fire-and-forget task, so without this a failed save would vanish without a word.
            Log.Crash("exporting text", ex);
            ShowError(ex);
        }
    }
}

/// <summary>Minimal ICommand for menu items.</summary>
internal sealed partial class RelayCommand(Action action) : System.Windows.Input.ICommand
{
#pragma warning disable CS0067 // never raised: always executable
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => action();
}

internal static class MenuFlyouts
{
    /// <summary>
    /// Runs the chosen item's command once the menu has closed and handed focus back, not while it's still open.
    /// Otherwise focus would go back to what had it before the menu after the command ran, so a command that moves
    /// focus (a split, a new tab) would lose it to the old pane for a few frames and the focus ring would flicker. A
    /// command run while the menu isn't open (a test hook) runs at once. Items with Click handlers aren't covered, so
    /// give them a Command.
    /// </summary>
    public static MenuFlyout RunAfterClose(this MenuFlyout menu)
    {
        var open = false;
        Action? chosen = null;
        menu.Opened += (_, _) => open = true;
        menu.Closed += (_, _) =>
        {
            open = false;
            if (chosen is { } run)
            {
                // Queued, not run here, because focus goes back after Closed (Closed, focus to the old pane, then this).
                chosen = null;
                menu.DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, run);
            }
        };

        Defer(menu.Items);
        return menu;

        void Defer(IList<MenuFlyoutItemBase> items)
        {
            foreach (var item in items)
            {
                if (item is MenuFlyoutSubItem sub)
                {
                    Defer(sub.Items);
                }
                else if (item is MenuFlyoutItem { Command: { } command } leaf)
                {
                    leaf.Command = new RelayCommand(() =>
                    {
                        void Run() => command.Execute(leaf.CommandParameter);
                        if (open)
                        {
                            chosen = Run;
                        }
                        else
                        {
                            Run();
                        }
                    });
                }
            }
        }
    }
}
