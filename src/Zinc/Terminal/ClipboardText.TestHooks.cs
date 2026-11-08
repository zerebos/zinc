namespace Zinc.Terminal;

// What the test harness (`MainWindow.ReplayTestKeys`) calls. Development only.
public static partial class ClipboardText
{
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
}
