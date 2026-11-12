using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Zinc.Input;
using Zinc.Panes;
using Zinc.Terminal;

namespace Zinc;

// The test harness. ZINC_TEST_KEYS replays keys and `test:` commands in a running window, so scripts can drive what
// posted input can't (modifier chords, drags, menus). It ships in every build, because the update tests drive a
// published build with it, but it's for development only.
public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _replayTimer;
    private static int _replayingWindows;

    /// <summary>
    /// ZINC_TEST_KEYS="alt+shift+plus,alt+left" replays chords (one per second) through the keybindings and action
    /// handler, as if pressed in the focused pane, because automated tests can't post real modifier state to the window.
    /// </summary>
    private void ReplayTestKeys()
    {
        if (Environment.GetEnvironmentVariable("ZINC_TEST_KEYS") is not { Length: > 0 } keys)
        {
            return;
        }

        // Every window replays the keys from when it starts. A key with a prefix, like `w2:alt+shift+minus`, runs only in
        // the second window to start, so one run can set up several windows. Keys without a prefix run in every window.
        var window = $"w{++_replayingWindows}:";
        var queue = new Queue<string>(keys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(k => !System.Text.RegularExpressions.Regex.IsMatch(k, @"^w\d+:") || k.StartsWith(window))
            .Select(k => k.StartsWith(window) ? k[window.Length..] : k));
        // Held in a field, because an unreferenced DispatcherQueueTimer is collected and stops firing.
        var timer = _replayTimer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            if (!queue.TryDequeue(out var chord))
            {
                timer.Stop();
                return;
            }

            // On the new-workspace page, a key goes to the page's own shortcuts, as a real press would.
            if (SelectedTab == null && StartPageItem() is { } page && ReferenceEquals(Tabs.SelectedItem, page) && !chord.Contains(':'))
            {
                var handled = _keybindings.TryGet(KeyChord.Parse(chord), out var onPage) && HandlePageAction(onPage, page);
                Log.Write($"test key {chord} on the new-workspace page: {(handled ? onPage.ToString() : "nothing")}");
                return;
            }

            // On the page, `palette:` runs one of the page's own commands, and `test:palette-open:` opens the palette
            // with a query typed.
            if (SelectedTab == null && StartPageItem() is { } shownPage && ReferenceEquals(Tabs.SelectedItem, shownPage) &&
                (chord.StartsWith("palette:") || chord.StartsWith("test:palette-open:")))
            {
                if (chord.StartsWith("test:palette-open:"))
                {
                    OpenCommandPalette(chord["test:palette-open:".Length..]);
                    Log.Write($"test {chord} on the new-workspace page: done");
                    return;
                }

                var command = BuildPageCommands(shownPage).FirstOrDefault(c => c.Title == chord["palette:".Length..]);
                Log.Write($"test {chord} on the new-workspace page: {(command != null ? "found" : "missing")}");
                if (command != null)
                {
                    RunPaletteCommand(command);
                }

                return;
            }

            if (SelectedTab is not { } tab)
            {
                timer.Stop();
                return;
            }

            if (chord.StartsWith("test:drop-middle:") || chord.StartsWith("test:drop-right:"))
            {
                // "test:drop-middle:1" drops workspace 1 into the focused pane, as a drag onto its middle would, and
                // "test:drop-right:1" drops it onto the pane's right edge. Drags can't be posted.
                var from = AllTabs().ElementAtOrDefault(int.Parse(chord[(chord.LastIndexOf(':') + 1)..]));
                Log.Write($"test {chord}: {(from != null && from != tab ? "dropping" : "no such workspace")}");
                if (from != null && from != tab && tab.Workspace.GroupOf(tab.Active) is { } target)
                {
                    DropWorkspace(this, from, tab, target, chord.Contains("middle") ? null : PaneDirection.Right);
                }

                return;
            }

            if (chord == "test:throw")
            {
                // Checks the safety net, which should log this to crash.log, show it, and survive it.
                DispatcherQueue.SafeEnqueue(() => throw new InvalidOperationException("test:throw, on purpose"));
                return;
            }

            if (chord == "test:layout-cycle")
            {
                // Checks that a layout cycle's report says what led to it, using an element that changes its width
                // each time its size changes, so layout never settles. This window may stop repainting afterwards, as
                // it does in a real cycle.
                var flipper = new Border { Width = 100, Height = 10, HorizontalAlignment = HorizontalAlignment.Left };
                flipper.SizeChanged += (_, _) => flipper.Width = flipper.Width == 100 ? 101 : 100;
                Root.Children.Add(flipper);
                Log.Write($"test {chord}: started");
                return;
            }

            if (chord == "test:chrome-hide")
            {
                // What the pointer leaving does, for both auto-hidden bars.
                _chromePinned = false;
                RevealSidebar(false);
                RevealTitleBar(false);
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord is "test:sidebar-show" or "test:titlebar-show")
            {
                // Reveals an auto-hidden bar and keeps it (a capture has no pointer to hold it open).
                if (chord == "test:sidebar-show")
                {
                    RevealSidebar(true, pinned: true);
                }
                else
                {
                    RevealTitleBar(true, pinned: true);
                }

                Log.Write($"test {chord}: done");
                return;
            }

            if (chord is "test:appearance-light" or "test:appearance-dark")
            {
                App.Instance.SetSystemScheme(chord.EndsWith("light") ? Ghostty.ColorScheme.Light : Ghostty.ColorScheme.Dark);
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord == "test:scrollbar-expand")
            {
                // Expands the focused pane's scrollbar and keeps it expanded, because hover can't be posted.
                tab.Active.Control.PinScrollBarExpandedForTest();
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord == "test:mem")
            {
                // Logs where memory goes, with the process's private bytes and how much of it is .NET's heap (after a
                // collection, so it's what's live). The rest is native: libghostty's screens, Win2D/Direct3D,
                // WinUI/XAML, DirectWrite.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using var self = System.Diagnostics.Process.GetCurrentProcess();
                var gc = GC.GetGCMemoryInfo();
                Log.Write($"test {chord}: private {self.PrivateMemorySize64 / 1048576} MB, working set {self.WorkingSet64 / 1048576} MB, " +
                    $".NET heap {gc.HeapSizeBytes / 1048576} MB (committed {gc.TotalCommittedBytes / 1048576} MB), panes {AllTabs().Sum(t => t.Workspace.Tabs.Count())}");
                return;
            }

            if (chord == "test:update")
            {
                // Check and download, as the daily check does (the update round trip test).
                Log.Write($"test {chord}");
                _ = Updater.CheckAsync();
                return;
            }

            if (chord == "test:update-restart")
            {
                // The notice's Restart now.
                Log.Write($"test {chord}");
                Updater.RestartNow();
                return;
            }

            if (chord == "test:gc")
            {
                // Frees whatever is unreferenced (e.g. a retired canvas), as a later GC would.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord.StartsWith("test:workspace-at:"))
            {
                // "test:workspace-at:0" moves the shown workspace to drop position 0, as a drag
                // in the workspace list would.
                Log.Write($"test {chord}");
                MoveWorkspace(tab, int.Parse(chord["test:workspace-at:".Length..]));
                return;
            }

            if (chord.StartsWith("test:workspace-move:"))
            {
                // "test:workspace-move:2:0" moves workspace 2 (shown or not) to drop position 0.
                var parts = chord["test:workspace-move:".Length..].Split(':');
                Log.Write($"test {chord}");
                MoveWorkspace(AllTabs().ElementAt(int.Parse(parts[0])), int.Parse(parts[1]));
                return;
            }

            if (chord.StartsWith("test:tab-"))
            {
                // What dragging the focused pane's shown tab would do. "test:tab-to:1" drops it into pane 1 (in layout
                // order) of this workspace, "test:tab-right:1" onto that pane's right edge, and "test:tab-at:0" at
                // position 0 of its own strip. Drags can't be posted.
                var arg = int.Parse(chord[(chord.LastIndexOf(':') + 1)..]);
                var pane = tab.Active;
                var target = chord.StartsWith("test:tab-at:") ? tab.Workspace.ActiveGroup : tab.Workspace.Groups.ElementAtOrDefault(arg);
                Log.Write($"test {chord}: {(target != null ? "moving" : "no such pane")}");
                if (target != null)
                {
                    MoveTerminalTab(this, pane, tab, target, chord.StartsWith("test:tab-right:") ? PaneDirection.Right : null,
                        chord.StartsWith("test:tab-at:") ? arg : null);
                }

                return;
            }

            if (chord.StartsWith("test:pane-"))
            {
                // What dragging the focused pane by its header would do. `test:pane-to:1` drops it into pane 1 (in layout
                // order) of this workspace, and `test:pane-right:1` or `test:pane-down:1` onto that pane's edge. A
                // workspace number before the pane picks another workspace, so `test:pane-to:0:1` drops it into pane 1
                // of workspace 0. `test:pane-title:0` drops it on the title bar at position 0.
                var args = chord[(chord.IndexOf(':', "test:pane-".Length) + 1)..].Split(':').Select(int.Parse).ToArray();
                var group = tab.Workspace.ActiveGroup;
                if (chord.StartsWith("test:pane-title:"))
                {
                    Log.Write($"test {chord}: dropping");
                    App.Instance.Drag = new TabDrag(this, group);
                    DropOnWorkspaces(args[0]);
                    App.Instance.Drag = null;
                    return;
                }

                if (chord.StartsWith("test:pane-window-"))
                {
                    // `test:pane-window-left:0` drops the focused pane on pane 0's left edge in another window's workspace.
                    var other = App.Instance.OtherWindow(this);
                    var otherTab = other?.SelectedTab;
                    var otherTarget = otherTab?.Workspace.Groups.ElementAtOrDefault(args[0]);
                    Log.Write($"test {chord}: {(otherTarget != null ? "moving" : "no such pane")}");
                    if (other != null && otherTab != null && otherTarget != null)
                    {
                        other.MovePane(this, group, otherTab, otherTarget,
                            Enum.Parse<PaneDirection>(chord["test:pane-window-".Length..chord.LastIndexOf(':')], ignoreCase: true));
                    }

                    return;
                }

                var into = args.Length > 1 ? AllTabs().ElementAtOrDefault(args[0]) : tab;
                var target = into?.Workspace.Groups.ElementAtOrDefault(args[^1]);
                PaneDirection? edge = chord.StartsWith("test:pane-right:") ? PaneDirection.Right
                    : chord.StartsWith("test:pane-down:") ? PaneDirection.Down : null;
                Log.Write($"test {chord}: {(target != null ? "moving" : "no such pane")}");
                if (into != null && target != null)
                {
                    MovePane(this, group, into, target, edge);
                }

                return;
            }

            if (chord.StartsWith("palette:"))
            {
                // "palette:Move tab to new workspace" runs the command palette entry with that title.
                var command = BuildPaletteCommands(tab).FirstOrDefault(c => c.Title == chord["palette:".Length..]);
                Log.Write($"test {chord}: {(command != null ? "found" : "missing")}");
                if (command != null)
                {
                    RunPaletteCommand(command); // as if chosen, so it becomes a recent command
                }

                return;
            }

            if (chord.StartsWith("test:palette-open:"))
            {
                // "test:palette-open:@" opens the palette with that query typed (for captures).
                OpenCommandPalette(chord["test:palette-open:".Length..]);
                Log.Write($"test {chord}: done");
                return;
            }

            if (chord.StartsWith("test:paste:"))
            {
                // A paste through the real path ("|" for line breaks), without the clipboard.
                tab.Active.Control.PasteForTest(chord["test:paste:".Length..].Replace('|', '\n'));
                return;
            }

            if (chord.StartsWith("test:click:"))
            {
                // A plain left click on viewport cell "col:row" of the focused pane (posted clicks can't reach WinUI).
                var cell = chord["test:click:".Length..].Split(':');
                Log.Write($"test {chord}");
                Ui.Guard("test:click", () => tab.Active.Control.ClickToMove(int.Parse(cell[0]), int.Parse(cell[1])));
                return;
            }

            if (chord.StartsWith("test:menu:"))
            {
                // Opens the context menu as a right-click on viewport cell "col:row" of the focused pane would.
                var cell = chord["test:menu:".Length..].Split(':');
                Log.Write($"test {chord}");
                Ui.Guard("test:menu", () => tab.Active.Control.ShowContextMenuAtCell(int.Parse(cell[0]), int.Parse(cell[1])));
                return;
            }

            if (chord.StartsWith("test:drop:"))
            {
                // Files dropped on the focused pane ("|" between paths), without a real drag.
                tab.Active.Control.PastePaths(chord["test:drop:".Length..].Split('|'));
                return;
            }

            if (chord.StartsWith("menu:"))
            {
                // "menu:Split pane/Split right" runs that tab context menu item's command.
                var item = FindMenuItem(tab.Item.ContextFlyout as MenuFlyout, chord["menu:".Length..].Split('/'));
                Log.Write($"test menu {chord}: {(item != null ? "found" : "missing")}");
                item?.Command?.Execute(null);
                return;
            }

            // Through the pane's key sequences first, like a real press ("ctrl+a" then "c").
            var step = tab.Active.Control.PressForTest(KeyChord.Parse(chord));
            if (step is not (KeyStep.NotMine or KeyStep.PassThrough))
            {
                Log.Write($"test key {chord}: {step}");
                return;
            }

            var bound = _keybindings.TryGet(KeyChord.Parse(chord), out var invocation);
            Log.Write($"test key {chord}: {(bound ? invocation.ToString() : "unbound")}");
            if (bound)
            {
                // The pane gets it first, like a real key press, so pane actions such as copy and find run there.
                tab.Active.Control.Invoke(invocation);
            }
        };
        timer.Start();
    }

    private static MenuFlyoutItem? FindMenuItem(MenuFlyout? menu, string[] path)
    {
        IList<MenuFlyoutItemBase>? items = menu?.Items;
        for (var i = 0; items != null && i < path.Length; i++)
        {
            var match = items.FirstOrDefault(x => x is MenuFlyoutItem m && m.Text == path[i] || x is MenuFlyoutSubItem sub && sub.Text == path[i]);
            if (i == path.Length - 1)
            {
                return match as MenuFlyoutItem;
            }

            items = (match as MenuFlyoutSubItem)?.Items;
        }

        return null;
    }
}
