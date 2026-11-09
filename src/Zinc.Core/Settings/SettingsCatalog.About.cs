namespace Zinc.Settings;

public static partial class SettingsCatalog
{
    /// <summary>The settings in the About section, in the order the window shows them.</summary>
    private static SettingDef[] AboutSettings() =>
    [
        new("about", "Updates", "updates.check", "Check for updates automatically",
            "Once a day. New versions download in the background, and you choose when to restart. Only for Zinc installed with its installer.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.UpdateCheck, (c, v) => c with { UpdateCheck = v }) },
        new("about", "Troubleshooting", "debug.logging", "Debug log",
            "Keeps a record of what Zinc does, to send with a bug report. Turn it on, make the problem happen, then copy the log.",
            SettingKind.Toggle)
            { Binding = Bind.Bool(c => c.Logging, (c, v) => c with { Logging = v }) },
    ];
}
