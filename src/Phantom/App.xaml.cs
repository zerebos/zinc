using Microsoft.UI.Xaml;
using Phantom.Input;

namespace Phantom;

public partial class App : Application
{
    private readonly List<Window> _windows = [];
    private ConfigFile _config = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Write($"unhandled: {e.Exception}");
    }

    internal static App Instance => (App)Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _config = ConfigFile.Load();
        var profile = _config.DefaultProfile;
        // Our own arguments / PHANTOM_SHELL override the first tab's command only.
        OpenWindow(profile, ShellResolver.Resolve(profile.Config));
    }

    internal void OpenWindow(Profile profile, string? command = null)
    {
        var window = new MainWindow(_config, Keybindings.Default, profile, command ?? ShellResolver.ForProfile(profile.Config));
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        window.Activate();
    }
}
