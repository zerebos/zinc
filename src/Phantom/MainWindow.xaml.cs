using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Phantom.Terminal;

namespace Phantom;

public sealed partial class MainWindow : Window
{
    private readonly TerminalControl _terminal;

    public MainWindow(Config config, string commandLine, string? workingDirectory)
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 700));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark;

        _terminal = new TerminalControl(config, commandLine, workingDirectory);
        _terminal.TitleChanged += title => Title = string.IsNullOrWhiteSpace(title) ? "Phantom" : title;
        _terminal.CloseRequested += Close;
        Root.Children.Add(_terminal);

        Closed += (_, _) => _terminal.Close();
    }
}
