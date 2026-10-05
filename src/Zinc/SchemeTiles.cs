using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Ghostty;
using Zinc.Terminal;
using Windows.UI;

namespace Zinc;

/// <summary>
/// Color schemes shown as themselves: a tile in the scheme's background with its 16 base colors
/// and its name in its text color. Used by the settings window's scheme pickers and the welcome
/// page's list, where a name alone (among ~650) is a guess.
/// </summary>
internal static class SchemeTiles
{
    /// <summary>The 16 base colors' names, as a color picker shows them.</summary>
    public static readonly string[] ColorNames =
    [
        "Black", "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White",
        "Bright black", "Bright red", "Bright green", "Bright yellow", "Bright blue", "Bright magenta", "Bright cyan", "Bright white",
    ];

    // Schemes are read once per library (a config reload makes a new library, so edits show).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ThemeLibrary, Dictionary<string, TerminalTheme?>> Cache = [];

    /// <summary>Scheme <paramref name="name"/>'s colors (null: Zinc's own), or null if there's no such scheme.</summary>
    public static TerminalTheme? ThemeOf(ThemeLibrary themes, string? name)
    {
        if (name == null)
        {
            return TerminalTheme.Default;
        }

        var cache = Cache.GetOrCreateValue(themes);
        if (!cache.TryGetValue(name, out var theme))
        {
            cache[name] = theme = themes.Apply(new Config(), name)?.Theme;
        }

        return theme;
    }

    public static Color ToColor(GhosttyColorRgb c) => Color.FromArgb(0xFF, c.R, c.G, c.B);

    public static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    /// <summary>A scheme's tile: its background, its 16 colors in two rows, its name in its text color.</summary>
    public static Border Tile(TerminalTheme theme, string label, double chip = 9)
    {
        var chips = new Grid { ColumnSpacing = 2, RowSpacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 8; i++)
        {
            chips.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(chip) });
        }

        chips.RowDefinitions.Add(new RowDefinition { Height = new GridLength(chip) });
        chips.RowDefinitions.Add(new RowDefinition { Height = new GridLength(chip) });
        var colors = theme.BaseColors;
        for (var i = 0; i < 16; i++)
        {
            var square = new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(ToColor(colors[i])) };
            Grid.SetColumn(square, i % 8);
            Grid.SetRow(square, i / 8);
            chips.Children.Add(square);
        }

        var name = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(ToColor(theme.Foreground)),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(name, 1);
        row.Children.Add(chips);
        row.Children.Add(name);
        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(ToColor(theme.Background)),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    /// <summary>Makes <paramref name="list"/> (items: scheme names) show each as its tile, built when it's scrolled into view.</summary>
    public static void ShowAsTiles(ListViewBase list, ThemeLibrary themes, string? defaultLabel)
    {
        list.ItemContainerStyle = new Style(typeof(ListViewItem))
        {
            Setters =
            {
                new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(Control.PaddingProperty, new Thickness(4, 2, 8, 2)),
                new Setter(FrameworkElement.MinHeightProperty, 0.0),
            },
        };
        // Each item's template is an empty presenter; its tile is built when the item is shown.
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ContentPresenter HorizontalAlignment='Stretch'/></DataTemplate>");
        list.ContainerContentChanging += (_, e) =>
        {
            if (e.InRecycleQueue || e.Item is not string name || e.ItemContainer.ContentTemplateRoot is not ContentPresenter presenter)
            {
                return;
            }

            var theme = ThemeOf(themes, name == defaultLabel ? null : name) ?? TerminalTheme.Default;
            presenter.Content = Tile(theme, name);
            AutomationProperties.SetName(e.ItemContainer, name);
            e.Handled = true;
        };
    }
}

/// <summary>
/// Picks a color scheme: a button showing the chosen scheme's tile, opening a list of every
/// scheme as a tile with a filter box. <see cref="Picked"/> reports the choice (null: the default
/// entry, when there is one).
/// </summary>
internal sealed partial class SchemePicker : Button
{
    private readonly ThemeLibrary _themes;
    private readonly IReadOnlyList<string> _names;
    private readonly string? _defaultLabel;
    // Arrow keys move through the list (previewing, see Highlighted); Space or Enter picks.
    private readonly ListView _list = new() { Height = 360, SelectionMode = ListViewSelectionMode.Single, SingleSelectionFollowsFocus = false };
    private readonly TextBox _filter = new();
    // Under the button's right edge; allowed past the window's edges, or WinUI moves it beside the
    // button when the window is short.
    private readonly Flyout _flyout = new()
    {
        Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        ShouldConstrainToRootBounds = false,
    };
    private readonly ContentPresenter _shown = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _quiet;
    private string _placeholder = "";
    private bool _picked;
    private string? _highlighted;

    public SchemePicker(ThemeLibrary themes, IReadOnlyList<string> names, string? defaultLabel, string automationName)
    {
        (_themes, _names, _defaultLabel) = (themes, names, defaultLabel);
        Width = 280;
        Padding = new Thickness(3, 3, 8, 3);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, automationName);
        var chevron = new FontIcon { Glyph = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(chevron, 1);
        content.Children.Add(_shown);
        content.Children.Add(chevron);
        Content = content;

        _filter.PlaceholderText = $"Search {names.Count} schemes";
        AutomationProperties.SetName(_filter, "Search schemes");
        AutomationProperties.SetName(_list, automationName);
        SchemeTiles.ShowAsTiles(_list, themes, defaultLabel);
        _list.ContainerContentChanging += (_, e) =>
        {
            // Under the pointer or keyboard focus: previewed (containers are reused: hooked once).
            if (!e.InRecycleQueue && e.ItemContainer.Tag is not "hooked")
            {
                e.ItemContainer.Tag = "hooked";
                e.ItemContainer.PointerEntered += (s, _) => HighlightItem(s);
                e.ItemContainer.GotFocus += (s, _) => HighlightItem(s);
            }
        };
        _flyout.Content = new StackPanel { Width = 320, Spacing = 8, Children = { _filter, _list } };
        Flyout = _flyout;

        _filter.TextChanged += (_, _) => Fill();
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && _list.Items.FirstOrDefault() is string first)
            {
                Pick(first);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down && _list.Items.Count > 0)
            {
                _list.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (!_quiet && _list.SelectedItem is string name)
            {
                Pick(name);
            }
        };
        _flyout.Opening += (_, _) =>
        {
            _picked = false;
            _highlighted = null;
            _filter.Text = "";
            Fill();
        };
        _flyout.Closed += (_, _) =>
        {
            // Closed without a pick: whatever was previewed goes back. (A pick is written, and the
            // reload that follows shows it.)
            Log.Write($"scheme picker: closed ({(_picked ? "picked" : $"previewed {_highlighted ?? "nothing"}")})");
            if (!_picked && _highlighted != null)
            {
                HighlightEnded?.Invoke();
            }
        };
        _flyout.Opened += (_, _) =>
        {
            if (_list.SelectedItem is { } chosen)
            {
                _list.ScrollIntoView(chosen, ScrollIntoViewAlignment.Leading);
            }

            _filter.Focus(FocusState.Programmatic);
        };
    }

    /// <summary>The user picked a scheme (null: the default entry).</summary>
    public event Action<string?>? Picked;

    /// <summary>A scheme in the open list is under the pointer or has keyboard focus (null: the default entry), for a preview.</summary>
    public event Action<string?>? Highlighted;

    /// <summary>The list closed without a pick: take back the preview.</summary>
    public event Action? HighlightEnded;

    private void HighlightItem(object container)
    {
        if (_list.ItemFromContainer((DependencyObject)container) is string item && item != _highlighted)
        {
            _highlighted = item;
            Highlighted?.Invoke(item == _defaultLabel ? null : item);
        }
    }

    /// <summary>The scheme shown (null: the default entry, or nothing).</summary>
    public string? Selected { get; private set; }

    /// <summary>Whether a scheme (or the default entry) is shown, rather than the placeholder.</summary>
    public bool HasSelection { get; private set; }

    /// <summary>Shown while nothing is selected.</summary>
    public string Placeholder
    {
        set
        {
            _placeholder = value;
            if (!HasSelection)
            {
                Show(null, false);
            }
        }
    }

    public bool Contains(string name) => _names.Contains(name);

    /// <summary>Shows <paramref name="name"/> (null with <paramref name="selected"/>: the default entry) without reporting a pick.</summary>
    public void Show(string? name, bool selected = true)
    {
        HasSelection = selected && (name == null ? _defaultLabel != null : _names.Contains(name));
        Selected = HasSelection ? name : null;
        _shown.Content = HasSelection
            ? SchemeTiles.Tile(SchemeTiles.ThemeOf(_themes, name) ?? TerminalTheme.Default, name ?? _defaultLabel!)
            : new TextBlock { Text = _placeholder, Opacity = 0.7, Margin = new Thickness(6, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetHelpText(this, HasSelection ? name ?? _defaultLabel! : _placeholder);
    }

    private void Fill()
    {
        _quiet = true;
        try
        {
            var query = _filter.Text.Trim();
            var all = _defaultLabel != null ? _names.Prepend(_defaultLabel) : _names;
            _list.ItemsSource = all.Where(n => query.Length == 0 || n.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            var current = HasSelection ? Selected ?? _defaultLabel : null;
            _list.SelectedItem = current != null && ((List<string>)_list.ItemsSource).Contains(current) ? current : null;
        }
        finally
        {
            _quiet = false;
        }
    }

    private void Pick(string item)
    {
        var name = item == _defaultLabel ? null : item;
        _picked = true;
        _flyout.Hide();
        Show(name);
        Picked?.Invoke(name);
    }
}

/// <summary>
/// A scheme's sample on the color schemes page's cards: its name in its text color, then its 16 colors in one row, on its
/// background (rounded at the top: the card's footer goes under it). Shown again with another scheme when its card is reused.
/// </summary>
internal sealed class SchemeSample
{
    /// <summary>What goes on the card.</summary>
    public Border Element { get; }

    private readonly SolidColorBrush _background = new();
    private readonly SolidColorBrush _foreground = new();
    private readonly SolidColorBrush[] _chips = new SolidColorBrush[16];
    private readonly TextBlock _name;

    public SchemeSample()
    {
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var i = 0; i < 16; i++)
        {
            _chips[i] = new SolidColorBrush();
            chips.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(2), Background = _chips[i] });
        }

        _name = new TextBlock
        {
            Foreground = _foreground,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Element = new Border
        {
            Background = _background,
            CornerRadius = new CornerRadius(5, 5, 0, 0),
            Padding = new Thickness(12, 10, 12, 12),
            Child = new StackPanel { Spacing = 8, Children = { _name, chips } },
        };
    }

    public void Show(TerminalTheme theme, string label)
    {
        _name.Text = label;
        _background.Color = SchemeTiles.ToColor(theme.Background);
        _foreground.Color = SchemeTiles.ToColor(theme.Foreground);
        var colors = theme.BaseColors;
        for (var i = 0; i < 16; i++)
        {
            _chips[i].Color = SchemeTiles.ToColor(colors[i]);
        }
    }
}
