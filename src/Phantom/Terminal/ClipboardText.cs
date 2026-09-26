using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;

namespace Phantom.Terminal;

/// <summary>Clipboard helpers, kept out of the unsafe control class so they can await.</summary>
public static class ClipboardText
{
    public static void Set(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
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
}
