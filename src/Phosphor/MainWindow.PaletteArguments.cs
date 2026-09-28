using Phosphor.Commands;
using Phosphor.Terminal;

namespace Phosphor;

// Palette commands that ask for an argument (the palette's second step): a color scheme picked from
// the list with a live preview, a command line to run in a new tab, a folder to open one in.
public sealed partial class MainWindow
{
    /// <summary>The palette's commands that ask for an argument.</summary>
    private IEnumerable<PaletteCommand> ArgumentCommands(WorkspaceTab tab)
    {
        PaletteCommand Asks(string title, Action ask) => new(title, null, ask, AsksArgument: true);
        yield return Asks("Change color scheme…", () => PickColorScheme(tab, tab.Active, asDefault: false));
        yield return Asks("Set default color scheme…", () => PickColorScheme(tab, tab.Active, asDefault: true));
        yield return Asks("New tab running…", () => AskPaletteArgument(new PaletteArgument(
            "A program to run in a new tab, with its arguments, e.g. ssh myserver (shell commands like dir need a shell)",
            Submit: line => NewTabWith(tab, line, WorkingFolderOf(tab.Active, line)))));
        yield return Asks("New tab in folder…", () => AskFolder(tab, "A folder to open a new tab in, e.g. ~/src"));
    }

    // ------------------------------------------------------------ color schemes

    /// <summary>
    /// Lists the color schemes, previewing the selected one on <paramref name="pane"/>. Chosen:
    /// that tab keeps it, or (<paramref name="asDefault"/>) it becomes config.toml's colors.theme.
    /// Cancelled: the pane gets its colors back.
    /// </summary>
    private void PickColorScheme(WorkspaceTab tab, TerminalTab pane, bool asDefault)
    {
        var themes = _config.Themes;
        // Previews start from the profile's own settings, not a scheme this tab already has.
        var profile = _config.FindProfile(pane.Profile.Id) ?? pane.Profile;
        var choices = themes.Names().Select(name => new PaletteCommand(name, null,
            () => ChooseScheme(tab, pane, profile, name, asDefault), Remember: false)).ToList();
        if (!asDefault && pane.ColorScheme != null)
        {
            choices.Insert(0, new PaletteCommand($"{profile.Name}'s own", null, () =>
            {
                pane.ColorScheme = null;
                SetPaneProfile(tab, pane, profile);
            }, Remember: false));
        }

        AskPaletteArgument(new PaletteArgument(
            asDefault ? "Default color scheme (previewed on this tab)" : "Color scheme for this tab",
            choices,
            Highlight: choice =>
            {
                var preview = asDefault
                    ? DefaultSchemePreview(pane, choice.Title)
                    : themes.Apply(profile.Config, choice.Title) ?? profile.Config;
                pane.Control.ApplyConfig(preview);
            },
            Cancel: () => pane.Control.ApplyConfig(pane.Profile.Config)));
    }

    /// <summary>
    /// What the pane would look like with <paramref name="name"/> as config.toml's colors.theme:
    /// the file read again with just that changed, so colors it sets explicitly (background,
    /// foreground, palette entries, a profile's own scheme) still win, as they will after saving.
    /// </summary>
    private Config DefaultSchemePreview(TerminalTab pane, string name)
    {
        try
        {
            var text = File.Exists(_config.Path) ? File.ReadAllText(_config.Path) : "";
            var edited = ConfigFile.Parse(ConfigEditor.Set(text, ["colors", "theme"], name), _config.Path);
            // A profile without its own section (a discovered one) has the global colors.
            var colors = (edited.FindProfile(pane.Profile.Id)?.Config ?? edited.Global).Theme;
            return pane.Profile.Config with { Theme = colors };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write($"palette: preview of {name} as default: {ex.Message}");
            return _config.Themes.Apply(pane.Profile.Config, name) ?? pane.Profile.Config;
        }
    }

    private void ChooseScheme(WorkspaceTab tab, TerminalTab pane, Profile profile, string name, bool asDefault)
    {
        if (!asDefault)
        {
            if (_config.Themes.Apply(profile.Config, name) is { } themed)
            {
                pane.ColorScheme = name;
                SetPaneProfile(tab, pane, profile with { Config = themed });
            }

            return;
        }

        // Written to the config; the reload that follows recolors every pane that doesn't choose
        // its own. Until then this one keeps the preview.
        try
        {
            ConfigEditor.SetInFile(_config.Path, ["colors", "theme"], name);
            Log.Write($"config: colors.theme = {name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"config: couldn't set colors.theme: {ex.Message}");
            pane.Control.ApplyConfig(pane.Profile.Config);
        }
    }

    /// <summary>Gives a pane new settings, and everything that shows its colors (strip, sidebar, dimming) follows.</summary>
    private void SetPaneProfile(WorkspaceTab tab, TerminalTab pane, Profile profile)
    {
        pane.Profile = profile;
        pane.Control.ApplyConfig(profile.Config);
        UpdateFocusBorders(tab);
        ApplyTabBackground(tab, force: true);
        RefreshWorkspaceList();
    }

    // ------------------------------------------------------------ new tabs

    /// <summary>
    /// A new tab next to the focused one (in its pane, with tab groups; else a new workspace),
    /// with the focused tab's profile running <paramref name="command"/> in <paramref name="folder"/>.
    /// </summary>
    private void NewTabWith(WorkspaceTab tab, string command, string? folder)
    {
        var profile = _config.FindProfile(tab.Active.Profile.Id) ?? tab.Active.Profile;
        var directory = folder ?? profile.Config.ResolveWorkingDirectory();
        Log.Write($"palette: new tab {profile.Name}: {command} in {directory}");
        if (_config.Global.TabGroups && tab.Workspace.GroupOf(tab.Active) is { } group)
        {
            NewTabInGroup(tab, group, profile, command, directory);
        }
        else
        {
            OpenTab(profile, command, directory);
        }
    }

    /// <summary>Where a program started from <paramref name="pane"/> should run: the Windows folder its shell is in, if any.</summary>
    private static string? WorkingFolderOf(TerminalTab pane, string command) =>
        ShellDirectory.Parse(pane.Control.ReportedDirectory) is { } reported &&
        ShellDirectory.LaunchIn(command, reported) is { WorkingDirectory: { } folder } && Directory.Exists(folder)
            ? folder
            : null;

    private void AskFolder(WorkspaceTab tab, string prompt) =>
        AskPaletteArgument(new PaletteArgument(prompt, Submit: typed => OpenInFolder(tab, typed)));

    /// <summary>
    /// A new tab of the focused tab's profile in <paramref name="typed"/>: <c>~</c> is home (WSL's own
    /// in WSL), a relative path starts from where the focused shell is. Asks again if the folder
    /// doesn't exist or doesn't fit the shell.
    /// </summary>
    private void OpenInFolder(WorkspaceTab tab, string typed)
    {
        var profile = _config.FindProfile(tab.Active.Profile.Id) ?? tab.Active.Profile;
        var command = ShellResolver.ForProfile(profile.Config);
        var folder = ShellDirectory.ResolveTyped(typed, command,
            ShellDirectory.Parse(tab.Active.Control.ReportedDirectory) ?? profile.Config.ResolveWorkingDirectory());
        if (folder != null && ShellDirectory.LaunchIn(command, folder) is { } launch &&
            (launch.WorkingDirectory is not { } windows || Directory.Exists(windows)))
        {
            NewTabWith(tab, launch.Command, launch.WorkingDirectory);
            return;
        }

        AskFolder(tab, $"No folder {typed} for {profile.Name}: try another");
    }
}
