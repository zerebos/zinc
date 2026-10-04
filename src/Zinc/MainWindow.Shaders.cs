namespace Zinc;

// Custom shaders that couldn't be used: one notice for the window, whichever terminals report them.
public sealed partial class MainWindow
{
    private readonly HashSet<string> _shaderProblems = [];
    private readonly HashSet<string> _dismissedShaderProblems = [];
    private bool _shaderNoticeWired;

    /// <summary>
    /// Adds a terminal's shader problems to the notice. Every terminal with that shader reports the same lines, and new
    /// tabs report them again: a problem the notice was closed on stays quiet until the next config reload.
    /// </summary>
    private void ShowShaderProblems(IReadOnlyList<string> problems)
    {
        if (!_shaderNoticeWired)
        {
            _shaderNoticeWired = true;
            OpenShaderConfigButton.Click += (_, _) => OpenConfigFile();
            ShaderNotice.Closed += (_, _) =>
            {
                _dismissedShaderProblems.UnionWith(_shaderProblems);
                _shaderProblems.Clear();
            };
        }

        var added = false;
        foreach (var problem in problems)
        {
            added |= !_dismissedShaderProblems.Contains(problem) && _shaderProblems.Add(problem);
        }

        if (added)
        {
            ShaderNotice.Message = string.Join("\n", _shaderProblems);
            ShaderNotice.IsOpen = true;
        }
    }

    /// <summary>A config reload: the terminals load their shaders again and report what's still wrong.</summary>
    private void ForgetShaderProblems()
    {
        _shaderProblems.Clear();
        _dismissedShaderProblems.Clear();
        ShaderNotice.IsOpen = false;
    }
}
