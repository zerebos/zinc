using Zinc.Terminal;

namespace Zinc;

// Custom shaders that couldn't be used get one notice for the window, whichever terminals report them.
public sealed partial class MainWindow
{
    // Each terminal's problems as of its last load (most share the same lines, because they use the same shaders).
    private readonly Dictionary<TerminalControl, IReadOnlyList<string>> _shaderProblems = [];
    private readonly HashSet<string> _dismissedShaderProblems = [];
    private bool _shaderNoticeWired;

    /// <summary>
    /// A terminal loaded its shaders. The notice shows every terminal's problems, and closes once none are left (a fixed
    /// file takes its line back). A problem the notice was closed on stays quiet until the next config reload.
    /// </summary>
    private void ShowShaderProblems(TerminalControl control, IReadOnlyList<string> problems)
    {
        if (!_shaderNoticeWired)
        {
            _shaderNoticeWired = true;
            OpenShaderConfigButton.Click += (_, _) => OpenConfigFile();
            ShaderNotice.CloseButtonClick += (_, _) => _dismissedShaderProblems.UnionWith(_shaderProblems.Values.SelectMany(p => p));
        }

        _shaderProblems[control] = problems;
        UpdateShaderNotice();
    }

    /// <summary>A terminal left the window (closed or moved), so its problems go with it.</summary>
    private void ForgetShaderProblems(TerminalControl control)
    {
        if (_shaderProblems.Remove(control))
        {
            UpdateShaderNotice();
        }
    }

    /// <summary>
    /// On a config reload the terminals load their shaders again, so problems the notice was closed on may show again.
    /// </summary>
    private void ForgetShaderProblems() => _dismissedShaderProblems.Clear();

    private void UpdateShaderNotice()
    {
        var lines = _shaderProblems.Values.SelectMany(p => p).Distinct().Where(p => !_dismissedShaderProblems.Contains(p)).ToList();
        if (lines.Count == 0)
        {
            ShaderNotice.IsOpen = false;
            return;
        }

        var message = string.Join("\n", lines);
        if (!ShaderNotice.IsOpen || ShaderNotice.Message != message)
        {
            ShaderNotice.Message = message;
            ShaderNotice.IsOpen = true;
        }
    }
}
