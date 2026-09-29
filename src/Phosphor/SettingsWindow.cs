using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Phosphor.Settings;
using Phosphor.Terminal;
using Windows.UI;

namespace Phosphor;

/// <summary>
/// The settings window: every setting in <see cref="SettingsCatalog"/>, grouped by section, each
/// written to config.toml as soon as it changes (one key at a time, spliced in place; a default is
/// written as nothing) and applied by the config watcher's live reload, so your terminals show it
/// right away. "Undo changes" puts the file back as it was when the window opened. While
/// config.toml has an error, nothing is written. Its own window, so fonts and colors are judged
/// on the real terminals beside it.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    private const string DefaultSchemeLabel = "Phosphor's own";
    private const string BundledFontLabel = BundledFonts.Family + " (included)";

    private readonly SettingsWriter _writer;
    private ConfigFile _config;
    private readonly NavigationView _nav;
    private readonly AutoSuggestBox _search;
    private readonly StackPanel _page = new() { Spacing = 4, MaxWidth = 960, Padding = new Thickness(40, 12, 40, 40) };
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly InfoBar _changes = new() { Severity = InfoBarSeverity.Informational, IsClosable = false };
    private readonly InfoBar _problems = new() { Severity = InfoBarSeverity.Error, IsClosable = false, Title = "config.toml has an error" };
    private readonly List<Action> _refreshers = [];
    private string _section = "appearance";
    private bool _updating;

    /// <summary>A change was written to config.toml.</summary>
    public event Action? Wrote;

    public SettingsWindow(ConfigFile config)
    {
        _config = config;
        _writer = new SettingsWriter(config.Path);
        Title = "Phosphor Settings";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = TestHooks.Deterministic ? null : new MicaBackdrop();
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1040, 780));

        // Title bar: the app's mark and the window's name, in the caption area.
        var titleBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Height = 40,
            Padding = new Thickness(16, 0, 0, 0),
            Children =
            {
                new Image { Width = 16, Height = 16, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"))) },
                new TextBlock { Text = "Phosphor Settings", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
            },
        };

        _search = new AutoSuggestBox { PlaceholderText = "Find a setting", QueryIcon = new SymbolIcon(Symbol.Find) };
        AutomationProperties.SetName(_search, "Find a setting");
        _search.TextChanged += (_, _) => Render();

        _nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            OpenPaneLength = 250,
            AutoSuggestBox = _search,
            Content = _scroller,
        };
        foreach (var section in SettingsCatalog.Sections)
        {
            var item = new NavigationViewItem { Content = section.Title, Tag = section.Id, Icon = new FontIcon { Glyph = section.Glyph } };
            (section.Id == "about" ? _nav.FooterMenuItems : _nav.MenuItems).Add(item);
        }

        _nav.SelectedItem = _nav.MenuItems[0];
        _nav.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is NavigationViewItem { Tag: string id })
            {
                _section = id;
                _search.Text = "";
                Render();
                _scroller.ChangeView(null, 0, null, disableAnimation: true);
            }
        };

        var undo = new Button { Content = "Undo changes" };
        undo.Click += (_, _) => Guard("undoing changes", () =>
        {
            _writer.Undo();
            Wrote?.Invoke();
            _changes.IsOpen = false;
        });
        _changes.ActionButton = undo;
        _changes.Message = "Your changes are saved in config.toml and applied as you make them.";

        var openFile = new Button { Content = "Open config.toml" };
        openFile.Click += (_, _) => OpenConfigFile();
        _problems.ActionButton = openFile;

        var root = new Grid { RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_nav, 1);
        root.Children.Add(titleBar);
        root.Children.Add(_nav);
        Content = root;
        SetTitleBar(titleBar);
        Render();
    }

    /// <summary>The config was read again (after our write, or an edit elsewhere): show what's in force.</summary>
    public void Refresh(ConfigFile config)
    {
        _config = config;
        _updating = true;
        try
        {
            UpdateBanners();
            foreach (var refresh in _refreshers)
            {
                refresh();
            }
        }
        finally
        {
            _updating = false;
        }
    }

    // ------------------------------------------------------------ pages

    private void Render()
    {
        _page.Children.Clear();
        _refreshers.Clear();
        var query = _search.Text.Trim();
        _page.Children.Add(new TextBlock
        {
            Text = query.Length > 0 ? "Results" : SettingsCatalog.Sections.First(s => s.Id == _section).Title,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 12),
        });
        _page.Children.Add(_problems);
        _page.Children.Add(_changes);
        UpdateBanners();

        if (query.Length > 0)
        {
            var hits = SettingsCatalog.All.Where(s => Matches(s, query)).ToList();
            if (hits.Count == 0)
            {
                _page.Children.Add(Note($"No setting matches “{query}”."));
            }

            foreach (var section in hits.GroupBy(s => s.Section))
            {
                AddGroup(SettingsCatalog.Sections.First(s => s.Id == section.Key).Title, section);
            }
        }
        else if (_section == "about")
        {
            AboutPage();
        }
        else if (_section is "profiles" or "keyboard")
        {
            ComingSoon();
        }
        else
        {
            foreach (var group in SettingsCatalog.All.Where(s => s.Section == _section).GroupBy(s => s.Group))
            {
                AddGroup(group.Key, group);
                if (group.Key == "Colors")
                {
                    _page.Children.Add(Note(SettingsCatalog.FileOnly["colors.palette"]));
                }
            }
        }

        _scroller.Content = _page;
    }

    private static bool Matches(SettingDef s, string query) =>
        $"{s.Label} {s.Description} {s.Key} {s.Group}".Contains(query, StringComparison.OrdinalIgnoreCase);

    private void UpdateBanners()
    {
        _problems.IsOpen = _config.HasErrors;
        _problems.Message = (_config.Warnings.FirstOrDefault() ?? "") + " Nothing is saved from here until it's fixed.";
        _changes.IsOpen = !_config.HasErrors && _writer.Changed;
    }

    private void AddGroup(string title, IEnumerable<SettingDef> settings)
    {
        _page.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) });
        foreach (var setting in settings)
        {
            _page.Children.Add(Card(setting));
        }
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(2, 8, 0, 0),
    };

    private void ComingSoon()
    {
        var what = _section == "profiles"
            ? "Profiles (each shell's command, folder, icon, font and colors) are edited here in the next update. For now, they're [profile.<id>] sections in config.toml; the README lists what they take."
            : "Keys are changed here in the next update. For now, they're in the [keys] section of config.toml; the README lists every action.";
        _page.Children.Add(Note(what));
        var open = new Button { Content = "Open config.toml", Margin = new Thickness(0, 12, 0, 0) };
        open.Click += (_, _) => OpenConfigFile();
        _page.Children.Add(open);
    }

    private void AboutPage()
    {
        _page.Children.Add(Row(TerminalSession.Version.Replace("phosphor", "Phosphor"),
            "Terminal emulation and the built-in glyphs by Ghostty (MIT). Cascadia Mono NF by Microsoft (OFL). Color schemes from iTerm2-Color-Schemes.",
            null));
        _page.Children.Add(Row("Config file", _config.Path, ButtonRow(("Open", OpenConfigFile), ("Open folder", () => Reveal(_config.Path)))));
        _page.Children.Add(Row("Crash log", Log.CrashPath, ButtonRow(("Open folder", () => Reveal(Log.CrashPath)))));
        _page.Children.Add(Row("Welcome page", "The first-run questions: how you work, colors and font, default shell.",
            ButtonRow(("Open", () => App.Instance.ShowWelcome(firstRun: false)))));
    }

    private static StackPanel ButtonRow(params (string Label, Action Run)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, run) in buttons)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => run();
            row.Children.Add(button);
        }

        return row;
    }

    // ------------------------------------------------------------ cards

    /// <summary>A setting's row: what it is (and its config key), the control, Reset, and a problem if its value won't do.</summary>
    private Border Card(SettingDef setting)
    {
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x99, 0xA4)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var reset = new HyperlinkButton { Content = "Reset", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2, 6, 2) };
        ToolTipService.SetToolTip(reset, "Back to the default (removes it from config.toml)");
        AutomationProperties.SetName(reset, $"Reset {setting.Label}");

        void Commit(object? value)
        {
            if (_updating)
            {
                return;
            }

            if (_config.HasErrors)
            {
                Show(error, "config.toml has an error; fix it first (see the top of the page).");
                return;
            }

            if (value != null && SettingsCatalog.Problem(setting, value) is { } problem)
            {
                Show(error, problem);
                return;
            }

            error.Visibility = Visibility.Collapsed;
            Guard($"saving {setting.Key}", () =>
            {
                _writer.Set(setting, value);
                Wrote?.Invoke();
                _changes.IsOpen = true;
                reset.Visibility = value != null && !SettingsCatalog.Same(value, setting.Default) ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        var (control, show) = Control(setting, Commit);
        reset.Click += (_, _) =>
        {
            Commit(null);
            _updating = true;
            try
            {
                show(null);
            }
            finally
            {
                _updating = false;
            }
        };

        void Refresh()
        {
            var value = _config.ValueOf(setting.Key);
            reset.Visibility = value != null && !SettingsCatalog.Same(value, setting.Default) ? Visibility.Visible : Visibility.Collapsed;
            show(value);
        }

        _refreshers.Add(Refresh);
        _updating = true;
        try
        {
            Refresh();
        }
        finally
        {
            _updating = false;
        }

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = setting.Label, TextWrapping = TextWrapping.Wrap });
        if (setting.Description.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = setting.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }

        text.Children.Add(new TextBlock
        {
            Text = setting.Key,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });
        text.Children.Add(error);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(reset);
        controls.Children.Add(control);

        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(controls, 1);
        grid.Children.Add(text);
        grid.Children.Add(controls);
        return CardBorder(grid);
    }

    private static Border Row(string title, string detail, FrameworkElement? control)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = detail,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    IsTextSelectionEnabled = true,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            },
        });
        if (control != null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        return CardBorder(grid);
    }

    private static Border CardBorder(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(16, 12, 16, 12),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
    };

    private static void Show(TextBlock error, string message)
    {
        error.Text = message;
        error.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The control for <paramref name="setting"/>, calling <paramref name="commit"/> with each new
    /// value (null: the default), and how to show a value from the file in it.
    /// </summary>
    private (FrameworkElement Control, Action<object?> Show) Control(SettingDef setting, Action<object?> commit) => setting.Kind switch
    {
        SettingKind.Toggle => ToggleControl(setting, commit),
        SettingKind.Choice => ChoiceControl(setting, commit),
        SettingKind.Number => NumberControl(setting, commit),
        SettingKind.Color => ColorControl(commit),
        SettingKind.Font => FontControl(commit),
        SettingKind.Scheme => SchemeControl(commit),
        _ => TextControl(setting, commit),
    };

    private static (FrameworkElement, Action<object?>) ToggleControl(SettingDef setting, Action<object?> commit)
    {
        var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off", MinWidth = 0 };
        AutomationProperties.SetName(toggle, setting.Label);
        toggle.Toggled += (_, _) => commit(toggle.IsOn);
        return (toggle, value => toggle.IsOn = value as bool? ?? setting.Default as bool? ?? false);
    }

    private static (FrameworkElement, Action<object?>) ChoiceControl(SettingDef setting, Action<object?> commit)
    {
        var box = new ComboBox { MinWidth = 220 };
        AutomationProperties.SetName(box, setting.Label);
        foreach (var choice in setting.Choices)
        {
            box.Items.Add(choice.Label);
        }

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < setting.Choices.Count)
            {
                commit(setting.Choices[box.SelectedIndex].Value);
            }
        };
        return (box, value =>
        {
            var wanted = value ?? setting.Default;
            var index = setting.Choices.ToList().FindIndex(c => SettingsCatalog.Same(c.Value, wanted));
            box.SelectedIndex = index;
            box.PlaceholderText = index < 0 && value != null ? $"{value} (set in config.toml)" : "";
        });
    }

    private static (FrameworkElement, Action<object?>) NumberControl(SettingDef setting, Action<object?> commit)
    {
        var box = new NumberBox
        {
            Minimum = setting.Minimum,
            Maximum = setting.Maximum,
            SmallChange = setting.Step,
            LargeChange = setting.Step * 4,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 130,
        };
        AutomationProperties.SetName(box, setting.Label);
        var special = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Visibility = Visibility.Collapsed };
        box.ValueChanged += (_, e) =>
            commit(double.IsNaN(e.NewValue) ? null : SettingsCatalog.FromText(setting, e.NewValue.ToString(CultureInfo.InvariantCulture)));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { special, box } };
        if (setting.Unit != null)
        {
            panel.Children.Add(new TextBlock { Text = setting.Unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        }

        return (panel, value =>
        {
            // Padding written as [left, right]: shown, edited in the file.
            var list = value is List<object>;
            special.Text = list ? $"{ConfigEditor.FormatValue(value!)} in config.toml" : "";
            special.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            box.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
            box.Value = value is long or double ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
                : setting.Default is { } d ? Convert.ToDouble(d, CultureInfo.InvariantCulture) : double.NaN;
        });
    }

    private static (FrameworkElement, Action<object?>) TextControl(SettingDef setting, Action<object?> commit)
    {
        var box = new TextBox { Width = 220, PlaceholderText = setting.Placeholder ?? Format(setting.Default) };
        AutomationProperties.SetName(box, setting.Label);
        string? shown = null;
        void Apply()
        {
            if (box.Text != shown)
            {
                shown = box.Text;
                commit(SettingsCatalog.FromText(setting, box.Text));
            }
        }

        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Apply();
                e.Handled = true;
            }
        };
        return (box, value =>
        {
            if (box.FocusState == FocusState.Unfocused)
            {
                box.Text = shown = Format(value);
            }
        });
    }

    private static (FrameworkElement, Action<object?>) ColorControl(Action<object?> commit)
    {
        var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        swatch.BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"];
        var box = new TextBox { Width = 120, PlaceholderText = "The scheme's" };
        var picker = new ColorPicker { IsAlphaEnabled = false, IsMoreButtonVisible = true, ColorSpectrumShape = ColorSpectrumShape.Ring };
        var pick = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Flyout = new Flyout { Content = picker } };
        AutomationProperties.SetName(pick, "Pick a color");
        string? shown = null;
        void Paint(string? hex) => swatch.Background = hex != null && TryHex(hex, out var c)
            ? new SolidColorBrush(c)
            : (Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"];
        void Apply(string text)
        {
            if (text != shown)
            {
                shown = text;
                Paint(text);
                commit(text.Trim().Length == 0 ? null : text.Trim());
            }
        }

        box.LostFocus += (_, _) => Apply(box.Text);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Apply(box.Text);
                e.Handled = true;
            }
        };
        ((Flyout)pick.Flyout).Opening += (_, _) =>
        {
            if (TryHex(box.Text, out var c))
            {
                picker.Color = c;
            }
        };
        ((Flyout)pick.Flyout).Closed += (_, _) =>
        {
            var c = picker.Color;
            box.Text = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
            Apply(box.Text);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, box, pick } };
        return (panel, value =>
        {
            if (box.FocusState == FocusState.Unfocused)
            {
                box.Text = shown = value as string ?? "";
                Paint(shown);
            }
        });
    }

    private static (FrameworkElement, Action<object?>) FontControl(Action<object?> commit)
    {
        var box = new ComboBox { MinWidth = 260 };
        AutomationProperties.SetName(box, "Font");
        box.Items.Add(BundledFontLabel);
        foreach (var family in WelcomeWindow.MonospaceFamilies())
        {
            box.Items.Add(family);
        }

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string family)
            {
                commit(family == BundledFontLabel ? null : family);
            }
        };
        return (box, value =>
        {
            var family = value as string;
            if (family != null && !box.Items.Contains(family))
            {
                box.Items.Add(family); // not installed, or not monospace: still shown as written
            }

            box.SelectedItem = family is null || family.Equals(BundledFonts.Family, StringComparison.OrdinalIgnoreCase) ? BundledFontLabel : family;
        });
    }

    /// <summary>
    /// A scheme, or (switched on) a light/dark pair that follows Windows: <c>"light:X,dark:Y"</c>.
    /// </summary>
    private (FrameworkElement, Action<object?>) SchemeControl(Action<object?> commit)
    {
        var names = _config.Themes.Names();
        ComboBox Box(string label)
        {
            var box = new ComboBox { MinWidth = 240, Header = label };
            AutomationProperties.SetName(box, label);
            box.Items.Add(DefaultSchemeLabel);
            foreach (var name in names)
            {
                box.Items.Add(name);
            }

            return box;
        }

        var single = Box("Scheme");
        var light = Box("When Windows is light");
        var dark = Box("When Windows is dark");
        light.Items.Remove(DefaultSchemeLabel); // a pair names two schemes
        dark.Items.Remove(DefaultSchemeLabel);
        var follow = new ToggleSwitch { Header = "Follow Windows light/dark", OnContent = "Two schemes", OffContent = "One scheme", MinWidth = 0 };
        AutomationProperties.SetName(follow, "Follow Windows light/dark");
        var local = false; // set while showing a value: not a change

        void Layout(bool pair)
        {
            single.Visibility = pair ? Visibility.Collapsed : Visibility.Visible;
            light.Visibility = dark.Visibility = pair ? Visibility.Visible : Visibility.Collapsed;
        }

        void Changed()
        {
            if (local)
            {
                return;
            }

            if (follow.IsOn)
            {
                if (light.SelectedItem is string l && dark.SelectedItem is string d)
                {
                    commit($"light:{l},dark:{d}");
                }
            }
            else
            {
                commit(single.SelectedItem is string s && s != DefaultSchemeLabel ? s : null);
            }
        }

        single.SelectionChanged += (_, _) => Changed();
        light.SelectionChanged += (_, _) => Changed();
        dark.SelectionChanged += (_, _) => Changed();
        follow.Toggled += (_, _) =>
        {
            if (local)
            {
                return;
            }

            local = true;
            if (follow.IsOn)
            {
                // Start both sides from the scheme in use (or a well-known pair for Phosphor's own).
                var current = single.SelectedItem as string;
                var named = current != null && current != DefaultSchemeLabel;
                light.SelectedItem = named ? current : names.FirstOrDefault(n => n == "Catppuccin Latte") ?? names.FirstOrDefault();
                dark.SelectedItem = named ? current : names.FirstOrDefault(n => n == "Catppuccin Mocha") ?? names.FirstOrDefault();
            }
            else
            {
                single.SelectedItem = dark.SelectedItem ?? DefaultSchemeLabel;
            }

            Layout(follow.IsOn);
            local = false;
            Changed();
        };

        var panel = new StackPanel { Spacing = 10, Children = { follow, single, light, dark } };
        return (panel, value =>
        {
            local = true;
            var text = value as string;
            var pair = text != null && ThemeLibrary.TryParsePair(text, out var l, out var d);
            follow.IsOn = pair;
            if (pair && ThemeLibrary.TryParsePair(text!, out l, out d))
            {
                light.SelectedItem = light.Items.Contains(l) ? l : null;
                dark.SelectedItem = dark.Items.Contains(d) ? d : null;
            }
            else
            {
                single.SelectedItem = text != null && single.Items.Contains(text) ? text : DefaultSchemeLabel;
            }

            Layout(pair);
            local = false;
        });
    }

    // ------------------------------------------------------------ helpers

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => ConfigEditor.FormatValue(value),
    };

    private static bool TryHex(string text, out Color color)
    {
        color = default;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private void OpenConfigFile()
    {
        try
        {
            if (!File.Exists(_config.Path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_config.Path)!);
                File.WriteAllText(_config.Path, FirstRun.Template);
            }

            Process.Start(new ProcessStartInfo(_config.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"settings: open with the default app failed ({ex.Message}); using notepad");
            Process.Start("notepad.exe", $"\"{_config.Path}\"");
        }
    }

    private static void Reveal(string file)
    {
        try
        {
            Process.Start("explorer.exe", File.Exists(file) ? $"/select,\"{file}\"" : $"\"{Path.GetDirectoryName(file)}\"");
        }
        catch (Exception ex)
        {
            Log.Write($"settings: couldn't show {file}: {ex.Message}");
        }
    }

    private static void Guard(string what, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Crash(what, ex);
        }
    }
}
