using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Zinc.Terminal;

/// <summary>
/// A pane's drawing surface with its own swap chain (<c>[window] low-latency-drawing</c>, experimental): a frame is
/// drawn and presented as soon as it's asked for, instead of waiting for WinUI's next render tick the way a
/// <see cref="CanvasControl"/> does (a tester's "typing feels sluggish" next to Windows Terminal, which presents from
/// its own swap chain too). At most one frame per refresh: a request right after a present waits for the next
/// tick (<see cref="CompositionTarget.Rendering"/>), so heavy output never queues frames the screen can't show.
///
/// Uses Win2D's shared device, as the canvases do; a lost device brings a new one and new resources (what
/// CanvasControl does on its own). No Loaded/Unloaded counting is involved: none of CanvasLifetime's workarounds apply.
/// </summary>
internal sealed class SwapChainSurface
{
    /// <summary>Whether panes opened from now on use one (<c>[window] low-latency-drawing</c>).</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// The panel. It can't take a Background (WinUI refuses it on a SwapChainPanel), so the pane gives its parent one
    /// for the pointer to hit.
    /// </summary>
    public CanvasSwapChainPanel Panel { get; } = new();

    public CanvasDevice Device { get; private set; }

    public Color ClearColor { get; set; }

    private readonly bool _software;
    private readonly Action<ICanvasResourceCreator> _createResources;
    private readonly Action<CanvasDrawingSession> _draw;
    private readonly Func<float> _scale;
    private readonly Func<double> _sinceOutput;
    private CanvasSwapChain? _chain;
    private bool _resourcesCreated, _drawQueued, _dirty, _presentedThisTick, _onRendering, _closed;
    private int _failures;

    /// <param name="createResources">Device-bound resources (fonts, the atlas): once loaded, and after a lost device.</param>
    /// <param name="draw">Draws a frame.</param>
    /// <param name="scale">The window's scale (RasterizationScale).</param>
    /// <param name="sinceOutput">Milliseconds since the program's output last arrived.</param>
    public SwapChainSurface(bool software, Action<ICanvasResourceCreator> createResources, Action<CanvasDrawingSession> draw, Func<float> scale,
        Func<double> sinceOutput)
    {
        _software = software;
        _createResources = createResources;
        _draw = draw;
        _scale = scale;
        _sinceOutput = sinceOutput;
        Device = CanvasDevice.GetSharedDevice(software);
        Device.DeviceLost += OnDeviceLost;
        Panel.Loaded += (_, _) =>
        {
            if (!_resourcesCreated)
            {
                _resourcesCreated = true;
                Log.Write("pane: swap chain: resources");
                _createResources(Device);
            }

            Resize();
        };
        Panel.SizeChanged += (_, _) => Resize();
    }

    /// <summary>Matches the swap chain to the panel's size and the window's scale (after a move or DPI change too).</summary>
    public void Resize()
    {
        var (width, height, dpi) = ((float)Panel.ActualWidth, (float)Panel.ActualHeight, 96 * _scale());
        if (_closed || width <= 0 || height <= 0)
        {
            return;
        }

        if (_chain == null)
        {
            _chain = new CanvasSwapChain(Device, width, height, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, CanvasAlphaMode.Premultiplied);
            Panel.SwapChain = _chain;
        }
        else if (_chain.Size.Width != width || _chain.Size.Height != height || _chain.Dpi != dpi)
        {
            _chain.ResizeBuffers(width, height, dpi);
        }
        else
        {
            return;
        }

        Invalidate();
    }

    /// <summary>A frame is wanted: drawn at once, or at the next refresh if one was presented since the last.</summary>
    public void Invalidate()
    {
        if (_closed || _drawQueued)
        {
            return;
        }

        if (_presentedThisTick)
        {
            _dirty = true;
            HookRendering();
            return;
        }

        _drawQueued = true;
        var asked = Stopwatch.GetTimestamp();
        void DrawWhenQuiet()
        {
            // A shell answers a key in several reads within a millisecond or so (PowerShell: 7-9). Presenting at the
            // first would show the screen before the letter arrived, and the frame with it would then wait for the
            // next refresh: so wait until output pauses (QuietFor), at most MaxWait. Other messages run meanwhile.
            if (_sinceOutput() < QuietFor && Stopwatch.GetElapsedTime(asked).TotalMilliseconds < MaxWait)
            {
                Panel.DispatcherQueue.SafeEnqueue(DispatcherQueuePriority.Normal, DrawWhenQuiet);
                return;
            }

            _drawQueued = false;
            Present();
        }

        Panel.DispatcherQueue.SafeEnqueue(DispatcherQueuePriority.High, DrawWhenQuiet);
    }

    /// <summary>How long output must have paused before a frame is presented, and the longest wait for that (ms).</summary>
    private const double QuietFor = 0.5, MaxWait = 3;

    private void Present()
    {
        if (_closed || _chain is not { } chain)
        {
            return;
        }

        try
        {
            using (var ds = chain.CreateDrawingSession(ClearColor))
            {
                _draw(ds);
            }

            chain.Present(1); // shown at the next refresh
            _presentedThisTick = true;
            HookRendering();
        }
        catch (Exception ex) when (Device.IsDeviceLost(ex.HResult))
        {
            Log.Write($"pane: swap chain: device lost while drawing ({ex.HResult:X8})");
            Device.RaiseDeviceLost();
        }
        catch (Exception ex)
        {
            if (_failures++ == 0)
            {
                Log.Crash("draw (swap chain)", ex);
            }
        }
    }

    /// <summary>Once per refresh while frames come: lets the next request present at once, or draws the one that waited.</summary>
    private void OnRendering(object? sender, object e)
    {
        _presentedThisTick = false;
        if (_dirty)
        {
            _dirty = false;
            Present();
        }
        else
        {
            UnhookRendering(); // idle: no per-refresh work until the next frame
        }
    }

    private void HookRendering()
    {
        if (!_onRendering && !_closed)
        {
            _onRendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void UnhookRendering()
    {
        if (_onRendering)
        {
            _onRendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnDeviceLost(CanvasDevice sender, object args)
    {
        Panel.DispatcherQueue.SafeEnqueue(() =>
        {
            if (_closed)
            {
                return;
            }

            Log.Write("pane: swap chain: new device");
            sender.DeviceLost -= OnDeviceLost;
            Panel.SwapChain = null;
            _chain?.Dispose();
            _chain = null;
            Device = CanvasDevice.GetSharedDevice(_software);
            Device.DeviceLost += OnDeviceLost;
            _createResources(Device);
            Resize();
        });
    }

    /// <summary>The pane closed: lets go of the swap chain and the device's event.</summary>
    public void Close()
    {
        _closed = true;
        UnhookRendering();
        Device.DeviceLost -= OnDeviceLost;
        Panel.SwapChain = null;
        _chain?.Dispose();
        _chain = null;
        Panel.RemoveFromVisualTree();
    }
}
