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

        // Where NavigationView would draw its pill (its template's SelectionIndicator), else centered at the item's left.
        var anchor = Descendant(item, "SelectionIndicator");
        Point at;
        try
        {
            at = (anchor ?? item).TransformToVisual(_indicatorLayer).TransformPoint(new Point(0, 0));
        }
        catch (ArgumentException)
        {
            return; // not in the tree yet
        }

        var (width, height) = anchor is { ActualHeight: > 0 } ? (anchor.ActualWidth, anchor.ActualHeight) : (3.0, 16.0);
        var target = anchor is { ActualHeight: > 0 }
            ? new Vector2((float)at.X, (float)at.Y)
            : new Vector2((float)at.X + 4, (float)(at.Y + (item.ActualHeight - height) / 2));
        var slide = _indicatorSlides && _indicatorAt is { } from && from.X == target.X && _indicator.Opacity > 0;
        _indicatorSlides = false;
        if (_indicatorAt == target)
        {
            return;
        }

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
