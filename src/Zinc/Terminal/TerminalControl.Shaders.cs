using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Zinc.Ghostty;
using Zinc.Shaders;

namespace Zinc.Terminal;

/// <summary>
/// Custom shaders (<c>[shaders] files</c>, Ghostty/Shadertoy GLSL) over the pane's picture: loaded off the UI thread
/// (<see cref="ShaderLoader"/>, shared between panes), run by the swap chain surface (low-latency drawing only), animated
/// per <c>[shaders] animation</c> and focus. Shaders that fail are left out and reported (<see cref="ShaderProblems"/>):
/// the pane draws with the rest, or plainly.
/// </summary>
public sealed partial class TerminalControl
{
    /// <summary>
    /// After each load: the pane's shaders that couldn't be used, a line each ("crt.glsl, line 12: ..."), for the window's
    /// notice; empty when they all run (a fixed file takes its line back).
    /// </summary>
    public event Action<IReadOnlyList<string>>? ShaderProblems;

    private int _shaderLoads; // a newer load wins over one still running

    /// <summary>The first load waits for the dispatcher, so the window has wired the pane before it hears of problems.</summary>
    private void StartShaders()
    {
        if (_swap != null)
        {
            _swap.FillUniforms = FillShaderUniforms;
            ShaderLoader.FileChanged += OnShaderFileChanged;
        }

        DispatcherQueue.SafeEnqueue(DispatcherQueuePriority.Low, () => ApplyShaders(_config));
    }

    /// <summary>A shader file was saved: load the pane's shaders again if it's one of them (the rest come from the cache).</summary>
    private void OnShaderFileChanged(string path) => DispatcherQueue.SafeEnqueue(() =>
    {
        if (!_closing.IsCancellationRequested && _config.ShaderFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            ApplyShaders(_config);
        }
    });

    /// <summary>The pane closed: no more file notices (a static event would keep it alive).</summary>
    private void StopShaders() => ShaderLoader.FileChanged -= OnShaderFileChanged;

    // The mouse for iMouse, in DIPs: where it is while the left button is down, where that press began.
    private Point _shaderMouse, _shaderMouseDown;
    private bool _shaderMouseIsDown;

    /// <summary>Loads the configured shaders (again: a reload may follow an edited shader file; unchanged files are cached).</summary>
    private async void ApplyShaders(Config config)
    {
        var load = ++_shaderLoads;
        try
        {
            if (_swap == null)
            {
                if (config.ShaderFiles.Count > 0)
                {
                    ShaderProblems?.Invoke(["Custom shaders need low-latency drawing (Settings > Terminal > Troubleshooting, or [window] low-latency-drawing = true)."]);
                }

                return;
            }

            UpdateShaderAnimation();
            var loaded = config.ShaderFiles.Count == 0 ? new LoadedShaders([], []) : await ShaderLoader.Load(config.ShaderFiles);
            if (load != _shaderLoads || _closing.IsCancellationRequested)
            {
                return; // a newer load, or the pane closed meanwhile
            }

            // The renderer keeps what shaders read from each frame only while there are shaders.
            _renderer.ForShaders = loaded.Shaders.Count > 0 ? _renderer.ForShaders ?? new ShaderFrame() : null;
            _swap.SetShaders(loaded.Shaders);
            ShaderProblems?.Invoke(loaded.Errors);
        }
        catch (Exception ex)
        {
            // After an await: an exception escaping would end the process.
            Log.Crash("shaders", ex);
            App.Instance.ShowSurvivedError(ex);
        }
    }

    /// <summary>
    /// The terminal's side of Ghostty's uniforms, from the last frame drawn: the cursor as drawn (pixels), its style, the
    /// live palette and colors, the selection colors the config sets, and the mouse.
    /// </summary>
    private void FillShaderUniforms(ShaderPass pass)
    {
        if (_renderer.ForShaders is not { } frame)
        {
            return;
        }

        var u = pass.Uniforms;
        var scale = DisplayScale;
        u.SetPalette(frame.Palette);
        u.SetColor("iBackgroundColor", frame.Background.R, frame.Background.G, frame.Background.B);
        u.SetColor("iForegroundColor", frame.Foreground.R, frame.Foreground.G, frame.Foreground.B);
        var cursor = frame.CursorColor ?? frame.Foreground;
        u.SetColor("iCursorColor", cursor.R, cursor.G, cursor.B);
        var theme = _config.Theme;
        var text = theme.CursorText ?? new GhosttyColorRgb(frame.Background.R, frame.Background.G, frame.Background.B);
        u.SetColor("iCursorText", text.R, text.G, text.B);
        if (theme.SelectionBackground is { } selectionBg)
        {
            u.SetColor("iSelectionBackgroundColor", selectionBg.R, selectionBg.G, selectionBg.B);
        }

        if (theme.SelectionForeground is { } selectionFg)
        {
            u.SetColor("iSelectionForegroundColor", selectionFg.R, selectionFg.G, selectionFg.B);
        }

        u.SetCursorStyle(frame.CursorStyle switch
        {
            CursorVisualStyle.Bar => ShaderUniforms.CursorStyle.Bar,
            CursorVisualStyle.Underline => ShaderUniforms.CursorStyle.Underline,
            CursorVisualStyle.BlockHollow => ShaderUniforms.CursorStyle.BlockHollow,
            _ => ShaderUniforms.CursorStyle.Block,
        }, frame.CursorVisible);
        if (frame.CursorDrawn)
        {
            var (r, c, color) = (frame.Cursor, scale, frame.CursorDrawnColor);
            u.SetCursor((float)(r.X * c), (float)(r.Bottom * c), (float)(r.Width * c), (float)(r.Height * c),
                color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f, (float)pass.Time);
        }

        u.SetMouse((float)(_shaderMouse.X * scale), (float)(_shaderMouse.Y * scale),
            (float)(_shaderMouseDown.X * scale), (float)(_shaderMouseDown.Y * scale), _shaderMouseIsDown);
    }

    private void NoteShaderMouse(PointerRoutedEventArgs e)
    {
        if (_renderer.ForShaders == null)
        {
            return;
        }

        var point = e.GetCurrentPoint(Surface);
        var down = point.Properties.IsLeftButtonPressed;
        if (down)
        {
            if (!_shaderMouseIsDown)
            {
                _shaderMouseDown = point.Position;
            }

            _shaderMouse = point.Position;
        }

        _shaderMouseIsDown = down;
    }

    /// <summary>Time-based shaders redraw on their own: always, never, or while the terminal has focus.</summary>
    private void UpdateShaderAnimation()
    {
        if (_swap == null)
        {
            return;
        }

        // Settings' preview is never focused, but stands for a focused terminal (its cursor is drawn so too).
        var focused = _focused || _previewScript != null;
        _swap.SetFocus(focused);
        _swap.Animate = _config.ShaderAnimation switch
        {
            ShaderAnimation.Always => true,
            ShaderAnimation.Never => false,
            _ => focused,
        };

        // Paused with the animation while unfocused: hovering a program's links or scrollbar redraws the terminal, and
        // with a running clock each of those frames showed the shader jumped ahead. With animation off (Ghostty's
        // false) time still runs: frames drawn on change show it moving, as in Ghostty.
        _swap.SetClock(_config.ShaderAnimation != ShaderAnimation.Focused || focused);
    }
}
