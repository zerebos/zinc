using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace Phosphor;

// Config reloads: a window takes the new settings in place, and shows what's wrong with the file.
public sealed partial class MainWindow
{
    private void SetUpConfigNotice()
    {
        OpenConfigButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(_config.Path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // No app for .toml files: fall back to Notepad.
                Log.Write($"config: open with the default app failed ({ex.Message}); using notepad");
                Process.Start("notepad.exe", $"\"{_config.Path}\"");
            }
        };
        ShowConfigProblems(_config, applied: true);
    }

    /// <summary>
    /// Takes new settings without restarting anything: every tab re-resolves its profile by id
    /// and updates its colors, font and padding; strips, dimming and menus follow. Where the
    /// workspace tabs sit (top or left) applies to windows opened from now on.
    /// </summary>
    internal void ApplyConfig(ConfigFile config)
    {
        _config = config;
        foreach (var tab in AllTabs())
        {
            foreach (var pane in tab.Workspace.Tabs)
            {
                // A profile no longer in the file keeps the settings it had.
                if (config.FindProfile(pane.Profile.Id) is { } profile)
                {
                    pane.Profile = profile;
                }

                pane.Control.ApplyConfig(pane.Profile.Config);
            }

            UpdateFocusBorders(tab);
            UpdateTitle(tab);
            ApplyTabBackground(tab, force: true);

            // Its menu shows shortcuts, which may have changed.
            tab.Item.ContextFlyout = BuildTabMenu(tab);
            if (_workspaceList?.ElementFor(tab) is { } listItem)
            {
                listItem.ContextFlyout = tab.Item.ContextFlyout;
            }
        }

        RefreshWorkspaceList();
        BuildNewTabMenu();
        ShowConfigProblems(config, applied: true);
    }

    /// <summary>
    /// Shows the file's problems, if any. <paramref name="applied"/> false: the file isn't valid
    /// TOML (likely saved mid-edit), so the settings in force stay until it is.
    /// </summary>
    internal void ShowConfigProblems(ConfigFile config, bool applied)
    {
        if (config.Warnings.Count == 0)
        {
            ConfigNotice.IsOpen = false;
            return;
        }

        ConfigNotice.Severity = applied ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
        ConfigNotice.Title = applied
            ? "Some settings in config.toml were ignored"
            : "config.toml isn't valid TOML; the previous settings stay in force";
        const int shown = 4;
        var lines = config.Warnings.Take(shown).ToList();
        if (config.Warnings.Count > shown)
        {
            lines.Add($"…and {config.Warnings.Count - shown} more");
        }

        ConfigNotice.Message = string.Join("\n", lines);
        ConfigNotice.IsOpen = true;
    }
}
