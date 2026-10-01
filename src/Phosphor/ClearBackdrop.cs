using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Phosphor;

/// <summary>
/// <c>backdrop = none</c>: a window backdrop that's a fully transparent brush, so the windows behind
/// show as they are (WinUI ships Mica and Acrylic only). The brush must come from the system
/// compositor (Windows.UI.Composition), which needs a Windows.System dispatcher queue on this
/// thread: WinUI's own (Microsoft.UI.Dispatching) isn't one, so one is made the first time.
/// </summary>
internal sealed class ClearBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? _compositor;
    private static object? _queueController;
    private Windows.UI.Composition.CompositionColorBrush? _brush;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        try
        {
            EnsureDispatcherQueue();
            _compositor ??= new Windows.UI.Composition.Compositor();
            _brush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
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
        base.OnTargetDisconnected(disconnectedTarget);
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

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, [MarshalAs(UnmanagedType.IUnknown)] out object controller);
}
