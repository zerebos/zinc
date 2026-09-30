namespace Phosphor.Terminal;

/// <summary>
/// <c>clipboard-read = ask</c>: a program's clipboard read waits (on the reader thread, the terminal
/// unlocked) while the window asks: allow once, allow in this tab, or deny.
/// </summary>
public sealed partial class TerminalControl
{
    /// <summary>"Allow in this tab" was chosen: this pane's programs read without asking.</summary>
    private volatile bool _clipboardReadsAllowed;

    /// <summary>Cancelled when the pane closes, ending a question nobody will answer.</summary>
    private readonly CancellationTokenSource _closing = new();

    private static readonly TimeSpan ClipboardQuestionTimeout = TimeSpan.FromMinutes(5);

    private bool AskToReadClipboard()
    {
        if (_clipboardReadsAllowed)
        {
            return true;
        }

        // Only output from the program asks (on its reader thread); the UI thread can't wait for itself.
        if (DispatcherQueue.HasThreadAccess)
        {
            return false;
        }

        var answer = new TaskCompletionSource<ClipboardReadAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var who = Title.Length > 0 ? Title : "A program";
        if (!DispatcherQueue.SafeEnqueue(() => ClipboardText.AskToRead(XamlRoot, who, answer)))
        {
            return false;
        }

        try
        {
            if (!answer.Task.Wait(ClipboardQuestionTimeout, _closing.Token))
            {
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (answer.Task.Result == ClipboardReadAnswer.InThisTab)
        {
            _clipboardReadsAllowed = true;
        }

        return answer.Task.Result != ClipboardReadAnswer.Deny;
    }
}
