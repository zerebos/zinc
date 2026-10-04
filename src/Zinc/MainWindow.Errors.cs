using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Zinc;

// A failure Zinc survived (App.UnhandledException): the notice says so, and where the details are.
public sealed partial class MainWindow
{
    /// <summary>
    /// Puts each notice on a solid backing. InfoBar's severity colors are see-through tints: fine over a terminal, but
    /// over the new-workspace page (on the window's Mica) the page's text showed through and a tester couldn't read the
    /// update notice. Its own Background replaces the tint instead of sitting under it, hence a backing behind it,
    /// shown only while that notice is open (an empty one would still take the panel's spacing).
    /// </summary>
    private void SolidNoticeBacks()
    {
        foreach (var notice in Notices.Children.OfType<InfoBar>().ToList())
        {
            var index = Notices.Children.IndexOf(notice);
            Notices.Children.RemoveAt(index);
            var back = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
                CornerRadius = notice.CornerRadius,
                Child = notice,
                Visibility = notice.IsOpen ? Visibility.Visible : Visibility.Collapsed,
            };
            notice.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty,
                (_, _) => back.Visibility = notice.IsOpen ? Visibility.Visible : Visibility.Collapsed);
            Notices.Children.Insert(index, back);
        }
    }

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
