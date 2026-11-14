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

// Custom shaders (`[shaders] files`) get a card that sums the list up and opens the shaders page
// (SettingsWindow.ShadersPage.cs).
internal sealed partial class SettingsWindow
{
    // The action that refreshes the rows on the shaders page when one of its files is saved, and whether the window
    // listens for those saves.
    private Action? _shaderRows;
    private bool _shaderWatch;

    // The shader list of the global settings (null) or of a profile, as config.toml has it. The members below give its
    // own list, what it inherits and what's in force, and `SaveShaders` writes it. A null own list means that a profile
    // inherits, or globally that there are no shaders.
    private IReadOnlyList<string>? OwnShaders(string? profile) => profile == null
        ? _config.ValueOf("shaders.files") is { } global ? ShaderPaths.Listed(global) : null
        : _config.ValueOf(profile, "shaders.files") is { } own ? ShaderPaths.Listed(own) : null;

    private IReadOnlyList<string> InheritedShaders() => ShaderPaths.Listed(_config.ValueOf("shaders.files"));

    private IReadOnlyList<string> ShownShaders(string? profile) => OwnShaders(profile) ?? (profile == null ? [] : InheritedShaders());

    private IReadOnlyList<string> ShownShaderFiles(string? profile) => [.. ShownShaders(profile).Select(w => ShaderPaths.Resolve(w, _config.Path))];

    /// <summary>
    /// Writes the list. Globally an empty list is the default, so nothing is written, and a profile's empty list means
    /// "none here".
    /// </summary>
    private bool SaveShaders(string? profile, IReadOnlyList<string> files, TextBlock? error)
    {
        if (_config.HasErrors)
        {
            if (error != null)
            {
                Show(error, "config.toml has an error. Fix it first. The error is shown at the top of this page.");
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
    /// The shader list as a card, which shows which shaders run (and whether one doesn't compile) and has Edit shaders,
    /// which opens their own page (the list, the library to choose from, folders). In a profile the card shows the
    /// inherited list until it's changed there, and the reset arrow makes the profile inherit again.
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
                ? "Shaders need \"Low-latency drawing\", which is off in Terminal > Troubleshooting."
                : "");
            if (files.Count > 0 && _config.Global.LowLatencyDrawing)
            {
                CheckAll(ShownShaderFiles(profile), ++check);
            }
        }

        // Checks whether they all compile, with the same check and cache the terminals use. The card says so when one
        // doesn't.
        async void CheckAll(IReadOnlyList<string> full, int current)
        {
            try
            {
                var loaded = await ShaderLoader.Load(full);
                if (current == check && loaded.Errors.Count > 0)
                {
                    SetText(state, loaded.Errors.Count == 1 ? $"One shader doesn't compile: {loaded.Errors[0]}" : $"{loaded.Errors.Count} shaders don't compile. \"Edit shaders\" shows why.");
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
        // One of Zinc's shows its name, and what it does where a path would be.
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
                Opacity = enabled ? 1 : 0, // a disabled button draws a gray box, so it's made invisible, which keeps its place
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
                var message = loaded.Errors[0];
                // The message names the shader first ("crt.glsl, line 3: ...", "nope: no shader..."). The row already
                // shows the name, so it's cut from the message.
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
        [.. _config.Global.ShaderFolders.Concat(_addedShaderFolders).Distinct(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>
    /// Opens the file in the app .glsl files open with, or else in Notepad, because most PCs have no app for them.
    /// </summary>
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
