using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Settings;
using Zinc.Shaders;
using Zinc.Terminal;

namespace Zinc;

// Custom shaders ([shaders] files): the list as a card of its own, each file with whether it compiles.
internal sealed partial class SettingsWindow
{
    private const string ShaderCollection = "https://github.com/0xhckr/ghostty-shaders";

    // The card on the page now, refreshed when one of its files is saved; and whether the window listens for that.
    private Action? _shaderRows;
    private bool _shaderWatch;

    /// <summary>
    /// The shader list: Add (several files at once), then a row per file in order, with its state (compiles, or why not)
    /// and buttons to move it, open it in an editor, or take it out. In a profile: the inherited list until changed here,
    /// when the profile gets a list of its own (empty: no shaders in it); the reset arrow inherits again.
    /// </summary>
    private Border ShaderFilesCard(SettingDef setting, string? profile)
    {
        IReadOnlyList<string> path = profile == null ? setting.Path : ["profile", profile, .. setting.Path];
        var error = ErrorText();
        var reset = ResetArrow(setting.Label, profile == null ? "No custom shaders (removes them from config.toml)" : "Back to the shaders every profile inherits");
        var description = DescriptionText();
        var rows = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        var add = new Button { Content = "Add…" };
        AutomationProperties.SetName(add, "Add a custom shader");
        var load = 0;

        IReadOnlyList<string>? Own() => profile == null
            ? _config.ValueOf(setting.Key) is { } global ? ShaderPaths.Listed(global) : null
            : _config.ValueOf(profile, setting.Key) is { } own ? ShaderPaths.Listed(own) : null;
        IReadOnlyList<string> Inherited() => ShaderPaths.Listed(_config.ValueOf(setting.Key));
        IReadOnlyList<string> Shown() => Own() ?? (profile == null ? [] : Inherited());

        void Save(IReadOnlyList<string> files)
        {
            if (_config.HasErrors)
            {
                Show(error, "config.toml has an error; fix it first (see the top of the page).");
                return;
            }

            error.Visibility = Visibility.Collapsed;
            // Globally an empty list is the default (nothing written); a profile's empty list means "none here".
            Write(path, profile == null && files.Count == 0 ? null : files.ToList());
        }

        void Refresh()
        {
            var own = Own();
            var files = Shown();
            reset.Visibility = (profile == null ? files.Count > 0 : own != null) ? Visibility.Visible : Visibility.Collapsed;
            if (profile == null)
            {
                SetText(description, setting.Description);
            }
            else
            {
                var inherited = Inherited();
                InheritedText(description, setting, inherited.Count == 0 ? "none" : string.Join(", ", inherited.Select(Path.GetFileName)), own != null);
            }

            // A row's button may have focus (it was just pressed): the rows are rebuilt, so focus goes to the same button in
            // the new rows, else Add. Left to itself it fell to the page's first field, and the page scrolled up there.
            var focused = rows.XamlRoot != null && FocusManager.GetFocusedElement(rows.XamlRoot) is Button { } button && IsWithin(button, rows)
                ? AutomationProperties.GetName(button)
                : null;
            rows.Children.Clear();
            if (!_config.Global.LowLatencyDrawing)
            {
                var off = ErrorText();
                off.Inlines.Add(new Run { Text = "They need low-latency drawing, which is off: " });
                var link = new Hyperlink();
                link.Inlines.Add(new Run { Text = "Terminal > Troubleshooting" });
                link.Click += (_, _) => GoTo("terminal", null, "window.low-latency-drawing");
                off.Inlines.Add(link);
                off.Inlines.Add(new Run { Text = "." });
                off.Visibility = Visibility.Visible;
                rows.Children.Add(off);
            }

            if (files.Count == 0)
            {
                var none = DescriptionText(profile != null && own != null ? "None in this profile." : "None yet. ");
                if (profile == null)
                {
                    var find = new Hyperlink { NavigateUri = new Uri(ShaderCollection) };
                    find.Inlines.Add(new Run { Text = "Ghostty shaders on GitHub" });
                    none.Inlines.Add(new Run { Text = "Collections to start from: " });
                    none.Inlines.Add(find);
                    none.Visibility = Visibility.Visible;
                }

                rows.Children.Add(none);
            }

            var current = ++load;
            for (var i = 0; i < files.Count; i++)
            {
                rows.Children.Add(ShaderRow(files, i, Save, () => current == load));
            }

            if (focused != null)
            {
                var again = rows.Children.OfType<Grid>().SelectMany(r => r.Children.OfType<StackPanel>()).SelectMany(t => t.Children.OfType<Button>())
                    .FirstOrDefault(b => b.IsEnabled && AutomationProperties.GetName(b) == focused);
                (again ?? (Control)add).Focus(FocusState.Programmatic);
            }
        }

        add.Click += async (_, _) =>
        {
            // Code after an await resumes as queued work, where an exception would end the app.
            try
            {
                var picked = await PickShaders();
                if (picked.Count > 0)
                {
                    Save([.. Shown(), .. picked.Select(p => ShaderPaths.ForConfig(p, _config.Path))]);
                }
            }
            catch (Exception ex)
            {
                Log.Crash("picking shaders", ex);
                Show(error, $"Couldn't open the file picker: {ex.Message}");
            }
        };

        reset.Click += (_, _) =>
        {
            FocusAfterReset(reset, add);
            Write(path, null);
        };

        _refreshers.Add(Refresh);
        _shaderRows = Refresh;
        WatchShaderFiles();
        Quietly(Refresh);

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new TextBlock { Text = setting.Label, VerticalAlignment = VerticalAlignment.Center }, reset } };
        text.Children.Add(title);
        text.Children.Add(description);
        text.Children.Add(KeyText(profile == null ? setting.Key : $"profile.{profile}.{setting.Key}"));
        text.Children.Add(error);
        var card = CardBorder(new StackPanel { Children = { TextAndControl(text, add), rows } });
        _cards[(profile, setting.Key)] = (card, add);
        return card;
    }

    /// <summary>One file of the list: its state, name and path, and Up, Down, Open, Remove.</summary>
    private FrameworkElement ShaderRow(IReadOnlyList<string> files, int index, Action<IReadOnlyList<string>> save, Func<bool> stillShown)
    {
        var written = files[index];
        var full = ShaderPaths.Resolve(written, _config.Path);
        var name = Path.GetFileName(full);
        var icon = new FontIcon { Glyph = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0), Opacity = 0.6 };
        var state = DescriptionText("Checking…");
        var text = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = written, FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis },
                state,
            },
        };

        Button Tool(string glyph, string label, bool enabled, Action run)
        {
            var button = new Button
            {
                Content = new FontIcon { Glyph = glyph, FontSize = 12 },
                Padding = new Thickness(8, 6, 8, 6),
                IsEnabled = enabled,
                Opacity = enabled ? 1 : 0, // a disabled button draws a grey box; this keeps its place instead
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, $"{label}: {name}");
            button.Click += (_, _) => run();
            return button;
        }

        List<string> Moved(int to)
        {
            var list = files.ToList();
            list.RemoveAt(index);
            list.Insert(to, written);
            return list;
        }

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                Tool("", "Move up", index > 0, () => save(Moved(index - 1))),
                Tool("", "Move down", index < files.Count - 1, () => save(Moved(index + 1))),
                Tool("", "Open in an editor", true, () => OpenShader(full)),
                Tool("", "Remove", true, () => save([.. files.Where((_, i) => i != index)])),
            },
        };

        var grid = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(0, 6, 0, 6),
            ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } },
            Children = { icon, text, tools },
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(tools, 2);
        ShowShaderState(full, icon, state, stillShown);
        return grid;
    }

    private static bool IsWithin(DependencyObject element, DependencyObject container)
    {
        for (var e = element; e != null; e = VisualTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, container))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the file compiles (the same check, and cache, the terminals use), shown on its row.</summary>
    private async void ShowShaderState(string full, FontIcon icon, TextBlock state, Func<bool> stillShown)
    {
        try
        {
            var loaded = await ShaderLoader.Load([full]);
            if (!stillShown())
            {
                return;
            }

            if (loaded.Errors.Count == 0)
            {
                icon.Glyph = ""; // check mark
                icon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                icon.Opacity = 1;
                SetText(state, loaded.Shaders[0].UsesTime ? "Compiles. Moves on its own." : "Compiles.");
            }
            else
            {
                icon.Glyph = ""; // warning
                icon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                icon.Opacity = 1;
                state.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                // The message names the file first ("crt.glsl, line 3: ..."); the row already does.
                var message = loaded.Errors[0];
                var name = Path.GetFileName(full);
                SetText(state, message.StartsWith(name + ", ") ? char.ToUpper(message[name.Length + 2]) + message[(name.Length + 3)..]
                    : message.StartsWith(name + ": ") ? char.ToUpper(message[name.Length + 2]) + message[(name.Length + 3)..] : message);
            }
        }
        catch (Exception ex)
        {
            Log.Crash("checking a shader", ex);
        }
    }

    /// <summary>Saving a shader file updates its row (the terminals reload it on their own).</summary>
    private void WatchShaderFiles()
    {
        if (_shaderWatch)
        {
            return;
        }

        _shaderWatch = true;
        void Changed(string _) => DispatcherQueue.SafeEnqueue(() => _shaderRows?.Invoke());
        ShaderLoader.FileChanged += Changed;
        Closed += (_, _) => ShaderLoader.FileChanged -= Changed;
    }

    private async Task<IReadOnlyList<string>> PickShaders()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".glsl");
        picker.FileTypeFilter.Add(".frag");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return [.. (await picker.PickMultipleFilesAsync()).Select(f => f.Path)];
    }

    /// <summary>In the app .glsl files open with, else Notepad (most PCs have none for them).</summary>
    private static void OpenShader(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception)
        {
            Log.Write($"shader: no app for {Path.GetExtension(file)} ({ex.Message}); using notepad");
            Process.Start("notepad.exe", $"\"{file}\"");
        }
    }
}
