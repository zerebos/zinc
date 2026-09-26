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
        var config = Config.Load();
        _window = new MainWindow(config, ShellResolver.Resolve(config),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _window.Activate();
    }
}
