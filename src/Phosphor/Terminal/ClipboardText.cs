using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace Phosphor.Terminal;

/// <summary>Clipboard helpers, kept out of the unsafe control class so they can await.</summary>
public static partial class ClipboardText
{
    public static void Set(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            // The clipboard can be transiently locked by another process.
            Log.Write($"clipboard set failed: {ex.Message}");
        }
    }

    public static async void PasteInto(TerminalSession session)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                session.Paste(await content.GetTextAsync());
            }
        }
        catch (Exception ex)
        {
            // The clipboard can be transiently locked by another process.
            Debug.WriteLine($"Paste failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The clipboard's text, read synchronously on any thread (for OSC 52 reads, which must be
    /// answered before the terminal continues parsing). Null if it's unavailable or not text.
    /// </summary>
    public static string? ReadNow()
    {
        const uint CfUnicodeText = 13;
        // Another process may hold the clipboard open for a moment.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(0))
            {
                Thread.Sleep(10);
                continue;
            }

            try
            {
                var handle = GetClipboardData(CfUnicodeText);
                if (handle == 0)
                {
                    return "";
                }

                var ptr = GlobalLock(handle);
                try
                {
                    return ptr == 0 ? null : Marshal.PtrToStringUni(ptr);
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        return null;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint handle);
}
