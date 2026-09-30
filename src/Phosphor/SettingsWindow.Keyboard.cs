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
// away; the reset arrow puts an action back to its default keys. An action that takes an argument
// (split: a direction; new tab: a profile) is one card listing its variants that have keys, and
// "Add a key for another …" binds any other. Written to [keys] (KeyBindingEditor).
internal sealed partial class SettingsWindow
{
    private readonly StackPanel _keyRows = new() { Spacing = 4 };
    private string _keyFilter = "";

    private static readonly string AddGlyph = ((char)0xE710).ToString();
    private static readonly string RemoveGlyph = ((char)0xE711).ToString();

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

    /// <summary>The profiles an action can name (new tab with …), by id and name.</summary>
    private List<(string Id, string Name)> KeyProfiles() =>
        [.. _config.Profiles.Where(p => p.Missing == ProfileMissing.None).Select(p => (p.Id, p.Name))];

    private List<KeyChord> ChordsOf(ActionInvocation action) =>
        [.. _config.Keybindings.All.Where(b => b.Action == action).Select(b => b.Chord)];

    private List<KeySequence> SequencesOf(ActionInvocation action) =>
        [.. _config.Keybindings.Sequences.Where(s => s.Action == action).Select(s => s.Keys)];

    private void FillKeyRows()
    {
        _keyRows.Children.Clear();
        var lines = KeyBindingEditor.Lines(_config);
        var query = _keyFilter.Trim();
        var profiles = KeyProfiles();
        bool Matches(string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);

        // Grouped (Clipboard, Tabs, Panes…), each group in the registry's order; groups the
        // filter leaves empty aren't shown.
        foreach (var group in KeyActions().GroupBy(Actions.Category).OrderBy(g => IndexOf(Actions.Categories, g.Key)))
        {
            var rows = new List<Border>();
            foreach (var family in group.GroupBy(Actions.Family))
            {
                var actions = family.ToList();
                var text = string.Join(' ', actions.Select(a =>
                    $"{Actions.Title(a)} {Actions.Format(a)} {Actions.VariantLabel(a, profiles)} {string.Join(' ', ChordsOf(a))} {string.Join(' ', SequencesOf(a))}"));
                if (!Matches($"{group.Key} {Actions.FamilyTitle(family.Key)} {text}"))
                {
                    continue;
                }

                if (Actions.TakesArgument(family.Key))
                {
                    rows.Add(FamilyCard(family.Key, actions, lines, profiles));
                }
                else
                {
                    rows.AddRange(actions.Select(a => KeyRow(a, Actions.Title(a), ChordsOf(a), SequencesOf(a), lines)));
                }
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

    /// <summary>An action's name (with the reset arrow while its keys are changed) and how it's written in [keys].</summary>
    private StackPanel ActionName(string title, string written, IReadOnlyList<KeyEdit> reset)
    {
        var name = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center } },
        };
        if (reset.Count > 0)
        {
            var arrow = ResetArrow(title, "Back to its default keys");
            arrow.Visibility = Visibility.Visible;
            arrow.Click += (_, _) => ApplyKeys(reset);
            name.Children.Add(arrow);
        }

        return new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                name,
                new TextBlock
                {
                    Text = written,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                },
            },
        };
    }

    /// <summary>Two shares of the width to the keys, one to the name (never under 200 px).</summary>
    private static Grid NameAndKeys(FrameworkElement name, FrameworkElement keys)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 200 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        Grid.SetColumn(keys, 1);
        grid.Children.Add(name);
        grid.Children.Add(keys);
        return grid;
    }

    private Border KeyRow(ActionInvocation action, string title, List<KeyChord> chords, List<KeySequence> sequences,
        IReadOnlyDictionary<string, string> lines) =>
        CardBorder(NameAndKeys(
            ActionName(title, Actions.Format(action), KeyBindingEditor.Reset(lines, action)),
            KeysPanel(action, title, chords, sequences, lines)));

    /// <summary>
    /// An action that takes an argument, as one card: its variants that have keys ("Right",
    /// "Down"…), each with its keys, and "Add a key for another direction" to bind any other.
    /// </summary>
    private Border FamilyCard(string family, List<ActionInvocation> actions, IReadOnlyDictionary<string, string> lines,
        List<(string Id, string Name)> profiles)
    {
        var title = Actions.FamilyTitle(family);
        var argument = Actions.ArgumentName(family);
        var variants = Actions.Variants(family, profiles);

        // In the variants' own order (Right, Down, Left, Up; 1-9, the last), others after them.
        int Rank(ActionInvocation a) => variants.ToList().FindIndex(v => v.Invocation == a) is var i and >= 0 ? i : int.MaxValue;
        actions = [.. actions.OrderBy(Rank).ThenBy(Actions.Format, StringComparer.Ordinal)];
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(ActionName(title, $"{family} <{argument}>", [.. actions.SelectMany(a => KeyBindingEditor.Reset(lines, a))]));

        foreach (var action in actions)
        {
            var label = new TextBlock
            {
                Text = Actions.VariantLabel(action, profiles),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            body.Children.Add(NameAndKeys(label, KeysPanel(action, Actions.Title(action), ChordsOf(action), SequencesOf(action), lines)));
        }

        // Any other variant: pick it in the capture flyout (the first one without keys to start).
        var first = variants.FirstOrDefault(v => !actions.Contains(v.Invocation))?.Invocation ?? variants[0].Invocation;
        var another = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = AddGlyph, FontSize = 12 }, new TextBlock { Text = $"Add a key for another {argument}" } },
            },
            Margin = new Thickness(16, 2, 0, 0),
            Padding = new Thickness(8, 4, 10, 4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Flyout = CaptureFlyout(first, title, lines, variants),
        };
        AutomationProperties.SetName(another, $"Add a key to {title} for another {argument}");
        body.Children.Add(another);
        return CardBorder(body);
    }

    /// <summary>An action's keys as chips (click one to remove it), wrapping onto more rows, and + to add one.</summary>
    private WrapPanel KeysPanel(ActionInvocation action, string title, List<KeyChord> chords, List<KeySequence> sequences,
        IReadOnlyDictionary<string, string> lines)
    {
        var keys = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6, AlignRight = true, VerticalAlignment = VerticalAlignment.Center };
        foreach (var chord in chords)
        {
            keys.Children.Add(KeyChip(chord.ToString(), $"Remove {chord} from {title}", () => ApplyKeys(KeyBindingEditor.Remove(lines, action, chord))));
        }

        foreach (var sequence in sequences)
        {
            keys.Children.Add(KeyChip(sequence.ToString(), $"Remove {sequence} from {title}", () => ApplyKeys(KeyBindingEditor.Remove(lines, sequence))));
        }

        var add = new Button { Content = new FontIcon { Glyph = AddGlyph, FontSize = 12 }, Padding = new Thickness(8, 6, 8, 6) };
        ToolTipService.SetToolTip(add, "Add a key");
        AutomationProperties.SetName(add, $"Add a key to {title}");
        add.Flyout = CaptureFlyout(action, title, lines);
        keys.Children.Add(add);
        return keys;
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
                    new FontIcon { Glyph = RemoveGlyph, FontSize = 9, Opacity = 0.7 },
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
    /// With <paramref name="variants"/>, it first asks which one (split: which direction).
    /// </summary>
    private Flyout CaptureFlyout(ActionInvocation action, string title, IReadOnlyDictionary<string, string> lines,
        IReadOnlyList<ActionVariant>? variants = null)
    {
        var current = action;
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
        var panel = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = $"A key for {title}", FontWeight = FontWeights.SemiBold } } };

        KeyChord? pending = null;

        // What the pressed key would do, for the action chosen now.
        void Evaluate()
        {
            if (pending is not { } chord)
            {
                return;
            }

            var keys = _config.Keybindings;
            (problem.Text, confirm.IsEnabled, confirm.Content) =
                chord.ToConfig() == null ? ("That key can't be written to config.toml yet.", false, "Add")
                : keys.StartsSequence(chord) ? ($"{chord} starts key sequences; change those in config.toml.", false, "Add")
                : keys.TryGet(chord, out var other) && other == current ? ("Already one of its keys.", false, "Add")
                : keys.TryGet(chord, out other) ? ($"Used by {Actions.Title(other)}: adding it here takes it from there.", true, "Replace")
                : ("", true, (object)"Add");
        }

        ComboBox? which = null;
        if (variants != null)
        {
            which = new ComboBox { Header = "For", HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(which, $"Which {Actions.ArgumentName(Actions.Family(action))}");
            foreach (var variant in variants)
            {
                which.Items.Add(variant.Label);
            }

            which.SelectionChanged += (_, _) =>
            {
                if (which.SelectedIndex >= 0)
                {
                    current = variants[which.SelectedIndex].Invocation;
                    Evaluate();
                }
            };
            panel.Children.Add(which);
        }

        panel.Children.Add(capture);
        panel.Children.Add(problem);
        panel.Children.Add(confirm);
        var flyout = new Flyout { Content = panel };
        flyout.Opened += (_, _) =>
        {
            pending = null;
            current = action;
            if (which != null && variants != null)
            {
                which.SelectedIndex = Math.Max(0, variants.ToList().FindIndex(v => v.Invocation == action));
            }

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
            Evaluate();
        };
        confirm.Click += (_, _) =>
        {
            flyout.Hide();
            if (pending is { } chord)
            {
                ApplyKeys(KeyBindingEditor.Add(lines, current, chord));
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
