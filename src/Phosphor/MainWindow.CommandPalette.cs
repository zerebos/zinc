using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Phosphor.Commands;
using Phosphor.Input;
using Phosphor.Panes;
using Windows.System;

namespace Phosphor;

// The command palette: every action, profile and workspace as a searchable list, recently used
// ones first; "@" searches this window's tabs instead (the tab switcher), dots first.
public sealed partial class MainWindow
{
    private const int MaxPaletteRows = 200;

    /// <summary>Typed first, the palette searches tabs (what "Go to tab…" opens with).</summary>
    private const string TabPrefix = "@";

    private List<PaletteCommand> _paletteCommands = [];
    private bool _paletteWired;

    /// <summary>
    /// A command's second step: the palette asks for its argument, from a list of
    /// <see cref="Choices"/> (each one's Run applies it) or typed and handed to <see cref="Submit"/>.
    /// <see cref="Highlight"/> follows the selected choice (a live preview); <see cref="Cancel"/>
    /// runs if the palette closes without a choice.
    /// </summary>
    private sealed record PaletteArgument(string Prompt, IReadOnlyList<PaletteCommand>? Choices = null,
        Action<string>? Submit = null, Action<PaletteCommand>? Highlight = null, Action? Cancel = null);

    private PaletteArgument? _argument;
    private const string DefaultPalettePrompt = "Type a command, or @ to find a tab";

    /// <summary>Turns the open palette into <paramref name="argument"/>'s question.</summary>
    private void AskPaletteArgument(PaletteArgument argument)
    {
        WirePalette(); // asked without the palette having been opened (a test key, or a binding)
        _argument = argument;
        PaletteQuery.PlaceholderText = argument.Prompt;
        PaletteQuery.Text = "";
        RefreshPalette();
        PaletteLayer.Visibility = Visibility.Visible;
        PaletteQuery.Focus(FocusState.Programmatic);
    }

    private void OpenCommandPalette(string prefix = "")
    {
        if (SelectedTab is not { } tab)
        {
            return;
        }

        WirePalette();
        if (_argument is { } pending)
        {
            _argument = null;
            pending.Cancel?.Invoke();
            PaletteQuery.PlaceholderText = DefaultPalettePrompt;
        }

        _paletteCommands = BuildPaletteCommands(tab);
        PaletteQuery.Text = prefix;
        PaletteQuery.SelectionStart = prefix.Length;
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
        if (_argument is { } unanswered)
        {
            _argument = null;
            unanswered.Cancel?.Invoke(); // e.g. put back the color scheme being previewed
        }

        PaletteQuery.PlaceholderText = DefaultPalettePrompt;
        if (SelectedTab is { } tab)
        {
            FocusActive(tab);
        }
    }

    private void RunPaletteCommand(PaletteCommand command)
    {
        if (command.Remember && !command.IsTab && _argument == null)
        {
            App.Instance.RecentCommands.Note(command.Title);
        }

        if (command.AsksArgument)
        {
            command.Run(); // asks its question in the palette
            return;
        }

        if (_argument != null)
        {
            // An answer: not a cancel. It may ask again (a folder that doesn't exist), so it runs
            // before the palette closes; closing first would hand focus back to the terminal and
            // shut the new question.
            _argument = null;
            command.Run();
            if (_argument == null)
            {
                ClosePalette();
            }

            return;
        }

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
        PaletteList.SelectionChanged += (_, _) =>
        {
            if (_argument?.Highlight is { } highlight && (PaletteList.SelectedItem as FrameworkElement)?.Tag is PaletteCommand choice)
            {
                highlight(choice);
            }
        };
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
            DispatcherQueue.SafeEnqueue(() =>
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
        var query = PaletteQuery.Text;
        IReadOnlyList<PaletteMatch> matches;
        if (_argument is { } argument)
        {
            // A question: its choices, or the typed value as the one thing Enter runs.
            matches = argument.Choices is { } choices
                ? CommandFilter.Filter(choices, query)
                : query.Trim().Length == 0
                    ? []
                    : [new PaletteMatch(new PaletteCommand(query.Trim(), "Enter", () => argument.Submit?.Invoke(query.Trim())), 0, [])];
        }
        else
        {
            var tabs = query.StartsWith(TabPrefix, StringComparison.Ordinal);
            matches = CommandFilter.Filter(_paletteCommands.Where(c => c.IsTab == tabs),
                tabs ? query[TabPrefix.Length..] : query, tabs ? null : App.Instance.RecentCommands);
        }

        foreach (var match in matches.Take(MaxPaletteRows))
        {
            PaletteList.Items.Add(PaletteRow(match));
        }

        if (PaletteList.Items.Count > 0)
        {
            SelectPaletteRow(0);
        }
    }

    /// <summary>One row: an icon (profiles, tabs), the title with matched letters in bold, "recent" and the shortcut on the right.</summary>
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

        var row = new Grid { Tag = match.Command, ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // A profile's icon (profile commands, tabs); a tab also shows its dot.
        var (profile, tabAttention) = match.Command.Icon switch
        {
            Profile p => (p, Attention.None),
            TerminalTab t => (t.Profile, t.Attention),
            _ => (null, Attention.None),
        };
        if (profile != null)
        {
            row.Children.Add(new IconSourceElement { IconSource = ProfileIcons.For(profile), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
        }

        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        if (tabAttention != Attention.None)
        {
            var dot = new AttentionDot { Corner = false };
            dot.Show(tabAttention);
            Grid.SetColumn(dot, 2);
            row.Children.Add(dot);
        }
        else if (match.Recent)
        {
            var recent = new TextBlock { Text = "recent", Opacity = 0.45, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(recent, 2);
            row.Children.Add(recent);
        }

        if (match.Command.Shortcut is { } shortcut)
        {
            var keys = new TextBlock
            {
                Text = shortcut,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            Grid.SetColumn(keys, 3);
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
    private List<PaletteCommand> BuildPaletteCommands(WorkspaceTab tab)
    {
        var commands = new List<PaletteCommand>();
        var pane = tab.Active.Control;

        string? Keys(ActionInvocation action) => _keybindings.ShortcutFor(action);

        // Titles come from the action registry, shortcuts from the current bindings.
        void Action(ActionInvocation action) =>
            commands.Add(new PaletteCommand(Actions.Title(action), Keys(action), () => pane.Invoke(action),
                SearchWords: Actions.SearchWords(action)));

        void Custom(string title, Action run, string? keys = null) =>
            commands.Add(new PaletteCommand(title, keys, run));

        // Tabs (terminals), workspaces (title-bar tabs) and windows.
        Action(TerminalAction.NewTab);
        Action(TerminalAction.NewWorkspace);
        if (App.Instance.ClosedWorkspaces.All.Count > 0)
        {
            Action(TerminalAction.ReopenWorkspace);
        }
        Action(TerminalAction.DuplicateTab);
        Action(TerminalAction.ClosePane);
        Custom("Close workspace", () => CloseTab(tab));
        Custom("Move workspace to new window", () => MoveToNewWindow(tab));
        if (tab.Workspace.Tabs.Count() > 1)
        {
            Custom("Move tab to new workspace", () => MoveTabToNewWorkspace(tab, tab.Active));
            Custom("Move tab to new window", () => MoveTabToNewWindow(tab, tab.Active));
        }

        if (tab.Workspace.Layout.Count > 1)
        {
            Custom("Move pane to new workspace", () => MovePaneToNewTab(tab, tab.Active));
            Custom("Move pane to new window", () => MovePaneToNewWindow(tab, tab.Active));
        }

        Custom("Close other workspaces", () => CloseTabs(AllTabs().Where(t => t != tab)));
        Custom("Close workspaces to the right", () => CloseTabs(AllTabs().SkipWhile(t => t != tab).Skip(1)));
        Action(TerminalAction.NextTab);
        Action(TerminalAction.PreviousTab);
        // From the palette, "Go to tab…" turns the palette itself into the tab switcher ("@"); run
        // as an action it closed the palette, and reopening it lost to the focus going back.
        commands.Add(new PaletteCommand(Actions.Title(TerminalAction.TabSwitcher), Keys(TerminalAction.TabSwitcher), () =>
        {
            PaletteQuery.Text = TabPrefix;
            PaletteQuery.SelectionStart = TabPrefix.Length;
        }, AsksArgument: true));
        Action(TerminalAction.NextAttention);
        Custom("Rename workspace…", () => ShowRename(tab));
        Custom("Export text…", () => _ = ExportText(tab));
        commands.AddRange(ArgumentCommands(tab));
        Action(TerminalAction.NewWindow);
        if (_workspaceList != null)
        {
            Action(new ActionInvocation(TerminalAction.ToggleSidebar, -1));
            Action(new ActionInvocation(TerminalAction.ToggleSidebar, (int)SidebarMode.Rail));
            Action(new ActionInvocation(TerminalAction.ToggleSidebar, (int)SidebarMode.AutoHide));
            Action(TerminalAction.ToggleTitleBar);
            Action(TerminalAction.FocusMode);
        }

        Action(TerminalAction.Quit);
        Action(TerminalAction.QuitFresh);
        Action(TerminalAction.ReloadConfig);
        Action(TerminalAction.OpenSettings);
        Custom("Welcome…", () => App.Instance.ShowWelcome(firstRun: false));

        // Panes.
        Action(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right));
        Action(new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Down));
        Action(TerminalAction.DuplicatePane);
        foreach (var (name, direction) in new[]
                 {
                     ("left", PaneDirection.Left), ("right", PaneDirection.Right),
                     ("up", PaneDirection.Up), ("down", PaneDirection.Down),
                 })
        {
            Action(new ActionInvocation(TerminalAction.MoveFocus, Direction: direction));
            Action(new ActionInvocation(TerminalAction.ResizePane, Direction: direction));
        }

        // The terminal.
        Action(TerminalAction.Copy);
        Action(TerminalAction.Paste);
        Action(TerminalAction.ZoomIn);
        Action(TerminalAction.ZoomOut);
        Action(TerminalAction.ZoomReset);
        Action(TerminalAction.Find);
        Action(TerminalAction.ScrollToTop);
        Action(TerminalAction.ScrollToBottom);
        Action(TerminalAction.ScrollPageUp);
        Action(TerminalAction.ScrollPageDown);
        Action(new ActionInvocation(TerminalAction.JumpToPrompt, -1));
        Action(new ActionInvocation(TerminalAction.JumpToPrompt, 1));
        Action(TerminalAction.CopyOutput);
        Action(TerminalAction.SelectOutput);

        // Profiles: open as a tab (in this pane, with tab groups), a workspace, a split either way, or a window.
        var group = _config.Global.TabGroups ? tab.Workspace.GroupOf(tab.Active) : null;
        var profiles = _config.VisibleProfiles.ToList();
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            void ForProfile(string title, Action run, string? keys = null) =>
                commands.Add(new PaletteCommand(title, keys, run, Icon: profile));
            var command = ShellResolver.ForProfile(profile.Config);
            var dir = profile.Config.ResolveWorkingDirectory();
            if (group != null)
            {
                ForProfile($"New tab: {profile.Name}", () => NewTabInGroup(tab, group, profile, command, dir),
                    ShortcutForProfile(profile, i));
            }

            ForProfile($"New workspace: {profile.Name}", () => OpenTab(profile, command, dir),
                group == null ? ShortcutForProfile(profile, i) : null);
            ForProfile($"Split right: {profile.Name}", () => SplitPane(tab, tab.Active, PaneDirection.Right, profile, command, dir));
            ForProfile($"Split down: {profile.Name}", () => SplitPane(tab, tab.Active, PaneDirection.Down, profile, command, dir));
            ForProfile($"New window: {profile.Name}", () => App.Instance.OpenWindow(profile));
        }

        // Open workspaces, and the tabs of this one.
        var index = 0;
        foreach (var t in AllTabs())
        {
            var target = t;
            var switchKeys = index < 8 ? Keys(new ActionInvocation(TerminalAction.SwitchToTab, index)) : null;
            commands.Add(new PaletteCommand($"Switch to workspace: {t.Header.Title}", switchKeys,
                () => Tabs.SelectedItem = target.Item, Remember: false, Icon: target.Active.Profile));
            index++;
        }

        commands.AddRange(TabSwitcherCommands());

        // Workspace colors.
        foreach (var (name, rgb) in TabColors)
        {
            var color = Windows.UI.Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            Custom($"Workspace color: {name}", () => SetTabColor(tab, color));
        }

        Custom("Workspace color: Reset", () => SetTabColor(tab, null));
        return commands;
    }
}
