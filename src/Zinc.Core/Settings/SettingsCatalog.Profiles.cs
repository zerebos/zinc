namespace Zinc.Settings;

public static partial class SettingsCatalog
{
    /// <summary>A profile's own fields (keys relative to its <c>[profile.&lt;id&gt;]</c> section).</summary>
    public static IReadOnlyList<SettingDef> ProfileFields { get; } =
    [
        new("profiles", "General", "name", "Name", "Shown in menus and tabs.", SettingKind.Text, null)
            { DefaultText = "The found shell's own name, or else the profile's id" },
        new("profiles", "General", "command", "Command", "What runs in its tabs: a shell, or any program.", SettingKind.Text, null)
            { DefaultText = "The found shell's own command, or else PowerShell 7, else Windows PowerShell, else Command Prompt" },
        new("profiles", "General", "working-directory", "Starting folder", "~ (your home), a path, or %VARIABLES%.", SettingKind.Text, null)
            { Placeholder = "~", DefaultText = "Your home folder" },
        new("profiles", "General", "icon", "Icon", "An image file for its tabs (default: the program's own icon).", SettingKind.Text, null)
            { DefaultText = "The program's own icon" },
        new("profiles", "General", "hidden", "Hidden", "Kept out of the menus (its tabs still reopen).", SettingKind.Toggle, false),
    ];

    /// <summary>Where Zinc looks for shells (<c>[discover]</c>), each on by default.</summary>
    public static IReadOnlyList<SettingDef> Discovery { get; } =
    [
        new("profiles", "Find shells on this PC", "discover.wsl", "WSL distributions", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.powershell", "PowerShell 7 and Windows PowerShell", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.cmd", "Command Prompt", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.git-bash", "Git Bash", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.msys2", "MSYS2", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.visual-studio", "Visual Studio developer shells", "", SettingKind.Toggle, true),
        new("profiles", "Find shells on this PC", "discover.ssh", "SSH hosts", "From ~/.ssh/config.", SettingKind.Toggle, true),
    ];
}
