using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Zinc.Pty;

namespace Zinc;

/// <summary>
/// "Default terminal app" is not a config.toml setting but Windows' own (Windows Settings can change it
/// too), so the switch shows and changes Windows' state (<see cref="DefaultTerminal"/>).
/// </summary>
internal sealed partial class SettingsWindow
{
    private void DefaultTerminalCard()
    {
        var dt = DefaultTerminal.ForUser();
        DefaultTerminal.State state;
        try
        {
            state = dt.Current();
        }
        catch (Exception ex)
        {
            Log.Write($"default terminal: couldn't read Windows' setting: {ex.Message}");
            return;
        }

        var (panel, toggle) = Switch("Default terminal app");
        toggle.IsOn = state == DefaultTerminal.State.Zinc;
        var detail = state switch
        {
            DefaultTerminal.State.NoConsoleHost =>
                "Choose Windows Terminal as the default terminal app in Windows Settings first. Zinc then takes over from Windows Terminal.",
            _ => "Programs that open a console outside a terminal (a script you double-click, Run, an installer) open in a Zinc tab.",
        };

        FrameworkElement control = panel;
        if (state == DefaultTerminal.State.NoConsoleHost)
        {
            toggle.IsEnabled = false;
            var open = new Button { Content = "Open Windows Settings" };
            open.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:developers") { UseShellExecute = true });
            control = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { open, panel } };
        }

        toggle.Toggled += (_, _) =>
        {
            try
            {
                if (toggle.IsOn && dt.Current() != DefaultTerminal.State.Zinc && Environment.ProcessPath is { } exe)
                {
                    var done = dt.Enable(exe);
                    Log.Write($"default terminal: {(done ? "Zinc is the default terminal" : "no console host to keep")}");
                }
                else if (!toggle.IsOn && dt.Current() == DefaultTerminal.State.Zinc)
                {
                    dt.Disable();
                    Log.Write("default terminal: given back");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"default terminal: couldn't change Windows' setting: {ex.Message}");
            }

            toggle.IsOn = dt.Current() == DefaultTerminal.State.Zinc; // what Windows has now
        };

        _page.Children.Add(Row("Default terminal app", detail, control));
    }
}
