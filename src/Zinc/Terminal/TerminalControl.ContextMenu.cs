using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Zinc.Input;
using Zinc.Panes;

namespace Zinc.Terminal;

// The terminal's context menu (right-click = "menu", the default; Shift+F10 or the Menu key always): copy, paste and
// find; the link or command that was clicked; split, new tab and close. Items run actions through Invoke, as their
// keys do, and show those keys.
public sealed partial class TerminalControl
{
    /// <summary>Where the right button went down, when its release should open the menu (Windows opens menus on release).</summary>
    private Windows.Foundation.Point? _menuOnRelease;

    /// <summary>Opens the menu at <paramref name="at"/> (pane coordinates): about what's there, then the pane's own items.</summary>
    private void ShowContextMenu(Windows.Foundation.Point at)
    {
        if (_session is not { } session)
        {
            return;
        }

        var menu = new MenuFlyout();
        var selection = HasSelection();
        Add(menu, "Copy", "", TerminalAction.Copy, enabled: selection);
        if (selection && !_config.CopyFormatting)
        {
            Add(menu, "Copy with formatting", "", TerminalAction.CopyFormatted); // (Copy already does, when it's on)
        }

        Add(menu, "Paste", "", TerminalAction.Paste);
        Add(menu, "Find", "", TerminalAction.Find);

        // What was clicked: a link, a command (with shell integration).
        var about = new List<MenuFlyoutItemBase>();
        if (LinkUnder(at) is { } link)
        {
            _linkContext ??= ShellEnvironment.LinkContextFor(_commandLine);
            var what = LinkTarget.Classify(link.Uri, _linkContext, out _);
            if (what is LinkAction.Open or LinkAction.Reveal)
            {
                about.Add(Item(what == LinkAction.Open ? "Open link" : "Show in folder", "", null, () => OpenLink(link.Uri)));
            }

            about.Add(Item("Copy link", "", null, () => ClipboardText.Set(link.Uri)));
        }

        var m = _renderer.Metrics;
        var row = m.Height > 0 ? (int)Math.Floor((at.Y - _gridTop) / m.Height) : -1;
        if (row >= 0 && row < _rows && session.CommandAt(row) is { } command)
        {
            if (command.Text is { } text)
            {
                about.Add(Item("Copy command", "", null, () => ClipboardText.Set(text)));
            }

            if (command.OutputRow != null)
            {
                about.Add(Item("Copy output", null, Shortcut(TerminalAction.CopyOutput), () => CopyOutputOf(session, command, copy: true)));
                about.Add(Item("Select output", null, Shortcut(TerminalAction.SelectOutput), () => CopyOutputOf(session, command, copy: false)));
            }
        }

        if (about.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var item in about)
            {
                menu.Items.Add(item);
            }
        }

        // The pane's own: these go to the window, as their keys do.
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(menu, "Split right", "", new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Right));
        Add(menu, "Split down", "", new ActionInvocation(TerminalAction.SplitPane, Direction: PaneDirection.Down));
        Add(menu, Actions.Title(TerminalAction.NewTab, _config.TabGroups), "", TerminalAction.NewTab);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(menu, "Close tab", "", TerminalAction.ClosePane);

        menu.ShowAt(this, new FlyoutShowOptions { Position = at, ShowMode = FlyoutShowMode.Standard });
    }

    /// <summary>The menu for the keyboard (Shift+F10, the Menu key): under the cursor, else at the pane's top left.</summary>
    private void ShowContextMenuAtCursor()
    {
        var m = _renderer.Metrics;
        var at = _renderer.CursorCell is var (col, row) && m.Width > 0
            ? new Windows.Foundation.Point(_gridLeft + col * m.Width, _gridTop + (row + 1) * m.Height)
            : new Windows.Foundation.Point(_gridLeft, _gridTop);
        ShowContextMenu(at);
    }

    /// <summary>For tests (<c>test:menu:col:row</c>): the menu a right-click on that viewport cell opens.</summary>
    internal void ShowContextMenuAtCell(int col, int row)
    {
        var m = _renderer.Metrics;
        ShowContextMenu(new Windows.Foundation.Point(_gridLeft + (col + 0.5) * m.Width, _gridTop + (row + 0.5) * m.Height));
    }

    private void CopyOutputOf(TerminalSession session, ShellCommand command, bool copy)
    {
        if (session.SelectOutput(command))
        {
            if (copy)
            {
                CopySelection();
            }

            Redraw();
        }
    }

    private void Add(MenuFlyout menu, string text, string glyph, TerminalAction action, bool enabled = true) =>
        Add(menu, text, glyph, new ActionInvocation(action), enabled);

    private void Add(MenuFlyout menu, string text, string glyph, ActionInvocation action, bool enabled = true)
    {
        var item = Item(text, glyph, _keybindings.ShortcutFor(action), () => Invoke(action));
        item.IsEnabled = enabled;
        menu.Items.Add(item);
    }

    private string? Shortcut(TerminalAction action) => _keybindings.ShortcutFor(new ActionInvocation(action));

    private static MenuFlyoutItem Item(string text, string? glyph, string? shortcut, Action run)
    {
        var item = new MenuFlyoutItem { Text = text, Command = new RelayCommand(() => Ui.Guard($"menu: {text}", run)) };
        if (glyph != null)
        {
            item.Icon = new FontIcon { Glyph = glyph };
        }

        if (shortcut != null)
        {
            item.KeyboardAcceleratorTextOverride = shortcut; // shown only; the keys themselves are Keybindings'
        }

        return item;
    }
}
