namespace Zinc.Terminal;

/// <summary>
/// With <c>clipboard-read = ask</c>, a program's clipboard read waits (on the reader thread, the terminal
/// unlocked) while the window asks whether to allow it once, allow it in this tab, or deny it.
/// </summary>
public sealed partial class TerminalControl
{
    /// <summary>"Allow in this tab" was chosen, so this pane's programs read without asking.</summary>
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

        // Only output from the program asks (on its reader thread), because the UI thread can't wait for itself.
        if (DispatcherQueue.HasThreadAccess)
        {
            return false;
        }

        var answer = new TaskCompletionSource<ClipboardReadAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The running command names the program ("npm test in this tab asked…"). The tab's title often names a folder
        // instead, so it isn't used.
        if (!DispatcherQueue.SafeEnqueue(() => ClipboardText.AskToRead(XamlRoot, RunningCommand?.Text ?? "A program", answer)))
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
