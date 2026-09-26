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
        var config = ConfigFile.Load();
        var profile = config.DefaultProfile;
        _window = new MainWindow(profile.Config, ShellResolver.Resolve(profile.Config),
            profile.Config.ResolveWorkingDirectory());
        _window.Activate();
    }
}
