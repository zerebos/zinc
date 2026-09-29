using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Phosphor.Session;
using Windows.Graphics;

namespace Phosphor;

/// <summary>
/// Sizes and places windows in DIPs (AppWindow works in physical pixels, so a size meant for 100%
/// came out two-thirds as big at 150%), kept within the screen's work area.
/// </summary>
internal static partial class WindowPlacement
{
    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    /// <summary>Physical pixels per DIP for <paramref name="window"/>'s display.</summary>
    public static double Scale(Window window) =>
        GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) is var dpi and > 0 ? dpi / 96.0 : 1;

    /// <summary>Sizes <paramref name="window"/> to <paramref name="width"/> × <paramref name="height"/> DIPs (at most 90% of its screen).</summary>
    public static void Resize(Window window, double width, double height)
    {
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = Scale(window);
        window.AppWindow.Resize(new SizeInt32(Fit(width * scale, area.Width), Fit(height * scale, area.Height)));
    }

    /// <summary>Sizes <paramref name="window"/> in DIPs and centers it over <paramref name="owner"/>, on the owner's screen.</summary>
    public static void CenterOver(Window window, Window owner, double width, double height)
    {
        var over = owner.AppWindow;
        var area = DisplayArea.GetFromWindowId(over.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = Scale(owner);
        int w = Fit(width * scale, area.Width), h = Fit(height * scale, area.Height);
        var x = Math.Clamp(over.Position.X + (over.Size.Width - w) / 2, area.X, area.X + area.Width - w);
        var y = Math.Clamp(over.Position.Y + (over.Size.Height - h) / 2, area.Y, area.Y + area.Height - h);
        window.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    /// <summary>Puts <paramref name="window"/> back where it was saved, if that's still on a screen.</summary>
    public static bool Restore(Window window, WindowBounds? saved)
    {
        if (saved is not { Width: > 0, Height: > 0 })
        {
            return false;
        }

        var rect = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
        if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is null)
        {
            return false; // that screen is gone
        }

        window.AppWindow.MoveAndResize(rect);
        return true;
    }

    /// <summary>The window's place while it's neither minimized nor maximized, else null.</summary>
    public static WindowBounds? Bounds(AppWindow window) =>
        window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored }
            ? new WindowBounds(window.Position.X, window.Position.Y, window.Size.Width, window.Size.Height)
            : null;

    private static int Fit(double size, int available) => (int)Math.Round(Math.Min(size, available * 0.9));
}
