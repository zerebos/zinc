namespace Zinc.Settings;

public static partial class SettingsCatalog
{
    /// <summary>The settings in the Quick terminal section, in the order the window shows them.</summary>
    private static SettingDef[] QuickSettings() =>
    [
        new("quick", "Summon", "quick-terminal.hotkey", "Hotkey", "Shows and hides the quick terminal from anywhere, for example win+grave or ctrl+alt+t.", SettingKind.Text)
            {
                Binding = Bind.Custom(
                    (c, v) => v is string s && Config.TryHotkey(s) ? Quick(c, q => q with { Hotkey = s }) : null,
                    [Accepts.Text()], c => c.QuickTerminal.Hotkey),
            },
        new("quick", "Placement", "quick-terminal.position", "Position", "", SettingKind.Choice)
            {
                Choices = [new("Top", "top"), new("Bottom", "bottom"), new("Left", "left"), new("Right", "right"), new("Center", "center")],
                Binding = Bind.Enum<QuickTerminalPosition>((c, v) => Quick(c, q => q with { Position = v })),
            },
        new("quick", "Placement", "quick-terminal.screen", "Screen", "", SettingKind.Choice)
            {
                Choices = [new("With the pointer", "mouse", "Opens on the screen the pointer is on."), new("Main", "main", "Always opens on the main screen.")],
                Binding = Bind.Enum<QuickTerminalScreen>((c, v) => Quick(c, q => q with { Screen = v })),
            },
        new("quick", "Placement", "quick-terminal.size", "Size",
            "Sets the quick terminal's size along its edge, as a percentage or in pixels (50%, 600px). When empty, the size is 50%, or 70% by 60% at \"Center\".", SettingKind.Text)
            {
                Placeholder = "50%",
                DefaultText = "50%, or 70% by 60% at \"Center\"",
                Binding = Bind.Custom((c, v) => QuickSize(v) is { } s && Config.TryQuickSize(s, out var a, out var b)
                    ? Quick(c, q => q with { Primary = a, Secondary = b })
                    : null,
                    [Accepts.Text(), Accepts.List([Accepts.Text()], 1, 2)]),
            },
        new("quick", "Behavior", "quick-terminal.autohide", "Hide when it loses focus", "", SettingKind.Toggle)
            {
                Binding = Bind.Bool(c => c.QuickTerminal.Autohide, (c, v) => Quick(c, q => q with { Autohide = v })),
            },
        new("quick", "Behavior", "quick-terminal.animation-duration", "Animation", "How long the quick terminal takes to fade in and out. At 0 it appears at once.",
            SettingKind.Number)
            {
                Unit = "seconds", Minimum = 0, Maximum = 2, Step = 0.05,
                Binding = Bind.Number(c => c.QuickTerminal.AnimationDuration, (c, v) => Quick(c, q => q with { AnimationDuration = v })),
            },
    ];

    /// <summary><paramref name="config"/> with its quick terminal settings changed.</summary>
    private static Config Quick(Config config, Func<QuickTerminalSettings, QuickTerminalSettings> change) =>
        config with { QuickTerminal = change(config.QuickTerminal) };

    /// <summary>The quick terminal's size as text: "50%", "600px", one number, or two of them as a list.</summary>
    private static string? QuickSize(object value) =>
        value is List<object> { Count: 1 or 2 } pair ? string.Join(",", pair.Select(Bind.AsText)) : Bind.AsText(value);
}
