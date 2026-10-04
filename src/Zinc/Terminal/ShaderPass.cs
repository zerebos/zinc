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
    private readonly ID3D11PixelShader[] _pixels;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _sampler;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CanvasRenderTarget? _frame, _between;
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
        _device = new ID3D11Device(DxgiInterface(canvasDevice, typeof(ID3D11Device).GUID));
        _context = _device.ImmediateContext;
        _vertex = _device.CreateVertexShader(ShaderLoader.VertexShader.Value);
        _pixels = [.. shaders.Select(s => _device.CreatePixelShader(s.Bytecode))];
        _constants = _device.CreateBuffer(new BufferDescription(ShaderUniforms.Size, BindFlags.ConstantBuffer, ResourceUsage.Dynamic,
            CpuAccessFlags.Write));
        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
    }

    /// <summary>The terminal gained or lost focus (iFocus, iTimeFocus).</summary>
    public void SetFocus(bool focused)
    {
        if (focused != _focused)
        {
            _focused = focused;
            _focusChanged = _clock.Elapsed.TotalSeconds;
        }
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

    /// <summary>Runs the shaders over the frame into <paramref name="chain"/>'s back buffer (Present is the caller's).</summary>
    public void Run(CanvasSwapChain chain)
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

        // Picture so far -> next: the frame, then back and forth with a second texture, the last into the back buffer.
        var source = frame;
        for (var i = 0; i < _pixels.Length; i++)
        {
            var last = i == _pixels.Length - 1;
            var target = last ? null : ReferenceEquals(source, frame) ? Sized(ref _between, chain) : frame;
            using var input = TextureOf(source);
            using var view = _device.CreateShaderResourceView(input);
            using var swapChain = last ? SwapChainOf(chain) : null;
            using var output = last ? swapChain!.GetBuffer<ID3D11Texture2D>(0) : TextureOf(target!);
            using var outputView = _device.CreateRenderTargetView(output);

            _context.OMSetRenderTargets(outputView);
            _context.PSSetShader(_pixels[i]);
            _context.PSSetShaderResource(0, view);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);
            _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
            source = target!;
        }
    }

    public void Dispose()
    {
        _frame?.Dispose();
        _between?.Dispose();
        _sampler.Dispose();
        _constants.Dispose();
        foreach (var pixel in _pixels)
        {
            pixel.Dispose();
        }

        _vertex.Dispose();
        _context.Dispose();
        _device.Dispose();
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
