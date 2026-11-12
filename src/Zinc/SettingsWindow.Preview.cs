using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// The live preview: a small terminal pinned above the Appearance page and each profile's page,
/// showing a canned session (a Powerline prompt, a diff, the palette, box drawing) in the settings
/// in force there. It follows each change as the config is read again.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>What the preview shows: prompt segments, bold, the 16 colors, built-in glyphs, the cursor.</summary>
    /// <remarks>The prompt's text is bright white, which reads on a blue fill in light schemes as well as dark ones.</remarks>
    private const string PreviewScript =
        "\e[44;97m ~/src/zinc \e[0;34m\e[0m git diff --stat\r\n" +
        " app.cs | 4 \e[32m++\e[31m--\e[0m\r\n" +
        "\e[1mdiff --git a/app.cs b/app.cs\e[0m\r\n" +
        "\e[36m@@ -1 +1 @@\e[0m\r\n" +
        "\e[31m-  Console.WriteLine(\"hello\");\e[0m\r\n" +
        "\e[32m+  Console.WriteLine(\"hello, Zinc\");\e[0m\r\n" +
        "\e[30m██\e[31m██\e[32m██\e[33m██\e[34m██\e[35m██\e[36m██\e[37m██" +
        "\e[90m██\e[91m██\e[92m██\e[93m██\e[94m██\e[95m██\e[96m██\e[97m██\e[0m" +
        "  \e[35m╭─ build ─╮\e[0m \e[33m▁▂▃▅▇█\e[0m\r\n" +
        "\e[44;97m ~/src/zinc \e[0;34m\e[0m ";

    private const double PreviewHeight = 172;

    /// <summary>The tallest the preview grows to fit its script (taller rows, a narrow window).</summary>
    private const double PreviewMaxHeight = 300;

    /// <summary>A page area shorter than this has no room for the preview (it would leave little for the settings).</summary>
    private const double PreviewRoom = 560;

    /// <summary>The area pinned above the scrolling page, which holds the page's heading and then the preview.</summary>
    private readonly StackPanel _pinned = new() { MaxWidth = PageWidth, Padding = new Thickness(40, 12, 40, 4) };

    private TerminalControl? _preview;
    private Border? _previewFrame;

    /// <summary>The settings the preview shows on this page, or null for no preview.</summary>
    private Config? PreviewConfig() =>
        _search.Text.Trim().Length > 0 ? null
        : _editingScheme is { } scheme ? _config.Themes.Apply(_config.Global, scheme) ?? _config.Global
        : _editingShaders ? _shadersProfile is { } shadersOf ? _config.FindProfile(shadersOf)?.Config ?? _config.Global : _config.Global
        : _editingSchemes ? _schemesProfile is { } schemesOf ? _config.FindProfile(schemesOf)?.Config ?? _config.Global : _config.Global
        : _section == "appearance" ? _config.Global
        : _section == "profiles" && _editing is { } id ? _config.FindProfile(id)?.Config ?? _config.Global
        : null;

    /// <summary>
    /// Runs after a page is built. When the page has a preview, the page's heading (and anything above it) moves
    /// into the pinned area over the preview, so they stay put while the settings scroll.
    /// </summary>
    private void PlacePreview(FrameworkElement heading)
    {
        _pinned.Children.Clear();
        if (PreviewConfig() is not { } config)
        {
            _pinned.Visibility = Visibility.Collapsed;
            return;
        }

        var at = _page.Children.IndexOf(heading);
        for (var i = 0; i <= at; i++)
        {
            var element = _page.Children[0];
            _page.Children.RemoveAt(0);
            _pinned.Children.Add(element);
        }

        if (_preview == null)
        {
            _preview = TerminalControl.Preview(config, PreviewScript);
            _previewFrame = new Border
            {
                Child = _preview,
                Height = PreviewHeight,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Margin = new Thickness(0, 0, 0, 8),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_previewFrame, "Preview");
            Closed += (_, _) => _preview.Close();
            // The preview can't scroll, and taller rows or a narrow window make its script need more room. So the frame
            // grows to fit the script (and shrinks back), up to a cap past which the top of the script scrolls out of view.
            _preview.PreviewHeightNeeded += needed =>
            {
                var height = Math.Clamp(Math.Ceiling(needed) + 2, PreviewHeight, PreviewMaxHeight); // + the frame's border
                if (_previewFrame != null && Math.Abs(_previewFrame.Height - height) >= 1)
                {
                    _previewFrame.Height = height;
                }
            };
        }
        else
        {
            _preview.ApplyConfig(config);
        }

        // The heading gets the show/hide button on its right (built with the heading, because headings are per page).
        var headingAt = _pinned.Children.IndexOf(heading);
        _pinned.Children.RemoveAt(headingAt);
        _previewToggle = PreviewToggle();
        _pinned.Children.Insert(headingAt, new Grid { Children = { heading, _previewToggle } });
        _pinned.Children.Add(_previewFrame!);
        _pinned.Visibility = Visibility.Visible;
        FitPreview();
    }

    private bool _previewHidden;
    private Button? _previewToggle;

    /// <summary>
    /// Whether the preview is hidden (by the button beside the page heading), which is remembered with the session.
    /// </summary>
    public bool PreviewHidden
    {
        get => _previewHidden;
        set
        {
            _previewHidden = value;
            FitPreview();
        }
    }

    /// <summary>The preview was hidden or shown again.</summary>
    public event Action<bool>? PreviewHiddenChanged;

    private Button PreviewToggle()
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 4, 10, 4),
            Margin = new Thickness(0, 8, 0, 12), // the heading's own margins, which center the button on the heading's text
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) =>
        {
            PreviewHidden = !PreviewHidden;
            PreviewHiddenChanged?.Invoke(PreviewHidden);
        };
        return button;
    }

    /// <summary>Shows the preview unless it's hidden, and only while the page area has room for it.</summary>
    private void FitPreview()
    {
        if (_previewFrame == null || VisualTreeHelper.GetParent(_pinned) is not FrameworkElement area || area.ActualHeight <= 0)
        {
            return;
        }

        var room = area.ActualHeight >= PreviewRoom;
        _previewFrame.Visibility = room && !_previewHidden ? Visibility.Visible : Visibility.Collapsed;
        if (_previewToggle is { } toggle)
        {
            toggle.Visibility = room ? Visibility.Visible : Visibility.Collapsed; // (nothing to show or hide)
            var label = _previewHidden ? "Show preview" : "Hide preview";
            toggle.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = ((char)(_previewHidden ? 0xE7B3 : 0xED1A)).ToString(), FontSize = 14 },
                    new TextBlock { Text = label },
                },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
        }
    }

    /// <summary>
    /// Shows <paramref name="scheme"/> in the preview as if it were written at <paramref name="path"/>. A null scheme
    /// means none of its own, which is Zinc's own or the inherited one. config.toml is read again with just that
    /// value changed, so colors the config sets itself (background, a palette entry) still win, as they do after saving.
    /// </summary>
    private void PreviewScheme(IReadOnlyList<string> path, string? scheme)
    {
        if (_preview == null || _pinned.Visibility != Visibility.Visible || PreviewConfig() is not { } shown)
        {
            return;
        }

        try
        {
            var text = File.Exists(_config.Path) ? File.ReadAllText(_config.Path) : "";
            var edited = ConfigFile.Parse(scheme == null ? ConfigEditor.Remove(text, path) : ConfigEditor.Set(text, path, scheme), _config.Path);
            // The edited text is read without looking for shells, so a discovered profile that has no section isn't
            // in it and gets the global colors.
            var colors = (_editing is { } id ? edited.FindProfile(id)?.Config ?? edited.Global : edited.Global).Theme;
            _preview.ApplyConfig(shown with { Theme = colors });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write($"settings: preview of {scheme}: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes the preview run <paramref name="files"/> (full paths) as its shaders, for a shader previewed on the shaders
    /// page.
    /// </summary>
    private void PreviewShaders(IReadOnlyList<string> files)
    {
        if (_preview != null && _pinned.Visibility == Visibility.Visible && PreviewConfig() is { } shown)
        {
            _preview.ApplyConfig(shown with { ShaderFiles = new(files) });
        }
    }

    // The preview is shown on the shaders and color schemes pages even though the user hid it, because previewing from
    // the library would show nothing otherwise.
    private bool _previewShownForPicker;

    private void PreviewForLibrary(bool open)
    {
        if (open && _previewHidden)
        {
            _previewShownForPicker = true;
            _previewHidden = false;
            FitPreview();
        }
        else if (!open && _previewShownForPicker)
        {
            _previewShownForPicker = false;
            _previewHidden = true;
            FitPreview();
        }
    }

    /// <summary>Called when the config was read again, so the preview shows what's in force now.</summary>
    private void RefreshPreview()
    {
        if (_preview != null && _pinned.Visibility == Visibility.Visible && PreviewConfig() is { } config)
        {
            _preview.ApplyConfig(config); // while hidden too, so the preview is current when shown again
        }
    }
}
