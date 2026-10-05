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

// Custom shaders ([shaders] files): a card that sums the list up and opens its page (SettingsWindow.ShadersPage.cs).
internal sealed partial class SettingsWindow
{
    private const string ShaderCollection = "https://github.com/0xhckr/ghostty-shaders";

    // The rows on the shaders page now, refreshed when one of its files is saved; and whether the window listens for that.
    private Action? _shaderRows;
    private bool _shaderWatch;

    // The shader list of the global settings (null) or of a profile, as config.toml has it: its own (null: inherits,
    // in a profile; none, globally), what it inherits, what's in force; and writing it.
    private IReadOnlyList<string>? OwnShaders(string? profile) => profile == null
        ? _config.ValueOf("shaders.files") is { } global ? ShaderPaths.Listed(global) : null
        : _config.ValueOf(profile, "shaders.files") is { } own ? ShaderPaths.Listed(own) : null;

    private IReadOnlyList<string> InheritedShaders() => ShaderPaths.Listed(_config.ValueOf("shaders.files"));

    private IReadOnlyList<string> ShownShaders(string? profile) => OwnShaders(profile) ?? (profile == null ? [] : InheritedShaders());

    private IReadOnlyList<string> ShownShaderFiles(string? profile) => [.. ShownShaders(profile).Select(w => ShaderPaths.Resolve(w, _config.Path))];

    /// <summary>Writes the list (globally an empty one is the default: nothing written; a profile's means "none here").</summary>
    private bool SaveShaders(string? profile, IReadOnlyList<string> files, TextBlock? error)
    {
        if (_config.HasErrors)
        {
            if (error != null)
            {
                Show(error, "config.toml has an error; fix it first (see the top of the page).");
            }

            return false;
        }

        if (error != null)
        {
            error.Visibility = Visibility.Collapsed;
        }

        IReadOnlyList<string> path = profile == null ? ["shaders", "files"] : ["profile", profile, "shaders", "files"];
        Write(path, profile == null && files.Count == 0 ? null : files.ToList());
        return true;
    }

    /// <summary>
    /// The shader list as a card: which shaders run (and whether one doesn't compile), and Edit shaders, which opens
    /// their own page (the list, the library to choose from, folders). In a profile: the inherited list until changed
    /// there; the reset arrow inherits again.
    /// </summary>
    private Border ShaderFilesCard(SettingDef setting, string? profile)
    {
        var reset = ResetArrow(setting.Label, profile == null ? "Back to the default: no shaders" : "Back to the shaders every profile inherits");
        var description = DescriptionText();
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var state = DescriptionText();
        var edit = new Button { Content = "Edit shaders" };
        AutomationProperties.SetName(edit, profile == null ? "Edit shaders" : $"Edit shaders of {profile}");
        edit.Click += (_, _) => OpenShaders(profile);
        var check = 0;

        void Refresh()
        {
            var own = OwnShaders(profile);
            var files = ShownShaders(profile);
            reset.Visibility = (profile == null ? files.Count > 0 : own != null) ? Visibility.Visible : Visibility.Collapsed;
            if (profile == null)
            {
                SetText(description, setting.Description);
            }
            else
            {
                var inherited = InheritedShaders();
                InheritedText(description, setting, inherited.Count == 0 ? "none" : string.Join(", ", inherited.Select(ShaderName)), own != null);
            }

            summary.Text = files.Count == 0 ? "None" : string.Join(", ", files.Select(ShaderName));
            SetText(state, !_config.Global.LowLatencyDrawing && files.Count > 0
                ? "Shaders need \"Low-latency drawing\", which is off (Terminal > Troubleshooting)."
                : "");
            if (files.Count > 0 && _config.Global.LowLatencyDrawing)
            {
                CheckAll(ShownShaderFiles(profile), ++check);
            }
        }

        // Whether they all compile (the terminals' check and cache): the card says so when one doesn't.
        async void CheckAll(IReadOnlyList<string> full, int current)
        {
            try
            {
                var loaded = await ShaderLoader.Load(full);
                if (current == check && loaded.Errors.Count > 0)
                {
                    SetText(state, loaded.Errors.Count == 1 ? $"One doesn't compile: {loaded.Errors[0]}" : $"{loaded.Errors.Count} don't compile; \"Edit shaders\" shows why.");
                    state.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                }
            }
            catch (Exception ex)
            {
                Log.Crash("checking shaders", ex);
            }
        }

        reset.Click += (_, _) =>
        {
            FocusAfterReset(reset, edit);
            Write(profile == null ? setting.Path : ["profile", profile, .. setting.Path], null);
        };

        _refreshers.Add(Refresh);
        Quietly(Refresh);

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new TextBlock { Text = setting.Label, VerticalAlignment = VerticalAlignment.Center }, reset } };
        text.Children.Add(title);
        text.Children.Add(description);
        text.Children.Add(summary);
        text.Children.Add(state);
        text.Children.Add(KeyText(profile == null ? setting.Key : $"profile.{profile}.{setting.Key}"));
        var card = CardBorder(TextAndControl(text, edit));
        _cards[(profile, setting.Key)] = (card, edit);
        return card;
    }

    /// <summary>How a list entry reads: Zinc's and yours by name, a file by its name.</summary>
    private string ShaderName(string written) =>
        ShaderPaths.IsName(written) ? written.Trim() : Path.GetFileNameWithoutExtension(ShaderPaths.Resolve(written, _config.Path));

    /// <summary>One file of the list: its state, name and path, and Up, Down, Open, Remove.</summary>
    private FrameworkElement ShaderRow(IReadOnlyList<string> files, int index, Action<IReadOnlyList<string>> save, Func<bool> stillShown)
    {
        var written = files[index];
        var full = ShaderPaths.Resolve(written, _config.Path);
        // One of Zinc's: its name, and what it does where a path would be.
        var bundled = ShaderPaths.IsBundled(full) && File.Exists(full);
        var name = bundled ? Path.GetFileNameWithoutExtension(full) : Path.GetFileName(full);
        var detail = bundled
            ? "Comes with Zinc. " + ShaderPaths.Bundled().FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Description
            : written;
        var icon = new FontIcon { Glyph = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0), Opacity = 0.6 };
        var state = DescriptionText("Checking…");
        var text = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = detail, FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis },
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
                Tool("", "Open in editor", true, () => OpenShader(full)),
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
                SetText(state, loaded.Shaders[0].UsesTime ? "Compiles. Animated." : "Compiles.");
                ToolTipService.SetToolTip(state, loaded.Shaders[0].UsesTime ? "Keeps redrawing while the terminal is idle. The \"Animation\" setting decides when." : null);
            }
            else
            {
                icon.Glyph = ""; // warning
                icon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                icon.Opacity = 1;
                state.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                // The message names the file first ("crt.glsl, line 3: ..."); the row already does.
                var message = loaded.Errors[0];
                // The message names the shader first ("crt.glsl, line 3: ...", "nope: no shader..."); the row already does.
                foreach (var name in new[] { Path.GetFileName(full), Path.GetFileNameWithoutExtension(full) })
                {
                    if ((message.StartsWith(name + ", ") || message.StartsWith(name + ": ")) && message.Length > name.Length + 2)
                    {
                        message = char.ToUpper(message[name.Length + 2]) + message[(name.Length + 3)..];
                        break;
                    }
                }

                SetText(state, message);
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
        var closed = false;
        void Changed(string _) => DispatcherQueue.SafeEnqueue(() =>
        {
            if (!closed)
            {
                _shaderRows?.Invoke();
            }
        });
        ShaderLoader.FileChanged += Changed;
        Closed += (_, _) =>
        {
            closed = true; // a notice already queued, or a row's check still running, finds the window gone
            _shaderRows = null;
            ShaderLoader.FileChanged -= Changed;
        };
    }

    // Folders added since config.toml was last read (the library lists them at once).
    private readonly List<string> _addedShaderFolders = [];

    /// <summary>The folders the library lists ([shaders] folders, with any just added or taken out), full paths.</summary>
    private IReadOnlyList<string> ShaderFolders() =>
        [.. _config.Global.ShaderFolders.Split('\n', StringSplitOptions.RemoveEmptyEntries).Concat(_addedShaderFolders).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !_hiddenShaderFolders.Contains(f, StringComparer.OrdinalIgnoreCase))];

    private IReadOnlyList<string> WrittenShaderFolders() => ShaderPaths.Listed(_config.ValueOf("shaders.folders"));

    private void WriteShaderFolders(IReadOnlyList<string> written) => Write(["shaders", "folders"], written.Count == 0 ? null : written.ToList());

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
