namespace Zinc.Settings;

public static partial class SettingsCatalog
{
    /// <summary>The settings in the Notifications section, in the order the window shows them.</summary>
    private static SettingDef[] NotificationSettings() =>
    [
        new("notifications", "Long commands", "notify.command-finished", "Long command finishes",
            "Says what happens when a long command finishes in a pane you weren't looking at.", SettingKind.Choice)
            { Choices = NotifyChoices, Binding = Bind.Enum<NotifyMode>((c, v) => c with { NotifyCommandFinished = v }) },
        new("notifications", "Long commands", "notify.command-finished-after", "Counts as long after",
            "This is in seconds unless you add a unit, like 45s or 2m.", SettingKind.Text)
            {
                Placeholder = "10", NumbersAsNumbers = true,
                Binding = Bind.Custom(
                    (c, v) => Bind.AsText(v) is { } s && Config.TryDuration(s, out var d)
                        ? c with { NotifyCommandFinishedAfter = d }
                        : null,
                    [Accepts.Number(0, null), Accepts.Text()],
                    c => Bind.Tidy(c.NotifyCommandFinishedAfter.TotalSeconds)),
            },
        new("notifications", "Long commands", "notify.command-finished-sound", "Sound",
            "Plays when a long command finishes out of view. A Windows notification has its own sound.", SettingKind.Choice)
            {
                Choices = [new("None", "off"), new("Windows' notification sound", "notification"), new("Windows' default beep", "beep")],
                Binding = Bind.Enum<FinishedSound>((c, v) => c with { CommandFinishedSound = v }),
            },
        new("notifications", "Programs", "notify.programs", "Program notifications",
            "Says what happens when a program asks for a notification with OSC 9 or 777, for example when a build script finishes.", SettingKind.Choice)
            { Choices = NotifyChoices, Binding = Bind.Enum<NotifyMode>((c, v) => c with { NotifyPrograms = v }) },
        new("notifications", "Dot colors", "notify.finished-color", "Finished",
            "A command finished, or a program sent a notification.", SettingKind.Color)
            {
                NullLabel = "Use Zinc's green", Placeholder = "Zinc's green",
                Binding = Bind.Color((c, v) => c with { DotFinishedColor = v }),
            },
        new("notifications", "Dot colors", "notify.bell-color", "Bell", "A program rang the bell.", SettingKind.Color)
            {
                NullLabel = "Use Zinc's amber", Placeholder = "Zinc's amber",
                Binding = Bind.Color((c, v) => c with { DotBellColor = v }),
            },
        new("notifications", "Dot colors", "notify.failed-color", "Failed", "A command failed, or a program exited with an error.", SettingKind.Color)
            {
                NullLabel = "Use Zinc's red", Placeholder = "Zinc's red",
                Binding = Bind.Color((c, v) => c with { DotFailedColor = v }),
            },
    ];

    // A property, not a field, because a field in another partial file could be read before it's set.
    private static SettingChoice[] NotifyChoices =>
    [
        new("Tab dot", "badge", "Zinc shows a dot on the pane's tab. The taskbar button also flashes if Zinc is in the background."),
        new("Notification", "toast", "Zinc shows a dot on the pane's tab, and a Windows notification if Zinc is in the background."),
        new("Always notify", "toast-always", "Zinc shows a Windows notification whenever you aren't looking at the pane, even while you're using Zinc."),
        new("None", "never", "Zinc shows neither a dot nor a notification."),
    ];
}
