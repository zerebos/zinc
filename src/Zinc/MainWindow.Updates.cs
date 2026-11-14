namespace Zinc;

// A downloaded update (Updater) shows a notice in each window, with Restart Zinc and Install when I quit.
public sealed partial class MainWindow
{
    private void SetUpUpdateNotice()
    {
        RestartForUpdateButton.Click += (_, _) => Updater.RestartNow();
        UpdateLaterButton.Click += (_, _) => Updater.DismissNotice();
        Updater.Changed += ShowUpdateNotice;
        ShowUpdateNotice();
    }

    /// <summary>
    /// The notice is open while an update is ready and nobody said Later. It never shows in the quick terminal, which
    /// comes and goes.
    /// </summary>
    private void ShowUpdateNotice()
    {
        if (_windowClosed)
        {
            return;
        }

        var show = !_quick && Updater.Status == UpdateStatus.Ready && !Updater.NoticeDismissed;
        if (show)
        {
            UpdateNotice.Title = $"Zinc {Updater.NewVersion} is ready";
            UpdateNotice.Message = "Restart to update now, or the update installs when you quit.";
        }

        UpdateNotice.IsOpen = show;
    }
}
