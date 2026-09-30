using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Phosphor.Input;
using Phosphor.Settings;
using Windows.System;
using Windows.UI.Core;

namespace Phosphor;

// The Keyboard page: every action with its keys. Add a key by pressing it; click a key to take it
// away; Reset puts an action back to its default keys. Written to [keys] (KeyBindingEditor).
internal sealed partial class SettingsWindow
{
    private readonly StackPanel _keyRows = new() { Spacing = 4 };
    private string _keyFilter = "";

    private void KeyboardPage()
    {
        _page.Children.Add(Note("Keys follow Windows Terminal's defaults; what you change goes in [keys] in config.toml. " +
            "Click a key to remove it. Key sequences (ctrl+a > c) are listed here and added in config.toml."));
        var filter = new TextBox { PlaceholderText = "Filter actions or keys", Text = _keyFilter, Margin = new Thickness(0, 12, 0, 8), Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(filter, "Filter actions");
        filter.TextChanged += (_, _) =>
        {
            _keyFilter = filter.Text;
            FillKeyRows();
        };
        _page.Children.Add(filter);
        _page.Children.Add(_keyRows);
        FillKeyRows();
        _refreshers.Add(FillKeyRows);
    }

    /// <summary>Every action without an argument, and every bound one (split right, go to workspace 3…), in the registry's order.</summary>
    private IEnumerable<ActionInvocation> KeyActions()
    {
        var keys = _config.Keybindings;
        return Actions.WithoutArguments
            .Concat(Keybindings.Default.All.Select(b => b.Action))
            .Concat(keys.All.Select(b => b.Action))
            .Concat(keys.Sequences.Select(s => s.Action))
            .Where(a => a.Action != TerminalAction.CopyIfSelection || keys.All.Any(b => b.Action == a))
            .Distinct()
            .OrderBy(Actions.Order).ThenBy(Actions.Format, StringComparer.Ordinal);
    }

    private void FillKeyRows()
    {
        _keyRows.Children.Clear();
        var lines = KeyBindingEditor.Lines(_config);
        var query = _keyFilter.Trim();

        // Grouped (Clipboard, Tabs, Panes…), each group in the registry's order; groups the
        // filter leaves empty aren't shown.
        foreach (var group in KeyActions().GroupBy(Actions.Category).OrderBy(g => IndexOf(Actions.Categories, g.Key)))
        {
            var rows = new List<Border>();
            foreach (var action in group)
            {
                var title = Actions.Title(action);
                var chords = _config.Keybindings.All.Where(b => b.Action == action).Select(b => b.Chord).ToList();
                var sequences = _config.Keybindings.Sequences.Where(s => s.Action == action).Select(s => s.Keys).ToList();
                if (query.Length > 0 &&
                    !$"{title} {Actions.Format(action)} {group.Key} {string.Join(' ', chords)} {string.Join(' ', sequences)}".Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rows.Add(KeyRow(action, title, chords, sequences, lines));
            }

            if (rows.Count > 0)
            {
                _keyRows.Children.Add(GroupTitle(group.Key));
                rows.ForEach(_keyRows.Children.Add);
            }
        }

        if (_keyRows.Children.Count == 0)
        {
            _keyRows.Children.Add(Note($"No action matches “{query}”."));
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string item) => list.ToList().IndexOf(item);

    private Border KeyRow(ActionInvocation action, string title, List<KeyChord> chords, List<KeySequence> sequences,
        IReadOnlyDictionary<string, string> lines)
    {
        // Chips wrap onto more rows rather than squeezing the action's name.
        var keys = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6, AlignRight = true, VerticalAlignment = VerticalAlignment.Center };
        foreach (var chord in chords)
        {
            keys.Children.Add(KeyChip(chord.ToString(), $"Remove {chord} from {title}", () => ApplyKeys(KeyBindingEditor.Remove(lines, action, chord))));
        }

        foreach (var sequence in sequences)
        {
            keys.Children.Add(KeyChip(sequence.ToString(), $"Remove {sequence} from {title}", () => ApplyKeys(KeyBindingEditor.Remove(lines, sequence))));
        }

        var add = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(8, 6, 8, 6) };
        ToolTipService.SetToolTip(add, "Add a key");
        AutomationProperties.SetName(add, $"Add a key to {title}");
        add.Flyout = CaptureFlyout(action, title, lines);
        keys.Children.Add(add);

        var name = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center } },
        };
        if (KeyBindingEditor.Customized(lines, action))
        {
            var reset = ResetArrow(title, "Back to its default keys");
            reset.Visibility = Visibility.Visible;
            reset.Click += (_, _) => ApplyKeys(KeyBindingEditor.Reset(lines, action));
            name.Children.Add(reset);
        }

        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                name,
                new TextBlock
                {
                    Text = Actions.Format(action),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                },
            },
        };
        // Two shares of the width to the keys, one to the name (never under 200 px).
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 200 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        Grid.SetColumn(keys, 1);
        grid.Children.Add(text);
        grid.Children.Add(keys);
        return CardBorder(grid);
    }

    /// <summary>
    /// A chord as keycaps, one per key ("Ctrl" "Shift" "C"), the way Windows shows shortcuts; a
    /// sequence's steps ("Ctrl+A > C") separated by an arrow.
    /// </summary>
    private static StackPanel Keycaps(string label)
    {
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var steps = label.Split(" > ");
        for (var s = 0; s < steps.Length; s++)
        {
            if (s > 0)
            {
                caps.Children.Add(new TextBlock { Text = "›", Opacity = 0.7, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            foreach (var key in steps[s].Split('+'))
            {
                caps.Children.Add(new Border
                {
                    Child = new TextBlock { Text = key, FontSize = 12 },
                    MinWidth = 22,
                    Padding = new Thickness(5, 1, 5, 2),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDisabledBrush"],
                    Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                });
            }
        }

        return caps;
    }

    /// <summary>A key as a chip; clicking it removes the key.</summary>
    private static Button KeyChip(string label, string removeName, Action remove)
    {
        var chip = new Button
        {
            Padding = new Thickness(8, 3, 6, 3),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    Keycaps(label),
                    new FontIcon { Glyph = "", FontSize = 9, Opacity = 0.7 },
                },
            },
        };
        ToolTipService.SetToolTip(chip, removeName);
        AutomationProperties.SetName(chip, removeName);
        chip.Click += (_, _) => remove();
        return chip;
    }

    /// <summary>
    /// "Press the keys": captures a chord (modifiers held, then a key), says what it would do
    /// (taken from another action, starts sequences, can't be written), and adds it on confirm.
    /// </summary>
    private Flyout CaptureFlyout(ActionInvocation action, string title, IReadOnlyDictionary<string, string> lines)
    {
        var shown = new TextBlock { Text = "Press the keys", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
        var capture = new Button
        {
            Content = shown,
            MinWidth = 280,
            MinHeight = 56,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(capture, $"Press the new key for {title}");
        var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 280, Opacity = 0.8 };
        var confirm = new Button { Content = "Add", IsEnabled = false, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetName(confirm, "Add the key");
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = $"A key for {title}", FontWeight = FontWeights.SemiBold },
                    capture,
                    problem,
                    confirm,
                },
            },
        };
        KeyChord? pending = null;
        flyout.Opened += (_, _) =>
        {
            pending = null;
            shown.Text = "Press the keys";
            problem.Text = "Hold Ctrl, Alt or Shift, then press a key. Esc cancels.";
            confirm.IsEnabled = false;
            confirm.Content = "Add";
            capture.Focus(FocusState.Programmatic);
        };
        capture.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key;
            if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftControl or VirtualKey.RightControl or
                VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            {
                e.Handled = true;
                return;
            }

            bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);
            var chord = new KeyChord(key, Down(VirtualKey.Control), Down(VirtualKey.Menu), Down(VirtualKey.Shift));
            if (chord is { Ctrl: false, Alt: false, Shift: false } && key is VirtualKey.Escape)
            {
                flyout.Hide();
                e.Handled = true;
                return;
            }

            if (chord is { Ctrl: false, Alt: false, Shift: false } && key is VirtualKey.Tab)
            {
                return; // moves on to the button
            }

            e.Handled = true;
            pending = chord;
            shown.Text = chord.ToString();
            var keys = _config.Keybindings;
            (problem.Text, confirm.IsEnabled, confirm.Content) =
                chord.ToConfig() == null ? ("That key can't be written to config.toml yet.", false, "Add")
                : keys.StartsSequence(chord) ? ($"{chord} starts key sequences; change those in config.toml.", false, "Add")
                : keys.TryGet(chord, out var other) && other == action ? ("Already one of its keys.", false, "Add")
                : keys.TryGet(chord, out other) ? ($"Used by {Actions.Title(other)}: adding it here takes it from there.", true, "Replace")
                : ("", true, (object)"Add");
        };
        confirm.Click += (_, _) =>
        {
            flyout.Hide();
            if (pending is { } chord)
            {
                ApplyKeys(KeyBindingEditor.Add(lines, action, chord));
            }
        };
        return flyout;
    }

    private void ApplyKeys(IReadOnlyList<KeyEdit> edits)
    {
        foreach (var edit in edits)
        {
            Write(edit.Path, edit.Value);
        }
    }
}
