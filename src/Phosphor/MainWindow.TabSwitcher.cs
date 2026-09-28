using Phosphor.Commands;
using Phosphor.Terminal;

namespace Phosphor;

// The tab switcher: "@" in the command palette (or "Go to tab…") lists every terminal tab in this
// window, searchable by title, running command, folder and workspace, tabs with a dot first. And
// "Go to next tab needing attention", which takes you to the most urgent dot.
public sealed partial class MainWindow
{
    private IEnumerable<PaletteCommand> TabSwitcherCommands()
    {
        var several = Tabs.TabItems.Count > 1;
        return AllTabs()
            .SelectMany((ws, n) => ws.Workspace.Tabs.Select(t => (Workspace: ws, Tab: t, Number: n + 1)))
            .Select((e, i) => (e.Workspace, e.Tab, e.Number, Order: i))
            .OrderByDescending(e => e.Tab.Attention) // failed, a program's notification, finished, then the rest
            .ThenBy(e => e.Order)
            .Select(e => new PaletteCommand(TabLabel(e.Workspace, e.Number, e.Tab, several), null,
                () => BringToPane(e.Workspace, e.Tab), Remember: false, Icon: e.Tab, IsTab: true));
    }

    /// <summary>"title · what it runs (or its folder) · workspace": what the switcher searches.</summary>
    private static string TabLabel(WorkspaceTab workspace, int number, TerminalTab tab, bool withWorkspace)
    {
        var title = GroupView.TitleOf(tab);
        var parts = new List<string> { title };
        if (tab.Control.RunningCommand is { Text: { } running })
        {
            parts.Add($"running {running}");
        }
        else if (ShellDirectory.Parse(tab.Control.ReportedDirectory) is { } folder &&
                 !title.Contains(folder, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(folder);
        }

        if (withWorkspace)
        {
            // Its name, or its number when the name would only repeat the title.
            parts.Add(workspace.Header.Title != title ? workspace.Header.Title : $"workspace {number}");
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Goes to the most urgent tab with a dot (failed, then a program's notification, then
    /// finished; the first in the window among equals). Looking at it clears its dot, so pressing
    /// again goes on to the next.
    /// </summary>
    private void GoToNextAttention()
    {
        var next = AllTabs()
            .SelectMany(ws => ws.Workspace.Tabs.Select(t => (Workspace: ws, Tab: t)))
            .Where(e => e.Tab.Attention != Attention.None)
            .OrderByDescending(e => e.Tab.Attention)
            .FirstOrDefault();
        if (next.Tab == null)
        {
            Log.Write("attention: nothing waiting");
            return;
        }

        BringToPane(next.Workspace, next.Tab);
    }
}
