using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Phosphor;

// The sidebar's selection pill, drawn by us. NavigationView's own animation is fixed at 600 ms:
// the old pill stretches toward the new item with a sharp ease-in (it sits still, then leaps),
// its position jumps in one step, and the new one spends 400 ms shrinking back; a click during
// it snaps. This one slides from wherever it is to the selected item, and a click mid-slide just
// turns it toward the new target. NavigationView's pill stays in place, made transparent, and
// ours takes its place and size from it.
internal sealed partial class SettingsWindow
{
    private static readonly TimeSpan IndicatorSlide = TimeSpan.FromMilliseconds(250);

    private readonly Canvas _indicatorLayer = new() { IsHitTestVisible = false };
    private readonly Rectangle _indicator = new() { Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5, Opacity = 0 };
    private Visual? _indicatorVisual;
    private Vector2? _indicatorAt;
    private bool _indicatorSlides;

    private void SetUpIndicator(Grid root)
    {
        _nav.Resources["NavigationViewSelectionIndicatorForeground"] = new SolidColorBrush(Colors.Transparent);
        _indicator.Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        _indicatorLayer.Children.Add(_indicator);
        Grid.SetRow(_indicatorLayer, Grid.GetRow(_nav));
        root.Children.Add(_indicatorLayer);
        ElementCompositionPreview.SetIsTranslationEnabled(_indicator, true);
        _indicatorVisual = ElementCompositionPreview.GetElementVisual(_indicator);

        // A selection slides there; anything else that moves the item (resizing) just follows.
        _nav.SelectionChanged += (_, _) => _indicatorSlides = true;
        _nav.LayoutUpdated += (_, _) => PlaceIndicator();
    }

    private void PlaceIndicator()
    {
        if (_indicatorVisual == null || _nav.SelectedItem is not NavigationViewItem { XamlRoot: not null } item || item.ActualHeight <= 0)
        {
            return;
        }

        // The size (and left edge) of NavigationView's own pill (its template's SelectionIndicator),
        // centered on the item. Not its position: NavigationView's animation still runs on it,
        // invisible, and starts the new item's pill at the old item's place, so reading it
        // mid-animation put ours back on the item it came from.
        var anchor = Descendant(item, "SelectionIndicator") is { ActualHeight: > 0 } found ? found : null;
        Point row, left;
        try
        {
            row = item.TransformToVisual(_indicatorLayer).TransformPoint(new Point(0, 0));
            left = anchor?.TransformToVisual(_indicatorLayer).TransformPoint(new Point(0, 0)) ?? new Point(row.X + 4, 0);
        }
        catch (ArgumentException)
        {
            return; // not in the tree yet
        }

        var (width, height) = anchor != null ? (anchor.ActualWidth, anchor.ActualHeight) : (3.0, 16.0);
        var target = new Vector2((float)left.X, (float)Math.Round(row.Y + (item.ActualHeight - height) / 2));
        var slide = _indicatorSlides && _indicatorAt is { } from && from.X == target.X && _indicator.Opacity > 0;
        _indicatorSlides = false;
        if (_indicatorAt == target)
        {
            return;
        }

        Log.Write($"indicator: {(item.Content as string)} at {target.Y} (from {_indicatorAt?.Y}) slide={slide} anchor={anchor != null}");
        _indicatorAt = target;
        _indicator.Width = width;
        _indicator.Height = height;
        Canvas.SetLeft(_indicator, target.X);
        _indicator.Opacity = 1;
        if (slide && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            // From wherever it is now (mid-slide included) to the new item, easing out.
            var compositor = _indicatorVisual.Compositor;
            var move = compositor.CreateScalarKeyFrameAnimation();
            move.InsertExpressionKeyFrame(0f, "this.StartingValue");
            move.InsertKeyFrame(1f, target.Y, compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
            move.Duration = IndicatorSlide;
            _indicatorVisual.StartAnimation("Translation.Y", move);
        }
        else
        {
            _indicatorVisual.StopAnimation("Translation.Y");
            _indicatorVisual.Properties.InsertVector3("Translation", new Vector3(0, target.Y, 0));
        }
    }

    private static FrameworkElement? Descendant(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } element && n == name)
            {
                return element;
            }

            if (Descendant(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
