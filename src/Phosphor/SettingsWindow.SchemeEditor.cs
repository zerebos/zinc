using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Phosphor.Settings;
using Windows.UI;

namespace Phosphor;

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
    /// Opens the editor for scheme <paramref name="name"/> (null: Phosphor's own, which becomes "Lit
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
        var back = new HyperlinkButton { Content = "‹ Back", Padding = new Thickness(0, 2, 0, 2) };
        back.Click += (_, _) => LeaveSchemeEditor();
        _page.Children.Insert(0, back);
        if (file == null)
        {
            _page.Children.Add(Note("This scheme isn't there any more."));
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
            ? $"Your copy of {name}, in the themes folder next to config.toml: it's used instead of the one Phosphor comes with. Every pane using it changes as you edit."
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
            var original = new Button { Content = "Back to the original" };
            ToolTipService.SetToolTip(original, $"Deletes your copy: {name} as Phosphor comes with it is used again.");
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

        var open = new Button { Content = "Open file" };
        open.Click += (_, _) => OpenInEditor(file);
        buttons.Children.Add(open);
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
            : SchemeTiles.ToColor(Phosphor.Terminal.TerminalSession.BuiltinBaseColors[index]);
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
