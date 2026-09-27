using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Phosphor.Input;
using Phosphor.Panes;
using Phosphor.Terminal;
using Windows.Storage.Pickers;

namespace Phosphor;

// The tab context menu: rename, color, duplicate, split, export, close.
public sealed partial class MainWindow
{
    private static readonly (string Name, uint Rgb)[] TabColors =
    [
        ("Red", 0xC0392B), ("Orange", 0xD35400), ("Yellow", 0xB7950B), ("Green", 0x229954),
        ("Teal", 0x148F77), ("Blue", 0x2471A3), ("Purple", 0x7D3C98), ("Pink", 0xB03A78),
    ];

    private MenuFlyout BuildTabMenu(WorkspaceTab tab)
    {
        var menu = new MenuFlyout();

        menu.Items.Add(Item("Rename tab…", "", null, () => ShowRename(tab)));

        var colors = new MenuFlyoutSubItem { Text = "Tab color", Icon = Glyph("") };
        foreach (var (name, rgb) in TabColors)
        {
            var color = Windows.UI.Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            colors.Items.Add(new MenuFlyoutItem
            {
                Text = name,
                Icon = new FontIcon { Glyph = "", Foreground = new SolidColorBrush(color) }, // filled circle
                Command = Run(() => SetTabColor(tab, color)),
            });
        }

        colors.Items.Add(new MenuFlyoutSeparator());
        colors.Items.Add(new MenuFlyoutItem { Text = "Reset", Command = Run(() => SetTabColor(tab, null)) });
        menu.Items.Add(colors);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Duplicate tab", "", "Ctrl+Shift+D", () =>
            OpenTab(tab.Active.Profile, tab.Active.Command, tab.Active.WorkingDirectory)));

        var split = new MenuFlyoutSubItem { Text = "Split pane", Icon = Glyph("") };
        split.Items.Add(Item("Split right", null, "Alt+Shift+Plus", () => SplitWithDefault(tab, PaneDirection.Right)));
        split.Items.Add(Item("Split down", null, "Alt+Shift+Minus", () => SplitWithDefault(tab, PaneDirection.Down)));
        split.Items.Add(Item("Duplicate pane", null, "Alt+Shift+D", () =>
            HandleAction(new ActionInvocation(TerminalAction.DuplicatePane), tab, tab.Active)));
        split.Items.Add(new MenuFlyoutSeparator());
        split.Items.Add(Item("Move pane to new tab", null, null, () => MovePaneToNewTab(tab, tab.Active)));
        split.Items.Add(Item("Move pane to new window", null, null, () => MovePaneToNewWindow(tab, tab.Active)));
        menu.Items.Add(split);

        menu.Items.Add(Item("Export text…", "", null, () => _ = ExportText(tab)));

        menu.Items.Add(Item("Move tab to new window", "", null, () => MoveToNewWindow(tab)));

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Close other tabs", null, null, () => CloseTabs(AllTabs().Where(t => t != tab))));
        menu.Items.Add(Item("Close tabs to the right", null, null, () =>
            CloseTabs(AllTabs().SkipWhile(t => t != tab).Skip(1))));
        menu.Items.Add(Item("Close tab", "", null, () => CloseTab(tab)));

        return menu;
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
            // Shown for discoverability; the real bindings live in Keybindings.
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        return item;
    }

    private static FontIcon Glyph(string glyph) => new() { Glyph = glyph };

    private static RelayCommand Run(Action action) => new(action);

    private void SplitWithDefault(WorkspaceTab tab, PaneDirection direction) =>
        HandleAction(new ActionInvocation(TerminalAction.SplitPane, Direction: direction), tab, tab.Active);

    private void CloseTabs(IEnumerable<WorkspaceTab> tabs)
    {
        foreach (var t in tabs.ToList())
        {
            CloseTab(t);
        }
    }

    // ---- rename ----

    private void ShowRename(WorkspaceTab tab)
    {
        var box = new TextBox
        {
            Text = tab.CustomTitle ?? tab.Header.Title,
            PlaceholderText = "Empty resets to the app's title",
            MinWidth = 240,
        };
        var flyout = new Flyout { Content = box, Placement = FlyoutPlacementMode.Bottom };

        void Commit()
        {
            tab.CustomTitle = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
            flyout.Hide();
            UpdateTitle(tab);
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Commit();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                flyout.Hide();
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        flyout.Closed += (_, _) => FocusActive(tab);
        flyout.ShowAt(tab.Item);
    }

    // ---- color ----

    private static readonly string[] TabBackgroundKeys =
    [
        "TabViewItemHeaderBackground", "TabViewItemHeaderBackgroundSelected",
        "TabViewItemHeaderBackgroundPointerOver", "TabViewItemHeaderBackgroundPressed",
    ];

    private void SetTabColor(WorkspaceTab tab, Windows.UI.Color? color)
    {
        tab.Color = color;
        ApplyTabBackground(tab, force: true);
    }

    /// <summary>
    /// Colors a tab: a chosen tab color if set; otherwise the selected tab takes the
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

        // Theme resources are resolved when the template applies; flipping the theme
        // makes the item re-resolve them now.
        tab.Item.RequestedTheme = ElementTheme.Light;
        tab.Item.RequestedTheme = ElementTheme.Dark;
    }

    // ---- export ----

    private async Task ExportText(WorkspaceTab tab)
    {
        var picker = new FileSavePicker { SuggestedFileName = "terminal", DefaultFileExtension = ".txt" };
        picker.FileTypeChoices.Add("Text", [".txt"]);
        // Unpackaged apps must tell the picker which window owns it.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            await File.WriteAllTextAsync(file.Path, tab.Active.Control.ReadText());
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
