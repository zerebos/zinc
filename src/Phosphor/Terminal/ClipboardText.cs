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

    /// <summary>
    /// Pastes the clipboard's text into <paramref name="session"/>, asking first when
    /// <see cref="PasteCheck"/> says to (lines that would run as commands, a large paste).
    /// </summary>
    public static async void PasteInto(TerminalSession session, Config config, Microsoft.UI.Xaml.XamlRoot? root)
    {
        // Code after an await resumes as queued work, where an exception would end the app.
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            await Paste(session, config, root, await content.GetTextAsync());
        }
        catch (Exception ex)
        {
            // The clipboard can be transiently locked by another process; a dialog may already be open.
            Log.Write($"paste failed: {ex.Message}");
        }
    }

    /// <summary>The same paste with given text (<c>test:paste:</c>, so tests never touch the real clipboard).</summary>
    public static async void PasteForTest(TerminalSession session, Config config, Microsoft.UI.Xaml.XamlRoot? root, string text)
    {
        try
        {
            await Paste(session, config, root, text);
        }
        catch (Exception ex)
        {
            Log.Write($"test paste failed: {ex.Message}");
        }
    }

    private static async Task Paste(TerminalSession session, Config config, Microsoft.UI.Xaml.XamlRoot? root, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var concern = PasteCheck.Concern(text, session.GetMode(Phosphor.Ghostty.GhosttyMode.BracketedPaste),
            config.PasteWarning, config.LargePasteWarning);
        Log.Write($"paste: {PasteCheck.Lines(text)} line(s), {(concern is { } c ? $"asks ({c})" : "no question")}");
        if (concern is { } why && (root == null || !await ConfirmPaste(text, why, root)))
        {
            return;
        }

        session.Paste(text);
    }

    /// <summary>Asks before a paste, showing its start. Cancel is the default where lines would run.</summary>
    private static async Task<bool> ConfirmPaste(string text, PasteConcern why, Microsoft.UI.Xaml.XamlRoot root)
    {
        var lines = PasteCheck.Lines(text);
        var kb = Math.Max(1, System.Text.Encoding.UTF8.GetByteCount(text) / 1024);
        var (title, message) = why switch
        {
            PasteConcern.CouldRunCommands => ($"Paste {lines} lines?",
                "Here each line break acts as Enter, so each line runs as a command as it arrives."),
            PasteConcern.MultipleLines => ($"Paste {lines} lines?", "The program receives them as one paste."),
            _ => ($"Paste {kb} KB?", "That's a lot of text; once it starts, it can't be stopped."),
        };

        // The first lines, as they'll arrive.
        var shown = string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Take(24));
        var preview = new Microsoft.UI.Xaml.Controls.ScrollViewer
        {
            MaxHeight = 220,
            HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            Content = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = shown,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                IsTextSelectionEnabled = true,
            },
        };
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
            Title = title,
            Content = new Microsoft.UI.Xaml.Controls.StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new Microsoft.UI.Xaml.Controls.TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                    new Microsoft.UI.Xaml.Controls.Border
                    {
                        Child = preview,
                        Padding = new Microsoft.UI.Xaml.Thickness(10, 8, 10, 8),
                        CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
                        Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    },
                },
            },
            PrimaryButtonText = "Paste",
            CloseButtonText = "Cancel",
            DefaultButton = why == PasteConcern.CouldRunCommands
                ? Microsoft.UI.Xaml.Controls.ContentDialogButton.Close
                : Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;
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
