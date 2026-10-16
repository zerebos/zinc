using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Zinc.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Zinc.Terminal;

/// <summary>
/// Custom shaders (Ghostty/Shadertoy style) over a terminal's frame, on one device: the frame is drawn with Win2D into
/// an offscreen texture (<see cref="FrameFor"/>), then each shader runs over the previous one's picture with
/// Direct3D 11 (a full-screen triangle, iChannel0 = the picture so far), the last into the swap chain's back buffer.
/// Runs on Win2D's own device, between its drawing sessions.
/// </summary>
internal sealed class ShaderPass : IDisposable
{
    private readonly CanvasDevice _canvasDevice;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertex;
    private readonly List<ID3D11PixelShader> _pixels = [];
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _sampler;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    // The terminal's picture (only ever drawn by the terminal: animation frames run the shaders over it again), and
    // two scratch targets the shaders between the first and the last write into, in turn.
    private CanvasRenderTarget? _frame, _scratchA, _scratchB;
    private int _frameCount;
    private double _lastTime, _focusChanged;
    private bool _focused;

    /// <summary>The shaders it runs, in order.</summary>
    public IReadOnlyList<CompiledShader> Shaders { get; }

    /// <summary>Whether a shader changes over time on its own (redraws every refresh while it may animate).</summary>
    public bool UsesTime { get; }

    /// <summary>The uniform values; the pass fills the time, size and focus ones itself.</summary>
    public ShaderUniforms Uniforms { get; } = new();

    public ShaderPass(CanvasDevice canvasDevice, IReadOnlyList<CompiledShader> shaders)
    {
        _canvasDevice = canvasDevice;
        Shaders = shaders;
        UsesTime = shaders.Any(s => s.UsesTime);
        try
        {
            _device = new ID3D11Device(DxgiInterface(canvasDevice, typeof(ID3D11Device).GUID));
            _context = _device.ImmediateContext;
            _vertex = _device.CreateVertexShader(ShaderLoader.VertexShader.Value);
            foreach (var shader in shaders)
            {
                _pixels.Add(_device.CreatePixelShader(shader.Bytecode));
            }

            _constants = _device.CreateBuffer(new BufferDescription(ShaderUniforms.Size, BindFlags.ConstantBuffer, ResourceUsage.Dynamic,
                CpuAccessFlags.Write));
            _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
        }
        catch
        {
            Dispose(); // what was made before the failure (a lost device, say)
            throw;
        }
    }

    /// <summary>
    /// Whether the shaders' clock (iTime) runs. Paused while the animation is (an unfocused terminal): a frame drawn
    /// meanwhile, because the terminal changed, then shows the shader where it stopped instead of jumping ahead.
    /// </summary>
    public void SetClock(bool running)
    {
        if (running)
        {
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }
    }

    /// <summary>The shaders' clock now (iTime's seconds).</summary>
    public double Time => _clock.Elapsed.TotalSeconds;

    /// <summary>
    /// Nothing moves (animation "never"): the clock is stopped, and what happens (a cursor move, focus) is stamped long
    /// before it, so effects timed from it (cursor trails, a focus fade) show as finished, not frozen at their start.
    /// </summary>
    public bool AtRest { get; set; }

    /// <summary>The time an event is stamped with (iTimeCursorChange, iTimeFocus).</summary>
    public double EventTime => AtRest ? Time - 10_000 : Time;

    /// <summary>The terminal gained or lost focus: iFocus, and iTimeFocus = when it was last gained (as Ghostty).</summary>
    public void SetFocus(bool focused)
    {
        if (focused && !_focused)
        {
            _focusChanged = EventTime;
        }

        _focused = focused;
    }

    /// <summary>A hidden pane gives its pictures back (made again, and the frame redrawn, at the next <see cref="FrameFor"/>).</summary>
    public void FreeTargets()
    {
        _frame?.Dispose();
        _scratchA?.Dispose();
        _scratchB?.Dispose();
        (_frame, _scratchA, _scratchB) = (null, null, null);
    }

    /// <summary>
    /// The offscreen frame for <paramref name="chain"/>'s size: draw the terminal into it, then <see cref="Run"/>.
    /// <paramref name="fresh"/>: a new one (first, or another size), which has to be drawn; otherwise it still holds the
    /// last picture, and the shaders may run over it again.
    /// </summary>
    public CanvasRenderTarget FrameFor(CanvasSwapChain chain, out bool fresh) => Sized(ref _frame, chain, out fresh);

    private CanvasRenderTarget Sized(ref CanvasRenderTarget? target, CanvasSwapChain chain) => Sized(ref target, chain, out _);

    private CanvasRenderTarget Sized(ref CanvasRenderTarget? target, CanvasSwapChain chain, out bool fresh)
    {
        fresh = target is not { } t || t.Size != chain.Size || t.Dpi != chain.Dpi;
        if (fresh)
        {
            target?.Dispose();
            target = new CanvasRenderTarget(_canvasDevice, (float)chain.Size.Width, (float)chain.Size.Height, chain.Dpi);
        }

        return target!;
    }

    /// <summary>
    /// Runs the shaders over the frame into <paramref name="chain"/>'s back buffer (Present is the caller's), or into
    /// <paramref name="into"/> (the chain's size: a still picture of the pane).
    /// </summary>
    public void Run(CanvasSwapChain chain, CanvasRenderTarget? into = null)
    {
        var frame = _frame!;
        var (width, height) = ((int)frame.SizeInPixels.Width, (int)frame.SizeInPixels.Height);
        var time = _clock.Elapsed.TotalSeconds;
        Uniforms.SetResolution(width, height);
        Uniforms.SetTime((float)time, _frameCount == 0 ? 0 : (float)(time - _lastTime), _frameCount++);
        Uniforms.SetDate(DateTime.Now);
        Uniforms.SetFocus(_focused, (float)_focusChanged);
        _lastTime = time;
        var mapped = _context.Map(_constants, 0, MapMode.WriteDiscard);
        Marshal.Copy(Uniforms.Bytes, 0, mapped.DataPointer, ShaderUniforms.Size);
        _context.Unmap(_constants, 0);

        _context.RSSetViewport(0, 0, width, height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(null);
        _context.VSSetShader(_vertex);
        _context.PSSetConstantBuffer(1, _constants);
        _context.PSSetSampler(0, _sampler);

        // Picture so far -> next: from the frame, back and forth between the scratch targets, the last into the back
        // buffer. Never into the frame: animation frames reuse it, and with 3+ shaders the effects compounded.
        var source = frame;
        for (var i = 0; i < _pixels.Count; i++)
        {
            var last = i == _pixels.Count - 1;
            var target = last ? null : ReferenceEquals(source, _scratchA) ? Sized(ref _scratchB, chain) : Sized(ref _scratchA, chain);
            using var input = TextureOf(source);
            using var view = _device.CreateShaderResourceView(input);
            using var swapChain = last && into == null ? SwapChainOf(chain) : null;
            using var output = !last ? TextureOf(target!) : into != null ? TextureOf(into) : swapChain!.GetBuffer<ID3D11Texture2D>(0);
            using var outputView = _device.CreateRenderTargetView(output);

            _context.OMSetRenderTargets(outputView);
            _context.PSSetShader(_pixels[i]);
            _context.PSSetShaderResource(0, view);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);
            _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
            source = target!;
        }

        // The device's context is shared (Win2D, every pane): leave nothing of ours bound, so nothing outlives the pass.
        _context.VSSetShader(null);
        _context.PSSetShader(null);
        _context.PSSetConstantBuffer(1, null);
        _context.PSSetSampler(0, null);
    }

    public void Dispose()
    {
        // Null-tolerant: the constructor calls it after a failure partway.
        _frame?.Dispose();
        _scratchA?.Dispose();
        _scratchB?.Dispose();
        _sampler?.Dispose();
        _constants?.Dispose();
        foreach (var pixel in _pixels)
        {
            pixel.Dispose();
        }

        _vertex?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }

    // ---------------------------------------------------------------- Win2D -> Direct3D

    private static readonly Guid DxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid ResourceWrapperNative = new("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A");

    private static nint ComPointer(object winrt) => ((WinRT.IWinRTObject)winrt).NativeObject.ThisPtr;

    /// <summary>IDirect3DDxgiInterfaceAccess::GetInterface on a WinRT Direct3D object (a device or a surface).</summary>
    private static unsafe nint DxgiInterface(object winrt, Guid iid)
    {
        var access = DxgiInterfaceAccess;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(ComPointer(winrt), in access, out var accessPtr));
        try
        {
            var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)accessPtr)[3];
            nint result;
            Marshal.ThrowExceptionForHR(getInterface(accessPtr, &iid, &result));
            return result;
        }
        finally
        {
            Marshal.Release(accessPtr);
        }
    }

    private static ID3D11Texture2D TextureOf(CanvasRenderTarget target) => new(DxgiInterface(target, typeof(ID3D11Texture2D).GUID));

    /// <summary>ICanvasResourceWrapperNative::GetNativeResource: the DXGI swap chain under a CanvasSwapChain.</summary>
    private static unsafe IDXGISwapChain1 SwapChainOf(CanvasSwapChain chain)
    {
        var wrapper = ResourceWrapperNative;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(ComPointer(chain), in wrapper, out var wrapperPtr));
        try
        {
            var getNative = (delegate* unmanaged[Stdcall]<nint, nint, float, Guid*, nint*, int>)(*(nint**)wrapperPtr)[3];
            var iid = typeof(IDXGISwapChain1).GUID;
            nint result;
            Marshal.ThrowExceptionForHR(getNative(wrapperPtr, 0, 0, &iid, &result));
            return new IDXGISwapChain1(result);
        }
        finally
        {
            Marshal.Release(wrapperPtr);
        }
    }
}
