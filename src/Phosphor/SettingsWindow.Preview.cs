using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Phosphor.Terminal;

namespace Phosphor;

/// <summary>
/// The live preview: a small terminal pinned above the Appearance page and each profile's page,
/// showing a canned session (a Powerline prompt, a diff, the palette, box drawing) in the settings
/// in force there. It follows each change as the config is read again.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>What the preview shows: prompt segments, bold, the 16 colors, built-in glyphs, the cursor.</summary>
    private const string PreviewScript =
        "\u001b[44;30m ~/src/phosphor \u001b[0;34m\u001b[0m git diff --stat\r\n" +
        " app.cs | 4 \u001b[32m++\u001b[31m--\u001b[0m\r\n" +
        "\u001b[1mdiff --git a/app.cs b/app.cs\u001b[0m\r\n" +
        "\u001b[36m@@ -1 +1 @@\u001b[0m\r\n" +
        "\u001b[31m-  Console.WriteLine(\"hello\");\u001b[0m\r\n" +
        "\u001b[32m+  Console.WriteLine(\"hello, Phosphor\");\u001b[0m\r\n" +
        "\u001b[30m██\u001b[31m██\u001b[32m██\u001b[33m██\u001b[34m██\u001b[35m██\u001b[36m██\u001b[37m██" +
        "\u001b[90m██\u001b[91m██\u001b[92m██\u001b[93m██\u001b[94m██\u001b[95m██\u001b[96m██\u001b[97m██\u001b[0m" +
        "  \u001b[35m╭─ build ─╮\u001b[0m \u001b[33m▁▂▃▅▇█\u001b[0m\r\n" +
        "\u001b[44;30m ~/src/phosphor \u001b[0;34m\u001b[0m ";

    private const double PreviewHeight = 172;

    /// <summary>A page area shorter than this has no room for the preview (it would leave little for the settings).</summary>
    private const double PreviewRoom = 560;

    /// <summary>Pinned above the scrolling page: the page's heading (and back link), then the preview.</summary>
    private readonly StackPanel _pinned = new() { MaxWidth = 960, Padding = new Thickness(40, 12, 40, 4) };

    private TerminalControl? _preview;
    private Border? _previewFrame;

    /// <summary>The settings the preview shows on this page, or null for no preview.</summary>
    private Config? PreviewConfig() =>
        _search.Text.Trim().Length > 0 ? null
        : _section == "appearance" ? _config.Global
        : _section == "profiles" && _editing is { } id ? _config.FindProfile(id)?.Config ?? _config.Global
        : null;

    /// <summary>
    /// After a page is built: with a preview, its heading (and anything above it) moves into the
    /// pinned area over the preview, so they stay put while the settings scroll.
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
        }
        else
        {
            _preview.ApplyConfig(config);
        }

        // The heading gets the show/hide button on its right (built with it: headings are per page).
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

    /// <summary>The preview is hidden (the button beside the page heading); remembered with the session.</summary>
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
            Margin = new Thickness(0, 8, 0, 12), // the heading's own margins: centered on its text
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
    /// Shows <paramref name="scheme"/> (null: none of its own, i.e. Phosphor's own or inherited) in
    /// the preview as if written at <paramref name="path"/>: config.toml read again with just that
    /// changed, so colors it sets itself (background, a palette entry) still win, as after saving.
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
            // (Read without looking for shells: a found profile without a section has the global colors.)
            var colors = (_editing is { } id ? edited.FindProfile(id)?.Config ?? edited.Global : edited.Global).Theme;
            _preview.ApplyConfig(shown with { Theme = colors });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write($"settings: preview of {scheme}: {ex.Message}");
        }
    }

    /// <summary>The config was read again: the preview shows what's in force now.</summary>
    private void RefreshPreview()
    {
        if (_preview != null && _pinned.Visibility == Visibility.Visible && PreviewConfig() is { } config)
        {
            _preview.ApplyConfig(config); // (while hidden too: it's current when shown again)
        }
    }
}
