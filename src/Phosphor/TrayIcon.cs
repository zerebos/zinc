using System.Runtime.InteropServices;

namespace Phosphor;

/// <summary>
/// Phosphor's notification-area icon: left click raises <see cref="Activated"/>, right click
/// shows the menu built by <see cref="BuildMenu"/>. Re-added when Explorer restarts.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    /// <summary>A menu entry; a null entry is a separator.</summary>
    public sealed record MenuItem(string Label, Action Run, bool Enabled = true);

    private const uint CallbackMessage = 0x8000 + 0x50; // WM_APP + 'P'
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private readonly nint _icon, _largeIcon;
    private bool _added;

    public TrayIcon(MessageWindow window, string icoPath)
    {
        _window = window;
        var size = GetSystemMetricsForDpi(49 /* SM_CXSMICON */, GetDpiForSystem());
        _icon = LoadImageW(0, icoPath, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        var large = GetSystemMetricsForDpi(11 /* SM_CXICON */, GetDpiForSystem());
        _largeIcon = LoadImageW(0, icoPath, 1, large, large, 0x10); // the notification's picture
        window.Message += OnMessage;
        Add();
    }

    public event Action? Activated;

    /// <summary>Builds the right-click menu each time it opens (labels can depend on state).</summary>
    public Func<IReadOnlyList<MenuItem?>>? BuildMenu { get; set; }

    private void Add()
    {
        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        _added = ShellNotifyIconW(NimAdd, &data);
        // Version 4: clicks arrive as NIN_SELECT / WM_CONTEXTMENU with the anchor point.
        data.uVersion = 4;
        ShellNotifyIconW(NimSetVersion, &data);
        Log.Write($"tray: added {_added}");
    }

    private NotifyIconData Data(uint flags)
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)sizeof(NotifyIconData),
            hWnd = _window.Hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        "Zinc".AsSpan().CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == _window.TaskbarCreated)
        {
            Add(); // Explorer restarted and forgot us
            return false;
        }

        if (msg != CallbackMessage)
        {
            return false;
        }

        switch ((uint)(lParam & 0xFFFF))
        {
            case NinBalloonUserClick:
                NotificationClicked?.Invoke();
                break;
            case NinSelect:
            case NinKeySelect:
                Activated?.Invoke();
                break;
            case WmContextMenu:
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
        }

        return true;
    }

    private void ShowMenu(int x, int y)
    {
        if (BuildMenu?.Invoke() is not { Count: > 0 } items)
        {
            return;
        }

        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is { } item)
                {
                    AppendMenuW(menu, item.Enabled ? MfString : MfString | MfGrayed, (nuint)(i + 1), item.Label);
                }
                else
                {
                    AppendMenuW(menu, MfSeparator, 0, null);
                }
            }

            // The owner must be foreground, or the menu won't close when clicking elsewhere.
            SetForegroundWindow(_window.Hwnd);
            var chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | TpmBottomAlign, x, y, _window.Hwnd, 0);
            PostMessageW(_window.Hwnd, 0 /* WM_NULL */, 0, 0);
            if (chosen > 0 && items[chosen - 1] is { Enabled: true } picked)
            {
                picked.Run();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        _window.Message -= OnMessage;
        if (_added)
        {
            var data = Data(0);
            ShellNotifyIconW(NimDelete, &data);
            _added = false;
        }

        if (_icon != 0)
        {
            DestroyIcon(_icon);
        }

        if (_largeIcon != 0)
        {
            DestroyIcon(_largeIcon);
        }
    }

    /// <summary>
    /// Shows a Windows notification from the tray icon (no app registration needed, unlike app
    /// notifications, whose identity would follow each install's versioned path). It respects
    /// Do Not Disturb; clicking it raises <see cref="NotificationClicked"/>.
    /// </summary>
    public void ShowNotification(string title, string body)
    {
        if (!_added)
        {
            return;
        }

        var data = Data(NifInfo);
        Copy(title, data.szInfoTitle, 64);
        Copy(body.Length == 0 ? " " : body, data.szInfo, 256); // an empty text removes it instead
        data.dwInfoFlags = NiifUser | NiifLargeIcon | NiifRespectQuietTime;
        data.hBalloonIcon = _largeIcon != 0 ? _largeIcon : _icon;
        ShellNotifyIconW(NimModify, &data);
    }

    /// <summary>A notification from <see cref="ShowNotification"/> was clicked.</summary>
    public event Action? NotificationClicked;

    private static void Copy(string text, char* into, int capacity)
    {
        var span = new Span<char>(into, capacity);
        span.Clear();
        var length = Math.Min(text.Length, capacity - 1);
        if (length < text.Length && length > 0)
        {
            length--; // room for the ellipsis
            text[..length].AsSpan().CopyTo(span);
            span[length] = '…';
            return;
        }

        text.AsSpan(0, length).CopyTo(span);
    }

    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NifInfo = 0x10, NifShowTip = 0x80;
    private const uint NiifUser = 0x4, NiifLargeIcon = 0x20, NiifRespectQuietTime = 0x80;
    private const uint NinSelect = 0x400, NinKeySelect = 0x401, NinBalloonUserClick = 0x405, WmContextMenu = 0x7B;
    private const uint MfString = 0, MfGrayed = 1, MfSeparator = 0x800;
    private const uint TpmReturnCmd = 0x100, TpmRightButton = 2, TpmBottomAlign = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion; // union with uTimeout
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
}
