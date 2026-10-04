using Microsoft.UI.Dispatching;

namespace Zinc.Terminal;

/// <summary>
/// Custom shaders (<c>[shaders] files</c>, Ghostty/Shadertoy GLSL) over the pane's picture: loaded off the UI thread
/// (<see cref="ShaderLoader"/>, shared between panes), run by the swap chain surface (low-latency drawing only), animated
/// per <c>[shaders] animation</c> and focus. Shaders that fail are left out and reported (<see cref="ShaderProblems"/>):
/// the pane draws with the rest, or plainly.
/// </summary>
public sealed partial class TerminalControl
{
    /// <summary>Shaders that couldn't be used, a line each ("crt.glsl, line 12: ..."), for the window's notice.</summary>
    public event Action<IReadOnlyList<string>>? ShaderProblems;

    private int _shaderLoads; // a newer load wins over one still running

    /// <summary>The first load waits for the dispatcher, so the window has wired the pane before it hears of problems.</summary>
    private void StartShaders() => DispatcherQueue.SafeEnqueue(DispatcherQueuePriority.Low, () => ApplyShaders(_config));

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

            _swap.SetShaders(loaded.Shaders);
            if (loaded.Errors.Count > 0)
            {
                ShaderProblems?.Invoke(loaded.Errors);
            }
        }
        catch (Exception ex)
        {
            // After an await: an exception escaping would end the process.
            Log.Crash("shaders", ex);
            App.Instance.ShowSurvivedError(ex);
        }
    }

    /// <summary>Time-based shaders redraw on their own: always, never, or while the terminal has focus.</summary>
    private void UpdateShaderAnimation()
    {
        if (_swap == null)
        {
            return;
        }

        _swap.SetFocus(_focused);
        _swap.Animate = _config.ShaderAnimation switch
        {
            ShaderAnimation.Always => true,
            ShaderAnimation.Never => false,
            _ => _focused,
        };

        // Paused with the animation while unfocused: hovering a program's links or scrollbar redraws the terminal, and
        // with a running clock each of those frames showed the shader jumped ahead. With animation off (Ghostty's
        // false) time still runs: frames drawn on change show it moving, as in Ghostty.
        _swap.SetClock(_config.ShaderAnimation != ShaderAnimation.Focused || _focused);
    }
}
