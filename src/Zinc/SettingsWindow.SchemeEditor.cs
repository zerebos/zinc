using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Settings;
using Windows.UI;

namespace Zinc;

/// <summary>
/// Editing a color scheme ("Customize" on the scheme cards): its file in the themes folder next to
/// config.toml (a bundled scheme is copied there first, and the copy stands in for it everywhere).
/// Each change is written as it settles; the config watcher then reloads, so every pane using the
/// scheme, and the preview pinned above, change as you edit. Undo puts back the file as it was.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The scheme being edited (a sub-page of whatever page opened it), or null.</summary>
    private string? _editingScheme;

    /// <summary>Its file's text when the editor opened, for Undo.</summary>
    private string? _schemeOriginal;

    private static readonly (string Key, string Label, string Description)[] SchemeBaseColors =
    [
        ("background", "Background", ""),
        ("foreground", "Text", "Text in the default color."),
        ("cursor-color", "Cursor", ""),
        ("cursor-text", "Cursor text", "The character under a block cursor."),
        ("selection-background", "Selection", "Behind selected text (not set: selection inverts the colors)."),
        ("selection-foreground", "Selected text", "Not set: selected text keeps its color."),
    ];

    /// <summary>
    /// Opens the editor for scheme <paramref name="name"/> (null: the default, which becomes "Lit
    /// Cell", named in config.toml so the edited copy is the one used).
    /// </summary>
    private void CustomizeScheme(string? name, IReadOnlyList<string> themePath)
    {
        if (name == null)
        {
            name = "Lit Cell";
            Write(themePath, name);
        }

        string? file;
        try
        {
            file = _config.Themes.Customize(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"settings: couldn't copy scheme {name}: {ex.Message}");
            return;
        }

        if (file == null)
        {
            return;
        }

        _editingScheme = name;
        _schemeThemePath = themePath;
        _schemeOriginal = ReadScheme(file);
        Render();
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static string ReadScheme(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void SchemeEditor(string name)
    {
        var file = _config.Themes.Customize(name);
        if (file == null)
        {
            _page.Children.Add(Note("This scheme isn’t there any more."));
            return;
        }

        string Current() => ReadScheme(file);
        void Save(string key, string? value)
        {
            try
            {
                File.WriteAllText(file, SchemeFile.Set(Current(), key, value));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Write($"settings: couldn't write scheme {name}: {ex.Message}");
            }
        }

        var replaces = _config.Themes.ReplacesBundled(name);
        _page.Children.Add(Note(replaces
            ? $"Your copy of {name}, used instead of the one Zinc comes with. Every pane using it changes as you edit."
            : $"Yours, in the themes folder next to config.toml. Every pane using it changes as you edit."));

        var undo = new Button { Content = "Undo changes" };
        undo.Click += (_, _) =>
        {
            try
            {
                File.WriteAllText(file, _schemeOriginal ?? "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Write($"settings: couldn't undo scheme {name}: {ex.Message}");
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { undo } };
        if (replaces)
        {
            var original = new Button { Content = "Restore the original" };
            ToolTipService.SetToolTip(original, $"Deletes your copy: {name} as Zinc comes with it is used again.");
            original.Click += (_, _) =>
            {
                try
                {
                    _config.Themes.BackToOriginal(name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Write($"settings: couldn't remove scheme {name}: {ex.Message}");
                }

                LeaveSchemeEditor();
            };
            buttons.Children.Add(original);
        }

        var open = new Button { Content = "Open in editor" };
        open.Click += (_, _) => OpenInEditor(file);
        buttons.Children.Add(open);
        buttons.Children.Add(new Button { Content = "Save as new scheme…", Flyout = NewSchemeFlyout(() => name, _ => name, _schemeThemePath) });
        if (!replaces)
        {
            var delete = new Button { Content = "Delete scheme" };
            delete.Click += (_, _) => DeleteScheme(name);
            buttons.Children.Add(delete);
        }
        _page.Children.Add(buttons);
        _refreshers.Add(() => undo.IsEnabled = Current() != _schemeOriginal);
        undo.IsEnabled = Current() != _schemeOriginal;

        _page.Children.Add(GroupTitle("Colors"));
        foreach (var (key, label, description) in SchemeBaseColors)
        {
            var def = new SettingDef("appearance", "Scheme", $"scheme.{key}", label, description, SettingKind.Color, null)
            {
                Placeholder = "Not set",
                NullLabel = "Not set (the default)",
            };
            var (control, show) = ColorControl(def, value => Save(key, value as string), null);
            show(SchemeFile.Get(Current(), key));
            _refreshers.Add(() => show(SchemeFile.Get(Current(), key)));
            _page.Children.Add(CardLayout(new TextBlock { Text = label }, null, DescriptionText(description), null, null, control));
        }

        _page.Children.Add(GroupTitle("Palette"));
        _page.Children.Add(Note("The 16 colors programs choose from. Click one to change it."));
        _page.Children.Add(CardBorder(PaletteGrid(Current, Save)));
    }

    /// <summary>Where the scheme being edited is chosen (the global <c>colors.theme</c>, or a profile's).</summary>
    private IReadOnlyList<string> _schemeThemePath = ["colors", "theme"];

    /// <summary>
    /// A name for a new scheme, copied from <paramref name="from"/> (the one being edited; with <paramref name="sources"/>,
    /// the first choice of a "Start from" list); with "Use it now", it replaces <paramref name="replacing"/> (given the
    /// scheme it starts from) where it's chosen. Then the editor opens on it.
    /// </summary>
    private Flyout NewSchemeFlyout(Func<string> from, Func<string, string?> replacing, IReadOnlyList<string> themePath, Func<IReadOnlyList<string>>? sources = null)
    {
        var name = new TextBox { PlaceholderText = "Name", Width = 260 };
        AutomationProperties.SetName(name, "New scheme’s name");
        var problem = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260,
            Visibility = Visibility.Collapsed,
        };
        var use = new CheckBox { Content = "Use it now", IsChecked = true };
        var create = new Button { Content = "Create", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        // "Start from" (when the caller lists schemes): a search box filled in with the suggested scheme; typing narrows the list.
        var start = new AutoSuggestBox
        {
            Header = "Start from",
            Width = 260,
            QueryIcon = new SymbolIcon(Symbol.Find),
            Visibility = sources == null ? Visibility.Collapsed : Visibility.Visible,
        };
        AutomationProperties.SetName(start, "Start from");
        IReadOnlyList<string> names = [];
        start.TextChanged += (_, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var typed = start.Text.Trim();
                start.ItemsSource = names.Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase)).Take(50).ToList();
            }
        };
        start.GotFocus += (_, _) =>
        {
            start.ItemsSource = names.Take(50).ToList(); // something to pick from before typing
        };
        start.SuggestionChosen += (_, e) => start.Text = e.SelectedItem as string ?? start.Text;
        var flyout = new Flyout { Content = new StackPanel { Spacing = 8, Children = { intro, name, start, problem, use, create } } };
        flyout.Opening += (_, _) =>
        {
            intro.Text = sources == null
                ? $"A copy of {from()} to change as you like, yours in the themes folder."
                : "A copy of a scheme to change as you like, yours in the themes folder.";
            if (sources != null)
            {
                names = sources();
                start.Text = names.FirstOrDefault(n => n.Equals(from(), StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault() ?? "";
            }

            name.Text = "";
            problem.Visibility = Visibility.Collapsed;
        };

        void Create()
        {
            if (_config.Themes.NameProblem(name.Text) is { } why)
            {
                problem.Text = why;
                problem.Visibility = Visibility.Visible;
                return;
            }

            var created = name.Text.Trim();
            var origin = from();
            if (sources != null)
            {
                var typed = start.Text.Trim();
                if (names.FirstOrDefault(n => n.Equals(typed, StringComparison.OrdinalIgnoreCase)) is not { } known)
                {
                    problem.Text = typed.Length == 0 ? "Pick a scheme to start from." : $"There’s no scheme called {typed}.";
                    problem.Visibility = Visibility.Visible;
                    return;
                }

                origin = known;
            }

            try
            {
                if (_config.Themes.Create(created, origin) == null)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem.Text = $"Couldn’t save it: {ex.Message}";
                problem.Visibility = Visibility.Visible;
                return;
            }

            flyout.Hide();
            if (use.IsChecked == true)
            {
                UseScheme(themePath, replacing(origin), created);
            }

            CustomizeScheme(created, themePath);
        }

        create.Click += (_, _) => Create();
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Create();
            }
        };
        return flyout;
    }

    /// <summary>
    /// Scheme <paramref name="name"/> in <paramref name="replacing"/>'s place where schemes are chosen
    /// (a light/dark pair keeps its other side); <paramref name="name"/> null: back to Zinc's own.
    /// </summary>
    private void UseScheme(IReadOnlyList<string> themePath, string? replacing, string? name)
    {
        var profile = themePath.Count > 2 ? themePath[1] : null;
        var value = (profile != null ? _config.ValueOf(profile, "colors.theme") : null) ?? _config.ValueOf("colors.theme");
        if (value is string text && ThemeLibrary.TryParsePair(text, out var light, out var dark))
        {
            // The side it replaces (else the dark one); gone (null), Lit Cell's side takes its place.
            if (string.Equals(light, replacing, StringComparison.OrdinalIgnoreCase))
            {
                light = name ?? "Lit Cell Light";
            }
            else
            {
                dark = name ?? "Lit Cell";
            }

            Write(themePath, $"light:{light},dark:{dark}");
        }
        else
        {
            Write(themePath, name);
        }
    }

    /// <summary>Deletes one of the user's own schemes, after asking; if it was in use, the default colors take its place.</summary>
    private async void DeleteScheme(string name)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = $"Delete {name}?",
                Content = "Removes its file from the themes folder. Anywhere it’s used goes back to the default colors.",
                PrimaryButtonText = "Delete scheme",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            bool Same(string other) => string.Equals(other, name, StringComparison.OrdinalIgnoreCase);
            if (_config.ValueOf("colors.theme") is string used &&
                (Same(used) || ThemeLibrary.TryParsePair(used, out var light, out var dark) && (Same(light) || Same(dark))))
            {
                UseScheme(["colors", "theme"], name, null);
            }

            _config.Themes.Delete(name);
            LeaveSchemeEditor();
        }
        catch (Exception ex)
        {
            Log.Crash("settings: deleting a scheme", ex);
        }
    }

    private void LeaveSchemeEditor()
    {
        _editingScheme = null;
        _schemeOriginal = null;
        Render();
    }

    private static void OpenInEditor(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception)
        {
            Process.Start("notepad.exe", $"\"{file}\"");
        }
    }

    /// <summary>The palette as two rows of eight (normal, bright), each a chip opening a color picker.</summary>
    private FrameworkElement PaletteGrid(Func<string> current, Action<string, string?> save)
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var c = 0; c < 8; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var (row, title) in new[] { (0, "Normal"), (1, "Bright") })
        {
            var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
        }

        for (var i = 0; i < 16; i++)
        {
            var chip = PaletteChip(i, current, save);
            Grid.SetRow(chip, i / 8);
            Grid.SetColumn(chip, 1 + i % 8);
            grid.Children.Add(chip);
        }

        return grid;
    }

    private Button PaletteChip(int index, Func<string> current, Action<string, string?> save)
    {
        var key = $"palette.{index}";
        var name = SchemeTiles.ColorNames[index];
        var fill = new SolidColorBrush(Colors.Transparent);
        var chip = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = fill,
            BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"],
        };
        foreach (var state in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
        {
            chip.Resources[state] = fill;
        }

        chip.Resources["ButtonBorderBrushPointerOver"] = Application.Current.Resources["TextFillColorPrimaryBrush"];

        // What the chip shows: the file's color, else what a terminal shows without one (libghostty's).
        Color Shown() => SchemeFile.Get(current(), key) is { } hex && TryHex(hex, out var c)
            ? c
            : SchemeTiles.ToColor(Zinc.Terminal.TerminalSession.BuiltinBaseColors[index]);
        void Paint()
        {
            var color = Shown();
            fill.Color = color;
            var hex = SchemeTiles.Hex(color);
            AutomationProperties.SetName(chip, $"{name}: {hex}");
            ToolTipService.SetToolTip(chip, $"{name} ({index}): {hex}");
        }

        Paint();
        _refreshers.Add(Paint);

        var picker = new ColorPicker { IsAlphaEnabled = false, IsMoreButtonVisible = true, ColorSpectrumShape = ColorSpectrumShape.Ring };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, picker } } };
        chip.Flyout = flyout;

        // Written once a drag settles: live, without a reload per pixel.
        var settle = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        settle.Interval = TimeSpan.FromMilliseconds(150);
        settle.IsRepeating = false;
        var open = false;
        settle.OnTick(_ =>
        {
            if (open && SchemeTiles.Hex(picker.Color) is var hex && !string.Equals(hex, SchemeFile.Get(current(), key), StringComparison.OrdinalIgnoreCase))
            {
                save(key, hex);
                fill.Color = picker.Color;
            }
        });
        _paletteTimers.Add(settle); // kept alive (a timer only a local holds stops firing)
        picker.ColorChanged += (_, _) =>
        {
            if (open)
            {
                settle.Stop();
                settle.Start();
            }
        };
        flyout.Opening += (_, _) =>
        {
            picker.Color = Shown();
            open = true;
        };
        flyout.Closed += (_, _) =>
        {
            open = false;
            settle.Stop();
            if (!string.Equals(SchemeTiles.Hex(picker.Color), SchemeTiles.Hex(Shown()), StringComparison.OrdinalIgnoreCase))
            {
                save(key, SchemeTiles.Hex(picker.Color)); // the last move, if it hadn't settled
            }
        };
        return chip;
    }

    private readonly List<Microsoft.UI.Dispatching.DispatcherQueueTimer> _paletteTimers = [];
}
