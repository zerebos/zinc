using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Zinc.Terminal;

/// <summary>
/// Zooming (keys, the palette, Ctrl+wheel): the font size changes for this pane, and a badge in
/// its middle shows the zoom against the configured size, fading out a moment after the last step.
/// </summary>
public sealed partial class TerminalControl
{
    private Border? _zoomBadge;
    private TextBlock? _zoomPercent;
    private TextBlock? _zoomPoints;

    // Held in a field: a timer only a local references is collected and stops firing.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _zoomTimer;

    /// <summary>This pane's zoom (<see cref="ZoomSteps"/>: 0 = 100%, each step 10% of the configured size).</summary>
    private int _zoomLevel;

    /// <summary>The configured font size (DIPs), as the pane's size is clamped.</summary>
    private float ConfiguredFontSize => Math.Clamp(_config.FontSizeDip, MinFontSize, MaxFontSize);

    /// <summary>The font size for the current zoom level (after a config change too: the zoom stays, the base moves).</summary>
    private float ZoomedFontSize()
    {
        _zoomLevel = ZoomSteps.Clamp(_zoomLevel, ConfiguredFontSize, MinFontSize, MaxFontSize);
        return ZoomSteps.Size(ConfiguredFontSize, _zoomLevel);
    }

    private void Zoom(int level)
    {
        _zoomLevel = level;
        SetFontSize(ZoomedFontSize());
        ShowZoom();
    }

    private void ShowZoom()
    {
        if (_zoomBadge == null)
        {
            _zoomPercent = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _zoomPoints = new TextBlock { FontSize = 12, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center };
            _zoomBadge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(20, 10, 20, 12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"],
                IsHitTestVisible = false,
                Opacity = 0,
                OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) },
                Child = new StackPanel { Children = { _zoomPercent, _zoomPoints } },
            };
            _root.Children.Add(_zoomBadge);

            _zoomTimer = DispatcherQueue.CreateTimer();
            _zoomTimer.Interval = TimeSpan.FromMilliseconds(1200);
            _zoomTimer.IsRepeating = false;
            _zoomTimer.OnTick(_ => _zoomBadge.Opacity = 0);
        }

        var size = _renderer.FontSize;
        _zoomPercent!.Text = $"{ZoomSteps.Percent(_zoomLevel)}%";
        _zoomPoints!.Text = $"{(size * 72f / 96f).ToString("0.#", CultureInfo.CurrentCulture)} pt";
        _zoomBadge.Opacity = 1;
        _zoomTimer!.Stop();
        _zoomTimer.Start();
        Log.Write($"zoom: {_zoomPercent.Text} ({_zoomPoints.Text})");
    }
}
