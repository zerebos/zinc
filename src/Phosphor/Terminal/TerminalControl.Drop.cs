using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Phosphor.Terminal;

/// <summary>
/// Files dropped on a pane: their paths are typed at the prompt as its shell writes them
/// (<see cref="DroppedPaths"/>), and the pane takes focus so Enter runs the line. Tabs dragged
/// between panes aren't files and fall through to the pane host.
/// </summary>
public sealed partial class TerminalControl
{
    private void EnableFileDrop()
    {
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            if (_session == null || !e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Paste the path";
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (_session is { } session && e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.Handled = true;
                FileDrop.PasteAsync(e.DataView, paths => PastePaths(session, paths));
            }
        };
    }

    /// <summary>The WSL distro this pane runs, if it's WSL (as file links and drops see it).</summary>
    internal string? WslDistro => (_linkContext ??= ShellEnvironment.LinkContextFor(_commandLine)).WslDistro;

    /// <summary>Types <paramref name="paths"/> (Windows paths) as this pane's shell names them.</summary>
    internal void PastePaths(IReadOnlyList<string> paths)
    {
        if (_session is { } session)
        {
            PastePaths(session, paths);
        }
    }

    private void PastePaths(TerminalSession session, IReadOnlyList<string> paths)
    {
        if (_session != session || paths.Count == 0)
        {
            return;
        }

        _linkContext ??= ShellEnvironment.LinkContextFor(_commandLine);
        var quoting = ShellEnvironment.QuotingFor(_commandLine, _linkContext);
        var text = DroppedPaths.Format(paths, _linkContext, quoting);
        Log.Write($"drop: {paths.Count} path(s) as {quoting}: {text}");
        session.Paste(text);
        Focus(FocusState.Programmatic);
    }
}

/// <summary>Reads dropped files' paths (async, so outside the terminal's unsafe code).</summary>
internal static class FileDrop
{
    public static async void PasteAsync(Windows.ApplicationModel.DataTransfer.DataPackageView data, Action<IReadOnlyList<string>> paste)
    {
        // Code after an await resumes as queued work, where an exception would end the app.
        try
        {
            var items = await data.GetStorageItemsAsync();
            // Items without a path (e.g. inside a zip) have nothing to type.
            paste([.. items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p))]);
        }
        catch (Exception ex)
        {
            Log.Write($"drop failed: {ex.Message}");
        }
    }
}
