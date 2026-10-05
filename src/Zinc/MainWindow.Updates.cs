namespace Zinc;

// A downloaded update (Updater): a notice in each window, with Restart now and Later.
public sealed partial class MainWindow
{
    private void SetUpUpdateNotice()
    {
        RestartForUpdateButton.Click += (_, _) => Updater.RestartNow();
        UpdateLaterButton.Click += (_, _) => Updater.DismissNotice();
        Updater.Changed += ShowUpdateNotice;
        ShowUpdateNotice();
    }

    /// <summary>Open while an update is ready and nobody said Later; not in the quick terminal (it comes and goes).</summary>
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
            UpdateNotice.Message = "Restart to update now, or it installs when you quit.";
        }

        UpdateNotice.IsOpen = show;
    }
}
