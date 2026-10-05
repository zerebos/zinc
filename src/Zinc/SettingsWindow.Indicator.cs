using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Zinc;

// The sidebar's selection pill, drawn by us (a SlidingPill). NavigationView's own animation is
// fixed at 600 ms: the old pill stretches toward the new item with a sharp ease-in (it sits
// still, then leaps), its position jumps in one step, and the new one spends 400 ms shrinking
// back; a click during it snaps. NavigationView's pill stays in place, made transparent, and
// ours takes its size from it.
internal sealed partial class SettingsWindow
{
    private SlidingPill? _indicator;

    private void SetUpIndicator(Grid root)
    {
        _nav.Resources["NavigationViewSelectionIndicatorForeground"] = new SolidColorBrush(Colors.Transparent);
        _indicator = new SlidingPill((Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]);
        Grid.SetRow(_indicator.Layer, Grid.GetRow(_nav));
        root.Children.Add(_indicator.Layer);

        // A selection slides there; anything else that moves the item (resizing) just follows.
        _nav.SelectionChanged += (_, _) => _indicator.SlideNext();
        _nav.LayoutUpdated += (_, _) => PlaceIndicator();
        // A narrow window hides the sidebar behind the hamburger: the pill goes and comes with it.
        _nav.PaneOpened += (_, _) => PlaceIndicator();
        _nav.PaneClosed += (_, _) => PlaceIndicator();
        _nav.DisplayModeChanged += (_, _) => PlaceIndicator();
    }

    private void PlaceIndicator()
    {
        if (_indicator == null)
        {
            return;
        }

        // No section selected (search results), or the sidebar hidden (a narrow window, hamburger closed): no pill.
        var hidden = _nav.SelectedItem == null || (_nav.DisplayMode == NavigationViewDisplayMode.Minimal && !_nav.IsPaneOpen);
        _indicator.Layer.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        if (hidden)
        {
            return;
        }
        if (_nav.SelectedItem is not NavigationViewItem { XamlRoot: not null } item || item.ActualHeight <= 0)
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
            row = item.TransformToVisual(_indicator.Layer).TransformPoint(new Point(0, 0));
            left = anchor?.TransformToVisual(_indicator.Layer).TransformPoint(new Point(0, 0)) ?? new Point(row.X + 4, 0);
        }
        catch (ArgumentException)
        {
            return; // not in the tree yet
        }

        var (width, height) = anchor != null ? (anchor.ActualWidth, anchor.ActualHeight) : (3.0, 16.0);
        var target = new Vector2((float)left.X, (float)Math.Round(row.Y + (item.ActualHeight - height) / 2));
        if (_indicator.MoveTo(target, width, height))
        {
            Log.Write($"indicator: {item.Content as string} at {target.Y}");
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
