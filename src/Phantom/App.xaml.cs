using Microsoft.UI.Xaml;
using Phantom.Input;

namespace Phantom;

public partial class App : Application
{
    private readonly List<Window> _windows = [];
    private ConfigFile _config = null!;
    private HotkeyListener? _hotkey;
    private GlobalHotkey? _hotkeyChord;
    private MainWindow? _quick;

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
        SetUpQuickTerminal();
    }

    internal void OpenWindow(Profile profile, string? command = null)
    {
        var window = new MainWindow(_config, Keybindings.Default, profile, command ?? ShellResolver.ForProfile(profile.Config));
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        window.Activate();
    }

    // ------------------------------------------------------- quick terminal

    private void SetUpQuickTerminal()
    {
        try
        {
            _hotkeyChord = GlobalHotkey.Parse(_config.Global.QuickTerminal.Hotkey);
        }
        catch (FormatException ex)
        {
            Log.Write($"quick terminal: bad hotkey: {ex.Message}");
            return;
        }

        _hotkey = new HotkeyListener(_hotkeyChord.Value);
        _hotkey.Pressed += ToggleQuickTerminal;
        _hotkey.TryRegister();
    }

    /// <summary>
    /// Null when the quick terminal hotkey is registered (or disabled); otherwise its
    /// display name, e.g. "Win+`", so windows can say it's taken. Retries registration
    /// first: another app may have released it since.
    /// </summary>
    internal string? UnavailableQuickTerminalHotkey()
    {
        if (_hotkey == null || _hotkey.TryRegister())
        {
            return null;
        }

        return HotkeyName(_hotkeyChord!.Value);
    }

    internal void ToggleQuickTerminal()
    {
        if (_quick == null)
        {
            var profile = _config.DefaultProfile;
            _quick = new MainWindow(_config, Keybindings.Default, profile, ShellResolver.ForProfile(profile.Config), quick: true);
            var quick = _quick;
            _quick.Closed += (_, _) =>
            {
                if (_quick == quick)
                {
                    _quick = null;
                }
            };
            _quick.ShowQuick();
        }
        else if (_quick.IsQuickShownAndActive)
        {
            _quick.HideQuick();
        }
        else
        {
            _quick.ShowQuick();
        }
    }

    private static string HotkeyName(GlobalHotkey hotkey)
    {
        var name = new KeyChord(hotkey.Key,
            (hotkey.Modifiers & GlobalHotkey.Control) != 0,
            (hotkey.Modifiers & GlobalHotkey.Alt) != 0,
            (hotkey.Modifiers & GlobalHotkey.Shift) != 0).ToString();
        return (hotkey.Modifiers & GlobalHotkey.Win) != 0 ? "Win+" + name : name;
    }
}
