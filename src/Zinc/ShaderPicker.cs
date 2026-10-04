using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Zinc.Shaders;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// Picks shaders to add to a list (Settings' Custom shaders card): a flyout with a filter, the shaders Zinc comes with,
/// yours and those in folders you added, each with a line about it, whether it moves on its own, and a check if the
/// list has it. Like the color scheme picker: the shader under the pointer or keyboard focus is previewed (with the
/// list's own, at the end) and checked for whether it compiles; a click or Enter adds it, and the flyout stays open for
/// more. Closing it takes the preview back.
/// </summary>
internal sealed partial class ShaderPicker
{
    private readonly ListView _list = new()
    {
        Height = 380,
        SelectionMode = ListViewSelectionMode.Single,
        SingleSelectionFollowsFocus = false, // arrows preview, Enter or Space adds
    };
    private readonly TextBox _filter = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 3, FontSize = 12, Opacity = 0.8, MinHeight = 32 };
    private readonly Button _fromFile = new() { Content = "From a file…" };
    private readonly Button _addFolder = new() { Content = "Add a folder…" };
    private readonly DropDownButton _folders = new() { Content = "Folders", Visibility = Visibility.Collapsed };
    private readonly Func<IReadOnlyList<string>> _inList; // the list's files, full paths
    private readonly Func<(string ConfigFile, IReadOnlyList<string> Folders)> _sources;
    private IReadOnlyList<ShaderSection> _sections = [];
    private ShaderEntry? _highlighted;
    // Added while open: the list itself (from the config) has them only once the file is read again.
    private readonly HashSet<string> _added = new(StringComparer.OrdinalIgnoreCase);
    private bool _quiet;
    private int _check;

    public Flyout Flyout { get; } = new()
    {
        Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        ShouldConstrainToRootBounds = false, // a popup window: past the settings window's edges if need be
    };

    /// <summary>A shader is under the pointer or keyboard focus: preview it.</summary>
    public event Action<ShaderEntry>? Highlighted;

    /// <summary>The flyout closed: take the preview back.</summary>
    public event Action? HighlightEnded;

    /// <summary>Add this shader to the list (the flyout stays open).</summary>
    public event Action<ShaderEntry>? Picked;

    /// <summary>"From a file…": the file picker (the flyout closes first).</summary>
    public event Action? FromFile;

    /// <summary>"Add a folder…": a folder picker, then the folder is listed (the flyout closes first).</summary>
    public event Action? AddFolder;

    /// <summary>Stop listing a folder you added.</summary>
    public event Action<string>? RemoveFolder;

    public event Action? Opening;

    public event Action? Closed;

    public ShaderPicker(Func<IReadOnlyList<string>> inList, Func<(string ConfigFile, IReadOnlyList<string> Folders)> sources)
    {
        (_inList, _sources) = (inList, sources);
        AutomationProperties.SetName(_list, "Shaders");
        AutomationProperties.SetName(_filter, "Filter shaders");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _list.ItemContainerStyle = new Style(typeof(ListViewItem))
        {
            Setters =
            {
                new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
                new Setter(FrameworkElement.MinHeightProperty, 0.0),
            },
        };
        // Each item's template is an empty presenter, filled when it's shown (as the scheme picker's tiles).
        _list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ContentPresenter HorizontalAlignment='Stretch'/></DataTemplate>");
        _list.ContainerContentChanging += (_, e) =>
        {
            if (e.InRecycleQueue || e.ItemContainer.ContentTemplateRoot is not ContentPresenter presenter)
            {
                return;
            }

            var container = e.ItemContainer;
            if (e.Item is ShaderSection section)
            {
                presenter.Content = Header(section);
                container.IsHitTestVisible = false;
                container.IsTabStop = false;
                AutomationProperties.SetName(container, section.Title);
            }
            else if (e.Item is ShaderEntry entry)
            {
                presenter.Content = Row(entry);
                container.IsHitTestVisible = true;
                container.IsTabStop = true;
                AutomationProperties.SetName(container, entry.Name);
                AutomationProperties.SetHelpText(container, entry.Description ?? "");
            }

            if (container.Tag is not "hooked")
            {
                container.Tag = "hooked"; // containers are reused: hooked once
                container.PointerEntered += (s, _) => HighlightItem(s);
                container.GotFocus += (s, _) => HighlightItem(s);
            }

            e.Handled = true;
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (!_quiet && _list.SelectedItem is ShaderEntry entry)
            {
                Pick(entry);
            }
        };
        _filter.TextChanged += (_, _) => Fill();
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && _list.Items.OfType<ShaderEntry>().FirstOrDefault() is { } first)
            {
                Pick(first);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down && _list.Items.OfType<ShaderEntry>().FirstOrDefault() is { } top &&
                     _list.ContainerFromItem(top) is ListViewItem item)
            {
                item.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
        };

        _fromFile.Click += (_, _) =>
        {
            Flyout.Hide();
            FromFile?.Invoke();
        };
        _addFolder.Click += (_, _) =>
        {
            Flyout.Hide();
            AddFolder?.Invoke();
        };
        AutomationProperties.SetName(_folders, "Folders you added");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _fromFile, _addFolder, _folders } };
        Flyout.Content = new StackPanel { Width = 400, Spacing = 8, Children = { _filter, _list, _status, footer } };

        Flyout.Opening += (_, _) =>
        {
            _highlighted = null;
            _added.Clear();
            _filter.Text = "";
            SetStatus("Point at a shader to see it in the preview.");
            Opening?.Invoke();
            Load();
        };
        Flyout.Opened += (_, _) => _filter.Focus(FocusState.Programmatic);
        Flyout.Closed += (_, _) =>
        {
            Log.Write($"shader picker: closed (previewed {_highlighted?.Name ?? "nothing"})");
            HighlightEnded?.Invoke();
            Closed?.Invoke();
        };
    }

    /// <summary>Reads the shaders (off the UI thread: a folder may hold many) and lists them.</summary>
    private async void Load()
    {
        try
        {
            var (configFile, folders) = _sources();
            _sections = await Task.Run(() => ShaderLibrary.List(configFile, folders));
            _filter.PlaceholderText = $"Filter {_sections.Sum(s => s.Shaders.Count)} shaders";
            var added = _sections.Where(s => s.Folder != null && s.Title != ShaderLibrary.YoursTitle).ToList();
            var menu = new MenuFlyout();
            foreach (var section in added)
            {
                var item = new MenuFlyoutItem { Text = $"Stop listing {section.Title}" };
                ToolTipService.SetToolTip(item, section.Folder);
                item.Click += (_, _) =>
                {
                    RemoveFolder?.Invoke(section.Folder!);
                    Load();
                };
                menu.Items.Add(item);
            }

            _folders.Flyout = menu;
            _folders.Visibility = added.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Fill();
        }
        catch (Exception ex)
        {
            Log.Crash("listing shaders", ex);
            SetStatus($"Couldn't list the shaders: {ex.Message}");
        }
    }

    private void Fill()
    {
        _quiet = true;
        try
        {
            var query = _filter.Text.Trim();
            var items = new List<object>();
            foreach (var section in _sections)
            {
                var shaders = section.Shaders.Where(s => query.Length == 0 || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (s.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
                if (shaders.Count > 0 || (section.Missing && query.Length == 0))
                {
                    items.Add(section);
                    items.AddRange(shaders);
                }
            }

            _list.ItemsSource = items;
            _list.SelectedItem = null;
        }
        finally
        {
            _quiet = false;
        }
    }

    private static FrameworkElement Header(ShaderSection section)
    {
        var title = new TextBlock { Text = section.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
        if (section.Folder == null || section.Title == ShaderLibrary.YoursTitle && !section.Missing)
        {
            return title;
        }

        return new StackPanel
        {
            Children =
            {
                title,
                new TextBlock
                {
                    Text = section.Missing ? $"Not found: {section.Folder}" : section.Folder,
                    FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };
    }

    private FrameworkElement Row(ShaderEntry entry)
    {
        var inList = InList(entry);
        var check = new FontIcon
        {
            Glyph = "", // check mark
            FontSize = 14,
            Opacity = inList ? 1 : 0,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold } } };
        if (entry.UsesTime)
        {
            name.Children.Add(new TextBlock { Text = "moves on its own", FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        }

        var text = new StackPanel { Spacing = 1, Children = { name } };
        if (entry.Description is { } about)
        {
            text.Children.Add(new TextBlock { Text = about, FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) } },
            Children = { check, text },
        };
        Grid.SetColumn(text, 1);
        ToolTipService.SetToolTip(row, entry.File);
        return row;
    }

    private void HighlightItem(object container)
    {
        if (_list.ItemFromContainer((DependencyObject)container) is not ShaderEntry entry || entry == _highlighted)
        {
            return;
        }

        _highlighted = entry;
        Highlighted?.Invoke(entry);
        CheckCompiles(entry);
    }

    /// <summary>Whether the highlighted shader compiles (the terminals' check and cache), in the status line.</summary>
    private async void CheckCompiles(ShaderEntry entry)
    {
        var check = ++_check;
        SetStatus($"{entry.Name}: checking…");
        try
        {
            var loaded = await ShaderLoader.Load([entry.File]);
            if (check != _check)
            {
                return; // another one is highlighted by now
            }

            SetStatus(loaded.Errors.Count == 0
                ? $"{entry.Name}: compiles.{(entry.UsesTime ? " Moves on its own (it redraws while the terminal is idle)." : "")}"
                : loaded.Errors[0], error: loaded.Errors.Count > 0);
        }
        catch (Exception ex)
        {
            Log.Crash("checking a shader", ex);
        }
    }

    private void Pick(ShaderEntry entry)
    {
        _quiet = true;
        _list.SelectedItem = null; // stays open for more: nothing stays selected
        _quiet = false;
        if (InList(entry))
        {
            SetStatus($"{entry.Name} is in the list already.");
            return;
        }

        Picked?.Invoke(entry);
        _added.Add(entry.File);
        SetStatus($"{entry.Name}: added.");
        // Its check mark, in place (rebuilding the list would lose the scroll position and focus).
        if (_list.ContainerFromItem(entry) is ListViewItem { ContentTemplateRoot: ContentPresenter presenter })
        {
            presenter.Content = Row(entry);
        }
    }

    private bool InList(ShaderEntry entry) => _added.Contains(entry.File) || _inList().Contains(entry.File, StringComparer.OrdinalIgnoreCase);

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.Opacity = error ? 1 : 0.8;
        if (error)
        {
            _status.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }
        else
        {
            _status.ClearValue(TextBlock.ForegroundProperty);
        }
    }
}
