using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Phantom.Input;
using Phantom.Terminal;

namespace Phantom;

/// <summary>A window of tabs; each tab hosts one terminal pane.</summary>
public sealed partial class MainWindow : Window
{
    private readonly ConfigFile _config;
    private readonly Keybindings _keybindings;

    private sealed record Tab(Profile Profile, string Command, string WorkingDirectory, TerminalControl Pane, TabViewItem Item);

    public MainWindow(ConfigFile config, Keybindings keybindings, Profile profile, string command)
    {
        _config = config;
        _keybindings = keybindings;
        InitializeComponent();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 700));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.White;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
        SystemBackdrop = new MicaBackdrop();
        // Unpackaged WinUI windows don't pick up the exe's icon on their own.
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Phantom.ico"));

        // Selected tabs blend into the terminal below them, like Windows Terminal.
        var bg = TerminalRenderer.ToColor(config.Global.Theme.Background);
        Root.Resources["TabViewItemHeaderBackgroundSelected"] = new SolidColorBrush(bg);

        BuildNewTabMenu();
        OpenTab(profile, command, profile.Config.ResolveWorkingDirectory());

        Closed += (_, _) =>
        {
            foreach (var tab in AllTabs().ToList())
            {
                tab.Pane.Close();
            }
        };
    }

    private IEnumerable<Tab> AllTabs() => Tabs.TabItems.Cast<TabViewItem>().Select(i => (Tab)i.Tag);

    private Tab? SelectedTab => (Tabs.SelectedItem as TabViewItem)?.Tag as Tab;

    // ---------------------------------------------------------------- tabs

    private void OpenTab(Profile profile, string command, string workingDirectory)
    {
        var pane = new TerminalControl(profile.Config, _keybindings, command, workingDirectory)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        // A canvas has no desired size of its own; make sure it fills the tab.
        var item = new TabViewItem
        {
            Header = profile.Name,
            Content = pane,
            IconSource = IconFor(profile),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var tab = new Tab(profile, command, workingDirectory, pane, item);
        item.Tag = tab;

        pane.ActionHandler = invocation => HandleAction(invocation, tab);
        pane.TitleChanged += _ => UpdateTitle(tab);
        pane.CloseRequested += () => CloseTab(tab);

        Tabs.TabItems.Add(item);
        Tabs.SelectedItem = item;
    }

    private void OpenTab(Profile profile) =>
        OpenTab(profile, ShellResolver.ForProfile(profile.Config), profile.Config.ResolveWorkingDirectory());

    private void CloseTab(Tab tab)
    {
        var index = Tabs.TabItems.IndexOf(tab.Item);
        if (index < 0)
        {
            return;
        }

        tab.Pane.Close();
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
        var title = string.IsNullOrWhiteSpace(tab.Pane.Title) ? tab.Profile.Name : tab.Pane.Title;
        tab.Item.Header = title;
        ToolTipService.SetToolTip(tab.Item, title);
        if (SelectedTab == tab)
        {
            Title = title;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTab is { } tab)
        {
            UpdateTitle(tab);
            // The pane focuses itself when (re)loaded; this covers selection changes
            // where it stays loaded.
            DispatcherQueue.TryEnqueue(() => tab.Pane.Focus(FocusState.Programmatic));
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
            var item = new MenuFlyoutItem { Text = profile.Name, Icon = IconFor(profile) is { } icon ? new ImageIconElement(icon) : null };
            if (index < 9)
            {
                // Mirrors the Ctrl+Shift+N (profile N) bindings.
                item.KeyboardAcceleratorTextOverride = $"Ctrl+Shift+{index + 1}";
            }

            item.Click += (_, _) => OpenTab(profile);
            NewTabMenu.Items.Add(item);
            index++;
        }
    }

    private static IconSource? IconFor(Profile profile) =>
        profile.Config.Icon is { } path && File.Exists(path)
            ? new BitmapIconSource { UriSource = new Uri(path), ShowAsMonochrome = false }
            : null;

    // ------------------------------------------------------------- actions

    private bool HandleAction(ActionInvocation invocation, Tab origin)
    {
        var count = Tabs.TabItems.Count;
        switch (invocation.Action)
        {
            case TerminalAction.NewTab:
                OpenTab(_config.DefaultProfile);
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
            case TerminalAction.OpenNewTabMenu:
                NewTabMenu.ShowAt(NewTabMenuButton);
                return true;
            case TerminalAction.ClosePane:
                CloseTab(origin);
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
                App.Instance.OpenWindow(_config.DefaultProfile);
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
