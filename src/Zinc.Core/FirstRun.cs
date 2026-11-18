using Zinc.Settings;

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
/// <param name="Scheme">A color scheme's name, or null to keep Zinc's own colors.</param>
/// <param name="FontFamily">Null keeps the default (the bundled Cascadia Mono NF).</param>
/// <param name="DefaultProfile">A profile id, or null to keep the first one.</param>
/// <param name="ToastWhenDone">A Windows notification (too) when a long command finishes out of view, or null when not asked.</param>
/// <param name="PaneStyle">Compact or Cards, or null to leave the pane settings as they are (a mix of your own).</param>
public sealed record WelcomeChoices(WorkStyle Style, WorkspaceTabs WorkspacesOn, string? Scheme, string? FontFamily, string? DefaultProfile,
    bool? ToastWhenDone = null, PaneStyleKind? PaneStyle = null);

/// <summary>Writes the welcome page's choices into config.toml.</summary>
public static class FirstRun
{
    /// <summary>
    /// What a new config file starts with (kept short, because the website's reference has every setting). The first
    /// line points editors that read <c>#:schema</c> (Taplo, in Even Better TOML) at the config schema, so they complete
    /// and check keys with no setup.
    /// </summary>
    public const string Template =
        "#:schema " + ConfigSchema.Url + "\n" +
        "# Zinc's settings. Changes apply when you save, and zincterminal.dev/docs/config lists every setting.\n" +
        "# To reopen the welcome page, run Welcome from the command palette (Ctrl+Shift+P).\n";

    /// <summary>
    /// <paramref name="text"/> (the current config, or null for a new one) with the choices set,
    /// everything else left as it was. With <paramref name="current"/> (the config in force),
    /// only what the choices change is written, so saving the page untouched leaves the file alone.
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

        if (global == null || global.PaneTabs != workspaces)
        {
            text = ConfigEditor.Set(text, ["window", "pane-tabs"], workspaces);
            text = ConfigEditor.Remove(text, ["window", "tab-groups"]); // its old name (ConfigFile.RenamedKeys) would be read too
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

        // A pane style sets its five settings. The default style's values are the defaults, so it removes them.
        var styleBefore = current == null ? PaneStyle.Default : PaneStyle.Of(current.ValueOf);
        var styleChanged = choices.PaneStyle is { } chosen && (current == null || styleBefore != chosen);
        if (choices.PaneStyle is { } style && styleChanged)
        {
            foreach (var (key, value) in PaneStyle.Values(style))
            {
                var path = key.Split('.');
                text = style == PaneStyle.Default ? ConfigEditor.Remove(text, path) : ConfigEditor.Set(text, path, value);
            }
        }

        // With the workspaces along the top, a header on every pane puts a second row right under the workspaces' row.
        // So Cards there shows headers only once a workspace is split, and moving the workspaces back to the sidebar
        // gives every pane its header again. A save that changes neither the style nor where the workspaces are leaves
        // the headers as they are.
        var tabsChanged = global == null || global.WorkspaceTabs != tabs;
        if ((choices.PaneStyle ?? styleBefore) == PaneStyleKind.Cards && (styleChanged || tabsChanged))
        {
            if (tabs == WorkspaceTabs.Top)
            {
                text = ConfigEditor.Set(text, ["window", "pane-headers"], "auto");
            }
            else if (global?.PaneHeaders == PaneHeaders.Auto)
            {
                text = ConfigEditor.Remove(text, ["window", "pane-headers"]);
            }
        }

        if (choices.DefaultProfile is { } profile && current?.DefaultProfile.Id != profile)
        {
            text = ConfigEditor.Set(text, ["default-profile"], profile);
        }

        return text;
    }
}
