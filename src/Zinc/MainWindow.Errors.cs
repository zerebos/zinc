using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Zinc;

// A failure Zinc survived (App.UnhandledException). The notice says so, and says where the details are.
public sealed partial class MainWindow
{
    /// <summary>
    /// Puts each notice on a solid backing, because InfoBar's severity colors are see-through tints. They're fine over
    /// a terminal, but over the new-workspace page (on the window's Mica) the page's text would show through and make
    /// the notice hard to read. An InfoBar's own Background replaces the tint instead of sitting under it, hence a
    /// backing behind it, shown only while that notice is open (an empty one would still take the panel's spacing).
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

        ErrorNotice.Message = $"{exception.Message.TrimEnd('.')}. Details are in crash.log.";
        ErrorNotice.IsOpen = true;
    }
}
