namespace Zinc;

/// <summary>The welcome page's two ways of working.</summary>
public enum WorkStyle
{
    /// <summary>Tabs on top, one terminal per tab (like Windows Terminal).</summary>
    Simple,

    /// <summary>Workspaces (in a sidebar, or on top), each with its own tabs and splits.</summary>
    Workspaces,
}

/// <summary>What the welcome page asks.</summary>
/// <param name="Scheme">A color scheme's name; null keeps Zinc's own colors.</param>
/// <param name="FontFamily">Null keeps the default (the bundled Cascadia Mono NF).</param>
/// <param name="DefaultProfile">A profile id; null keeps the first one.</param>
/// <param name="ToastWhenDone">A Windows notification (too) when a long command finishes out of view; null: not asked.</param>
public sealed record WelcomeChoices(WorkStyle Style, WorkspaceTabs WorkspacesOn, string? Scheme, string? FontFamily, string? DefaultProfile,
    bool? ToastWhenDone = null);

/// <summary>Writes the welcome page's choices into config.toml.</summary>
public static class FirstRun
{
    /// <summary>What a new config file starts with (kept short: the README has every setting).</summary>
    public const string Template =
        "# Zinc's settings. Changes apply when you save; the README lists every setting.\n" +
        "# Reopen the welcome page from the command palette (Ctrl+Shift+P): Welcome…\n";

    /// <summary>
    /// <paramref name="text"/> (the current config, or null for a new one) with the choices set,
    /// everything else left as it was. With <paramref name="current"/> (the config in force),
    /// only what the choices change is written: saving the page untouched leaves the file alone.
    /// </summary>
    public static string Apply(string? text, WelcomeChoices choices, ConfigFile? current = null)
    {
        text ??= Template;
        var global = current?.Global;
        var workspaces = choices.Style == WorkStyle.Workspaces;
        var tabs = workspaces && choices.WorkspacesOn == WorkspaceTabs.Left ? WorkspaceTabs.Left : WorkspaceTabs.Top;
        if (global == null || global.WorkspaceTabs != tabs)
        {
            text = ConfigEditor.Set(text, ["window", "workspace-tabs"], tabs == WorkspaceTabs.Left ? "left" : "top");
        }

        if (global == null || global.TabGroups != workspaces)
        {
            text = ConfigEditor.Set(text, ["window", "tab-groups"], workspaces);
        }

        if (current == null || current.SchemeName != choices.Scheme)
        {
            text = choices.Scheme is { } scheme
                ? ConfigEditor.Set(text, ["colors", "theme"], scheme)
                : ConfigEditor.Remove(text, ["colors", "theme"]);
        }

        if (global == null || global.FontFamily != (choices.FontFamily ?? new Config().FontFamily))
        {
            text = choices.FontFamily is { } family
                ? ConfigEditor.Set(text, ["font", "family"], family)
                : ConfigEditor.Remove(text, ["font", "family"]);
        }

        // Notifications: "toast" (a "toast-always" already there counts), else the default dot.
        if (choices.ToastWhenDone is { } toast)
        {
            var toasting = global?.NotifyCommandFinished is NotifyMode.Toast or NotifyMode.ToastAlways;
            if (toast && !toasting)
            {
                text = ConfigEditor.Set(text, ["notify", "command-finished"], "toast");
            }
            else if (!toast && (toasting || global == null))
            {
                text = ConfigEditor.Remove(text, ["notify", "command-finished"]);
            }
        }

        if (choices.DefaultProfile is { } profile && current?.DefaultProfile.Id != profile)
        {
            text = ConfigEditor.Set(text, ["default-profile"], profile);
        }

        return text;
    }
}
