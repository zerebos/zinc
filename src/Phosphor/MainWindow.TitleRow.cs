using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Phosphor.Input;
using Phosphor.Terminal;
using Windows.Foundation;
using Windows.Graphics;

namespace Phosphor;

// The title bar row in vertical-tabs mode: the app's name over the sidebar and, in the middle, a
// command center showing what the focused tab is doing; clicking it opens the command palette.
// The row is the window's drag area (caption), so the command center is registered as a
// click-through region. See .scratch/NOTES.md "Chrome plan" for where this is heading.
public sealed partial class MainWindow
{
    private TextBlock? _commandContext;
    private TextBlock? _commandShortcut;

    private FrameworkElement BuildTitleRow()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WorkspaceList.DefaultWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // the window buttons

        var mark = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"))),
                    Width = 16,
                    Height = 16,
                },
                new TextBlock { Text = "Phosphor", FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        row.Children.Add(mark);

        _commandContext = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _commandShortcut = new TextBlock { FontSize = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new FontIcon { Glyph = "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_commandContext, 1);
        content.Children.Add(_commandContext);
        Grid.SetColumn(_commandShortcut, 2);
        content.Children.Add(_commandShortcut);

        var center = new Button
        {
            Height = 24,
            MinWidth = 280,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            IsTabStop = false,
            Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Content = content,
        };
        AutomationProperties.SetName(center, "Search commands");
        center.Click += (_, _) => OpenCommandPalette();
        Grid.SetColumn(center, 1);
        row.Children.Add(center);

        KeepClickThrough(center);
        UpdateCommandCenter();
        return row;
    }

    /// <summary>What the focused tab of the shown workspace is doing: its title, and the folder its shell reported.</summary>
    private void UpdateCommandCenter()
    {
        if (_commandContext == null || _commandShortcut == null)
        {
            return;
        }

        var text = "";
        if (SelectedTab is { } tab)
        {
            var pane = tab.Active;
            var title = string.IsNullOrWhiteSpace(pane.Control.Title) ? pane.Profile.Name : pane.Control.Title;
            var folder = ShellDirectory.Parse(pane.Control.ReportedDirectory);
            text = folder != null && !title.Contains(folder, StringComparison.OrdinalIgnoreCase) ? $"{title}  ·  {folder}" : title;
        }

        if (_commandContext.Text != text)
        {
            _commandContext.Text = text;
            ToolTipService.SetToolTip(_commandContext, text);
        }

        _commandShortcut.Text = _keybindings.ChordFor(TerminalAction.OpenCommandPalette)?.ToString() ?? "";
    }

    /// <summary>
    /// Lets an element in the title row take clicks: the row is caption (it drags the window),
    /// so the element is registered as a click-through region, kept in step with layout.
    /// </summary>
    private void KeepClickThrough(FrameworkElement element)
    {
        RectInt32? set = null;
        void Update()
        {
            if (element.XamlRoot is not { } root || element.ActualWidth <= 0)
            {
                return;
            }

            var scale = root.RasterizationScale;
            var r = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var rect = new RectInt32((int)(r.X * scale), (int)(r.Y * scale), (int)Math.Ceiling(r.Width * scale), (int)Math.Ceiling(r.Height * scale));
            // LayoutUpdated fires for every layout pass in the window: only tell Windows about changes.
            if (set is { } previous && previous.Equals(rect))
            {
                return;
            }

            set = rect;
            InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
        }

        element.SizeChanged += (_, _) => Update();
        element.LayoutUpdated += (_, _) => Update();
    }
}
