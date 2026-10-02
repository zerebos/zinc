namespace Zinc;

// A failure Zinc survived (App.UnhandledException): the notice says so, and where the details are.
public sealed partial class MainWindow
{
    private bool _errorNoticeWired;

    internal void ShowError(Exception exception)
    {
        if (!_errorNoticeWired)
        {
            _errorNoticeWired = true;
            OpenCrashLogButton.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.CrashPath) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
                {
                    Log.Write($"couldn't open the crash log: {ex.Message}");
                }
            };
        }

        ErrorNotice.Message = $"{exception.GetType().Name}: {exception.Message.TrimEnd('.')}. Zinc kept running; the details are in crash.log.";
        ErrorNotice.IsOpen = true;
    }
}
