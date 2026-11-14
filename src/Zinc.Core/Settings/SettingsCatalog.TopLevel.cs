namespace Zinc.Settings;

/// <summary>
/// A top-level key the reader handles with its own code instead of a setting (<c>include</c>, <c>keys</c>), with
/// what it takes in words, and what leaving it out does (null when that needs no words).
/// </summary>
public sealed record TopLevelKey(string Key, string Label, string Description, string Takes, string? DefaultText = null);

public static partial class SettingsCatalog
{
    /// <summary>The top-level keys that aren't settings. The config schema and the reference both describe them from here.</summary>
    public static IReadOnlyList<TopLevelKey> TopLevel { get; } =
    [
        new("include", "Include", "Reads more config files first, by path or pattern. What this file says wins.", "A path, or a list of paths"),
        new("default-profile", "Default profile", "New tabs open with the profile that has this id.", "A profile id",
            "The first profile in the menu. That's your own first profile, or else the first shell Zinc finds, which is your default WSL distribution when you have one"),
        new("profile-order", "Profile order", "The menus list profiles in this order, by id.", "A list of profile ids"),
        new("environment", "Environment", "Sets environment variables for the programs Zinc starts. An empty value removes that variable.",
            "A table of names and values"),
        new("keys", "Keys", "Binds a key chord or sequence, like `ctrl+shift+t` or `ctrl+a > c`, to the action it runs. A key bound to `none` goes to the program instead.",
            "A table of keys and actions"),
    ];

    /// <summary>The choice a color setting with named choices offers for any color (Settings lists it last).</summary>
    public const string CustomColorLabel = "Custom color";

    /// <summary>The top-level key <paramref name="key"/>'s description.</summary>
    internal static string TopLevelDescription(string key) => TopLevel.First(k => k.Key == key).Description;
}
