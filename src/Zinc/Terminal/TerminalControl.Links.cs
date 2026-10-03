using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;

namespace Zinc.Terminal;

// Links: with Ctrl held, the OSC 8 hyperlink or URL under the pointer is underlined and
// the pointer becomes a hand; Ctrl+click opens it (LinkTarget decides what may open).
public sealed partial class TerminalControl
{
    private Windows.Foundation.Point? _pointerPosition;
    private ToolTip? _linkTip;
    private LinkContext? _linkContext;

    private static bool IsCtrlKey(VirtualKey key) =>
        key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl;

    /// <summary>Re-evaluates the hovered link at the last pointer position.</summary>
    private void UpdateHoverLink(bool ctrl)
    {
        var link = ctrl && !_selecting && _pointerPosition is { } pos ? LinkUnder(pos) : null;
        if (link == _renderer.HoverLink)
        {
            return;
        }

        _renderer.HoverLink = link;
        UpdatePointerCursor(_arrowCursor);
        ShowLinkTip(link);
        Redraw();
    }

    private LinkMatch? LinkUnder(Windows.Foundation.Point pos)
    {
        var m = _renderer.Metrics;
        if (_session is not { } session || m.Width <= 0)
        {
            return null;
        }

        var col = (int)Math.Floor((pos.X - _gridLeft) / m.Width);
        var row = (int)Math.Floor((pos.Y - _gridTop) / m.Height);
        return col >= 0 && col < _cols && row >= 0 && row < _rows ? session.LinkAt((ushort)col, (uint)row) : null;
    }

    private void ShowLinkTip(LinkMatch? link)
    {
        // A hyperlink's text can say anything, so show where it really goes. Plain URLs
        // already show their target.
        if (link is { Explicit: true } l)
        {
            _linkTip ??= new ToolTip { Placement = PlacementMode.Mouse };
            _linkTip.Content = l.Uri;
            ToolTipService.SetToolTip(this, _linkTip);
            _linkTip.IsOpen = true;
        }
        else if (_linkTip != null)
        {
            _linkTip.IsOpen = false;
            ToolTipService.SetToolTip(this, null);
        }
    }

    private void OpenLink(string uri)
    {
        try
        {
            // File links use the shell's own paths (WSL, Git Bash): resolve them per pane.
            _linkContext ??= ShellEnvironment.LinkContextFor(_commandLine);
            switch (LinkTarget.Classify(uri, _linkContext, out var target))
            {
                case LinkAction.Open:
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    break;
                case LinkAction.Reveal when Directory.Exists(target):
                    Process.Start("explorer.exe", $"\"{target}\"");
                    break;
                case LinkAction.Reveal when File.Exists(target):
                    Process.Start("explorer.exe", $"/select,\"{target}\"");
                    break;
                default:
                    Log.Write($"link: not opening {uri}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"link: opening {uri} failed: {ex.Message}");
        }
    }
}
