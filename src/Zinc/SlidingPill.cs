using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Zinc;

/// <summary>
/// A sidebar's selection pill that slides from item to item. It's one element on an overlay
/// <see cref="Layer"/>, moved by a composition animation that starts from wherever it is (a new
/// selection mid-slide just turns it). The owner says where the pill goes (<see cref="MoveTo"/>,
/// in the layer's coordinates) and whether the next move is a selection (<see cref="SlideNext"/>).
/// Other moves (items added, reordered, resized) jump, so the pill doesn't glide while things
/// shift under it. Used by the settings window's sidebar and the workspace list.
/// </summary>
internal sealed class SlidingPill
{
    private static readonly TimeSpan Slide = TimeSpan.FromMilliseconds(250);
    private static readonly Windows.UI.ViewManagement.UISettings System = new();

    private readonly Rectangle _pill = new() { Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5, Opacity = 0 };
    private readonly Brush _fill;
    private readonly Visual _visual;
    private Vector2? _at;
    private bool _slideNext;

    public SlidingPill(Brush fill)
    {
        _pill.Fill = _fill = fill;
        Layer.Children.Add(_pill);
        ElementCompositionPreview.SetIsTranslationEnabled(_pill, true);
        _visual = ElementCompositionPreview.GetElementVisual(_pill);
    }

    /// <summary>The overlay the pill is drawn on, which the owner lays over the items in the same coordinates.</summary>
    public Canvas Layer { get; } = new() { IsHitTestVisible = false };

    /// <summary>Keeps the pill visible on what the selected item is filled with (a tab color close to the accent).</summary>
    public void DrawOn(Color background)
    {
        var fill = Contrast.AccentOn(background, _fill);
        if (_pill.Fill != fill)
        {
            _pill.Fill = fill;
        }
    }

    /// <summary>Marks the next move as a selection, so the pill slides to it.</summary>
    public void SlideNext() => _slideNext = true;

    public void Hide()
    {
        _pill.Opacity = 0;
        _at = null;
        _slideNext = false;
    }

    /// <summary>
    /// Puts the pill's top-left at <paramref name="target"/> (<paramref name="width"/> ×
    /// <paramref name="height"/>). It slides there after <see cref="SlideNext"/>, or else moves at once.
    /// Returns whether it moved.
    /// </summary>
    public bool MoveTo(Vector2 target, double width, double height)
    {
        var slide = _slideNext && _at is { } from && from.X == target.X && _pill.Opacity > 0;
        _slideNext = false;
        if (_at == target)
        {
            return false;
        }

        _at = target;
        _pill.Width = width;
        _pill.Height = height;
        Canvas.SetLeft(_pill, target.X);
        _pill.Opacity = 1;
        if (slide && System.AnimationsEnabled)
        {
            // From wherever it is now (mid-slide included) to the new item, easing out.
            var compositor = _visual.Compositor;
            var move = compositor.CreateScalarKeyFrameAnimation();
            move.InsertExpressionKeyFrame(0f, "this.StartingValue");
            move.InsertKeyFrame(1f, target.Y, compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
            move.Duration = Slide;
            _visual.StartAnimation("Translation.Y", move);
        }
        else
        {
            _visual.StopAnimation("Translation.Y");
            _visual.Properties.InsertVector3("Translation", new Vector3(0, target.Y, 0));
        }

        return true;
    }
}
