using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;

namespace Zinc;

// The whole pane is dragged from the header's empty part, which is a grip behind the tabs and buttons, so the grip only
// gets the pointer where nothing else does. A one-tab pane's tab already moves its whole pane. The fold's count doesn't
// drag the pane, because it's a button, which takes the pointer on press, and a folded header still leaves empty room
// beside it. The window decides where the pane can go (MainWindow.Drag.cs).
internal sealed partial class GroupView
{
    private readonly Border _grip = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };

    /// <summary>The pane starts being dragged (the handler fills in the drag's data).</summary>
    public event Action<DragStartingEventArgs>? PaneDragStarting;

    /// <summary>The pane's drag ended, with what the drop target did.</summary>
    public event Action<DataPackageOperation>? PaneDragCompleted;

    private void AddGrip()
    {
        SetColumnSpan(_grip, 5);
        _strip.Children.Add(_grip); // added first, so it's behind everything else in the header
        _grip.CanDrag = true;
        _grip.DragStarting += (_, e) =>
        {
            PaneDragStarting?.Invoke(e);
            ShowDragPicture(e);
        };
        _grip.DropCompleted += (_, e) => PaneDragCompleted?.Invoke(e.DropResult);
    }

    /// <summary>
    /// The drag carries a picture of the header (the grip itself is see-through, so the default picture would be empty).
    /// The picture leaves out the terminal, because its swap chain doesn't render into a bitmap.
    /// </summary>
    private async void ShowDragPicture(DragStartingEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(_strip);
            var pixels = await bitmap.GetPixelsAsync();
            var picture = SoftwareBitmap.CreateCopyFromBuffer(pixels, BitmapPixelFormat.Bgra8, bitmap.PixelWidth, bitmap.PixelHeight,
                BitmapAlphaMode.Premultiplied);
            // Held where it was grabbed (the bitmap is in pixels, the position in DIPs).
            var scale = XamlRoot?.RasterizationScale ?? 1;
            var at = e.GetPosition(_strip);
            e.DragUI.SetContentFromSoftwareBitmap(picture, new Windows.Foundation.Point(at.X * scale, at.Y * scale));
        }
        catch (Exception ex)
        {
            Log.Write($"pane drag picture: {ex.Message}"); // the default picture, then
        }
        finally
        {
            deferral.Complete();
        }
    }
}
