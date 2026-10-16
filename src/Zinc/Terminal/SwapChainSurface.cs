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
    /// How long a pane stays hidden (its tab not shown) before it gives its swap chain and shader pictures back
    /// (<c>[window] free-hidden-memory</c>, <c>free-hidden-after</c>); null: never. Read when a pane is hidden.
    /// </summary>
    public static TimeSpan? FreeHiddenAfter { get; set; } = TimeSpan.FromSeconds(60);

    // Started when the panel unloads; if it's still hidden when this fires, the memory goes. Kept in a field: a timer
    // only a local referenced would be collected and never fire.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _freeTimer;

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

    // Custom shaders (SetShaders): the pass for the current device, rebuilt after a lost one.
    private ShaderPass? _shader;
    private IReadOnlyList<CompiledShader> _shaders = [];
    private bool _animate, _focused, _clockRunning = true, _atRest;

    // The terminal changed since its picture was last drawn for the shaders: an animation frame with nothing new only
    // runs the shaders again over the last picture (redrawing the terminal each refresh cost ~30% of a core).
    private bool _stale = true;
    private bool _resourcesCreated, _drawQueued, _dirty, _presentedThisTick, _onRendering, _closed, _presenting;
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
            // Shown again (a tab selected, a pane moved): frames asked for while it was hidden weren't drawn. Drawn now,
            // not queued: the swap chain still holds the picture from before it was hidden, and a queued frame let that
            // show first, so whatever changed meanwhile (a prompt, a git status) visibly jumped into place.
            _freeTimer?.Stop();
            _stale = true;
            Present();
            if (Animating)
            {
                HookRendering();
            }
        };
        Panel.SizeChanged += (_, _) => Resize();
        Panel.Unloaded += (_, _) => StartFreeTimer();
    }

    private void StartFreeTimer()
    {
        if (_closed || FreeHiddenAfter is not { } after || _chain == null)
        {
            return;
        }

        if (_freeTimer == null)
        {
            _freeTimer = Panel.DispatcherQueue.CreateTimer();
            _freeTimer.IsRepeating = false;
            _freeTimer.OnTick(_ => FreeHidden());
        }

        _freeTimer.Interval = after;
        _freeTimer.Start();
    }

    /// <summary>
    /// Still hidden: lets go of the swap chain (two full-size buffers) and the shader pass's pictures. Shown again,
    /// Loaded's Resize makes a new chain and Invalidate draws its first frame (the glyphs are still in the shared atlas).
    /// </summary>
    private void FreeHidden()
    {
        if (_closed || Panel.IsLoaded || _chain == null)
        {
            return;
        }

        var pixels = _chain.SizeInPixels;
        Panel.SwapChain = null;
        _chain.Dispose();
        _chain = null;
        _shader?.FreeTargets();
        _stale = true;
        UnhookRendering();
        Log.Write($"pane: swap chain: freed while hidden ({pixels.Width}x{pixels.Height})");
    }

    /// <summary>The custom shaders to run over every frame, in order (none: frames go straight to the swap chain).</summary>
    public void SetShaders(IReadOnlyList<CompiledShader> shaders)
    {
        if (_closed || shaders.SequenceEqual(_shaders))
        {
            return;
        }

        _shaders = shaders;
        BuildShaderPass();
        Invalidate();
    }

    private void BuildShaderPass()
    {
        _shader?.Dispose();
        _shader = null;
        if (_shaders.Count == 0)
        {
            return;
        }

        try
        {
            _shader = new ShaderPass(Device, _shaders);
            _shader.AtRest = _atRest;
            _shader.SetFocus(_focused);
            _shader.SetClock(_clockRunning);
            Log.Write($"shader: running {string.Join(", ", _shaders.Select(s => Path.GetFileName(s.File)))}{(_shader.UsesTime ? " (animated)" : "")}");
            if (Animating)
            {
                HookRendering();
            }
        }
        catch (Exception ex)
        {
            Log.Crash("shader pass", ex);
            _shader = null;
        }
    }

    /// <summary>
    /// Whether shaders that use time may redraw every refresh on their own (<c>[shaders] animation</c> and focus decide);
    /// otherwise a frame is drawn only when the terminal changes.
    /// </summary>
    public bool Animate
    {
        get => _animate;
        set
        {
            _animate = value;
            if (Animating)
            {
                HookRendering();
            }
        }
    }

    // Not on a hidden tab (TabView unloads it): nobody would see the frames.
    private bool Animating => _animate && _shader is { UsesTime: true } && Panel.IsLoaded;

    /// <summary>Whether nothing moves at all (animation "never": see <see cref="ShaderPass.AtRest"/>).</summary>
    public void SetAtRest(bool atRest)
    {
        _atRest = atRest;
        if (_shader != null)
        {
            _shader.AtRest = atRest;
        }
    }

    /// <summary>Whether the shaders' clock runs (paused while their animation is: see <see cref="ShaderPass.SetClock"/>).</summary>
    public void SetClock(bool running)
    {
        _clockRunning = running;
        _shader?.SetClock(running);
    }

    /// <summary>Fills the shaders' uniforms from the terminal (cursor, colors, mouse) before each frame's pass.</summary>
    public Action<ShaderPass>? FillUniforms { get; set; }

    /// <summary>The terminal's focus, for the shaders' iFocus and iTimeFocus.</summary>
    public void SetFocus(bool focused)
    {
        _focused = focused;
        _shader?.SetFocus(focused);
    }

    /// <summary>Matches the swap chain to the panel's size and the window's scale (after a move or DPI change too).</summary>
    public void Resize()
    {
        var (width, height, dpi) = ((float)Panel.ActualWidth, (float)Panel.ActualHeight, 96 * _scale());
        // Compared in whole pixels (DIP floats differ by rounding at scales like 125%: a needless ResizeBuffers and
        // full redraw on every window change), and at least a pixel each way (a 0-pixel swap chain throws).
        var (pixelsWide, pixelsHigh) = ((int)Math.Round(width * dpi / 96), (int)Math.Round(height * dpi / 96));
        if (_closed || pixelsWide < 1 || pixelsHigh < 1)
        {
            return;
        }

        try
        {
            if (_chain == null)
            {
                _chain = new CanvasSwapChain(Device, width, height, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, CanvasAlphaMode.Premultiplied);
                Panel.SwapChain = _chain;
            }
            else if (_chain.SizeInPixels.Width != pixelsWide || _chain.SizeInPixels.Height != pixelsHigh || _chain.Dpi != dpi)
            {
                _chain.ResizeBuffers(width, height, dpi);
            }
            else
            {
                return;
            }
        }
        catch (Exception ex) when (Device.IsDeviceLost(ex.HResult))
        {
            // Found lost here first (a resize after a driver reset, while idle): the rebuild resizes again.
            Log.Write($"pane: swap chain: device lost while resizing ({ex.HResult:X8})");
            Device.RaiseDeviceLost();
            return;
        }

        Invalidate();
    }

    /// <summary>A frame is wanted: drawn at once, or at the next refresh if one was presented since the last.</summary>
    public void Invalidate()
    {
        _stale = true;
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
            if (_presentedThisTick)
            {
                // A refresh's frame went out while this waited (an animation tick): this one waits for the next.
                _dirty = true;
                HookRendering();
                return;
            }

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

        if (!Panel.IsLoaded)
        {
            // A hidden tab (TabView unloads it) or a pane between hosts: nobody would see the frame. Loaded draws one.
            _stale = true;
            return;
        }

        if (_presenting)
        {
            // Asked for from inside this surface's own frame: a contended lock on the UI thread (the session's, while
            // the reader parses) runs High-priority queued work during its wait, and Invalidate's DrawWhenQuiet is one.
            // A second drawing session on the chain would throw ("The last drawing session ... must be disposed"): drawn
            // at the next refresh instead, like a frame asked for right after a present.
            Log.Write("pane: swap chain: frame asked for during a frame (drawn at the next refresh)");
            _stale = true;
            _dirty = true;
            HookRendering();
            return;
        }

        _presenting = true;
        try
        {
            if (_shader is { } shader)
            {
                var frame = shader.FrameFor(chain, out var fresh);
                if (_stale || fresh)
                {
                    _stale = false;
                    using var ds = frame.CreateDrawingSession();
                    ds.Clear(ClearColor);
                    _draw(ds);
                }

                try
                {
                    FillUniforms?.Invoke(shader);
                    shader.Run(chain);
                }
                catch (Exception ex) when (!Device.IsDeviceLost(ex.HResult))
                {
                    // Not worth a blank pane: carry on without shaders (the log says why), drawn plainly this time.
                    Log.Crash("shader pass", ex);
                    _shader.Dispose();
                    _shader = null;
                    _shaders = [];
                    using var plain = chain.CreateDrawingSession(ClearColor);
                    _draw(plain);
                }
            }
            else
            {
                using var ds = chain.CreateDrawingSession(ClearColor);
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
        finally
        {
            _presenting = false;
        }
    }

    /// <summary>Once per refresh while frames come: lets the next request present at once, or draws the one that waited.</summary>
    private void OnRendering(object? sender, object e)
    {
        _presentedThisTick = false;
        if (Animating)
        {
            _dirty = false;
            Present(); // a shader that uses time: a frame every refresh
        }
        else if (_dirty)
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
            // One loss is raised several times (each pane's draw, each surface), and every raise queues this: only the
            // first, from the device in use, rebuilds; the rest would each subscribe again to the new device.
            if (_closed || !ReferenceEquals(sender, Device))
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
            BuildShaderPass();
            Resize();
        });
    }

    /// <summary>The pane closed: lets go of the swap chain and the device's event.</summary>
    public void Close()
    {
        _closed = true;
        _freeTimer?.Stop();
        UnhookRendering();
        _shader?.Dispose();
        _shader = null;
        Device.DeviceLost -= OnDeviceLost;
        Panel.SwapChain = null;
        _chain?.Dispose();
        _chain = null;
        Panel.RemoveFromVisualTree();
    }
}
