using Microsoft.UI.Xaml;

namespace Phantom;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(ShellResolver.Resolve(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _window.Activate();
    }
}
