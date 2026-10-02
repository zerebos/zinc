using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Zinc;

/// <summary>
/// <c>backdrop = none</c> ("Clear glass"): a window backdrop that's a see-through color brush (the tint,
/// at <c>backdrop-tint</c>'s opacity), so the windows behind show as they are (WinUI ships Mica and
/// Acrylic only). A transparent brush alone shows black: the window must also be made see-through,
/// as WinUIEx's TransparentTintBackdrop does it: DWM "blur behind" with an empty region (per-pixel
/// alpha, nothing blurred), the frame extended by nothing, and the window's own background erased to
/// black (transparent once blur-behind is on); set up again when DWM's composition changes.
/// The brush comes from the system compositor (Windows.UI.Composition), which needs a Windows.System
/// dispatcher queue on this thread: WinUI's own (Microsoft.UI.Dispatching) isn't one, so one is made.
/// </summary>
internal sealed unsafe partial class ClearBackdrop(Windows.UI.Color tint) : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? _compositor;
    private static object? _queueController;
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private nint _hwnd;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        try
        {
            _hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
            if (_hwnd != 0)
            {
                SeeThrough(_hwnd, true);
                SetWindowSubclass(_hwnd, &SubclassProc, SubclassId, 0);
            }

            EnsureDispatcherQueue();
            _compositor ??= new Windows.UI.Composition.Compositor();
            _brush = _compositor.CreateColorBrush(tint); // see-through as its alpha says (backdrop-tint)
            connectedTarget.SystemBackdrop = _brush;
        }
        catch (Exception ex)
        {
            Log.Write($"backdrop: couldn't make it clear: {ex.Message}");
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
        _brush?.Dispose();
        _brush = null;
        if (_hwnd != 0)
        {
            RemoveWindowSubclass(_hwnd, &SubclassProc, SubclassId);
            SeeThrough(_hwnd, false);
            _hwnd = 0;
        }

        base.OnTargetDisconnected(disconnectedTarget);
    }

    /// <summary>Per-pixel transparency for <paramref name="hwnd"/> (or back to an opaque window).</summary>
    private static void SeeThrough(nint hwnd, bool on)
    {
        var margins = default(Margins);
        DwmExtendFrameIntoClientArea(hwnd, &margins);
        var region = on ? CreateRectRgn(-2, -2, -1, -1) : 0; // off-screen: nothing is blurred
        var blur = new BlurBehind { Flags = on ? 3u : 1u /* enable (+ region) */, Enable = on ? 1 : 0, Region = region };
        DwmEnableBlurBehindWindow(hwnd, &blur);
        if (region != 0)
        {
            DeleteObject(region);
        }
    }

    private const nuint SubclassId = 0x50484F53; // "PHOS"
    private const uint WmEraseBkgnd = 0x0014, WmDwmCompositionChanged = 0x031E;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (msg == WmEraseBkgnd && GetClientRect(hwnd, out var rect))
            {
                FillRect(wParam, &rect, GetStockObject(4 /* black brush: transparent with blur-behind on */));
                return 1;
            }

            if (msg == WmDwmCompositionChanged)
            {
                SeeThrough(hwnd, true);
            }
        }
        catch (Exception ex)
        {
            Log.Crash("clear backdrop", ex); // a window procedure: nothing above to catch it
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private static void EnsureDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null || _queueController != null)
        {
            return;
        }

        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2 /* current */, ApartmentType = 2 /* STA */ };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var controller));
        _queueController = controller; // kept: the queue lives as long as its controller
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        public int Enable;
        public nint Region;
        public int TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, [MarshalAs(UnmanagedType.IUnknown)] out object controller);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, Margins* margins);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmEnableBlurBehindWindow(nint hwnd, BlurBehind* blur);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial nint GetStockObject(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint hdc, Rect* rect, nint brush);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
