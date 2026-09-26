using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Phantom.Commands;
using Phantom.Input;
using Phantom.Panes;
using Windows.System;

namespace Phantom;

// The command palette: every action, profile and tab as a searchable list.
public sealed partial class MainWindow
{
    private const int MaxPaletteRows = 200;

    private List<PaletteCommand> _paletteCommands = [];
    private bool _paletteWired;

    private void OpenCommandPalette()
    {
        if (SelectedTab is not { } tab)
        {
            return;
        }

        WirePalette();
        _paletteCommands = BuildPaletteCommands(tab);
        PaletteQuery.Text = "";
        RefreshPalette();
        PaletteLayer.Visibility = Visibility.Visible;
        PaletteQuery.Focus(FocusState.Programmatic);
    }

    private void ClosePalette()
    {
        if (PaletteLayer.Visibility == Visibility.Collapsed)
        {
            return;
        }

        PaletteLayer.Visibility = Visibility.Collapsed;
        if (SelectedTab is { } tab)
        {
            FocusActive(tab);
        }
    }

    private void RunPaletteCommand(PaletteCommand command)
    {
        // Close first so commands that open UI or move focus (rename, splits) start clean.
        ClosePalette();
        command.Run();
    }

    private void WirePalette()
    {
        if (_paletteWired)
        {
            return;
        }

        _paletteWired = true;
        PaletteQuery.TextChanged += (_, _) => RefreshPalette();
        PaletteQuery.PreviewKeyDown += OnPaletteKey;
        PaletteList.ItemClick += (_, e) =>
        {
            if ((e.ClickedItem as FrameworkElement)?.Tag is PaletteCommand command)
            {
                RunPaletteCommand(command);
            }
        };
        PaletteLayer.PointerPressed += (_, e) =>
        {
            // Only clicks on the empty layer, not inside the panel.
            if (ReferenceEquals(e.OriginalSource, PaletteLayer))
            {
                ClosePalette();
            }
        };
        PaletteQuery.LostFocus += (_, _) =>
        {
            // Clicking into the list keeps the palette; anything else closes it.
            DispatcherQueue.TryEnqueue(() =>
            {
                var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
                if (!IsInside(focused, PalettePanel))
                {
                    ClosePalette();
                }
            });
        };
    }

    private void OnPaletteKey(object sender, KeyRoutedEventArgs e)
    {
        var count = PaletteList.Items.Count;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                ClosePalette();
                break;
            case VirtualKey.Enter:
                if ((PaletteList.SelectedItem as FrameworkElement)?.Tag is PaletteCommand command)
                {
                    RunPaletteCommand(command);
                }

                break;
            case VirtualKey.Down when count > 0:
                SelectPaletteRow((PaletteList.SelectedIndex + 1) % count);
                break;
            case VirtualKey.Up when count > 0:
                SelectPaletteRow((PaletteList.SelectedIndex - 1 + count) % count);
                break;
            case VirtualKey.PageDown when count > 0:
                SelectPaletteRow(Math.Min(count - 1, PaletteList.SelectedIndex + 8));
                break;
            case VirtualKey.PageUp when count > 0:
                SelectPaletteRow(Math.Max(0, PaletteList.SelectedIndex - 8));
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void SelectPaletteRow(int index)
    {
        PaletteList.SelectedIndex = index;
        PaletteList.ScrollIntoView(PaletteList.SelectedItem);
    }

    private void RefreshPalette()
    {
        PaletteList.Items.Clear();
        foreach (var match in CommandFilter.Filter(_paletteCommands, PaletteQuery.Text).Take(MaxPaletteRows))
        {
            PaletteList.Items.Add(PaletteRow(match));
        }

        if (PaletteList.Items.Count > 0)
        {
            SelectPaletteRow(0);
        }
    }

    /// <summary>One row: the title with matched letters in bold, the shortcut on the right.</summary>
    private static FrameworkElement PaletteRow(PaletteMatch match)
    {
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var matched = match.Positions.ToHashSet();
        var text = match.Command.Title;
        var start = 0;
        while (start < text.Length)
        {
            var bold = matched.Contains(start);
            var end = start;
            while (end < text.Length && matched.Contains(end) == bold)
            {
                end++;
            }

            title.Inlines.Add(new Run { Text = text[start..end], FontWeight = bold ? FontWeights.Bold : FontWeights.Normal });
            start = end;
        }

        var row = new Grid { Tag = match.Command, ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(title);
        if (match.Command.Shortcut is { } shortcut)
        {
            var keys = new TextBlock
            {
                Text = shortcut,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            Grid.SetColumn(keys, 1);
            row.Children.Add(keys);
        }

        AutomationProperties.SetName(row, match.Command.Title);
        return row;
    }

    private static bool IsInside(DependencyObject? node, DependencyObject container)
    {
        for (; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node == container)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- commands

    /// <summary>Every command, in the order shown for an empty query.</summary>
    private List<PaletteCommand> BuildPaletteCommands(Tab tab)
    {
        var commands = new List<PaletteCommand>();
        var pane = tab.Active.Control;

        string? Keys(ActionInvocation action) => _keybindings.ChordFor(action)?.ToString();

        void Action(string title, ActionInvocation action) =>
            commands.Add(new PaletteCommand(title, Keys(action), () => pane.Invoke(action)));

        void Custom(string title, Action run, string? keys = null) =>
            commands.Add(new PaletteCommand(title, keys, run));

        // Tabs and windows.
        Action("New tab", TerminalAction.NewTab);
        Action("Duplicate tab", TerminalAction.DuplicateTab);
        Action("Close pane", TerminalAction.ClosePane);
        Custom("Close tab", () => CloseTab(tab));
        Custom("Close other tabs", () => CloseTabs(AllTabs().Where(t => t != tab)));
        Custom("Close tabs to the right", () => CloseTabs(AllTabs().SkipWhile(t => t != tab).Skip(1)));
        Action("Next tab", TerminalAction.NextTab);
        Action("Previous tab", TerminalAction.PreviousTab);
        Custom("Rename tab…", () => ShowRename(tab));
        Custom("Export text…", () => _ = ExportText(tab));
        Action("New window", TerminalAction.NewWindow);

        // Panes.
        Action("Split right", new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right));
        Action("Split down", new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Down));
        Action("Duplicate pane", TerminalAction.DuplicatePane);
        foreach (var (name, direction) in new[]
                 {
                     ("left", PaneDirection.Left), ("right", PaneDirection.Right),
                     ("up", PaneDirection.Up), ("down", PaneDirection.Down),
                 })
        {
            Action($"Focus pane {name}", new ActionInvocation(TerminalAction.MoveFocus, Direction: direction));
            Action($"Resize pane {name}", new ActionInvocation(TerminalAction.ResizePane, Direction: direction));
        }

        // The terminal.
        Action("Copy", TerminalAction.Copy);
        Action("Paste", TerminalAction.Paste);
        Action("Increase font size", TerminalAction.FontSizeIncrease);
        Action("Decrease font size", TerminalAction.FontSizeDecrease);
        Action("Reset font size", TerminalAction.FontSizeReset);
        Action("Find", TerminalAction.Find);
        Action("Scroll to top", TerminalAction.ScrollToTop);
        Action("Scroll to bottom", TerminalAction.ScrollToBottom);
        Action("Scroll page up", TerminalAction.ScrollPageUp);
        Action("Scroll page down", TerminalAction.ScrollPageDown);

        // Profiles: open as a tab, a split either way, or a window.
        for (var i = 0; i < _config.Profiles.Count; i++)
        {
            var profile = _config.Profiles[i];
            var command = ShellResolver.ForProfile(profile.Config);
            var dir = profile.Config.ResolveWorkingDirectory();
            Custom($"New tab: {profile.Name}", () => OpenTab(profile, command, dir),
                Keys(new ActionInvocation(TerminalAction.NewTabFromProfile, i)));
            Custom($"Split right: {profile.Name}", () => SplitPane(tab, tab.Active, PaneDirection.Right, profile, command, dir));
            Custom($"Split down: {profile.Name}", () => SplitPane(tab, tab.Active, PaneDirection.Down, profile, command, dir));
            Custom($"New window: {profile.Name}", () => App.Instance.OpenWindow(profile));
        }

        // Open tabs.
        var index = 0;
        foreach (var t in AllTabs())
        {
            var target = t;
            var switchKeys = index < 8 ? Keys(new ActionInvocation(TerminalAction.SwitchToTab, index)) : null;
            Custom($"Switch to tab: {t.Header.Title}", () => Tabs.SelectedItem = target.Item, switchKeys);
            index++;
        }

        // Tab colors.
        foreach (var (name, rgb) in TabColors)
        {
            var color = Windows.UI.Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            Custom($"Tab color: {name}", () => SetTabColor(tab, color));
        }

        Custom("Tab color: Reset", () => SetTabColor(tab, null));
        return commands;
    }
}
